using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;

namespace Warden.Core.Launchers
{
    internal class UriLauncher : ILauncher
    {
        private CancellationToken _cancelToken;

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
                    throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
                }
                var started = await Task.Run(async () =>
                {
                    var startTime = DateTime.UtcNow;
                    while (DateTime.UtcNow - startTime < TimeSpan.FromMinutes(1))
                    {
                        if (_cancelToken.IsCancellationRequested)
                        {
                            return false;
                        }
                        if (ProcessUtils.GetProcess(path) != null)
                        {
                            return true;
                        }
                        //aggressive poll
                        await Task.Delay(TimeSpan.FromMilliseconds(5), _cancelToken);
                    }
                    return false;
                }, _cancelToken);
                return !started
                    ? null
                    : new WardenProcess(Path.GetFileNameWithoutExtension(path), 0, path, ProcessState.Alive, arguments, ProcessTypes.Uri);
            }
            catch (Exception ex)
            {
                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Launched, ex.Message), ex);
            }
        }
        public Task<WardenProcess> Launch(string path, string arguments)
        {
            throw new NotImplementedException();
        }

        public async Task<WardenProcess> PrepareUri(string uri, string path, string arguments, CancellationToken cancelToken)
        {
            _cancelToken = cancelToken;
            return await LaunchUri(uri, path, arguments);
        }
    }
}
