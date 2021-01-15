using System;
using Warden.Windows;

namespace Warden.Core
{
    /// <summary>
    ///     Extension methods that add extra functionality to <see cref="WardenProcess"/> instances.
    /// </summary>
    public static class WardenProcessExtensions
    {
        /// <summary>
        ///     Attempts to send a termination signal to the underlying system process and blocks the calling thread until it has exited.
        /// </summary>
        /// <param name="process">The process that will be terminated.</param>
        /// <param name="entireProcessTree">
        ///     If set to true the termination signal will be sent to all processes descendant from the
        ///     current <see cref="WardenProcess"/>.
        /// </param>
        /// <param name="exitCode">An optional exit code to be used by the process and threads terminated as a result of this call. </param>
        /// <returns>Returns true if the process terminated and false if any exceptions were encountered.</returns>
        public static bool TryTerminate(this WardenProcess? process, bool entireProcessTree, int exitCode = 0)
        {
            // exceptions are expensive so lets do the safety checks first anyway.
            if (process is null) return false;
            if (!process.HasProcessAssociation) return false;
            if (process.HasExited) return false;
            if (process.IsDisposed) return false;
            try { process.Terminate(entireProcessTree, exitCode); return true; }
            catch
            {
                return false;
            }
        }

        /// <summary>
        ///     Retrieves the name for the parent of the specified process.
        /// </summary>
        /// <returns>
        ///     If the specified process had a parent and its information was retrievable a string containing the name is
        ///     returned. Otherwise null.
        /// </returns>
        public static string? GetParentName(this WardenProcess? process)
        {
            if (process is null) return null;
            if (!process.HasProcessAssociation) return null;
            // The process has no parent.
            if (process.Info!.ParentProcessId <= 0) return null;
            // The process had a parent but its dead.
            if (!ProcessNative.IsProcessRunning(process.Info.ParentProcessId)) return null;
            try { return ProcessNative.GetParentProcessName(process.Info.ParentProcessId); }
            catch
            {
                // Nothing to do
            }
            return null;
        }

        /// <summary>
        ///     Check if the current process is hosted as a Windows Service.
        /// </summary>
        /// <returns><c>True</c> if the current process is hosted as a Windows Service, otherwise <c>false</c>.</returns>
        public static bool IsWindowsService(this WardenProcess? process)
        {
            if (process is null) return false;
            if (!process.HasProcessAssociation) return false;
            var parentProcessName = process.GetParentName();
            if (string.IsNullOrWhiteSpace(parentProcessName)) return false;
            if (!ProcessNative.ProcessIdToSessionId((uint) process.Info!.ParentProcessId, out var parentSessionId)) return false;
            return parentSessionId == 0 && parentProcessName.Equals("services", StringComparison.OrdinalIgnoreCase);
        }
    }
}