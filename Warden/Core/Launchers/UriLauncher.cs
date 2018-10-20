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
            var HKLM = Registry.LocalMachine;
            if (SID != null)
            {
                HKLM = Registry.Users;
                scheme = $@"{SID}\{scheme}";
            }
            var schemeKey = HKLM.OpenSubKey(scheme);
            if (schemeKey != null)
            {
                if (schemeKey.GetValue("URL Protocol") != null)
                {
                    var commandKey = schemeKey.OpenSubKey(@"Shell\Open\Command");
                    var commandValue = commandKey.GetValue(null) as string;
                    commandValue = commandValue.Replace("%1", uri);
                    for (var i = 0; i < args.Length; ++i)
                    {
                        if (commandValue.Contains($"%i{i + 2}"))
                        {
                            commandValue = commandValue.Replace($"%{i + 2}", args[i]);
                        }
                        else
                        {
                            commandValue += $" {args[i]}";
                        }
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

        public async Task<WardenProcess> LaunchUri(string uri, string path, string arguments, bool asUser, string workingDir)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = uri,
                    Arguments = string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments,
                    WorkingDirectory = (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir)) ? string.Empty : workingDir,
                };
                if (asUser)
                {
                    if (!Api.StartProcessAndBypassUac(UnwrapURI(uri, null, arguments), out var procInfo, workingDir))
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

        public async Task<WardenProcess> PrepareUri(string uri, string path, string arguments, CancellationToken cancelToken, Guid id, bool asUser = false, string workingDir = null)
        {
            _id = id;
            _cancelToken = cancelToken;
            return _id != Guid.Empty
                ? LaunchUriAsync(uri, path, arguments, asUser, workingDir)
                : await LaunchUri(uri, path, arguments, asUser, workingDir);
        }

        private WardenProcess LaunchUriAsync(string uri, string path, string arguments, bool asUser, string workingDir)
        {
            var unwrappedURI = UnwrapURI(uri, null, arguments);
            var unwrappedURISplit = unwrappedURI.SplitSpace();
            var startInfo = new ProcessStartInfo
            {
                FileName = unwrappedURISplit[0],
                Arguments = string.Join(" ", unwrappedURISplit.Skip(1)),
                WorkingDirectory = (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir)) ? string.Empty : workingDir,
                UseShellExecute = false
            };

            if (asUser)
            {
                if (Api.StartProcessAndBypassUac(unwrappedURI, out var procInfo, workingDir))
                {
                    return new WardenProcess(Path.GetFileNameWithoutExtension(path), 0, path, ProcessState.Alive,
                        arguments?.SplitSpace(), ProcessTypes.Uri, null);
                }
                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
            }
            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
            }
            return new WardenProcess(Path.GetFileNameWithoutExtension(path), 0, path, ProcessState.Alive, arguments?.SplitSpace(), ProcessTypes.Uri, null);
        }

    }
}
