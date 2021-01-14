using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Warden.Windows
{
    /// <summary>
    /// Provides a managed wait handle for a Windows process.
    /// </summary>
    internal class ProcessWaitHandle : WaitHandle
    {
        /// <summary>
        /// Ignores the dwDesiredAccess parameter. The duplicate handle has the same access as the source handle.
        /// </summary>
        private const int DuplicateSameAccess = 2;

        /// <summary>
        /// Duplicates an object handle.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
        private static extern bool DuplicateHandle(HandleRef hSourceProcessHandle,
            SafeHandle hSourceHandle,
            HandleRef hTargetProcess,
            out SafeWaitHandle targetHandle,
            int dwDesiredAccess,
            bool bInheritHandle,
            int dwOptions);

        /// <summary>
        /// Retrieves a pseudo handle for the current process.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// Duplicates a process handle.
        /// </summary>
        /// <param name="processHandle">The handle to duplicate.</param>
        internal ProcessWaitHandle(SafeProcessHandle processHandle)
        {
            if (!DuplicateHandle(new HandleRef(this, GetCurrentProcess()),
                processHandle,
                new HandleRef(this, GetCurrentProcess()),
                out var waitHandle,
                0,
                false,
                DuplicateSameAccess))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
            SafeWaitHandle = waitHandle;
        }
    }
}
