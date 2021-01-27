using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Warden.Core;

namespace Warden.Windows
{
    /// <summary>
    ///     A utility class for creating processes as the interactive user.
    /// </summary>
    public static class InteractiveProcessCreator
    {
        /// <summary>
        ///     Retrieves the process ID of Windows Logon component for the specified session identifier.
        /// </summary>
        /// <param name="sessionId">The ID of the session winlogon.exe should be running in.</param>
        /// <returns>The system-unique identifier of the Windows Logon component. If this function fails it returns zero.</returns>
        private static int GetWinLogonProcessId(uint sessionId)
        {
            const string winLogonName = "winlogon";
            foreach (var process in ProcessNative.GetProcesses())
            {
                if (process.Name.Equals(winLogonName) && ProcessNative.ProcessIdToSessionId((uint) process.Id, out var winLogonSessionId) && winLogonSessionId == sessionId)
                {
                    return process.Id;
                }
            }
            return 0;
        }

        /// <summary>
        ///     Attempts to retrieve the <see cref="WindowsIdentity"/> of the first interactive user.
        /// </summary>
        /// <returns>The <see cref="WindowsIdentity"/> of the first interactive user.</returns>
        internal static WindowsIdentity GetInteractiveSessionUserIdentity()
        {
            var sessionId = INVALID_SESSION_ID;
            var sessions = IntPtr.Zero;
            var sessionCount = 0;

            try
            {
                if (WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, ref sessions, ref sessionCount))
                {
                    var arrayElementSize = Marshal.SizeOf<WTS_SESSION_INFO>();
                    var current = sessions;
                    for (var i = 0; i < sessionCount; i++)
                    {
                        var sessionInfo = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);
                        current += arrayElementSize;

                        if (sessionInfo.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                        {
                            sessionId = sessionInfo.SessionID;
                            break;
                        }
                    }
                }
            }
            finally
            {
                if (sessions != IntPtr.Zero)
                {
                    WTSFreeMemory(sessions);
                }
            }

            // If none of the enumerated sessions were active we try to retrieve
            // the session identifier of the console session that is physically attached.
            if (sessionId == INVALID_SESSION_ID)
            {
                sessionId = WTSGetActiveConsoleSessionId();
            }
            if (WTSQueryUserToken(sessionId, out var interactiveToken))
            {
                return new WindowsIdentity(interactiveToken.DangerousGetHandle());
            }
            throw new Win32Exception("GetInteractiveSessionUserIdentity failed.");
        }

        /// <summary>
        ///     Attempts to retrieve and duplicate the e primary token token for first interactive user.
        /// </summary>
        /// <param name="sessionId">If this function succeeds this is the ID of the interactive user session.</param>
        /// <param name="userToken">If this function succeeds this is the a handle for the interactive user token.</param>
        /// <returns>True if the function could obtain a handle to the interactive user token. Otherwise it is false.</returns>
        private static bool TryGetInteractiveUserToken(out uint sessionId, out SafeAccessTokenHandle userToken)
        {
            sessionId = INVALID_SESSION_ID;
            var sessions = IntPtr.Zero;
            var sessionCount = 0;

            try
            {
                if (WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, ref sessions, ref sessionCount))
                {
                    var arrayElementSize = Marshal.SizeOf<WTS_SESSION_INFO>();
                    var current = sessions;
                    for (var i = 0; i < sessionCount; i++)
                    {
                        var sessionInfo = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);
                        current += arrayElementSize;

                        if (sessionInfo.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                        {
                            sessionId = sessionInfo.SessionID;
                            break;
                        }
                    }
                }
            }
            finally
            {
                if (sessions != IntPtr.Zero)
                {
                    WTSFreeMemory(sessions);
                }
            }

            // If none of the enumerated sessions were active we try to retrieve
            // the session identifier of the console session that is physically attached.
            if (sessionId == INVALID_SESSION_ID)
            {
                sessionId = WTSGetActiveConsoleSessionId();
            }
            if (!WTSQueryUserToken(sessionId, out var primaryUserToken))
            {
                primaryUserToken.Dispose();
                userToken = null!;
                return false;
            }
            // Convert the impersonation token to a primary token
            return DuplicateTokenEx(primaryUserToken, 0, null,
                SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TokenType.Primary,
                out userToken);
        }

        /// <summary>
        /// Loads the specified user's profile. The profile can be a local user profile or a roaming user profile.
        /// Called before <see cref="T:CreateProcessAsUser" /> as it does not load the specified user's profile into <see cref="T:HKEY_USERS" />
        /// </summary>
        /// <param name="activeSessionId">Session identifier to indicate the session in which the calling application is running (or the current session)</param>
        /// <param name="userToken">Token for the user, which is returned by the LogonUser, CreateRestrictedToken, DuplicateToken, OpenProcessToken, or OpenThreadToken function.
        /// The token must have TOKEN_QUERY, TOKEN_IMPERSONATE, and TOKEN_DUPLICATE access.</param>
        /// <param name="info"></param>
        private static SafeRegistryHandle LoadUserProfile(int activeSessionId, SafeAccessTokenHandle userToken, out ProfileInfo info)
        {
           
            IntPtr userInfoBuffer = IntPtr.Zero, domainBuffer = IntPtr.Zero, nameBuffer = IntPtr.Zero;
            try
            {
                // get the name of the user associated with the session.
                if (!WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, activeSessionId, WTS_INFO_CLASS.WTSUserName, out nameBuffer, out _))
                {
                    throw new Win32Exception($"WTSQuerySessionInformation failed: { Marshal.GetLastWin32Error()}");
                }
                // copy the buffer to a string and set it on the user profile
                var username = Marshal.PtrToStringAnsi(nameBuffer);

                // get the domain of the user associated with the session.
                if (!WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, activeSessionId, WTS_INFO_CLASS.WTSDomainName, out domainBuffer, out _))
                {
                    throw new Win32Exception($"WTSQuerySessionInformation failed: { Marshal.GetLastWin32Error()}");
                }
                // copy the buffer to a string and set it on the user profile
                var domain = Marshal.PtrToStringAnsi(domainBuffer);

                if (string.IsNullOrWhiteSpace(username))
                {
                    throw new Win32Exception($"Profile username is null: {Marshal.GetLastWin32Error()}");
                }
                if (!GetProfileType(out var profileType))
                {
                    throw new Win32Exception($"GetProfileType failed: {Marshal.GetLastWin32Error()}");
                }
                info = new ProfileInfo();
                info.Size = (uint) Marshal.SizeOf<ProfileInfo>();
                info.UserName = username;
                info.Flags = ProfileInfoFlags.NoUI;
                if (profileType == ProfileType.Roaming)
                {
                    var status = NetUserGetInfo(domain, username, 4, out userInfoBuffer);
                    if (status != NetApiStatus.Success)
                    {
                        throw new Win32Exception($"NetUserGetInfo failed: {status}");
                    }
                    var userInfo = Marshal.PtrToStructure<UserInfo>(userInfoBuffer);
                    if (string.IsNullOrWhiteSpace(userInfo.usri4_profile))
                    {
                        throw new Win32Exception($"Roaming Profile Missing: {status}");
                    }
                    info.ProfilePath = userInfo.usri4_profile;
                }
                if (!LoadUserProfile(userToken, ref info))
                {
                    throw new Win32Exception($"LoadUserProfile failed: {Marshal.GetLastWin32Error()}");
                }
                if (info.Profile == IntPtr.Zero)
                {
                    throw new Win32Exception($"LoadUserProfile: HKCU handle was not loaded. Error code: {profileType} {info.ServerName} / {info.DefaultPath} / { info.Size} / {Marshal.GetLastWin32Error()}");
                }
                return new SafeRegistryHandle(info.Profile, false);
            }
            finally
            {
                NetApiBufferFree(userInfoBuffer);
                WTSFreeMemory(nameBuffer);
                WTSFreeMemory(domainBuffer);
            }
        }


        /// <summary>
        ///     Creates a new process object with the privileges of LocalSystem in the interactive desktop session.
        /// </summary>
        /// <param name="info">
        ///     The <see cref="WardenStartInfo"/> that contains the information that is used to start the process,
        ///     including the PackageFamilyName, ApplicationId, and any command-line arguments.
        /// </param>
        /// <returns>A <see cref="WardenProcess"/> instance that is associated with the created process.</returns>
        internal static WardenProcess? AsLocalSystem(WardenStartInfo info)
        {
            var environmentBlockHandle = IntPtr.Zero;
            var startInfo = new StartupInfo();
            var processInformation = new ProcessInformation();
            startInfo.cb =  Marshal.SizeOf<StartupInfo>();
            try
            {
                if (!TryGetInteractiveUserToken(out var sessionId, out var interactiveUserToken))
                {
                    throw new Win32Exception("GetSessionUserToken failed.");
                }

                // By default CreateProcessAsUser creates a process on a non-interactive window station, meaning
                // the window station has a desktop that is invisible and the process is incapable of receiving
                // user input. To remedy this we set the lpDesktop parameter to indicate we want to enable user 
                // interaction with the new process.
                startInfo.wShowWindow = (short) SW.SW_SHOW;
                startInfo.lpDesktop = InteractiveWindowStation;

                using var registryHandle = LoadUserProfile((int) sessionId, interactiveUserToken, out _);
                if (registryHandle.IsInvalid)
                {
                    throw new Win32Exception("LoadUserProfile failed.");
                }
                // copy the users env block
                if (!CreateEnvironmentBlock(ref environmentBlockHandle, interactiveUserToken, false))
                {
                    throw new Win32Exception("CreateEnvironmentBlock failed.");
                }

                var logonProcessId = GetWinLogonProcessId(sessionId);
                if (logonProcessId == 0)
                {
                    throw new Win32Exception($"Unable to find the WinLogon process ID for session '{sessionId}'.");
                }

                using var processHandle = ProcessNative.OpenProcessHandle(ProcessNative.ProcessAccessFlags.MaximumAllowed, logonProcessId);
                if (processHandle.IsInvalid)
                {
                    throw new Win32Exception("Unable to obtain a valid handle for winlogon.exe");
                }
                if (!ProcessNative.OpenProcessToken(processHandle, TOKEN_ALL_ACCESS, out var processToken))
                {
                    throw new Win32Exception("Unable to open the process token for winlogon.exe");
                }

                // ReSharper disable once UseObjectOrCollectionInitializer
                var securityAttributes = new SecurityAttributes();
                securityAttributes.Length = Marshal.SizeOf<SecurityAttributes>();


                // copy the access token of the explorer process; the newly created token will be a primary token
                if (!DuplicateTokenEx(processToken, MAXIMUM_ALLOWED, securityAttributes, SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TokenType.Primary, out var winLogonToken))
                {
                    throw new Win32Exception("Unable to duplicate the winlogon.exe process token");
                }
                
                const int creationFlags = CREATE_UNICODE_ENVIRONMENT | DETACHED_PROCESS;
                if (!CreateProcessAsUser(winLogonToken,
                    info.FileName,
                    info.Arguments,
                     securityAttributes,
                     securityAttributes,
                    false,
                    creationFlags,
                    environmentBlockHandle,
                    info.WorkingDirectory,
                    ref startInfo,
                    out processInformation))
                {
                    throw new Win32Exception($"CreateProcessAsUserW failed: {Marshal.GetLastWin32Error()}");
                }
                UnloadUserProfile(interactiveUserToken, registryHandle);
                return WardenProcess.GetProcessById(processInformation.ProcessId, info.Track, info.FilteredImages);
            }
            finally
            {
                DestroyEnvironmentBlock(environmentBlockHandle);
                CloseHandle(processInformation.ThreadHandle);
                CloseHandle(processInformation.ProcessHandle);
                CloseHandle(startInfo.hStdError);
                CloseHandle(startInfo.hStdInput);
                CloseHandle(startInfo.hStdOutput);
                CloseHandle(startInfo.lpReserved2);
               
            }
        }

        /// <summary>
        ///     Creates a new process object the interactive desktop session.
        /// </summary>
        /// <param name="info">
        ///     The <see cref="WardenStartInfo"/> that contains the information that is used to start the process,
        ///     including the PackageFamilyName, ApplicationId, and any command-line arguments.
        /// </param>
        /// <returns>A <see cref="WardenProcess"/> instance that is associated with the created process.</returns>
        internal static WardenProcess? AsUser(WardenStartInfo info)
        {
            var environmentBlockHandle = IntPtr.Zero;
            var startInfo = new StartupInfo();
            var processInformation = new ProcessInformation();
            startInfo.cb = Marshal.SizeOf<StartupInfo>();
            try
            {
                if (!TryGetInteractiveUserToken(out var sessionId, out var interactiveUserToken))
                {
                    throw new Win32Exception("GetSessionUserToken failed.");
                }

                // By default CreateProcessAsUser creates a process on a non-interactive window station, meaning
                // the window station has a desktop that is invisible and the process is incapable of receiving
                // user input. To remedy this we set the lpDesktop parameter to indicate we want to enable user 
                // interaction with the new process.
                startInfo.wShowWindow = (short) SW.SW_SHOW;
                startInfo.lpDesktop = InteractiveWindowStation;


                using var registryHandle = LoadUserProfile((int)sessionId, interactiveUserToken, out _);
                if (registryHandle.IsInvalid)
                {
                    throw new Win32Exception("LoadUserProfile failed.");
                }

                // copy the users env block
                if (!CreateEnvironmentBlock(ref environmentBlockHandle, interactiveUserToken, false))
                {
                    throw new Win32Exception("CreateEnvironmentBlock failed.");
                }
                

                const int creationFlags = CREATE_UNICODE_ENVIRONMENT | DETACHED_PROCESS;
                if (!CreateProcessAsUser(interactiveUserToken,
                    info.FileName,
                    info.Arguments,
                     null,
                     null,
                    false,
                    creationFlags,
                    environmentBlockHandle,
                    info.WorkingDirectory,
                    ref startInfo,
                    out processInformation))
                {
                    throw new Win32Exception("CreateProcessAsUser failed");
                }
                UnloadUserProfile(interactiveUserToken, registryHandle);
                return WardenProcess.GetProcessById(processInformation.ProcessId, info.Track, info.FilteredImages);
            }
            finally
            {
                DestroyEnvironmentBlock(environmentBlockHandle);
                CloseHandle(processInformation.ThreadHandle);
                CloseHandle(processInformation.ProcessHandle);
                CloseHandle(startInfo.hStdError);
                CloseHandle(startInfo.hStdInput);
                CloseHandle(startInfo.hStdOutput);
                CloseHandle(startInfo.lpReserved2);

            }
        }

    #region Windows API

        /// <summary>
        ///     Retrieves the environment variables for the specified user. This block can then be passed to the
        ///     CreateProcessAsUser function.
        /// </summary>
        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateEnvironmentBlock(ref IntPtr lpEnvironment, SafeAccessTokenHandle hToken, bool bInherit);

        /// <summary>
        ///     Retrieves a list of sessions on a Remote Desktop Session Host (RD Session Host) server.
        /// </summary>
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSEnumerateSessions(IntPtr server,
            int reserved,
            int version,
            ref IntPtr sessionInfo,
            ref int count);

        /// <summary>
        ///     Frees memory allocated by a Remote Desktop Services function.
        /// </summary>
        [DllImport("wtsapi32.dll", ExactSpelling = true)]
        private static extern void WTSFreeMemory(IntPtr sessionInfo);

        /// <summary>
        ///     Obtains the primary access token of the logged-on user specified by the session ID. To call this function
        ///     successfully, the calling application must be running within the context of the LocalSystem account and have the
        ///     SE_TCB_NAME privilege.
        /// </summary>
        [DllImport("wtsapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle phToken);
        

        /// <summary>
        ///     Retrieves the session identifier of the console session. The console session is the session that is currently
        ///     attached to the physical console. Note that it is not necessary that Remote Desktop Services be running for this
        ///     function to succeed.
        /// </summary>
        /// <returns>
        ///     The session identifier of the session that is attached to the physical console. If there is no session
        ///     attached to the physical console, (for example, if the physical console session is in the process of being attached
        ///     or detached), this function returns 0xFFFFFFFF.
        /// </returns>
        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        /// <summary>
        ///     Closes an open object handle.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        ///     The DuplicateTokenEx function creates a new access token that duplicates an existing token. This function can
        ///     create either a primary token or an impersonation token.
        /// </summary>
        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existingToken,
            uint desiredAccess,
            [In] [Out] SecurityAttributes? tokenAttributes,
            SECURITY_IMPERSONATION_LEVEL impersonationLevel,
            TokenType tokenType,
            out SafeAccessTokenHandle duplicateTokenHandle);

        /// <summary>
        ///     Creates a new process and its primary thread. The new process runs in the security context of the user represented
        ///     by the specified token.
        /// </summary>
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(SafeAccessTokenHandle token,
            string applicationName,
            string commandLine,
            [In] SecurityAttributes? processAttributes,
            [In] SecurityAttributes? threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            [In] ref StartupInfo startupInfo,
            [Out] out ProcessInformation processInformation);


        /// <summary>
        ///     <para>
        ///         Loads the specified user's profile. The profile can be a local user profile or a roaming user profile.
        ///     </para>
        ///     <para>
        ///         From: https://docs.microsoft.com/zh-cn/windows/win32/api/userenv/nf-userenv-loaduserprofilew
        ///     </para>
        /// </summary>
        /// <param name="hToken">
        ///     Token for the user, which is returned by the <see cref="T:LogonUser" />, <see cref="T:CreateRestrictedToken" />
        ///     ,
        ///     <see cref="T:DuplicateToken" />, <see cref="T:OpenProcessToken" />, or <see cref="T:OpenThreadToken" />
        ///     function.
        ///     The token must have <see cref="T:TOKEN_QUERY" />, <see cref="T:TOKEN_IMPERSONATE" />, and
        ///     <see cref="T:TOKEN_DUPLICATE" /> access.
        ///     For more information, see Access Rights for Access-Token Objects.
        /// </param>
        /// <param name="lpProfileInfo">
        ///     Pointer to a <see cref="ProfileInfo" /> structure.
        ///     <see cref="LoadUserProfile" /> fails and returns <see cref="T:ERROR_INVALID_PARAMETER" />
        ///     if the <see cref="ProfileInfo.Size" /> member of the structure is not set to <code>sizeof(ProfileInfo)</code>
        ///     or
        ///     if the <see cref="ProfileInfo.UserName" /> member is <see cref="T:null" />.
        ///     For more information, see Remarks.
        /// </param>
        /// <returns>
        ///     <see cref="T:bool" /> if successful; otherwise, <see cref="T:false" />. To get extended error information, call
        ///     <see cref="T:GetLastError" />.
        ///     The function fails and returns <see cref="T:ERROR_INVALID_PARAMETER" /> if the
        ///     <see cref="ProfileInfo.Size" />
        ///     member
        ///     of the structure at <paramref name="lpProfileInfo" /> is not set to <code>sizeof(ProfileInfo)</code> or
        ///     if the <see cref="ProfileInfo.UserName" /> member is <see cref="T:null" />.
        /// </returns>
        /// <remarks>
        ///     When a user logs on interactively, the system automatically loads the user's profile.
        ///     If a service or an application impersonates a user, the system does not load the user's profile.
        ///     Therefore, the service or application should load the user's profile with <see cref="T:LoadUserProfile" />.
        ///     Services and applications that call <see cref="T:LoadUserProfile" /> should check to see if the user has a
        ///     roaming
        ///     profile.
        ///     If the user has a roaming profile, specify its path as the <see cref="T:PROFILEINFO.lpProfilePath" /> member of
        ///     <see cref="T:PROFILEINFO" />.
        ///     To retrieve the user's roaming profile path, you can call the <see cref="T:NetUserGetInfo" /> function,
        ///     specifying
        ///     information level 3 or 4.
        ///     Upon successful return, the <see cref="T:PROFILEINFO.hProfile" /> member of <see cref="T:PROFILEINFO" /> is
        ///     a registry key handle opened to the root of the user's hive.
        ///     It has been opened with full access (<see cref="T:KEY_ALL_ACCESS" />).
        ///     If a service that is impersonating a user needs to read or write to the user's registry file,
        ///     use this handle instead of <see cref="T:HKEY_CURRENT_USER" />. Do not close the
        ///     <see cref="T:PROFILEINFO.hProfile" /> handle.
        ///     Instead, pass it to the <see cref="T:UnloadUserProfile" /> function. This function closes the handle.
        ///     You should ensure that all handles to keys in the user's registry hive are closed.
        ///     If you do not close all open registry handles, the user's profile fails to unload.
        ///     For more information, see Registry Key Security and Access Rights and Registry Hives.
        ///     Note that it is your responsibility to load the user's registry hive into the <see cref="T:HKEY_USERS" />
        ///     registry
        ///     key
        ///     with the <see cref="T:LoadUserProfile" /> function before you call <see cref="T:CreateProcessAsUser" />.
        ///     This is because <see cref="T:CreateProcessAsUser" /> does not load the specified user's profile into
        ///     <see cref="T:HKEY_USERS" />.
        ///     This means that access to information in the <see cref="T:HKEY_CURRENT_USER" /> registry key
        ///     may not produce results consistent with a normal interactive logon.
        ///     The calling process must have the SE_RESTORE_NAME and SE_BACKUP_NAME privileges.
        ///     For more information, see Running with Special Privileges.
        ///     Starting with Windows XP Service Pack 2 (SP2) and Windows Server 2003, the caller must be an administrator or the
        ///     LocalSystem account.
        ///     It is not sufficient for the caller to merely impersonate the administrator or LocalSystem account.
        /// </remarks>
        [DllImport("userenv.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadUserProfileW", ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LoadUserProfile([In] SafeAccessTokenHandle hToken, ref ProfileInfo lpProfileInfo);


        /// <summary>
        ///     <para>
        ///         Unloads a user's profile that was loaded by the <see cref="LoadUserProfile" /> function.
        ///         The caller must have administrative privileges on the computer.
        ///         For more information, see the Remarks section of the <see cref="LoadUserProfile" /> function.
        ///     </para>
        ///     <para>
        ///         From: https://docs.microsoft.com/zh-cn/windows/win32/api/userenv/nf-userenv-unloaduserprofile
        ///     </para>
        /// </summary>
        /// <param name="hToken">
        ///     Token for the user, returned from the <see cref="T:LogonUser" />, <see cref="T:CreateRestrictedToken" />,
        ///     <see cref="T:DuplicateToken" />,
        ///     <see cref="T:OpenProcessToken" />, or <see cref="T:OpenThreadToken" /> function.
        ///     The token must have <see cref="T:TOKEN_IMPERSONATE" /> and <see cref="T:TOKEN_DUPLICATE" /> access.
        ///     For more information, see Access Rights for Access-Token Objects.
        /// </param>
        /// <param name="hProfile">
        ///     Handle to the registry key. This value is the <see cref="T:PROFILEINFO.hProfile" /> member of the
        ///     <see cref="T:PROFILEINFO" /> structure.
        ///     For more information see the Remarks section of <see cref="T:LoadUserProfile" /> and Registry Key Security and
        ///     Access Rights.
        /// </param>
        /// <returns>
        ///     <see cref="T:TRUE" /> if successful; otherwise, <see cref="T:FALSE" />.
        ///     To get extended error information, call <see cref="T:GetLastError" />.
        /// </returns>
        /// <remarks>
        ///     Before calling UnloadUserProfile you should ensure that all handles to keys that you have opened in the user's
        ///     registry hive are closed.
        ///     If you do not close all open registry handles, the user's profile fails to unload.
        ///     For more information, see Registry Key Security and Access Rights and Registry Hives.
        ///     For more information about calling functions that require administrator privileges, see Running with Special
        ///     Privileges.
        /// </remarks>
        [DllImport("userenv.dll", CharSet = CharSet.Unicode, EntryPoint = "UnloadUserProfile", ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnloadUserProfile([In] SafeAccessTokenHandle hToken, [In] SafeRegistryHandle hProfile);


        /// <summary>
        ///     Retrieves the type of the currently loaded user profile
        /// </summary>
        /// <param name="profileType">Pointer to a variable that receives the profile type</param>
        /// <returns></returns>
        [DllImport("userenv.dll", CharSet = CharSet.Unicode, EntryPoint = "GetProfileType", ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProfileType([Out] out ProfileType profileType);


        [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserProfileDirectory(IntPtr hToken, StringBuilder path, ref int dwSize);

        /// <summary>
        ///     The NetApiBufferFree function frees the memory that the NetApiBufferAllocate function allocates. Applications
        ///     should also call NetApiBufferFree to free the memory that other network management functions use internally to
        ///     return information.
        /// </summary>
        /// <param name="buffer">
        ///     A pointer to a buffer returned previously by another network management function or memory
        ///     allocated by calling the NetApiBufferAllocate function.
        /// </param>
        /// <returns>If the function succeeds, the return value is NERR_Success.</returns>
        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);


        [DllImport("Wtsapi32.dll", EntryPoint = "WTSQuerySessionInformation", SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out uint pBytesReturned);

        /// <summary>
        ///     retrieves information about a particular user account on a server.
        /// </summary>
        /// <param name="serverName">
        ///     A a constant string that specifies the DNS or NetBIOS name of the remote server on which the
        ///     function is to execute. If this parameter is NULL, the local computer is used.
        /// </param>
        /// <param name="username"> A constant string that specifies the name of the user account for which to return information.</param>
        /// <param name="level">The information level of the data.</param>
        /// <param name="buffer">
        ///     A pointer to the buffer that receives the data. The format of this data depends on the value of
        ///     the level parameter. This buffer is allocated by the system and must be freed using the NetApiBufferFree function.
        /// </param>
        /// <returns>If the function succeeds, the return value is NERR_Success.</returns>
        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "NetUserGetInfo", ExactSpelling = true,
            SetLastError = true)]
        private static extern NetApiStatus NetUserGetInfo([In][MarshalAs(UnmanagedType.LPWStr)] string serverName,
            [In][MarshalAs(UnmanagedType.LPWStr)] string username, int level, [Out] out IntPtr buffer);


        #endregion


        #region Consts

        /// <summary>
        ///     When an interactive user logs on, the system associates the interactive window station with the user logon session.
        ///     The system also creates the default input desktop for the interactive window station (Winsta0\default).
        /// </summary>
        private const string InteractiveWindowStation = "winsta0\\default";

        private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const int CREATE_NO_WINDOW = 0x08000000;

        private const int CREATE_NEW_CONSOLE = 0x00000010;
        private const int DETACHED_PROCESS = 0x00000008;
        private const uint MAXIMUM_ALLOWED = 0x2000000;
        private const uint INVALID_SESSION_ID = 0xFFFFFFFF;
        private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
        private const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;
        private const int TOKEN_ASSIGN_PRIMARY = 0x0001;
        private const int TOKEN_DUPLICATE = 0x0002;
        private const int TOKEN_IMPERSONATE = 0x0004;
        private const int TOKEN_QUERY = 0x0008;
        private const int TOKEN_QUERY_SOURCE = 0x0010;
        private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const int TOKEN_ADJUST_GROUPS = 0x0040;
        private const int TOKEN_ADJUST_DEFAULT = 0x0080;
        private const int TOKEN_ADJUST_SESSIONID = 0x0100;

        private const int TOKEN_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE | TOKEN_QUERY | TOKEN_QUERY_SOURCE | TOKEN_ADJUST_PRIVILEGES | TOKEN_ADJUST_GROUPS |
            TOKEN_ADJUST_DEFAULT;

    #endregion

    #region Enums

        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous = 0,
            SecurityIdentification = 1,
            SecurityImpersonation = 2,
            SecurityDelegation = 3
        }
        private enum WTS_INFO_CLASS
        {
            WTSInitialProgram,
            WTSApplicationName,
            WTSWorkingDirectory,
            WTSOEMId,
            WTSSessionId,
            WTSUserName,
            WTSWinStationName,
            WTSDomainName,
            WTSConnectState,
            WTSClientBuildNumber,
            WTSClientName,
            WTSClientDirectory,
            WTSClientProductId,
            WTSClientHardwareId,
            WTSClientAddress,
            WTSClientDisplay,
            WTSClientProtocolType
        }
        private enum SW
        {
            SW_HIDE = 0,
            SW_SHOWNORMAL = 1,
            SW_NORMAL = 1,
            SW_SHOWMINIMIZED = 2,
            SW_SHOWMAXIMIZED = 3,
            SW_MAXIMIZE = 3,
            SW_SHOWNOACTIVATE = 4,
            SW_SHOW = 5,
            SW_MINIMIZE = 6,
            SW_SHOWMINNOACTIVE = 7,
            SW_SHOWNA = 8,
            SW_RESTORE = 9,
            SW_SHOWDEFAULT = 10,
            SW_MAX = 10
        }

        private enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }
        /// <summary>
        ///     Lmcons.h
        ///     #define NET_API_STATUS DWORD
        /// </summary>
        private enum NetApiStatus : uint
        {
            Success = 0,

            /// <summary>
            ///     This computer name is invalid.
            /// </summary>
            InvalidComputer = 2351,

            /// <summary>
            ///     This operation is only allowed on the primary domain controller of the domain.
            /// </summary>
            NotPrimary = 2226,

            /// <summary>
            ///     This operation is not allowed on this special group.
            /// </summary>
            SpeGroupOp = 2234,

            /// <summary>
            ///     This operation is not allowed on the last administrative account.
            /// </summary>
            LastAdmin = 2452,

            /// <summary>
            ///     The password parameter is invalid.
            /// </summary>
            BadPassword = 2203,

            /// <summary>
            ///     The password does not meet the password policy requirements.
            ///     Check the minimum password length, password complexity and password history requirements.
            /// </summary>
            PasswordTooShort = 2245,

            /// <summary>
            ///     The user name could not be found.
            /// </summary>
            UserNotFound = 2221,
            AccessDenied = 5,
            NotEnoughMemory = 8,
            InvalidParameter = 87,
            InvalidName = 123,
            InvalidLevel = 124,
            MoreData = 234,
            SessionCredentialConflict = 1219,

            /// <summary>
            ///     The RPC server is not available. This error is returned if a remote computer was specified in
            ///     the lpServer parameter and the RPC server is not available.
            /// </summary>
            ServerUnavailable = 2147944122, // 0x800706BA

            /// <summary>
            ///     Remote calls are not allowed for this process. This error is returned if a remote computer was
            ///     specified in the lpServer parameter and remote calls are not allowed for this process.
            /// </summary>
            RemoteDisabled = 2147549468 // 0x8001011C
        }

        private enum TokenType
        {
            Primary = 1,
            Impersonation = 2
        }

        /// <summary>
        ///     <see cref="ProfileInfo" /> Flags
        /// </summary>
        private enum ProfileInfoFlags : uint
        {
            /// <summary>
            ///     Prevents the display of profile error messages.
            /// </summary>
            NoUI = 0x00000001,

            /// <summary>
            ///     Not supported.
            /// </summary>
            ApplyPolicy = 0x00000002
        }

        /// <summary>
        ///     Profile type flags.
        /// </summary>
        [Flags]
        private enum ProfileType : uint
        {
            /// <summary>
            ///     The user has a Temporary User Profiles; it will be deleted at logoff.
            /// </summary>
            Temporary = 0x00000001,

            /// <summary>
            ///     The user has a Roaming User Profiles.
            /// </summary>
            Roaming = 0x00000002,

            /// <summary>
            ///     The user has a Mandatory User Profiles.
            /// </summary>
            Mandatory = 0x00000004,

            /// <summary>
            ///     The user has a Roaming User Profile that was created on another PC and is being downloaded. This profile type
            ///     implies <c>Roaming</c>.
            /// </summary>
            RoamingPreexisting = 0x00000008
        }


        #endregion


        #region Structs

        /// <summary>
        ///     <para>
        ///         Contains information used when loading or unloading a user profile.
        ///     </para>
        ///     <para>
        ///         From: https://docs.microsoft.com/zh-cn/windows/win32/api/profinfo/ns-profinfo-profileinfow
        ///     </para>
        /// </summary>
        /// <remarks>
        ///     Do not use environment variables when specifying a path.
        ///     The <see cref="T:LoadUserProfile" /> function does not expand environment variables, such as %username%, in a path.
        ///     When the <see cref="T:LoadUserProfile" /> call returns successfully, the <see cref="T:hProfile" /> member receives
        ///     a registry key handle
        ///     opened to the root of the user's subtree, opened with full access (<see cref="T:KEY_ALL_ACCESS" />).
        ///     For more information see the Remarks sections in <see cref="T:LoadUserProfile" />, Registry Key Security and Access
        ///     Rights, and Registry Hives.
        ///     Services and applications that call <see cref="T:LoadUserProfile" /> should check to see if the user has a roaming
        ///     profile.
        ///     If the user has a roaming profile, specify its path as the <see cref="T:lpProfilePath" /> member of this structure.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProfileInfo
        {
            /// <summary>
            ///     The size of this structure, in bytes.
            /// </summary>
            public uint Size;

            /// <summary>
            ///     This member can be one of the following flags:
            ///     <see cref="ProfileInfoFlags.NoUI" />: Prevents the display of profile error messages.
            ///     <see cref="ProfileInfoFlags.ApplyPolicy" />: Not supported.
            /// </summary>
            public ProfileInfoFlags Flags;

            /// <summary>
            ///     A pointer to the name of the user.
            ///     This member is used as the base name of the directory in which to store a new profile.
            /// </summary>
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;

            /// <summary>
            ///     A pointer to the roaming user profile path.
            ///     If the user does not have a roaming profile, this member can be <see langword="null" />.
            ///     To retrieve the user's roaming profile path, call the <see cref="T:NetUserGetInfo" /> function, specifying
            ///     information level 3 or 4.
            ///     For more information, see Remarks.
            /// </summary>
            [MarshalAs(UnmanagedType.LPWStr)] public string ProfilePath;

            /// <summary>
            ///     A pointer to the default user profile path. This member can be <see langword="null" />.
            /// </summary>
            [MarshalAs(UnmanagedType.LPWStr)] public string DefaultPath;

            /// <summary>
            ///     A pointer to the name of the validating domain controller, in NetBIOS format.
            /// </summary>
            [MarshalAs(UnmanagedType.LPWStr)] public string ServerName;

            /// <summary>
            ///     Not used, set to <see langword="null" />.
            /// </summary>
            [MarshalAs(UnmanagedType.LPWStr)] public string PolicyPath;

            /// <summary>
            ///     A handle to the <see cref="T:HKEY_CURRENT_USER" /> registry subtree.
            ///     For more information, see Remarks.
            /// </summary>
            public IntPtr Profile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private class SecurityAttributes
        {
            public int Length;
            private IntPtr SecurityDescriptor;
            private bool InheritHandle;
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
			public string lpReserved;
			public string lpDesktop;
			public string lpTitle;
			public int dwX;
			public int dwY;
			public int dwXSize;
			public int dwYSize;
			public int dwXCountChars;
			public int dwYCountChars;
			public int dwFillAttribute;
			public int dwFlags;
			public Int16 wShowWindow;
			public Int16 cbReserved2;
			public IntPtr lpReserved2;
			public IntPtr hStdInput;
			public IntPtr hStdOutput;
			public IntPtr hStdError;
        }

        /// <summary>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct UserInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_name;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_password;
            public uint usri4_password_age;
            public uint usri4_priv;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_home_dir;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_comment;
            public uint usri4_flags;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_script_path;
            public uint usri4_auth_flags;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_full_name;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_usr_comment;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_parms;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_workstations;
            public uint usri4_last_logon;
            public uint usri4_last_logoff;
            public uint usri4_acct_expires;
            public uint usri4_max_storage;
            public uint usri4_units_per_week;
            public IntPtr usri4_logon_hours;
            public uint usri4_bad_pw_count;
            public uint usri4_num_logons;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_logon_server;
            public uint usri4_country_code;
            public uint usri4_code_page;
            public IntPtr usri4_user_sid;
            public uint usri4_primary_group_id;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_profile;
            [MarshalAs(UnmanagedType.LPWStr)] public string usri4_home_dir_drive;
            public uint usri4_password_expired;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public readonly IntPtr ProcessHandle;
            public readonly IntPtr ThreadHandle;
            public readonly int ProcessId;
            private readonly int ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public readonly uint SessionID;

            [MarshalAs(UnmanagedType.LPStr)]
            private readonly string pWinStationName;

            public readonly WTS_CONNECTSTATE_CLASS State;
        }

    #endregion
    }
}