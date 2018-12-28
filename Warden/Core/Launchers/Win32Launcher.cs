using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;
using Warden.Windows;


[assembly: InternalsVisibleTo("Warden.Tests")]
namespace Warden.Core.Launchers
{
    internal class Win32Launcher : ILauncher
    {
        private string _workingDir;

        internal static string GetSafePath(string path)
        {
            var regexForLocalAndNetworkPaths = new Regex(@"([A-Z]:\\[^/:\*\?<>\|]+\.((exe)))|(\\{2}[^/:\*\?<>\|]+\.((exe)))", RegexOptions.IgnoreCase);
            var regexForExecutables = new Regex(@"([A-Z0-9]*)\.((exe))", RegexOptions.IgnoreCase);
            var regexForCommands = new Regex(@"([A-Z0-9]*)", RegexOptions.IgnoreCase);
            var regexForWardenCommands = new Regex("\"(.*?)\"", RegexOptions.IgnoreCase); //TODO: It accepts wverything between quotation marks for now.

            string result = null;

            if (regexForWardenCommands.IsMatch(path))
                result = regexForWardenCommands.Match(path).Value;
            else if (regexForLocalAndNetworkPaths.IsMatch(path))
                result = regexForLocalAndNetworkPaths.Match(path).Value;
            else if (regexForExecutables.IsMatch(path))
                result = regexForExecutables.Match(path).Value;
            else if (regexForCommands.IsMatch(path))
                result = regexForCommands.Match(path).Value;

            return result;
        }

        internal static string GetSafeArgs(string path, string filePath, string arguments)
        {
            if (!string.IsNullOrWhiteSpace(arguments)) return arguments;

            //Lets check our original path for arguments
            var regexForArguments = new Regex(
                      @"(?:^[ \t]*((?>[^ \t""\r\n]+|""[^""]+(?:""|$))+)|(?!^)[ \t]+((?>[^ \t""\\\r\n]+|(?<!\\)(?:\\\\)*""[^""\\\r\n]*(?:\\.[^""\\\r\n]*)*""{1,2}|(?:\\(?:\\\\)*"")+|\\+(?!""))+)|([^ \t\r\n]))",
                      RegexOptions.IgnoreCase);

            var argumentCollection = regexForArguments.Matches(path);
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

            return arguments;
        }

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
                var safePath = GetSafePath(path);

                if (safePath == null)
                {
                    throw new WardenLaunchException(Resources.Exception_Process_Not_Launched_Unknown);
                }

                var filePath = safePath;
                arguments = GetSafeArgs(path, filePath, arguments);

                if (string.IsNullOrWhiteSpace(_workingDir))
                {
                    _workingDir = new FileInfo(filePath).Directory?.FullName;
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
