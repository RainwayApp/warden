using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;
using Warden.Windows;
using Microsoft.Win32;
using System.Linq;

namespace Warden.Core.Launchers
{
    internal class UriLauncher : ILauncher
    {
        private CancellationToken _cancelToken;
        private Guid _id;

        public string UnwrapURI(string uri, string SID, params string[] args)
        {
            var scheme = $@"SOFTWARE\Classes\{new Uri(uri).Scheme}";
            RegistryKey HKLM = Registry.LocalMachine;
            if (SID != null)
            {
                HKLM = Registry.Users;
                scheme = $@"{SID}\{scheme}";
            }
            RegistryKey schemeKey = HKLM.OpenSubKey(scheme);
            if (schemeKey != null)
            {
                if ((schemeKey.GetValue(null) as string).StartsWith("URL:"))
                {
                    RegistryKey commandKey = schemeKey.OpenSubKey(@"Shell\Open\Command");
                    string commandValue = commandKey.GetValue(null) as string;
                    commandValue = commandValue.Replace("%1", uri);
                    for (int i = 0; i < args.Length; ++i)
                    {
                        commandValue = commandValue.Replace($"%{i + 2}", args[i]);
                    }
                    return commandValue;
                }
            }
            else if (SID == null)
            {
                foreach (var key in Registry.Users.GetSubKeyNames())
                {
                    var v = UnwrapURI(uri, key, args);
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        return v;
                    }
                }
            }

            return string.Empty;
        }

        public async Task<WardenProcess> LaunchUri(string uri, string path, string arguments, bool asUser)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = uri,
                    Arguments = string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments,
                };
                if (asUser)
                {
                    if (!Api.StartProcessAndBypassUac(UnwrapURI(uri, null, arguments), out var procInfo))
                    {
                        throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start,
                            startInfo.FileName, startInfo.Arguments));
                    }
                }
                else
                {
                    var process = new Process { StartInfo = startInfo };
                    if (!process.Start())
                    {
                        throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
                    }

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
                    : new WardenProcess(Path.GetFileNameWithoutExtension(path), 0, path, ProcessState.Alive, arguments?.SplitSpace(), ProcessTypes.Uri, null);
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

        public Task<WardenProcess> LaunchUri(string uri, string path, string arguments)
        {
            throw new NotImplementedException();
        }

        public async Task<WardenProcess> PrepareUri(string uri, string path, string arguments, CancellationToken cancelToken, Guid id, bool asUser = false)
        {
            _id = id;
            _cancelToken = cancelToken;
            return _id != Guid.Empty
                ? LaunchUriAsync(uri, path, arguments, asUser)
                : await LaunchUri(uri, path, arguments, asUser);
        }

        private WardenProcess LaunchUriAsync(string uri, string path, string arguments, bool asUser)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = uri,
                Arguments = string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments
            };

            if (asUser)
            {
                if (Api.StartProcessAndBypassUac(UnwrapURI(uri, null, arguments), out var procInfo))
                {
                    return new WardenProcess(Path.GetFileNameWithoutExtension(path), 0, path, ProcessState.Alive,
                        arguments?.SplitSpace(), ProcessTypes.Uri, null);
                }
                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
            }
            var process = new Process { StartInfo = startInfo, };
            if (!process.Start())
            {
                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
            }
            return new WardenProcess(Path.GetFileNameWithoutExtension(path), 0, path, ProcessState.Alive, arguments?.SplitSpace(), ProcessTypes.Uri, null);
        }

    }
}
