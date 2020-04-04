using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Warden.Core;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;

namespace Warden.Windows.Win32
{
    /// <summary>
    /// 
    /// </summary>
    public static class UserShell
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns></returns>
        public static WardenProcess LaunchWin32App(WardenStartInfo startInfo)
        {
            if (!new FileInfo(startInfo.FileName).Exists)
            {
                throw new WardenLaunchException($"Unable to launch {startInfo.FileName} -- the file is missing.");
            }
            if (startInfo.AsUser)
            {
                if (!Api.StartProcessAndBypassUac(startInfo.FileName, startInfo.Arguments, startInfo.WorkingDirectory, out var procInfo))
                {
                    throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
                }
                return WardenProcess.GetProcessFromId((int)procInfo.dwProcessId);
            }
            var processStartInfo = new ProcessStartInfo
            {
                FileName = startInfo.FileName,
                Arguments = startInfo.Arguments,
                WorkingDirectory = startInfo.WorkingDirectory,
                UseShellExecute = true
            };
            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    throw new WardenLaunchException(Resources.Exception_Process_Not_Launched_Unknown);
                }
                return WardenProcess.GetProcessFromId(process.Id);
            }
        }
    }
}
