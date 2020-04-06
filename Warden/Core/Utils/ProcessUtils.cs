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
        private static readonly Random GetRandom = new Random();


        public static int GenerateProcessId()
        {
            lock (GetRandom) // synchronize
            {
                return GetRandom.Next(1000000, 2000000);
            }
        }


        [DllImport("Kernel32.dll")]
        static extern uint QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder text, out uint size);

        public static List<string> GetCommandLineFromString(string arguments)
        {
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

        public static List<string> GetCommandLine(int id)
        {
            var commandLine = new StringBuilder();
            commandLine.Append(" ");
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + id))
            {
                foreach (var @object in searcher.Get())
                {
                    using (@object)
                    {
                        commandLine.Append(@object["CommandLine"]);
                        commandLine.Append(" ");
                    }
                }
            }
            var arguments = commandLine.ToString().Trim();
            return string.IsNullOrWhiteSpace(arguments) ? null : GetCommandLineFromString(arguments);
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
                    pathToExe = PathUtils.NormalizePath(buff.ToString());
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
