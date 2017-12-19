using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Windows
{
    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IApplicationActivationManager
    {
        IntPtr ActivateApplication([In] String appUserModelId, [In] String arguments, [In] ActivateOptionsEnum options, [Out] out UInt32 processId);

        IntPtr ActivateForFile([In] String appUserModelId, [In] IntPtr itemArray, [In] String verb, [Out] out UInt32 processId);

        IntPtr ActivateForProtocol([In] String appUserModelId, [In] IntPtr itemArray, [Out] out UInt32 processId);
    }
}
