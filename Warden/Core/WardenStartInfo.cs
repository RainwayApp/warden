using System.Collections.Generic;
using Warden.Utils;

namespace Warden.Core
{
    /// <summary>
    ///     Specified how a new window should appear when the system starts a process.
    /// </summary>
    public enum WindowStyle
    {
        /// <summary>
        ///     The normal, visible window style. The system displays a window with <see cref="Normal"/> style on the screen, in a default
        ///     location. If a window is visible, the user can supply input to the window and view the window's output. Frequently,
        ///     an application may initialize a new window to the <see cref="Hidden"/> style while it customizes the window's appearance, and
        ///     then make the window style <see cref="Normal"/>.
        /// </summary>
        Normal = 0,

        /// <summary>
        ///     The hidden window style. A window can be either visible or hidden. The system displays a hidden window by not
        ///     drawing it. If a window is hidden, it is effectively disabled. A hidden window can process messages from the system
        ///     or from other windows, but it cannot process input from the user or display output. Frequently, an application may
        ///     keep a new window hidden while it customizes the window's appearance, and then mStarake the window style <see cref="Normal"/>.
        /// </summary>
        Hidden = 1,

        /// <summary>
        ///     The minimized window style. By default, the system reduces a minimized window to the size of its taskbar button and
        ///     moves the minimized window to the taskbar.
        /// </summary>
        Minimized = 2,

        /// <summary>
        ///     The maximized window style. By default, the system enlarges a maximized window so that it fills the screen or, in
        ///     the case of a child window, the parent window's client area. If the window has a title bar, the system
        ///     automatically moves it to the top of the screen or to the top of the parent window's client area. Also, the system
        ///     disables the window's sizing border and the window-positioning capability of the title bar so that the user cannot
        ///     move the window by dragging the title bar.
        /// </summary>
        Maximized = 3
    }
    
    /// <summary>
    ///     Specifies a set of values that are used when you start a process.
    /// </summary>
    public class WardenStartInfo
    {
        /// <summary>
        ///     A single string containing the arguments to pass to the target application specified in the <see cref="FileName"/>
        ///     property.
        ///     The default is an empty string ("").
        /// </summary>
        public string Arguments
        {
            get => _arguments;
            set => _arguments = value.FormatCommandLine();
        }

        /// <summary>
        ///     Gets or sets the working directory for the process to be started.
        /// </summary>
        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => _workingDirectory = value.NormalizePath();
        }

        /// <summary>
        ///     Gets or sets the application or document to start.
        /// </summary>
        public string FileName
        {
            get => _fileName;
            set => _fileName = value.NormalizePath();
        }

        /// <summary>
        ///     <para>Gets or sets the value Warden will use to automatically associate a newly launched system process to the <see cref="WardenProcess"/> returned by <see cref="WardenProcess.Start"/> by matching its image name.</para>
        /// </summary>
        public string TargetImage
        {
            get => _targetImage;
            set => _targetImage = value.NormalizePath();
        }

        /// <summary>
        ///     The package family name for the package that contains the endpoint for the app service.
        /// </summary>
        public string? PackageFamilyName { get; set; }

        /// <summary>
        ///     For Microsoft Store and UWP applications the application ID can be found in the AppxManifest.xml file, under the
        ///     <Applications/> element.
        /// </summary>
        public string? ApplicationId { get; set; }

        /// <summary>
        ///     If this is set to true, the process will attempt to launch into an administrative context.
        ///     Using the shell this means appending "runas" where as with StartProcessAsUser it will use the security token of
        ///     WinLogon
        /// </summary>
        public bool RaisePrivileges { get; set; }

        /// <summary>
        ///     If set to false the process family tree is not tracked by <see cref="Monitor.SystemProcessMonitor"/> when calling
        ///     any launch start method.
        /// </summary>
        public bool Track { get; set; } = true;

        /// <summary>
        ///     Gets or sets the filtered process image names that will not be added as children to a <see cref="WardenProcess"/> family tree.
        /// </summary>
        public List<string>? FilteredImages { get; set; }
        /// <summary>
        /// Gets or sets the window state to use when the process is started.
        /// </summary>
        public WindowStyle WindowStyle { get; set; }

        /// <inheritdoc cref="Arguments"/>
        private string _arguments = string.Empty;

        /// <inheritdoc cref="FileName"/>
        private string _fileName = string.Empty;

        /// <inheritdoc cref="TargetImage"/>
        private string _targetImage = string.Empty;

        /// <inheritdoc cref="WorkingDirectory"/>
        private string _workingDirectory = string.Empty;
    }
}