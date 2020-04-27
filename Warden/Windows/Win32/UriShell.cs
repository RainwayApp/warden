using System;
using System.IO;
using Microsoft.Win32;
using Warden.Core;
using Warden.Core.Exceptions;
using Warden.Core.Utils;

namespace Warden.Windows.Win32
{
    /// <summary>
    /// A class for interact with URI shell components 
    /// </summary>
    internal static class UriShell
    {

        /// <summary>
        /// Looks at the system registry and determines if the URI is valid and the files it maps to exist
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns></returns>
        internal static (string FileName, string Arguments, string WorkingDirectory) ValidateUri(WardenStartInfo startInfo)
        {
            var unwrappedUri = UnwrapUri(startInfo.FileName, new[] { startInfo.Arguments });
            
            var launcher = unwrappedUri.LauncherFileName;
            if (string.IsNullOrWhiteSpace(launcher) || !new FileInfo(launcher).Exists)
            {
                throw new WardenLaunchException($"Unable to run the launcher for URI {startInfo.FileName} because it does not exist..");
            }
            var launcherArguments = unwrappedUri.LauncherArguments;

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
        /// Attempts to unwrap an application URI scheme into it's fully qualified executable path.
        /// </summary>
        /// <param name="uri">the application URI</param>
        /// <param name="arguments">arguments that need to be appended to the executable path.</param>
        /// <returns></returns>
        private static (string LauncherFileName, string LauncherArguments) UnwrapUri(string uri, string[] arguments)
        {
            var uriScheme = new Uri(uri).Scheme;
            if (string.IsNullOrWhiteSpace(uriScheme))
            {
                throw new WardenLaunchException($"{uri} contained no .Scheme when parsed.");
            }
            //the HKEY_CLASSES_ROOT key provides a view of the registry that merges the information from
            //HKEY_LOCAL_MACHINE\Software\Classes with the information from HKEY_CURRENT_USER\Software\Classes.
            using (var registry = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default))
            using (var schemeKey = registry.OpenSubKey(uriScheme))
            {
                if (schemeKey == null)
                {
                    throw new WardenLaunchException($"{uri} does not exist in ClassesRoot");
                }
                // the actual command for launching is stored as a child key
                using (var commandKey = schemeKey.OpenSubKey(@"Shell\Open\Command"))
                {
                    // if the URI has been uninstalled this key will be missing
                    if (commandKey == null)
                    {
                        throw new WardenLaunchException($"{uri} does not contain Shell\\Open\\Command");
                    }
                    // the command's value is stored in the default key which is accessed using an empty string.
                    var commandValue = commandKey.GetValue(string.Empty, string.Empty) as string;
                    // the command string is missing, so we return.
                    if (string.IsNullOrWhiteSpace(commandValue))
                    {
                        throw new WardenLaunchException($"{uri} does not have a valid commandValue");
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
