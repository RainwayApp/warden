using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Warden.Core.Exceptions;

namespace Warden.Core.Launchers
{
    internal class UriLauncher : ILauncher
    {
        public async Task<WardenProcess> LaunchUri(string uri, string path, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = uri,
                    Arguments = string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments
                };
                var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    throw new WardenLaunchException("Process could not start");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                var warden = new WardenProcess(Path.GetFileNameWithoutExtension(path),
                    new Random().Next(100000, 199999), path, ProcessState.Alive, arguments, ProcessTypes.Uri);
                return warden;
            }
            catch (Exception ex)
            {
                throw new WardenLaunchException("Process could not be launched: " + ex.Message);
            }
        }
        public Task<WardenProcess> Launch(string path, string arguments)
        {
            throw new NotImplementedException();
        }
    }
}
