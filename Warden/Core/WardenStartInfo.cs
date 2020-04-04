using System.Collections.Generic;
using Warden.Core.Models;
using Warden.Core.Utils;

namespace Warden.Core
{
    /// <summary>
    /// Specifies a set of values that are used when you start a process.
    /// </summary>
    public class WardenStartInfo
    {
        /// <summary>
        /// A single string containing the arguments to pass to the target application specified in the <see cref="FileName"/> property.
        /// The default is an empty string ("").
        /// </summary>
        public string Arguments { get; set; }

        /// <summary>
        /// </summary>
        private string _workingDirectory = string.Empty;

        /// <summary>
        /// The fully qualified name of the directory that contains the process to be started.
        /// The default is an empty string ("").
        /// </summary>
        public string WorkingDirectory {
            get
            {
                return _workingDirectory;
            }
            set { _workingDirectory = PathUtils.NormalizePath(value); }
        }

        /// <summary>
        /// </summary>
        private string _fileName = string.Empty;

        /// <summary>
        /// The name of the application to start, or the associated application scheme that has a default open action available to it.
        /// The default is an empty string ("").
        /// </summary>
        public string FileName
        {
            get
            {
                return _fileName;
            }
            set { _fileName = PathUtils.NormalizePath(value); }
        }

        /// <summary>
        /// </summary>
        private string _targetFileName = string.Empty;

        /// <summary>
        /// When launching a URI Warden will wait for a specific application to spawn on the machine.
        /// Set the fully qualified name of the spawned application on this property before launching the URI.
        /// </summary>
        public string TargetFileName
        {
            get
            {
                return _targetFileName;
            }
            set { _targetFileName = PathUtils.NormalizePath(value); }
        }

        /// <summary>
        /// The package family name for the package that contains the endpoint for the app service.
        /// </summary>
        public string PackageFamilyName { get; set; }

        /// <summary>
        /// For Microsoft Store and UWP applications the application ID can be found in the AppxManifest.xml file, under the <Applications/> element.
        /// </summary>
        public string ApplicationId { get; set; }

        /// <summary>
        /// If this is set to true the application will be launched with full administrative privileges and bypass UAC prompts.
        /// You will want to do this if you're running a SYSTEM service and need to launch software into the users desktop.
        /// The default is false.
        /// </summary>
        public bool AsUser { get; set; } = false;

        /// <summary>
        /// Process filters prevent certain child applications from polluting the process family tree.
        /// For example if you launch a game and it opens a web browser, you can prevent the browser from entering the family.
        /// </summary>
        public List<ProcessFilter> Filters { get; set; }

    }
}
