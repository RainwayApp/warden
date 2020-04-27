using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Warden.Properties;

namespace Warden.Windows.Win32
{
    internal class ShellStartInfo
    {
        private string _arguments;
        private string _verb = "open";
        private string _workingDirectory;

        public ShellStartInfo(string address, string arguments, string workingDirectory) : this(address, arguments)
        {
            WorkingDirectory = workingDirectory;
        }

        public ShellStartInfo(string address, string arguments) : this(address)
        {
            Arguments = arguments;
        }

        public ShellStartInfo(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("Shell address cannot be null");
            }
            Address = address;
        }

        public string Address { get; }

        public string Arguments
        {
            get => _arguments;
            set => _arguments = value ?? string.Empty;
        }

        public string Verb
        {
            get => _verb;
            set => _verb = value ?? "open";
        }

        public ProcessWindowStyle WindowStyle { get; set; } = ProcessWindowStyle.Normal;

        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => _workingDirectory = value ?? Environment.CurrentDirectory;
        }
    }
}
