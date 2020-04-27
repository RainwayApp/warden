using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using Warden.Core.Exceptions;
using Warden.Windows.Win32;

namespace Warden.Windows
{
    internal static class Api
    {
        /// <summary>
        ///     Starts process using ShellExecute so it launches with the lowest privileges or resolves URIs 
        /// </summary>
        /// <param name="startInfo">Contains the information about the <see cref="Process" /> to be started</param>
        public static void StartByShell(ShellStartInfo startInfo)
        {
            var emptyObject = new object();
            object shellWindows = null;
            object desktopWindow = null;
            object desktopBrowser = null;
            object desktopView = null;
            object backgroundFolderView = null;
            object applicationDispatch = null;

            var shellWindowsType = Type.GetTypeFromCLSID(ShellWindowsServer, false);

            if (shellWindowsType == null)
            {
                throw new WardenLaunchException("This operation is not available in this environment.");
            }

            try
            {
                shellWindows = Activator.CreateInstance(shellWindowsType);

                desktopWindow = ((IShellWindows)shellWindows).FindWindowSW(
                    ref emptyObject,
                    ref emptyObject,
                    ShellWindowsClass.Desktop,
                    out _,
                    ShellWindowsFindOptions.NeedDispatch
                );

                ((IServiceProvider)desktopWindow).QueryService(
                    TopLevelBrowser,
                    typeof(IShellBrowser).GUID,
                    out desktopBrowser
                );

                ((IShellBrowser)desktopBrowser).QueryActiveShellView(out desktopView);

                ((IShellView)desktopView).GetItemObject(
                    ShellViewGetItemObject.Background,
                    typeof(IDispatch).GUID,
                    out backgroundFolderView
                );

                applicationDispatch = ((IShellFolderViewDual)backgroundFolderView).Application;

                var showFlags = new object();

                switch (startInfo.WindowStyle)
                {
                    case ProcessWindowStyle.Normal:
                        showFlags = ShellDispatchExecuteShowFlags.Normal;

                        break;
                    case ProcessWindowStyle.Hidden:
                        showFlags = ShellDispatchExecuteShowFlags.Hidden;

                        break;
                    case ProcessWindowStyle.Minimized:
                        showFlags = ShellDispatchExecuteShowFlags.Minimized;

                        break;
                    case ProcessWindowStyle.Maximized:
                        showFlags = ShellDispatchExecuteShowFlags.Maximized;

                        break;
                }

                ((IShellDispatch2)applicationDispatch).ShellExecute(
                    startInfo.Address,
                    startInfo.Arguments,
                    startInfo.WorkingDirectory,
                    startInfo.Verb ?? emptyObject,
                    showFlags
                );
            }
            catch (Exception e)
            {
                throw new WardenLaunchException("Failed to start application.", e);
            }
            finally
            {
                if (applicationDispatch != null)
                {
                    Marshal.ReleaseComObject(applicationDispatch);
                }

                if (backgroundFolderView != null)
                {
                    Marshal.ReleaseComObject(backgroundFolderView);
                }

                if (desktopView != null)
                {
                    Marshal.ReleaseComObject(desktopView);
                }

                if (desktopBrowser != null)
                {
                    Marshal.ReleaseComObject(desktopBrowser);
                }

                if (desktopWindow != null)
                {
                    Marshal.ReleaseComObject(desktopWindow);
                }

                if (shellWindows != null)
                {
                    Marshal.ReleaseComObject(shellWindows);
                }
            }
        }

        /// <summary>
        /// Gets the session handle for the currently active user
        /// </summary>
        /// <param name="activeSessionId"></param>
        /// <param name="phUserToken"></param>
        /// <returns></returns>
        private static bool GetSessionUserToken(ref uint activeSessionId, ref IntPtr phUserToken)
        {
            var bResult = false;
            var hImpersonationToken = IntPtr.Zero;
            var pSessionInfo = IntPtr.Zero;
            var sessionCount = 0;

            if (WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, ref pSessionInfo, ref sessionCount) != 0)
            {
                var arrayElementSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                var current = pSessionInfo;

                for (var i = 0; i < sessionCount; i++)
                {
                    var si = (WTS_SESSION_INFO)Marshal.PtrToStructure(current, typeof(WTS_SESSION_INFO));
                    current += arrayElementSize;

                    if (si.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                    {
                        activeSessionId = si.SessionID;
                    }
                }
            }

            // Enumeration might not workout, so we use our old friend.
            if (activeSessionId == INVALID_SESSION_ID)
            {
                activeSessionId = WTSGetActiveConsoleSessionId();
            }

            if (WTSQueryUserToken(activeSessionId, ref hImpersonationToken) != 0)
            {
                // Convert the impersonation token to a primary token
                bResult = DuplicateTokenEx(hImpersonationToken, 0, IntPtr.Zero,
                    (int)SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, (int)TOKEN_TYPE.TokenPrimary,
                    ref phUserToken);

                CloseHandle(hImpersonationToken);
            }

            return bResult;
        }

        /// <summary>
        /// Finds the WinLogon process ID for the active session
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        private static int GetWinLogonId(ref uint sessionId)
        {
            foreach (var p in Process.GetProcessesByName("winlogon"))
            {
                try
                {
                    if (p.SessionId != sessionId) continue;
                    return p.Id;
                }
                catch
                {
                    // Ignore forbidden processes so we can get a list of processes we do have access to
                }
            }
            return 0;
        }

        /// <summary>
        /// Starts a process as the current user, fails if that process needs administrator.
        /// </summary>
        /// <param name="appPath"></param>
        /// <param name="arguments"></param>
        /// <param name="workingDir"></param>
        /// <param name="processId"></param>
        /// <returns></returns>
        internal static bool StartProcessAsUser(string appPath, string arguments, string workingDir, out int processId)
        {

            var hUserToken = IntPtr.Zero;
            var startInfo = new STARTUPINFO();
            var procInfo = new PROCESS_INFORMATION();
            var pEnv = IntPtr.Zero;
            var activeSessionId = INVALID_SESSION_ID;
            startInfo.cb = Marshal.SizeOf(typeof(STARTUPINFO));

            try
            {
                if (!GetSessionUserToken(ref activeSessionId, ref hUserToken))
                {
                    throw new WardenLaunchException("GetSessionUserToken failed.");
                }

                // By default CreateProcessAsUser creates a process on a non-interactive window station, meaning
                // the window station has a desktop that is invisible and the process is incapable of receiving
                // user input. To remedy this we set the lpDesktop parameter to indicate we want to enable user 
                // interaction with the new process.
                const uint dwCreationFlags = CREATE_UNICODE_ENVIRONMENT | (uint)CREATE_NEW_CONSOLE;
                startInfo.wShowWindow = (short)(SW.SW_SHOW);
                startInfo.lpDesktop = "winsta0\\default";

                // copy the users env block
                if (!CreateEnvironmentBlock(ref pEnv, hUserToken, false))
                {
                    throw new WardenLaunchException("CreateEnvironmentBlock failed.");
                }

                // create a new process in the current user's logon session
                if (!CreateProcessAsUser(hUserToken,
                    appPath, // file to execute
                    arguments,  // command line
                    IntPtr.Zero, // pointer to process SECURITY_ATTRIBUTES
                    IntPtr.Zero, // pointer to thread SECURITY_ATTRIBUTES
                    false, // handles are not inheritable
                    dwCreationFlags, // creation flags
                    pEnv, // pointer to new environment block 
                    workingDir, // name of current directory 
                    ref startInfo,  // pointer to STARTUPINFO structure
                    out procInfo)) // receives information about new process
                {
                    var iResultOfCreateProcessAsUser = Marshal.GetLastWin32Error();
                    throw new WardenLaunchException(
                        $"CreateProcessAsUser failed.  Error Code -{iResultOfCreateProcessAsUser}");
                }
                processId = (int)procInfo.dwProcessId;
                Marshal.GetLastWin32Error();
            }
            finally
            {
                CloseHandle(hUserToken);
                if (pEnv != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(pEnv);
                }
                CloseHandle(procInfo.hThread);
                CloseHandle(procInfo.hProcess);
            }
            return true;
        }

        /// <summary>
        ///     Launches the given application with full admin rights, and in addition bypasses the Vista UAC prompt.
        ///     You should only call this for applications you trust.
        /// </summary>
        /// <param name="appPath">The fully qualified path of the application you want to launch</param>
        /// <param name="arguments"></param>
        /// <param name="workingDir"></param>
        /// <param name="processId">the ID of the executed process (if any)</param>
        /// <returns></returns>
        internal static bool StartProcessAsPrivilegedUser(string appPath, string arguments, string workingDir, out int processId)
        {

            var activeSessionId = INVALID_SESSION_ID;
            var hUserToken = IntPtr.Zero;
            var startInfo = new STARTUPINFO();
            var procInfo = new PROCESS_INFORMATION();
            var pEnv = IntPtr.Zero;
            IntPtr explorerToken = IntPtr.Zero, hPToken = IntPtr.Zero, hProcess = IntPtr.Zero;
            startInfo.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            try
            {
                if (!GetSessionUserToken(ref activeSessionId, ref hUserToken))
                {
                    throw new WardenLaunchException("GetSessionUserToken failed.");
                }


                // By default CreateProcessAsUser creates a process on a non-interactive window station, meaning
                // the window station has a desktop that is invisible and the process is incapable of receiving
                // user input. To remedy this we set the lpDesktop parameter to indicate we want to enable user 
                // interaction with the new process.
                const uint dwCreationFlags = CREATE_UNICODE_ENVIRONMENT | (uint)CREATE_NEW_CONSOLE;
                startInfo.wShowWindow = (short)(SW.SW_SHOW);
                startInfo.lpDesktop = "winsta0\\default";

                // copy the users env block
                if (!CreateEnvironmentBlock(ref pEnv, hUserToken, false))
                {
                    throw new WardenLaunchException("CreateEnvironmentBlock failed.");
                }

                var explorerId = GetWinLogonId(ref activeSessionId);
                if (explorerId == 0)
                {
                    throw new WardenLaunchException("Finding Explorer process ID failed.");
                }

                // obtain a handle to the winlogon process
                hProcess = OpenProcess(MAXIMUM_ALLOWED, false, (uint) explorerId);
                // obtain a handle to the access token of the winlogon process
                if (!OpenProcessToken(hProcess, TOKEN_ALL_ACCESS, ref hPToken))
                {
                    throw new WardenLaunchException("failed to open explorer process token.");
                }


                // Security attibute structure used in DuplicateTokenEx and CreateProcessAsUser
                // I would prefer to not have to use a security attribute variable and to just 
                // simply pass null and inherit (by default) the security attributes
                // of the existing token. However, in C# structures are value types and therefore
                // cannot be assigned the null value.
                var sa = new SECURITY_ATTRIBUTES();
                sa.Length = Marshal.SizeOf(sa);

                // copy the access token of the explorer process; the newly created token will be a primary token
                if (!DuplicateTokenEx(hPToken, MAXIMUM_ALLOWED, ref sa, (int)SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, (int)TOKEN_TYPE.TokenPrimary, ref explorerToken))
                {
                    throw new WardenLaunchException("Unable to duplicate security token");
                }

                // create a new process in the current user's logon session
                if (!CreateProcessAsUser(explorerToken,
                    appPath, // file to execute
                    arguments,  // command line
                    ref sa, // pointer to process SECURITY_ATTRIBUTES
                    ref sa, // pointer to thread SECURITY_ATTRIBUTES
                    false, // handles are not inheritable
                    (int) dwCreationFlags, // creation flags
                    pEnv, // pointer to new environment block 
                    workingDir, // name of current directory 
                    ref startInfo,  // pointer to STARTUPINFO structure
                    out procInfo)) // receives information about new process
                {
                    var iResultOfCreateProcessAsUser = Marshal.GetLastWin32Error();
                    throw new WardenLaunchException(
                        $"CreateProcessAsUser failed.  Error Code -{iResultOfCreateProcessAsUser}");
                }
                processId = (int) procInfo.dwProcessId;
                Marshal.GetLastWin32Error();
            }
            finally
            {
                CloseHandle(hProcess);
                CloseHandle(hPToken);
                CloseHandle(hUserToken);
                CloseHandle(explorerToken);
                if (pEnv != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(pEnv);
                }
                CloseHandle(procInfo.hThread);
                CloseHandle(procInfo.hProcess);
            }
            return true;
        }

    #region Imports


        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUser", SetLastError = true, CharSet = CharSet.Ansi,
            CallingConvention = CallingConvention.StdCall)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandle,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUser", SetLastError = true, CharSet = CharSet.Ansi,
            CallingConvention = CallingConvention.StdCall)]
        private static extern bool CreateProcessAsUser(IntPtr hToken, string lpApplicationName, string lpCommandLine,
            ref SECURITY_ATTRIBUTES lpProcessAttributes,
            ref SECURITY_ATTRIBUTES lpThreadAttributes, bool bInheritHandle, int dwCreationFlags, IntPtr lpEnvironment,
            string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx")]
        private static extern bool DuplicateTokenEx(
            IntPtr ExistingTokenHandle,
            uint dwDesiredAccess,
            IntPtr lpThreadAttributes,
            int TokenType,
            int ImpersonationLevel,
            ref IntPtr DuplicateTokenHandle);

        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx")]
        private static extern bool DuplicateTokenEx(IntPtr ExistingTokenHandle, uint dwDesiredAccess,
            ref SECURITY_ATTRIBUTES lpThreadAttributes, int TokenType,
            int ImpersonationLevel, ref IntPtr DuplicateTokenHandle);


        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("advapi32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, ref IntPtr TokenHandle);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(ref IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hSnapshot);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("Wtsapi32.dll")]
        private static extern uint WTSQueryUserToken(uint SessionId, ref IntPtr phToken);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern int WTSEnumerateSessions(
            IntPtr hServer,
            int Reserved,
            int Version,
            ref IntPtr ppSessionInfo,
            ref int pCount);

        #endregion

        #region Constants


            [DllImport("Kernel32")]
            internal static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

            // A delegate type to be used as the handler routine
            // for SetConsoleCtrlHandler.
            public delegate bool HandlerRoutine(CtrlTypes ctrlType);

            // An enumerated type for the control messages
            // sent to the handler routine.
            public enum CtrlTypes
            {
                CtrlCEvent = 0,
                CtrlBreakEvent,
                CtrlCloseEvent,
                CtrlLogoffEvent = 5,
                CtrlShutdownEvent
            }

        private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const int CREATE_NO_WINDOW = 0x08000000;

        private const int CREATE_NEW_CONSOLE = 0x00000010;
        public const uint MAXIMUM_ALLOWED = 0x2000000;
        private const uint INVALID_SESSION_ID = 0xFFFFFFFF;
        private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
        public const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;
        private static readonly Guid ShellWindowsServer = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private const int TOKEN_ASSIGN_PRIMARY = 0x0001;
        private const int TOKEN_DUPLICATE = 0x0002;
        private const int TOKEN_IMPERSONATE = 0x0004;
        private const int TOKEN_QUERY = 0x0008;
        private const int TOKEN_QUERY_SOURCE = 0x0010;
        private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const int TOKEN_ADJUST_GROUPS = 0x0040;
        private const int TOKEN_ADJUST_DEFAULT = 0x0080;
        private const int TOKEN_ADJUST_SESSIONID = 0x0100;
        private static readonly Guid TopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private const int TOKEN_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE | TOKEN_QUERY | TOKEN_QUERY_SOURCE | TOKEN_ADJUST_PRIVILEGES | TOKEN_ADJUST_GROUPS | TOKEN_ADJUST_DEFAULT;
        #endregion

        #region structs


            [StructLayout(LayoutKind.Sequential)]
            private struct SECURITY_ATTRIBUTES
            {
                public int Length;
                public IntPtr lpSecurityDescriptor;
                public bool bInheritHandle;
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous = 0,
            SecurityIdentification = 1,
            SecurityImpersonation = 2,
            SecurityDelegation = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public String lpReserved;
            public String lpDesktop;
            public String lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        private enum TOKEN_TYPE
        {
            TokenPrimary = 1,
            TokenImpersonation = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public readonly UInt32 SessionID;

            [MarshalAs(UnmanagedType.LPStr)]
            public readonly String pWinStationName;

            public readonly WTS_CONNECTSTATE_CLASS State;
        }


        [ComImport]
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IServiceProvider
        {
            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall)]
            int QueryService(
                [MarshalAs(UnmanagedType.LPStruct)] Guid serviceId,
                [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
                [MarshalAs(UnmanagedType.IUnknown)] out object serviceObject
            );
        }

        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellBrowser
        {
            void _VtblGap0_12(); // Skip 12 members.

            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            void QueryActiveShellView(
                [MarshalAs(UnmanagedType.IUnknown)] out object shellView
            );
        }

        [ComImport]
        [Guid("000214E3-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellView
        {
            void _VtblGap0_12(); // Skip 12 members.

            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            int GetItemObject(
                ShellViewGetItemObject item,
                [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
                [MarshalAs(UnmanagedType.IUnknown)] out object itemObject
            );
        }

        [ComImport]
        [Guid("00020400-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        internal interface IDispatch
        {

        }

        [ComImport]
        [Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
        internal interface IShellFolderViewDual
        {
            object Application
            {
                [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
                [return: MarshalAs(UnmanagedType.IDispatch)]
                get;
            }
        }

        [ComImport]
        [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
        private interface IShellWindows
        {
            // ReSharper disable once IdentifierTypo
            void _VtblGap0_8(); // Skip 8 members.

            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            [return: MarshalAs(UnmanagedType.IDispatch)]
            // ReSharper disable once TooManyArguments
            object FindWindowSW(
                [MarshalAs(UnmanagedType.Struct)] [In] ref object locationPIDL,
                [MarshalAs(UnmanagedType.Struct)] [In] ref object locationRootPIDL,
                [In] ShellWindowsClass windowClass,
                out int windowHandle,
                [In] ShellWindowsFindOptions options
            );
        }

        [ComImport]
        [Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
        internal interface IShellDispatch2
        {
            void _VtblGap0_24(); // Skip 24 members.

            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            void ShellExecute(
                [MarshalAs(UnmanagedType.BStr)] [In] string file,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object arguments,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object workingDirectory,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object verb,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object showFlags
            );
        }

        internal enum ShellWindowsFindOptions
        {
            NeedDispatch = 1
        }
        internal enum ShellWindowsClass
        {
            Desktop = 8
        }

        internal enum ShellViewGetItemObject
        {
            Background = 0
        }

        internal enum ShellDispatchExecuteShowFlags
        {
            /// <summary>
            /// Open the application with a hidden window.
            /// </summary>
            Hidden = 0,
            /// <summary>
            /// Open the application with a normal window. If the window is minimized or maximized,
            /// the system restores it to its original size and position.
            /// </summary>
            Normal = 1,
            /// <summary>
            /// Open the application with a minimized window.
            /// </summary>
            Minimized = 2,
            /// <summary>
            /// Open the application with a maximized window.
            /// </summary>
            Maximized = 3
        }
        #endregion
    }
}
