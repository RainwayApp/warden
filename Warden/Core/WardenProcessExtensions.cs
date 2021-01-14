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
        ///     Retrieves the name for the parent of the specified process.
        /// </summary>
        /// <returns>
        ///     If the specified process had a parent and its information was retrievable a string containing the name is
        ///     returned. Otherwise null.
        /// </returns>
        public static string? GetParentName(this WardenProcess process)
        {
            if (process.Info is null)
            {
                return null;
            }
            // The process has no parent.
            if (process.Info.ParentProcessId <= 0)
            {
                return null;
            }
            // The process had a parent but its dead.
            if (!ProcessNative.IsProcessRunning(process.Info.ParentProcessId))
            {
                return null;
            }
            try
            {
                return ProcessNative.GetParentProcessName(process.Info.ParentProcessId);
            }
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
        public static bool IsWindowsService(this WardenProcess process)
        {
            if (process.Info is null)
            {
                return false;
            }
            var parentProcessName = process.GetParentName();
            if (string.IsNullOrWhiteSpace(parentProcessName))
            {
                return false;
            }
            if (!ProcessNative.ProcessIdToSessionId((uint) process.Info.ParentProcessId, out var parentSessionId))
            {
                return false;
            }
            return parentSessionId == 0 && parentProcessName.Equals("services", StringComparison.OrdinalIgnoreCase);
        }
    }
}