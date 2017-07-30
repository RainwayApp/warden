using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Warden.Core.Exceptions;

namespace Warden.Windows
{
    internal static class Api
    {
        public enum ActivateOptions
        {
            None = 0x00000000, // No flags set
            DesignMode = 0x00000001, // The application is being activated for design mode, and thus will not be able to

            // to create an immersive window. Window creation must be done by design tools which
            // load the necessary components by communicating with a designer-specified service on
            // the site chain established on the activation manager.  The splash screen normally
            // shown when an application is activated will also not appear.  Most activations
            // will not use this flag.
            NoErrorUi = 0x00000002, // Do not show an error dialog if the app fails to activate.
            NoSplashScreen = 0x00000004 // Do not show the splash screen when activating the app.
        }

        /// <summary>
        ///     Launch a UWP App using a ApplicationActivationManager and sets a internal id to launched proccess id
        /// </summary>
        /// <param name="packageFamilyName">The AUMID of the app to launch</param>
        /// <param name="pId"></param>
        public static Task<int> LaunchUwpApp(string packageFamilyName) // No async because the method does not need await
        {
            return Task.Run(() =>
            {
                var mgr = new ApplicationActivationManager();
                try
                {
                    uint processId;
                    mgr.ActivateApplication(packageFamilyName, null, ActivateOptions.None, out processId);
                    var pId = (int)processId;
                    return pId;
                }
                catch (Exception e)
                {
                    throw new WardenLaunchException($"Error while trying to launch your app: {e.Message}");
                }
            });
        }

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

        [ComImport]
        [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            // Activates the specified immersive application for the "Launch" contract, passing the provided arguments
            // string into the application.  Callers can obtain the process Id of the application instance fulfilling this contract.
            IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments, [In] ActivateOptions options,
                [Out] out uint processId);

            IntPtr ActivateForFile([In] string appUserModelId,
                [In] [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] /*IShellItemArray* */
                IShellItemArray itemArray, [In] string verb, [Out] out uint processId);

            IntPtr ActivateForProtocol([In] string appUserModelId, [In] IntPtr /* IShellItemArray* */itemArray,
                [Out] out uint processId);
        }

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] //Application Activation Manager
        private class ApplicationActivationManager : IApplicationActivationManager
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime) /*, PreserveSig*/]
            public extern IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments,
                [In] ActivateOptions options, [Out] out uint processId);

            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            public extern IntPtr ActivateForFile([In] string appUserModelId,
                [In] [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] /*IShellItemArray* */
                IShellItemArray itemArray, [In] string verb, [Out] out uint processId);

            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            public extern IntPtr ActivateForProtocol([In] string appUserModelId,
                [In] IntPtr /* IShellItemArray* */itemArray, [Out] out uint processId);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        private interface IShellItem
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        private interface IShellItemArray
        {
        }
    }
}