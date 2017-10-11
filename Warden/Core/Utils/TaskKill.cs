using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Warden.Core.Utils
{
    public class TaskSwitch
    {

        private TaskSwitch(string @switch, bool requiresValue)
        {
            RequiresValue = requiresValue;
            Switch = @switch;
        }

        /// <summary>
        /// The physical argument for taskkill 
        /// </summary>
        public string Switch { get; }


        /// <summary>
        /// Informs you if the argument switch needs a value
        /// </summary>
        public bool RequiresValue { get; }


        /// <summary>
        /// The value to pass with the argument.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Specifies the name or IP address of a remote computer (do not use backslashes). The default is the local computer.
        /// </summary>
        public static TaskSwitch Computer => new TaskSwitch("/s", true);
        /// <summary>
        /// Runs the command with the account permissions of the user specified by User or Domain\User. The default is the permissions of the current logged on user on the computer issuing the command.
        /// </summary>
        public static TaskSwitch DomainUser => new TaskSwitch("/u", true);
        /// <summary>
        /// Specifies the password of the user account that is specified in the /u parameter.
        /// </summary>
        public static TaskSwitch Password => new TaskSwitch("/p", true);
        /// <summary>
        /// Specifies the types of process(es) to include in or exclude from termination. The following are valid filter names, operators, and values.
        /// </summary>
        public static TaskSwitch FilterName => new TaskSwitch("/fi", true);
        /// <summary>
        /// Specifies the process ID of the process to be terminated.
        /// </summary>
        public static TaskSwitch ProcessId => new TaskSwitch("/pid", true);

        public static TaskSwitch Force => new TaskSwitch("/f", false);
        /// <summary>
        /// Specifies the image name of the process to be terminated. Use the wildcard (*) to specify all image names.
        /// </summary>
        public static TaskSwitch ImageName => new TaskSwitch("/im", true);
        /// <summary>
        /// Specifies that process(es) be forcefully terminated. This parameter is ignored for remote processes; all remote processes are forcefully terminated.
        /// </summary>
        public static TaskSwitch TerminateChildren => new TaskSwitch("/t", false);
    }


    public class TaskKill
    {
        /// <summary>
        /// All of the arguments you wish to pass to the taskkill process
        /// </summary>
        public List<TaskSwitch> Arguments { get; set; }


        private void ExecuteTaskKill(out string output, out string error)
        {

            var argumentBuilder = new StringBuilder();
            foreach (var argument in Arguments)
            {
                if (argument.RequiresValue && string.IsNullOrWhiteSpace(argument.Value))
                {
                    error = $"ERROR: The argument {argument.Switch} was passed without a value.";
                    output = null;
                    return;
                }
                argumentBuilder.Append($"{argument.Switch} {(argument.RequiresValue ? argument.Value : string.Empty)}");
            }
            using (var p = new Process())
            {
                p.StartInfo = new ProcessStartInfo()
                {
                    FileName = "taskkill",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = argumentBuilder.ToString()
                };
                p.Start();
                output = p.StandardOutput.ReadToEnd();
                error = p.StandardError.ReadToEnd();
                p.WaitForExit();
            }
        }

        public void Execute(out string output, out string error)
        {
            if (Arguments == null || Arguments.Count == 0)
            {
                error = "No arguments provided.";
                output = null;
                return;
            }
            ExecuteTaskKill(out output, out error);
        }
    }
}
