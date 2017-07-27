using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Windows;

namespace Warden.Core.Launchers
{
    internal class UwpLauncher : ILauncher
    {
        public async Task<WardenProcess> Launch(string path, string arguments)
        {
            var pId = await Api.LaunchUwpApp($"{path}{arguments}");
            if (pId <= 0)
            {
                throw new WardenLaunchException($"Could not find process id for {path}");
            }
            var pName = string.Empty;
            ProcessState state;
            try
            {
                var process = Process.GetProcessById(pId);
                pName = process.ProcessName;
                state = process.HasExited ? ProcessState.Dead : ProcessState.Alive;
            }
            catch (Exception)
            {
               state = ProcessState.Dead;
            }
            return new WardenProcess(pName, pId, path, state, arguments, ProcessTypes.Uwp);
        }

        public Task<WardenProcess> LaunchUri(string uri, string path, string arguments)
        {
            throw new NotImplementedException();
        }
    }
}
