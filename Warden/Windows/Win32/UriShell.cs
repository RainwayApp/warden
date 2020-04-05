using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Warden.Core;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;

namespace Warden.Windows.Win32
{
    /// <summary>
    /// A class for launching Win32 processes via an application URI scheme.
    /// </summary>
    public static class UriShell
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns></returns>
        private static (string FileName, string Arguments, string WorkingDirectory) ValidateUri(WardenStartInfo startInfo)
        {
            var unwrappedUri = UnwrapUri(startInfo.FileName, new[] { startInfo.Arguments });
            if (!unwrappedUri.HasValue)
            {
                throw new WardenLaunchException($"Unable to unwrap the following URI: {startInfo.FileName}");
            }

            var launcher = unwrappedUri.Value.LauncherFileName;
            if (string.IsNullOrWhiteSpace(launcher) || !new FileInfo(launcher).Exists)
            {
                throw new WardenLaunchException($"Unable to run the launcher for URI {startInfo.FileName} because it does not exist..");
            }
            var launcherArguments = unwrappedUri.Value.LauncherArguments;

            var launcherWorkingDir = PathUtils.GetDirectoryName(launcher);

            if (string.IsNullOrWhiteSpace(launcher) || !new DirectoryInfo(launcherWorkingDir).Exists)
            {
                throw new WardenLaunchException($"Unable to determine the working directory for the following URI: {startInfo.FileName}.");
            }

            if (string.IsNullOrWhiteSpace(startInfo.TargetFileName) || !new FileInfo(startInfo.TargetFileName).Exists)
            {
                throw new WardenLaunchException($"Unable to launch URI {startInfo.FileName} because target file {startInfo.TargetFileName} does not exist.");
            }
            return (launcher, launcherArguments, launcherWorkingDir);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns></returns>
        public static bool LaunchUriDeferred(WardenStartInfo startInfo)
        {
            var (fileName, arguments, workingDirectory) = ValidateUri(startInfo);
            if (startInfo.AsUser)
            {
                if (Api.StartProcessAndBypassUac(fileName, arguments, workingDirectory, out _))
                {
                    return true;
                }

                throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            };

            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
                }
            }
            return true;
        }

        /// <summary>
        /// Launches an application URI Scheme and waits for the target process to appear
        /// </summary>
        /// <param name="startInfo"></param>
        /// <param name="cancelToken">A cancellation token to configure how long Warden will wait.</param>
        /// <returns></returns>
        public static async Task<bool> LaunchUri(WardenStartInfo startInfo, CancellationTokenSource cancelToken)
        {
            var (fileName, arguments, workingDirectory) = ValidateUri(startInfo);

            if (startInfo.AsUser)
            {
                if (!Api.StartProcessAndBypassUac(fileName, arguments, workingDirectory, out _))
                {
                    throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, fileName, arguments));
                }
            }
            else
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        throw new WardenLaunchException(string.Format(Resources.Exception_Process_Not_Start, startInfo.FileName, startInfo.Arguments));
                    }
                }
            }
            while (!cancelToken.IsCancellationRequested)
            {
                using (var process = ProcessUtils.GetProcess(startInfo.TargetFileName))
                {
                    if (process != null)
                    {
                        return true;
                    }
                }
                //aggressive poll
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancelToken.Token);
            } 
            return false;
        }

        /// <summary>
        /// Attempts to unwrap an application URI scheme into it's fully qualified executable path.
        /// </summary>
        /// <param name="uri">the application URI</param>
        /// <param name="arguments">arguments that need to be appended to the executable path.</param>
        /// <returns></returns>
        private static (string LauncherFileName, string LauncherArguments)? UnwrapUri(string uri, string[] arguments)
        {
            var uriScheme = new Uri(uri).Scheme;
            if (string.IsNullOrWhiteSpace(uriScheme))
            {
                return null;
            }
            //the HKEY_CLASSES_ROOT key provides a view of the registry that merges the information from
            //HKEY_LOCAL_MACHINE\Software\Classes with the information from HKEY_CURRENT_USER\Software\Classes.
            using (var registry = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default))
            using (var schemeKey = registry.OpenSubKey(uriScheme))
            {
                // if the URI is not registered with the system then this will return null
                if (schemeKey?.GetValue("URL Protocol") == null)
                {
                    return null;
                }
                // the actual command for launching is stored as a child key
                using (var commandKey = schemeKey.OpenSubKey(@"Shell\Open\Command"))
                {
                    // if the URI has been uninstalled this key will be missing
                    if (commandKey == null)
                    {
                        return null;
                    }
                    // the command's value is stored in the default key which is accessed using an empty string.
                    var commandValue = commandKey.GetValue(string.Empty, string.Empty) as string;
                    // the command string is missing, so we return.
                    if (string.IsNullOrWhiteSpace(commandValue))
                    {
                        return null;
                    }
                   
                    // now that we have turned the URI into it's 
                    commandValue = commandValue.Replace("%1", uri);
                    for (var i = 0; i < arguments.Length; ++i)
                    {
                        if (commandValue.Contains($"%i{i + 2}"))
                        {
                            commandValue = commandValue.Replace($"%{i + 2}", arguments[i]);
                        }
                        else
                        {
                            commandValue += $" {arguments[i]}";
                        }
                    }
                    return (PathUtils.FindFullyQualifiedName(commandValue), PathUtils.FindCommandLineArguments(commandValue));
                }
            }
        }
    }
}
