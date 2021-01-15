using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Warden.Core;
using Warden.Utils;
using Warden.Windows;

namespace Warden.Monitor
{
    /// <summary>
    ///     Monitors the system and its process objects for changes.
    /// </summary>
    public static class SystemProcessMonitor
    {
        /// <summary>
        ///     A cancellation token source that controls the lifetime of <see cref="Monitor"/>
        /// </summary>
        private static CancellationTokenSource? _cancelToken;

        /// <summary>
        ///     A thread-safe collection of <see cref="WardenProcess"/> instances which have their family trees tracked.
        /// </summary>
        private static ConcurrentDictionary<Guid, WardenProcess>? _trackedProcesses;

        /// <summary>
        ///     The thread that <see cref="Monitor"/> is executing on.
        /// </summary>
        private static Thread? _monitorThread;

        /// <summary>
        ///     Options that will control the behavior of the monitor.
        /// </summary>
        private static MonitorOptions _options = null!;

        /// <summary>
        ///     Indicates if the system process monitor is actively running.
        /// </summary>
        internal static bool Running { get; private set; }
        
        /// <summary>
        ///     A delegate for process started events.
        /// </summary>
        private static EventHandler<ProcessInfo>? _onProcessStarted;

        /// <summary>
        ///     A delegate for process stopped events.
        /// </summary>
        private static EventHandler<ProcessInfo>? _onProcessStopped;

        /// <summary>
        ///     These applications are ones we've identified as troublesome to session management. For example if you launch a game
        ///     and that game launches a browser, Warden will assign it as a child. <br/><br/>
        ///     That means if the game closes but the browser never does (which is common),
        ///     <see cref="WardenProcess.HasTreeExited"/> will always be false. To disable this set
        ///     <see cref="MonitorOptions.DisableProcessBlacklist"/> to true.
        /// </summary>
        private static readonly HashSet<string> ProcessBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost",
            "svchost.exe",
            "runtimebroker",
            "runtimebroker.exe",
            "backgroundtaskhost",
            "backgroundtaskhost.exe",
            "gamebarpresencewriter",
            "gamebarpresencewriter.exe",
            "searchfilterhost",
            "searchfilterhost.exe",
            "dllhost",
            "dllhost.exe",
            "EasyAntiCheat.exe",
            "iexplore.exe",
            "msedge.exe",
            "MicrosoftEdge.exe",
            "MicrosoftEdgeCP.exe",
            "MicrosoftEdgeSH.exe",
            "firefox.exe",
            "chrome.exe",
            "brave.exe",
            "opera.exe",
            "tor.exe",
            "vivaldi.exe"
        };

        /// <summary>
        ///     Starts the system process monitor.
        /// </summary>
        /// <param name="options"></param>
        /// <remarks>
        ///     Process monitoring takes place on a background thread.
        /// </remarks>
        public static void Start(MonitorOptions options)
        {
            if (Running)
            {
                return;
            }
            _trackedProcesses = new ConcurrentDictionary<Guid, WardenProcess>();
            Running = true;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _cancelToken = new CancellationTokenSource();
            using var readyEvent = new ManualResetEvent(false); 
            _monitorThread = new Thread(Monitor) {Name = "Warden System Process Monitor", IsBackground = true};
            _monitorThread.Start(readyEvent);
            // Wait for the first snapshot of the process list to be taken before returning.
            // Without doing this an immediate launch of a process after calling Start will lead to misses.
            readyEvent.WaitOne();

        }

        /// <summary>
        ///     Stops the system process monitor.
        /// </summary>
        public static void Stop()
        {
            if (!Running)
            {
                throw new InvalidOperationException("The system process monitor is already stopped.");
            }
            if (_cancelToken is {Token: {CanBeCanceled: true}} source)
            {
                source.Cancel();
                _cancelToken = null;
            }
            if (_trackedProcesses is not null)
            {
                foreach (var process in _trackedProcesses.Values)
                {
                    if (process is not null)
                    {
                        if (_options.KillTrackedProcesses)
                        {
                            process.TryTerminate(_options.RecursiveKill);
                        }
                    }
                }
                _trackedProcesses.Clear();
                _trackedProcesses = null;
            }
            if (_onProcessStarted is not null)
            {
                foreach (var d in _onProcessStarted.GetInvocationList())
                {
                    _onProcessStarted -= d as EventHandler<ProcessInfo>;
                }
                _onProcessStarted = null;
            }
            if (_onProcessStopped is not null)
            {
                foreach (var d in _onProcessStopped.GetInvocationList())
                {
                    _onProcessStopped -= d as EventHandler<ProcessInfo>;
                }
                _onProcessStopped = null;
            }
            _monitorThread?.Join();
            _monitorThread = null;
            Running = false;
        }

        /// <summary>
        ///     Polls the underlying operating system for system process objects and diffs them.
        /// </summary>
        /// <remarks>
        ///     Polling rate specified by using <see cref="MonitorOptions.PollingInterval"/>. The default interval is one second.
        /// </remarks>
        private static void Monitor(object? state)
        {
            if (state is ManualResetEvent readyEvent)
            {
                var currentProcessSnapshot = new HashSet<ProcessInfo>(ProcessNative.GetProcesses());
                readyEvent.Set();
                while (_cancelToken is not null && !_cancelToken.IsCancellationRequested)
                {
                    var newSnapshot = new HashSet<ProcessInfo>(ProcessNative.GetProcesses());
                    var updateSnapshot = new HashSet<ProcessInfo>();
                    // find newly launched processes
                    foreach (var process in newSnapshot.Where(process => !currentProcessSnapshot.Contains(process)))
                    {
                        currentProcessSnapshot.Add(process);
                        OnProcessStart(process);
                    }
                    foreach (var knownProcess in currentProcessSnapshot)
                    {
                        if (newSnapshot.Contains(knownProcess))
                        {
                            updateSnapshot.Add(knownProcess);
                        }
                        else
                        {
                            OnProcessStop(knownProcess);
                        }
                    }
                    currentProcessSnapshot = updateSnapshot;
                    Thread.Sleep(_options!.PollingInterval);
                }
            }
        }

        /// <summary>
        ///     Event handler that will be called when an orphan process that with no known family tree has stopped execution.
        /// </summary>
        public static event EventHandler<ProcessInfo> OnProcessStopped
        {
            add => _onProcessStopped += value;
            remove => _onProcessStopped -= value;
        }

        /// <summary>
        ///     Event handler that will be called when an orphan process that with no known family tree has started execution.
        /// </summary>
        public static event EventHandler<ProcessInfo> OnProcessStarted
        {
            add => _onProcessStarted += value;
            remove => _onProcessStarted -= value;
        }

        /// <summary>
        ///     Waits for the specified process to exit.
        /// </summary>
        /// <param name="processId">The system-unique identifier of a process resource.</param>
        /// <param name="timeout">
        ///     The amount of time, in milliseconds, to wait for the associated process to exit. A value of 0
        ///     specifies an immediate return, and a value of -1 specifies an infinite wait.
        /// </param>
        /// <returns>The code that the associated process specified when it terminated (if any).</returns>
        /// <remarks>
        /// It is safe for a non-elevated process to use this method to wait for the exit of a process in a different session or of higher privilege.
        /// </remarks>
        public static int WaitForExit(int processId, int timeout = -1)
        {
            // an event so the wait thread spawned below can signal when it completes.
           using var processExitEvent = new ManualResetEvent(false);
            // the process exit code; if none could be retrieved this will stay zero.
            var exitCode = 0;

            var waitThread = new Thread(state =>
            {
                if (state is ManualResetEvent resetEvent)
                {
                    // check to ensure the process is running 
                    if (ProcessNative.IsProcessRunning(processId))
                    {
                        try
                        {
                            // the ideal path which will use the process wait handle.
                            using var processWatcher = new ProcessHook(processId);
                            processWatcher.WaitForExit(timeout);
                            exitCode = processWatcher.ExitCode;
                        }
                        catch
                        {
                            var now = DateTime.UtcNow;
                            // we were unable to acquire a process wait handle so now we have to manually poll.
                            // not the worst thing but we won't get an exit code.
                            while (ProcessNative.IsProcessRunning(processId))
                            {
                                // if a timeout was specified bail out if we're over it.
                                if (timeout > -1 && DateTime.UtcNow > now.AddMilliseconds(timeout))
                                {
                                    break;
                                }
                                Thread.Sleep(TimeSpan.FromSeconds(1));
                            }
                        }
                    }
                    // signal the exit event
                    resetEvent.Set();
                }
            }) {Name = "Process Wait Thread", IsBackground = true};
            waitThread.Start(processExitEvent);
            // we do not need to specify a timeout here as the background thread
            // will take care of that.
            processExitEvent.WaitOne();
            waitThread.Join();
            return exitCode;
        }

        /// <summary>
        ///     The routine responsible for handling when a process has begun executing.
        /// </summary>
        /// <param name="newProcessInfo">The process info corresponding to a newly launched process.</param>
        private static void OnProcessStart(ProcessInfo newProcessInfo)
        {
            // If the new process info was matched to a tracking request
            // we return as no other steps are possible. 
            if (TryMatch(newProcessInfo))
            {
                return;
            }
            // Likewise if the new process information can be assigned as a child or grandchild of a tracked process, then we return.
            if (TryFindFamily(newProcessInfo))
            {
                return;
            }
            // Only announce orphan processes. 
            AnnounceProcessStart(newProcessInfo);
        }


        /// <summary>
        ///     Attempts to match the specified <paramref name="newProcessInfo"/> to a process that requested asynchronous launch
        ///     tracking.
        /// </summary>
        /// <param name="newProcessInfo">The information corresponding to a newly launched process.</param>
        private static bool TryMatch(ProcessInfo newProcessInfo)
        {
            if (_trackedProcesses is not null)
            {
                foreach (var process in _trackedProcesses.Values)
                {
                    // Don't process non-tracked processes 
                    if (process.Monitor is null)
                    {
                        continue;
                    }
                    // In some scenarios a process such as "HeroesOfTheStorm" shuts itself down after spawning a sub process named "HeroesOfTheStorm_x64".
                    // To handle that we extract the most common substring between the target process name and the new process name.
                    var targetImageName = Path.GetFileNameWithoutExtension(process.Monitor.TargetImage);
                    StringHelpers.LongestCommonSubstring(targetImageName, newProcessInfo.Name, out var commonImageName);
                    // First try matching the target image to the new process image. This will be good enough 99% of the time.
                    if (process.Monitor.TargetImage.Equals(newProcessInfo.Image, StringComparison.OrdinalIgnoreCase)
                        || targetImageName.Equals(commonImageName, StringComparison.OrdinalIgnoreCase))
                    {
                        // update the info of the tracked process.
                        process.Initialize(newProcessInfo);
                        // raise its callback.
                        process.RaiseOnFound();
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        ///     Attempts to match the specified <paramref name="newProcessInfo"/> to the family tree of a tracked process.
        /// </summary>
        /// <param name="newProcessInfo">The information corresponding to a newly launched process.</param>
        private static bool TryFindFamily(ProcessInfo newProcessInfo)
        {
            if (_trackedProcesses is not null)
            {
                foreach (var process in _trackedProcesses.Values)
                {
                    if (process.Info is null)
                    {
                        continue;
                    }
                    var newProcessImageName = Path.GetFileName(newProcessInfo.Image);
                    // check to ensure the tracked process was created before the new process since IDs are recyclable.
                    if (process.Info.Id == newProcessInfo.ParentProcessId && process.Info.CreationDate < newProcessInfo.CreationDate)
                    {
                        if (process.FilteredImageNames is not null && process.FilteredImageNames.Contains(newProcessImageName) || !_options.DisableProcessBlacklist && ProcessBlacklist.Contains(newProcessImageName))
                        {
                            return false;
                        }
                        process.AddChild(new WardenProcess(newProcessInfo));
                        return true;
                    }
                    var child = process.FindChildProcess(newProcessInfo.ParentProcessId);
                    if (child is {Info: not null})
                    {
                        // same as above, we need to make sure the child was created before the new process before giving it a grandchild.
                        if (child.Info.CreationDate < newProcessInfo.CreationDate)
                        {
                            
                            if (child.FilteredImageNames is not null && child.FilteredImageNames.Contains(newProcessImageName) || !_options.DisableProcessBlacklist && ProcessBlacklist.Contains(newProcessImageName))
                            {
                                return false;
                            }
                            child.AddChild(new WardenProcess(newProcessInfo));
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        ///     Raises the <see cref="OnProcessStarted"/> event with the specified <paramref name="newProcessInfo"/>.
        /// </summary>
        /// <param name="newProcessInfo">The information corresponding to a newly launched process.</param>
        private static void AnnounceProcessStart(ProcessInfo newProcessInfo)
        {
            if (_onProcessStarted is { } handler)
            {
                handler.Invoke(null, newProcessInfo);
            }
        }

        /// <summary>
        ///     The routine responsible for handling when a process has stopped executing.
        /// </summary>
        /// <param name="processInfo">The process info corresponding to a now stopped process.</param>
        private static void OnProcessStop(ProcessInfo processInfo)
        {
            HandleStoppedProcess(processInfo);
        }

        /// <summary>
        ///     Attempts to match the specified <paramref name="processInfo"/> to a tracked process and updates its state.
        ///     If the process does not belong to a tracked process it is raised to <see cref="OnProcessStopped"/>.
        /// </summary>
        /// <param name="processInfo">The process info corresponding to a now stopped process.</param>
        private static void HandleStoppedProcess(ProcessInfo processInfo)
        {
            if (_trackedProcesses is not null)
            {
                var wasProcessTracked = false;
                foreach (var kvp in _trackedProcesses)
                {
                    var key = kvp.Key;
                    var process = kvp.Value;
                    if (process.Info is null)
                    {
                        continue;
                    }
                    if (process.Info.Id == processInfo.Id)
                    {
                        if (process.HasExited)
                        {
                            process.Dispose();
                        }
                        _trackedProcesses.TryRemove(key, out _);
                        wasProcessTracked = true;
                        break;
                    }
                    if (process.FindChildProcess(processInfo.Id) is { } child)
                    {
                        if (child.HasExited)
                        {
                            child.Dispose();
                        }
                        _trackedProcesses.TryRemove(key, out _);
                        wasProcessTracked = true;
                        break;
                    }
                }
                if (!wasProcessTracked && _onProcessStopped is { } handler)
                {
                    handler.Invoke(null, processInfo);
                }
            }
        }

        /// <summary>
        ///     Begins tracking the process family tree for the specified <paramref name="process"/>.
        /// </summary>
        /// <param name="process">The process to track.</param>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when a null <see cref="WardenProcess"/> object is passed into the
        ///     method.
        /// </exception>
        /// <exception cref="NullReferenceException">Thrown when <see cref="_trackedProcesses"/> dictionary is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown the method is called while <see cref="SystemProcessMonitor"/> is not
        ///     running.
        /// </exception>
        internal static void TrackProcessFamily(WardenProcess process)
        {
            if (!Running)
            {
                throw new InvalidOperationException("Cannot begin tracking the provided Warden process as the system monitor is not running.");
            }
            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }
            if (_trackedProcesses is null)
            {
                throw new NullReferenceException("The process tracking dictionary is null");
            }
            var key = Guid.NewGuid();
            if (!_trackedProcesses.TryAdd(key, process))
            {
                throw new InvalidOperationException("Failed to add process to the tracking dictionary.");
            }
        }
    }
}