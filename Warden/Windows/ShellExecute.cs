using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Warden.Core;

namespace Warden.Windows
{
    internal static class ShellExecute
    {

        /// <summary>
        ///   Starts a process using the operating system shell.
        /// </summary>
        /// <param name="startInfo">
        ///     The <see cref="WardenStartInfo"/> that contains the information that is used to start the process,
        ///     including the file name and any command-line arguments.
        /// </param>
        public static void Start(WardenStartInfo startInfo)
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
                throw new Exception("This operation is not available in this environment.");
            }

            try
            {
                shellWindows = Activator.CreateInstance(shellWindowsType);

                desktopWindow = ((IShellWindows) shellWindows).FindWindowSW(ref emptyObject,
                    ref emptyObject,
                    ShellWindowsClass.Desktop,
                    out _,
                    ShellWindowsFindOptions.NeedDispatch);

                ((IServiceProvider) desktopWindow).QueryService(TopLevelBrowser,
                    typeof(IShellBrowser).GUID,
                    out desktopBrowser);

                ((IShellBrowser) desktopBrowser).QueryActiveShellView(out desktopView);

                ((IShellView) desktopView).GetItemObject(ShellViewGetItemObject.Background,
                    typeof(IDispatch).GUID,
                    out backgroundFolderView);

                applicationDispatch = ((IShellFolderViewDual) backgroundFolderView).Application;

                var showFlags = new object();

                switch (startInfo.WindowStyle)
                {
                    case WindowStyle.Normal:
                        showFlags = ShellDispatchExecuteShowFlags.Normal;

                        break;
                    case WindowStyle.Hidden:
                        showFlags = ShellDispatchExecuteShowFlags.Hidden;

                        break;
                    case WindowStyle.Minimized:
                        showFlags = ShellDispatchExecuteShowFlags.Minimized;

                        break;
                    case WindowStyle.Maximized:
                        showFlags = ShellDispatchExecuteShowFlags.Maximized;
                        break;
                }

                ((IShellDispatch2) applicationDispatch).ShellExecute(startInfo.FileName,
                    startInfo.Arguments,
                    startInfo.WorkingDirectory,
                    startInfo.RaisePrivileges ? "runas" : "open",
                    showFlags);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to start application.", e);
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


    #region Structs

        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellBrowser
        {
            void _VtblGap0_12(); // Skip 12 members.

            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object shellView);
        }

        [ComImport]
        [Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
        internal interface IShellDispatch2
        {
            void _VtblGap0_24(); // Skip 24 members.

            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            void ShellExecute([MarshalAs(UnmanagedType.BStr)] [In] string file,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object arguments,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object workingDirectory,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object verb,
                [MarshalAs(UnmanagedType.Struct)] [In] [Optional]
                object showFlags);
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
            object FindWindowSW([MarshalAs(UnmanagedType.Struct)] [In] ref object locationPIDL,
                [MarshalAs(UnmanagedType.Struct)] [In] ref object locationRootPIDL,
                [In] ShellWindowsClass windowClass,
                out int windowHandle,
                [In] ShellWindowsFindOptions options);
        }


        [ComImport]
        [Guid("000214E3-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellView
        {
            void _VtblGap0_12(); // Skip 12 members.

            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            int GetItemObject(ShellViewGetItemObject item,
                [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
                [MarshalAs(UnmanagedType.IUnknown)] out object itemObject);
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
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IServiceProvider
        {
            [PreserveSig]
            [MethodImpl(MethodImplOptions.InternalCall)]
            int QueryService([MarshalAs(UnmanagedType.LPStruct)] Guid serviceId,
                [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
                [MarshalAs(UnmanagedType.IUnknown)] out object serviceObject);
        }

    #endregion

    #region Consts

        private static readonly Guid ShellWindowsServer = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

        private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

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
            ///     Open the application with a hidden window.
            /// </summary>
            Hidden = 0,

            /// <summary>
            ///     Open the application with a normal window. If the window is minimized or maximized,
            ///     the system restores it to its original size and position.
            /// </summary>
            Normal = 1,

            /// <summary>
            ///     Open the application with a minimized window.
            /// </summary>
            Minimized = 2,

            /// <summary>
            ///     Open the application with a maximized window.
            /// </summary>
            Maximized = 3
        }

    #endregion
    }
}