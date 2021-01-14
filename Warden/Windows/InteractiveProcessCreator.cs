using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
                
                const int creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE;
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

                // copy the users env block
                if (!CreateEnvironmentBlock(ref environmentBlockHandle, interactiveUserToken, false))
                {
                    throw new Win32Exception("CreateEnvironmentBlock failed.");
                }
                

                const int creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE;
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

        private enum TokenType
        {
            Primary = 1,
            Impersonation = 2
        }

    #endregion


    #region Structs

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