using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Warden.Core.Utils
{
    internal static class ProcessUtils
    {
        public static bool IsWindowsApp(string path)
        {
            return path.Contains("WindowsApps");
        }

        public static IEnumerable<Process> GetChildProcesses(int id)
        {
            var mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={id}");
            return (from ManagementObject mo in mos.Get() select Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]))).ToList();
        }


        public static string GetCommandLine(int id)
        {
            var commandLine = new StringBuilder();
            commandLine.Append(" ");
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + id))
            {
                foreach (var @object in searcher.Get())
                {
                    commandLine.Append(@object["CommandLine"]);
                    commandLine.Append(" ");
                }
            }
            var arguments = commandLine.ToString().Trim();
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return arguments;
            }
            const string strRegex = @"[ ](?=(?:[^""]*""[^""]*"")*[^""]*$)";
            var myRegex = new Regex(strRegex, RegexOptions.IgnoreCase);
            var split = myRegex.Split(arguments).ToList();
            split.RemoveAt(0);
            arguments = string.Join(" ", split);
            return arguments;
        }
        public static Process GetProcess(string path)
        {
            try
            {
                return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)).FirstOrDefault();
            }
            catch
            {

                return null;
            }
        }

        public static string GetProcessPath(int processId)
        {
            var methodResult = string.Empty;
            try
            {
                var query = "SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId;

                using (var mos = new ManagementObjectSearcher(query))
                {
                    using (var moc = mos.Get())
                    {
                        var executablePath = (from mo in moc.Cast<ManagementObject>() select mo["ExecutablePath"]).First().ToString();
                        methodResult = executablePath;
                    }
                }
            }
            catch
            {
                // ignored
            }
            return methodResult;
        }
    }
}
