using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Warden.Monitor;
using Warden.Utils;
using Warden.Windows;

namespace Warden.Core
{
    /// <summary>
    ///     Encapsulates a system process object for tracking and reliable lifetime management.
    /// </summary>
    public sealed class WardenProcess : IDisposable
    {
        /// <summary>
        ///     Initialize a new <see cref="WardenProcess"/> instance that is associated with the specified process
        ///     <paramref name="info"/>.
        /// </summary>
        /// <param name="info">The process information of a system process object.</param>
        /// <param name="filteredImages"></param>
        internal WardenProcess(ProcessInfo? info, IEnumerable<string>? filteredImages = null) : this(filteredImages)
        {
            if (info is null)
            {
                throw new ArgumentNullException(nameof(info));
            }
            Initialize(info);
        }


        /// <summary>
        ///     Creates a new <see cref="WardenProcess"/> instance that will be initialized asynchronously.
        /// </summary>
        /// <param name="info">The system process monitor information that will assist future process association.</param>
        /// <param name="filteredImages"></param>
        internal WardenProcess(MonitorInfo info, IEnumerable<string>? filteredImages = null) : this(filteredImages)
        {
            Monitor = info ?? throw new ArgumentNullException(nameof(info));
        }


        private WardenProcess(IEnumerable<string>? filteredImages)
        {
            if (filteredImages is not null)
            {
                FilteredImageNames = new HashSet<string>(filteredImages, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        ///     Information used by <see cref="SystemProcessMonitor"/> to help associate a new process launch with this
        ///     <see cref="WardenProcess"/> instance if it is being tracked.
        /// </summary>
        internal MonitorInfo? Monitor { get; private set; }

        /// <summary>
        /// Indicates if the current <see cref="WardenProcess"/> has been disposed.
        /// </summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Indicates if the current <see cref="WardenProcess"/> object is associated with a system process.
        /// </summary>
        /// <remarks>
        /// Check this before accessing members of this class. For example if you call <see cref="HasExited"/> while no system process is associated with the <see cref="WardenProcess"/> it will throw.
        /// </remarks>
        public bool HasProcessAssociation => Monitor is null && Info is not null && _watcher is not null;

        /// <summary>
        ///     Gets the value that was specified by the associated process when it was terminated.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when trying to retrieve the exit code before the process has exited.</exception>
        /// <exception cref="SystemException">
        ///     Thrown when no process ID has been set, and a Handle from which the ID property can
        ///     be determined does not exist.
        /// </exception>
        public int ExitCode
        {
            get
            {
                if (_isCurrentProcess)
                {
                    return 0;
                }
                if (!HasExited)
                {
                    throw new InvalidOperationException("The process has not exited.");
                }
                if (_watcher is not null)
                {
                    return _watcher.ExitCode;
                }
                throw new SystemException("There is no system process associated with this WardenProcess object.");
            }
        }


        /// <summary>
        ///     Indicates whether the current process has been terminated.
        /// </summary>
        /// <exception cref="SystemException">
        ///     Thrown when no process ID has been set, and a Handle from which the ID property can
        ///     be determined does not exist.
        /// </exception>
        public bool HasExited
        {
            get
            {
                if (_isCurrentProcess)
                {
                    return false;
                }
                if (_watcher is not null)
                {
                    return _watcher.HasExited;
                }
                throw new SystemException("There is no system process associated with this WardenProcess object.");
            }
        }

        /// <summary>
        ///     Indicates if the current process and all processes descendant from it children have stopped executing.
        /// </summary>
        /// <exception cref="SystemException">
        ///     Thrown when no process ID has been set on the current or a child process, and a Handle from which the ID property
        ///     can
        ///     be determined does not exist.
        /// </exception>
        public bool HasTreeExited
        {
            get
            {
                // If the current application process is calling this then we can just return false
                if (_isCurrentProcess)
                {
                    return false;
                }
                // First things first we check on the kids.
                if (Children is {Count: > 0})
                {
                    // If any of the children or their grandchildren (and so on and so forth) is still kicking that means the tree has not exited.
                    if (Children.Any(child => !child.HasExited || !child.HasTreeExited))
                    {
                        return false;
                    }
                }
                // If there are no children or they've all gone to live on a farm check if the current WardenProcess has exited.
                return HasExited;
            }
        }

        /// <summary>
        ///     A collection of process image names (executables) that will not be added as children to this
        ///     <see cref="WardenProcess"/> even if the underlying system process object is the parent.
        /// </summary>
        public HashSet<string>? FilteredImageNames { get; }

        /// <summary>
        ///     Information about the current process such as its name and working directory.
        /// </summary>
        public ProcessInfo? Info { get; private set; }

        /// <summary>
        ///     A collection of child processes that were spawned by the current process.
        /// </summary>
        public ConcurrentBag<WardenProcess>? Children { get; private set; }


        /// <summary>
        ///     indicates if the current <see cref="WardenProcess"/> has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        ///     A delegate for <see cref="OnChildExit"/>
        /// </summary>
        private EventHandler<int>? _onChildExit;

        /// <summary>
        ///     A delegate for <see cref="OnExit"/>
        /// </summary>
        private EventHandler<int>? _onExit;


        /// <summary>
        ///     A delegate for <see cref="OnFound"/>
        /// </summary>
        private EventHandler<WardenProcess>? _onFound;

        /// <summary>
        ///     A watcher that listens for the underlying system process to exit.
        /// </summary>
        private ProcessHook? _watcher;
        /// <summary>
        /// When set to true this field indicates that the current <see cref="WardenProcess"/> is associated with the currently active process.
        /// </summary>
        private bool _isCurrentProcess;


        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <remarks>
        /// You almost certainly should not call this yourself. It is called automatically when the associated process exit.
        /// </remarks>
        public void Dispose()
        {
            // Dispose of unmanaged resources.
            Dispose(true);
            // Suppress finalization.
            GC.SuppressFinalize(this);
        }


        /// <summary>
        ///     A destructor for the current <see cref="WardenProcess"/>.
        /// </summary>
        ~WardenProcess()
        {
            Dispose(false);
        }


        /// <summary>
        ///     Disposes the current <see cref="WardenProcess"/> and all of its child processes.
        /// </summary>
        /// <param name="disposeManaged">Indicates if dispose was called by the finalizer or not.</param>
        private void Dispose(bool disposeManaged)
        {
            // the warden process has been disposed so there is nothing left to do.
            if (_disposed)
            {
                return;
            }
            if (disposeManaged)
            {
                _watcher?.Dispose();
                if (_onFound is not null)
                {
                    foreach (var d in _onFound.GetInvocationList())
                    {
                        _onFound -= d as EventHandler<WardenProcess>;
                    }
                    _onFound = null;
                }
                if (_onExit is not null)
                {
                    foreach (var d in _onExit.GetInvocationList())
                    {
                        _onExit -= d as EventHandler<int>;
                    }
                    _onExit = null;
                }
            }
            _disposed = true;
        }

        /// <summary>
        ///     An event that is fired when the current <see cref="WardenProcess"/> has been associated with a system process after
        ///     requesting an asynchronous launch.
        /// </summary>
        internal event EventHandler<WardenProcess> OnFound
        {
            add => _onFound += value;
            remove => _onFound -= value;
        }

        /// <summary>
        ///     An event that is fired when a child process of the current <see cref="WardenProcess"/> exits.
        /// </summary>
        public event EventHandler<int> OnChildExit
        {
            add => _onChildExit += value;
            remove => _onChildExit -= value;
        }

        /// <summary>
        ///     An event that is fired when the current <see cref="WardenProcess"/> exits.
        /// </summary>
        public event EventHandler<int> OnExit
        {
            add => _onExit += value;
            remove => _onExit -= value;
        }

        /// <summary>
        ///     Adds the specified <paramref name="child"/> process to the current processes <see cref="Children"/> collection.
        /// </summary>
        /// <param name="child">The child process that will be added.</param>
        /// <exception cref="NullReferenceException">
        ///     Thrown when trying to add a child process to the <see cref="Children"/>
        ///     collection while its null.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown when the specified <paramref name="child"/> argument is null</exception>
        /// <exception cref="ObjectDisposedException">
        ///     Thrown when trying to add a child process after the
        ///     <see cref="WardenProcess"/> instance was disposed.
        /// </exception>
        internal void AddChild(WardenProcess child)
        {
            if (child is null)
            {
                throw new ArgumentNullException(nameof(child));
            }
            if (Children is null)
            {
                throw new NullReferenceException(nameof(Children));
            }
            Children.Add(child);
            child.OnExit += OnChildProcessExitHandler;
        }


        /// <summary>
        ///     A event handler that is raised when a child process of the current <see cref="WardenProcess"/> exits.
        /// </summary>
        private void OnChildProcessExitHandler(object? sender, int e)
        {
            if (_onChildExit is not null && sender is WardenProcess child)
            {
                var exitCode = child.ExitCode;
                _onChildExit(child, exitCode);
                if (HasTreeExited)
                {
                    // All descendant processes have stopped execution so we clean up the event subscribers.
                    // Realistically a child process would not be added long after all others have exited.
                    foreach (var d in _onChildExit.GetInvocationList())
                    {
                        _onChildExit -= d as EventHandler<int>;
                    }
                    _onChildExit = null;
                }
            }
        }


        /// <summary>
        ///     Initializes the current <see cref="WardenProcess"/> object and associates it with the specified process
        ///     <paramref name="info"/>.
        /// </summary>
        /// <param name="info">The process information the current <see cref="WardenProcess"/> object will be tied to.</param>
        /// <exception cref="UnauthorizedAccessException">
        ///     Thrown when the AppDomain lacks the necessary elevation to obtain a
        ///     handle on the target process.
        /// </exception>
        internal void Initialize(ProcessInfo info)
        {
            // set to null so the system process monitor skips further matching attempts.
            Monitor = null;
            Info = info;
            Children = new ConcurrentBag<WardenProcess>();
            if (ProcessNative.CurrentProcessId == info.Id)
            {
                _isCurrentProcess = true;
                return;
            }
            _watcher = new ProcessHook(info.Id);
            _watcher.Exited += OnWatcherExit;
            _watcher.Start();
        }

        /// <summary>
        ///     A event handler that is raised when the underlying system process object exits.
        /// </summary>
        private void OnWatcherExit(object? sender, EventArgs e)
        {
            if (_onExit is not null && _watcher is {HasExited: true})
            {
                var exitCode = _watcher.ExitCode;
                _onExit(this, exitCode);
                Dispose();
            }
        }

        /// <summary>
        ///     Attempts to find a child process of the current <see cref="WardenProcess"/> that belongs to the specified
        ///     <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The system-unique identifier of the process.</param>
        /// <returns>If the child is found its <see cref="WardenProcess"/> is returned. Otherwise this function returns null.</returns>
        internal WardenProcess? FindChildProcess(int id)
        {
            if (Children is null)
            {
                return null;
            }
            foreach (var child in Children)
            {
                if (child is {Info: not null})
                {
                    if (child.Info.Id == id)
                    {
                        return child;
                    }
                    if (child.Children is null)
                    {
                        continue;
                    }
                    if (child.FindChildProcess(id) is { } extendedChild)
                    {
                        return extendedChild;
                    }
                }
            }
            return null;
        }

        /// <summary>
        ///     Raises the event indicating that the current <see cref="WardenProcess"/> is now initialized with known process
        ///     information.
        /// </summary>
        internal void RaiseOnFound()
        {
            _onFound?.Invoke(null, this);
        }

        /// <summary>
        /// Blocks the calling thread until the associated process terminates.
        /// </summary>
        /// <param name="milliseconds">The amount of time, in milliseconds, to wait for the associated process to exit. A value of 0 specifies an immediate return, and a value of -1 specifies an infinite wait.</param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the current <see cref="HasProcessAssociation"/> is false or the <see cref="WardenProcess"/> object is associated with the current application.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        ///     Thrown when trying to kill a <see cref="WardenProcess"/> that has already
        ///     been disposed.
        /// </exception>
        /// <returns>true if the associated process has exited; otherwise, false.</returns>
        /// <remarks>To avoid blocking use the <see cref="OnExit"/> event. If you wish to wait on an arbitrary process use <see cref="SystemProcessMonitor.WaitForExit"/></remarks>
        public bool WaitForExit(int milliseconds = -1)
        {
            if (_isCurrentProcess)
            {
                throw new InvalidOperationException("This WardenProcess object is associated with the current application and cannot wait on itself to exit.");
            }
            if (_disposed)
            {
                throw new ObjectDisposedException("This WardenProcess object has been disposed.");
            }
            if (!HasProcessAssociation)
            {
                throw new InvalidOperationException("There is no system process associated with this WardenProcess object.");
            }
            return _watcher!.WaitForExit(milliseconds);
        }

        /// <summary>
        ///     Sends a termination signal to the underlying system process object and blocks the calling thread until it has exited.
        /// </summary>
        /// <param name="entireProcessTree">
        ///     If set to true the termination signal will be sent to all processes descendant from the
        ///     current <see cref="WardenProcess"/>.
        /// </param>
        /// <param name="exitCode">An optional exit code to be used by the process and threads terminated as a result of this call. </param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the current <see cref="HasProcessAssociation"/> is false or the <see cref="WardenProcess"/> object is associated with the current application.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        ///     Thrown when trying to kill a <see cref="WardenProcess"/> that has already
        ///     been disposed.
        /// </exception>
        public void Terminate(bool entireProcessTree, int exitCode = 0)
        {
            if (_isCurrentProcess)
            {
                throw new InvalidOperationException("This WardenProcess object is associated with the current application and cannot terminate itself.");
            }
            if (_disposed)
            {
                throw new ObjectDisposedException("This WardenProcess object has been disposed.");
            }
            if (!HasProcessAssociation)
            {
                throw new InvalidOperationException("There is no system process associated with this WardenProcess object.");
            }
            ProcessNative.TerminateProcess(Info!.Id, exitCode);
            if (entireProcessTree && Children is not null)
            {
                foreach (var child in Children)
                {
                    if (!child.HasExited && !child.IsDisposed)
                    {
                        child.Terminate(entireProcessTree, exitCode);
                    }
                }
            }
        }


    #region Factory Methods

        /// <summary>
        ///     <para>
        ///         Starts the process resource that is specified by the start <paramref name="info"/> (for
        ///         example, the file name of the process to start) as the current interactive user.
        ///     </para>
        /// </summary>
        /// <param name="info">
        ///     The <see cref="WardenStartInfo"/> that contains the information that is used to start the process,
        ///     including the file name and any command-line arguments.
        /// </param>
        /// <returns>A new  <see cref="WardenProcess"/> that is associated with the created process resource.</returns>
        /// <remarks>
        ///     The calling application must be running within the context of the LocalSystem account or this will throw.
        /// </remarks>
        public static WardenProcess? StartAsUser(WardenStartInfo info)
        {
            if (!WindowsIdentity.GetCurrent().IsSystem)
            {
                throw new UnauthorizedAccessException(" The calling application must be running within the context of the LocalSystem account");
            }
            if (string.IsNullOrWhiteSpace(info.FileName))
            {
                throw new ArgumentException(nameof(info.FileName));
            }
            if (info.Track && string.IsNullOrWhiteSpace(info.TargetImage))
            {
                throw new ArgumentException(nameof(info.TargetImage));
            }
            if (info.Track && !SystemProcessMonitor.Running)
            {
                throw new InvalidOperationException("Cannot track process because the System Process Monitor is not running.");
            }
            if (string.IsNullOrWhiteSpace(info.WorkingDirectory) && Path.GetDirectoryName(info.FileName) is { } workingDirectory)
            {
                info.WorkingDirectory = workingDirectory;
            }
            return info.RaisePrivileges ? InteractiveProcessCreator.AsLocalSystem(info) : InteractiveProcessCreator.AsUser(info);
        }


        /// <summary>
        ///     <para>
        ///         Starts the process resource that is specified by the start <paramref name="info"/> (for
        ///         example, the file name of the process to start) using the operating system shell to start the process.
        ///     </para>
        ///     <para>
        ///         If tracking is disabled no process will ever be associated with this start call
        ///         even if it succeeds, so this function will return null.
        ///     </para>
        ///     <para>
        ///         If tracking is enabled this function will returned an orphaned <see cref="WardenProcess"/>.
        ///         It will be automatically associated with a system process object that matches the specified target image once
        ///         it begins executing.
        ///     </para>
        /// </summary>
        /// <param name="info">
        ///     The <see cref="WardenStartInfo"/> that contains the information that is used to start the process,
        ///     including the file name and any command-line arguments.
        /// </param>
        /// <param name="onFoundHandler">
        ///     If tracking is enabled an optional event handler can be supplied that is raised once the
        ///     <see cref="WardenProcess"/> has been associated with a system process object.
        /// </param>
        /// <returns>A new orphaned <see cref="WardenProcess"/>, or null if no tracking was enabled.</returns>
        public static WardenProcess? Start(WardenStartInfo info, EventHandler<WardenProcess>? onFoundHandler = null)
        {
            if (info is null)
            {
                throw new ArgumentException(nameof(info));
            }
            if (string.IsNullOrWhiteSpace(info.FileName))
            {
                throw new ArgumentException(nameof(info.FileName));
            }
            if (info.Track && string.IsNullOrWhiteSpace(info.TargetImage))
            {
                throw new ArgumentException(nameof(info.TargetImage));
            }
            if (info.Track && !SystemProcessMonitor.Running)
            {
                throw new InvalidOperationException("Cannot track process because the System Process Monitor is not running.");
            }
            if (info.FileName.IsValidUri() && !UriHelpers.IsValidCustomProtocol(info.FileName))
            {
                throw new InvalidOperationException($"No file or protocol association could be found for '{info.FileName}'");
            }
            // If WorkingDirectory is an empty string, the current directory is understood to contain the executable.
            if (string.IsNullOrWhiteSpace(info.WorkingDirectory))
            {
                info.WorkingDirectory = Environment.CurrentDirectory;
            }
            WardenProcess? process = null;
            if (info.Track)
            {
                process = new WardenProcess(new MonitorInfo(info.TargetImage), info.FilteredImages);
                if (onFoundHandler is not null)
                {
                    process.OnFound += onFoundHandler;
                }
                SystemProcessMonitor.TrackProcessFamily(process);
            }
            ShellExecute.Start(info);
            return process;
        }

        /// <summary>
        ///     Launches a Microsoft Store / Universal Windows Platform app.
        /// </summary>
        /// <param name="info">
        ///     The <see cref="WardenStartInfo"/> that contains the information that is used to start the process,
        ///     including the PackageFamilyName, ApplicationId, and any command-line arguments.
        /// </param>
        /// <returns>A <see cref="WardenProcess"/> instance that is associated with the activated application.</returns>
        public static WardenProcess? StartUniversalApp(WardenStartInfo info)
        {
            if (info is null)
            {
                throw new ArgumentException(nameof(info));
            }
            if (string.IsNullOrWhiteSpace(info.PackageFamilyName))
            {
                throw new ArgumentException(nameof(info.PackageFamilyName));
            }
            // some Microsoft Store apps have an empty application ID.
            if (string.IsNullOrWhiteSpace(info.ApplicationId))
            {
                info.ApplicationId = string.Empty;
            }
            var aumid = $"{info.PackageFamilyName}!{info.ApplicationId}";
            var mgr = new ApplicationActivationManager();
            mgr.ActivateApplication(aumid, info.Arguments, ActivateOptionsEnum.NoErrorUI, out var processId);
            return GetProcessById((int) processId, info.Track, info.FilteredImages);
        }


        /// <summary>
        ///     Creates a new <see cref="WardenProcess"/> from the specified <paramref name="processId"/>.
        /// </summary>
        /// <param name="processId">The system-unique identifier of a process resource.</param>
        /// <param name="trackProcessTree">
        ///     If set to true the newly created <see cref="WardenProcess"/> will have its process family tree
        ///     tracked.
        /// </param>
        /// <param name="filteredImages">
        ///     A collection of process image names that will not be added as children to the created
        ///     <see cref="WardenProcess"/> when tracking is enabled.
        /// </param>
        /// <exception cref="ArgumentException">
        ///     The process specified by the <paramref name="processId"/> parameter is not running
        ///     or can't be accessed.
        /// </exception>
        /// <returns>A <see cref="WardenProcess"/> instance that is associated with the <paramref name="processId"/> parameter.</returns>
        public static WardenProcess? GetProcessById(int processId, bool trackProcessTree, IEnumerable<string>? filteredImages = null)
        {
            var process = new WardenProcess(ProcessNative.GetProcessInfoById(processId), filteredImages);
            if (!trackProcessTree)
            {
                return process;
            }
            SystemProcessMonitor.TrackProcessFamily(process);
            return process;
        }


        /// <summary>
        /// Gets a new <see cref="WardenProcess"/> and associates it with the currently active process.
        /// </summary>
        /// <returns>A new <see cref="WardenProcess"/> associated with the process information of the calling application.</returns>
        public static WardenProcess GetCurrentProcess() => new(ProcessNative.GetCurrentProcessInfo());

    #endregion
    }
}