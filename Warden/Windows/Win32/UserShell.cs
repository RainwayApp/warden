using System.Diagnostics;
using System.IO;
using Warden.Core;
using Warden.Core.Exceptions;
using Warden.Properties;

namespace Warden.Windows.Win32
{
    /// <summary>
    /// 
    /// </summary>
    internal static class UserShell
    {
        /// <summary>
        /// Attempts to create a process outside of session zero.
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns></returns>
        internal static WardenProcess CreateProcessAsUser(WardenStartInfo startInfo)
        {
            if (!new FileInfo(startInfo.FileName).Exists)
            {
                throw new WardenLaunchException($"Unable to launch {startInfo.FileName} -- the file is missing.");
            }
            if (startInfo.RaisePrivileges)
            {
                if (Api.StartProcessAsPrivilegedUser(startInfo.FileName, startInfo.Arguments, startInfo.WorkingDirectory, out var privInfo))
                {
                    return WardenProcess.GetProcessFromId(privInfo, startInfo.Filters, startInfo.Track);
                }
                throw new WardenLaunchException("Unable to start process as privileged user");
            }
            if (Api.StartProcessAsUser(startInfo.FileName, startInfo.Arguments, startInfo.WorkingDirectory, out var procInfo))
            {
                return WardenProcess.GetProcessFromId(procInfo, startInfo.Filters, startInfo.Track);
            }
            throw new WardenLaunchException("Unable to start process as user");
        }
    }
}
