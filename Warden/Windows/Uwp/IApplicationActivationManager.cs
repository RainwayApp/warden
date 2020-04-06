using System;
using System.Runtime.InteropServices;

namespace Warden.Windows.Uwp
{
    /// <summary>
    /// Provides methods which activate Windows Store apps for the Launch, File, and Protocol extensions. You would normally use this interface in debuggers and design tools.
    /// </summary>
    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        /// <summary>
        /// Activates the specified Windows Store app for the generic launch contract (Windows.Launch) in the current session.
        /// </summary>
        /// <param name="appUserModelId">The application user model ID of the Windows Store app.</param>
        /// <param name="arguments">app-specific, argument string.</param>
        /// <param name="options">flags used to support design mode, debugging, and testing scenarios.</param>
        /// <param name="processId">the launched process ID.</param>
        /// <returns>when this method returns successfully, receives the process ID of the app instance that fulfills this contract.</returns>
        IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments, [In] ActivateOptionsEnum options, [Out] out uint processId);
        /// <summary>
        /// Activates the specified Windows Store app for the file contract (Windows.File).
        /// </summary>
        /// <param name="appUserModelId">The application user model ID of the Windows Store app.</param>
        /// <param name="itemArray">A pointer to an array of Shell items, each representing a file. This value is converted to a VectorView of StorageItem objects that is passed to the app through FileActivatedEventArgs.</param>
        /// <param name="verb">The verb being applied to the file or files specified by itemArray.</param>
        /// <param name="processId">the launched process ID.</param>
        /// <returns>when this method returns successfully, receives the process ID of the app instance that fulfills this contract.</returns>
        IntPtr ActivateForFile([In] string appUserModelId, [In] IntPtr itemArray, [In] string verb, [Out] out uint processId);
        /// <summary>
        /// Activates the specified Windows Store app for the protocol contract (Windows.Protocol).
        /// </summary>
        /// <param name="appUserModelId">The application user model ID of the Windows Store app.</param>
        /// <param name="itemArray">A pointer to an array of a single Shell item. The first item in the array is converted into a Uri object that is passed to the app through ProtocolActivatedEventArgs. Any items in the array except for the first element are ignored.</param>
        /// <param name="processId">the launched process ID.</param>
        /// <returns>when this method returns successfully, receives the process ID of the app instance that fulfills this contract.</returns>
        IntPtr ActivateForProtocol([In] string appUserModelId, [In] IntPtr itemArray, [Out] out uint processId);
    }
}
