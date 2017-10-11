using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Warden.Core.Utils
{
    internal static class ProcessUtils
    {

        [DllImport("Kernel32.dll")]
        static extern uint QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder text, out uint size);

        public static bool IsWindowsApp(string path)
        {
            return path.Contains("WindowsApps");
        }

        public static IEnumerable<Process> GetChildProcesses(int id)
        {
            var mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={id}");
            return (from ManagementObject mo in mos.Get() select Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]))).ToList();
        }

   
        public static List<string> GetCommandLine(int id)
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
                return null;
            }
            const string strRegex = @"[ ](?=(?:[^""]*""[^""]*"")*[^""]*$)";
            var myRegex = new Regex(strRegex, RegexOptions.IgnoreCase);
            var split = myRegex.Split(arguments).ToList();
            split.RemoveAt(0);
            arguments = string.Join(" ", split);
            return arguments.SplitSpace();
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

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
            return Path.GetFullPath(new Uri(path).LocalPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }

        public static string GetProcessPath(int processId)
        {
            var pathToExe = string.Empty;
            try
            {
              
                var proc = Process.GetProcessById(processId);
                uint nChars = 256;
                var buff = new StringBuilder((int)nChars);
                var success = QueryFullProcessImageName(proc.Handle, 0, buff, out nChars);
                if (0 != success)
                {
                    pathToExe = buff.ToString();
                    pathToExe = NormalizePath(buff.ToString());
                }
            }
            catch
            {
                // ignored
            }
            return pathToExe;
        }
    }
}
