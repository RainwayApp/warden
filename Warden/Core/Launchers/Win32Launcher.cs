using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;
using Warden.Windows;

namespace Warden.Core.Launchers
{
    internal class Win32Launcher : ILauncher
    {
        internal static Regex ProgramPath = new Regex(@"([A-Z]:\\[^/:\*\?<>\|]+\.((exe)))|(\\{2}[^/:\*\?<>\|]+\.((exe)))", RegexOptions.IgnoreCase);

        internal static Regex Arguments =
            new Regex(
                @"(?:^[ \t]*((?>[^ \t""\r\n]+|""[^""]+(?:""|$))+)|(?!^)[ \t]+((?>[^ \t""\\\r\n]+|(?<!\\)(?:\\\\)*""[^""\\\r\n]*(?:\\.[^""\\\r\n]*)*""{1,2}|(?:\\(?:\\\\)*"")+|\\+(?!""))+)|([^ \t\r\n]))",
                RegexOptions.IgnoreCase);

        private string _workingDir;

        public async Task<WardenProcess> Launch(string path, string arguments, string workingDir, bool asUser)
        {
            _workingDir = workingDir;

            if (asUser)
            {
                var formattedPath = $"{path} {arguments}";
                if (Api.StartProcessAndBypassUac(formattedPath, out var procInfo, _workingDir) && procInfo.dwProcessId > 0)
                {
                    return WardenProcess.GetProcessFromId((int)procInfo.dwProcessId);
                }
            }
            else
            {
                return await Launch(path, arguments);
            }
            return null;
        }

        public async Task<WardenProcess> Launch(string path, string arguments)
        {
            try
            {
                var getSafeFileName = ProgramPath.Match(path);
               
                if (!getSafeFileName.Success)
                {
                    throw new WardenLaunchException(Resources.Exception_Process_Not_Launched_Unknown);
                }

                var filePath = getSafeFileName.Value;
                if (string.IsNullOrWhiteSpace(arguments))
                {
                    //Lets check our original path for arguments
                    var argumentCollection = Arguments.Matches(path);
                    var safeArguments = new StringBuilder();
                    foreach (Match arg in argumentCollection)
                    {
                        var argumentValue = arg.Value;
                        if (argumentValue.Contains(filePath))
                        {
                            continue;
                        }
                        safeArguments.Append(arg.Value);
                    }
                    arguments = safeArguments.ToString();
                }

                if (string.IsNullOrWhiteSpace(_workingDir))
                {
                    _workingDir = new FileInfo(filePath).Directory.FullName;
                }

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = arguments,
                    WorkingDirectory = (string.IsNullOrWhiteSpace(_workingDir) || !Directory.Exists(_workingDir)) ? string.Empty : _workingDir,
                    UseShellExecute = true
                });
               
                if (process == null)
                {
                    throw new WardenLaunchException(Resources.Exception_Process_Not_Launched_Unknown);
                }
                await WaitForProcessStart(process, TimeSpan.FromSeconds(10));
                var warden = new WardenProcess(process.ProcessName, process.Id, path,
                    process.HasExited ? ProcessState.Dead : ProcessState.Alive, arguments?.SplitSpace(), ProcessTypes.Win32, null);
                return warden;
            }
            catch (Exception ex)
            {
                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Launched, ex.Message), ex);
            }
        }

        public Task<WardenProcess> LaunchUri(string uri, string path, string arguments)
        {
            throw new NotImplementedException();
        }

        internal static async Task<bool> WaitForProcessStart(Process process, TimeSpan processTimeout)
        {
            var processStopwatch = Stopwatch.StartNew();
            var isProcessRunning = false;
            while (processStopwatch.ElapsedMilliseconds <= processTimeout.TotalMilliseconds && !isProcessRunning)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2));
                try
                {
                    if (process != null)
                    {
                        isProcessRunning = process.Handle != IntPtr.Zero && process.Id > 0 && !process.HasExited;
                    }
                }
                catch
                {
                    isProcessRunning = false;
                }
            }
            return isProcessRunning;
        }
    }
}
