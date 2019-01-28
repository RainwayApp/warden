using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using Warden.Properties;
using Warden.Windows;
using static Warden.Core.WardenProcess;

namespace Warden.Core
{

    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class WardenOptions
    {
        /// <summary>
        /// If set to true, Warden will watch for the exit event of the host process and kill all 
        /// monitored processes as it shutsdown.
        /// </summary>
        public bool CleanOnExit { get; set; }

        /// <summary>
        /// If set to true, when WardenProcess.Kill is called it will kill the entire process tree.
        /// </summary>
        public bool DeepKill { get; set; }

        /// <summary>
        /// If set to true, Warden will read the portable executable file headers for the binary.
        /// </summary>
        public bool ReadFileHeaders { get; set; }

        /// <summary>
        /// If set to true, Warden will kill processes via Process.Kill instead of Tskill
        /// </summary>
        public bool UseLegacyKill { get; set; }

        /// <summary>
        /// Processes not to kill
        /// </summary>
        public IEnumerable<string> KillWhitelist { get; set; } = Array.Empty<string>();

        /// <summary>
        /// WMI Polling Interval
        /// </summary>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    }

    public static class WardenManager
    {
        private static ManagementEventWatcher _processStartEvent;
        private static ManagementEventWatcher _processStopEvent;

        public static IWardenLogger Logger = new DummyWardenLogger();

        public static ConcurrentDictionary<Guid, WardenProcess> ManagedProcesses = new ConcurrentDictionary<Guid, WardenProcess>();

        public delegate void UntrackedProcessHandler(object sender, UntrackedProcessEventArgs e);
        public static event UntrackedProcessHandler OnUntrackedProcessAdded;


        /// <summary>
        ///     Creates the Warden service which monitors processes on the computer.
        /// </summary>
        /// <param name="options"></param>
        public static void Initialize(WardenOptions options)
        {
            if (!Api.IsAdmin())
            {
                throw new WardenManageException(Resources.Exception_No_Admin);
            }

            Stop();
            Options = options ?? throw new WardenManageException(Resources.Exception_No_Options);
            try
            {
                ShutdownUtils.RegisterEvents();
                var wmiOptions = new ConnectionOptions
                {
                    Authentication = AuthenticationLevel.Default,
                    EnablePrivileges = true,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Timeout = TimeSpan.MaxValue
                };
                var scope = new ManagementScope($@"\\{Environment.MachineName}\root\cimv2", wmiOptions);
                scope.Connect();
                _processStartEvent = new ManagementEventWatcher(scope, new WqlEventQuery("__InstanceCreationEvent", options.PollingInterval, "TargetInstance isa \"Win32_Process\""));
                _processStartEvent.EventArrived += ProcessStarted;
                _processStopEvent = new ManagementEventWatcher(scope, new WqlEventQuery("__InstanceDeletionEvent", options.PollingInterval, "TargetInstance isa \"Win32_Process\""));
                _processStopEvent.EventArrived += ProcessStopped;
                _processStartEvent.Start();
                _processStopEvent.Start();
                Initialized = true;
                Logger?.Debug("Initialized");
            }
            catch (Exception ex)
            {
                throw new WardenException(ex.Message, ex);
            }
        }

        public static bool Initialized { get; private set; }

        public static WardenOptions Options { get; private set; }

        /// <summary>
        ///     Flushes a top level process.
        /// </summary>
        /// <param name="processId"></param>
        public static void Flush(int processId)
        {
            try
            {
                var key = ManagedProcesses.FirstOrDefault(x => x.Value.Id == processId).Key;
                ManagedProcesses.TryRemove(key, out _);
            }
            catch
            {
                //
            }
        }

        /// <summary>
        ///     Fired when a process dies.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void ProcessStopped(object sender, EventArrivedEventArgs e)
        {
            try 
            {
                var targetInstance = (ManagementBaseObject) e.NewEvent["TargetInstance"];
                var processId      = int.Parse(targetInstance["ProcessId"].ToString());
                targetInstance.Dispose();
                e.NewEvent.Dispose();
                #if DEBUG
                Logger?.Debug($"{processId} stopped");
                #endif
                new Thread(() =>
                           {
                               try
                               {
                                   HandleStoppedProcess(processId);
                               }
                               catch(Exception ex)
                               {
                                   Logger?.Error(ex.ToString());
                               }                                   
                           }).Start();
            }
            catch(Exception ex)
            {
                Logger?.Error(ex.ToString());
            }
        }

        /// <summary>
        ///     Attempt to update the state of the process if its found in a tree.
        /// </summary>
        /// <param name="processId"></param>
        private static void HandleStoppedProcess(int processId)
        {
            Parallel.ForEach(ManagedProcesses.Values, managed =>
            {
                if (managed.Id == processId)
                {
                    managed.UpdateState(ProcessState.Dead);
                    return;
                }
                var child = FindChildById(processId, managed.Children);
                child?.UpdateState(ProcessState.Dead);
            });
        }

        /// <summary>
        ///     Shutdown the Warden service
        /// </summary>
        public static void Stop()
        {
            _processStartEvent?.Stop();
            _processStopEvent?.Stop();
            if (Options?.CleanOnExit == true)
            {
                Parallel.ForEach(ManagedProcesses.Values, managed =>
                {
                    managed.Kill();
                });
            }
            ManagedProcesses.Clear();
        }
        /// <summary>
        /// Uri launches when done asynchronously are stored with a large process id
        /// We then loop over our stored tree and see if the newly created process matches the target of our async launch
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="processId"></param>
        private static void PreProcessing(string processName, int processId)
        {
            //needed for uri promises
            Parallel.ForEach(ManagedProcesses.ToArray(), kvp =>
            {
                var process = kvp.Value;
                if (process.Id < 999999)
                {
                    return;
                }
                var newProcesWithoutExt = Path.GetFileNameWithoutExtension(processName);
                //Some games from Blizzard have sub executables, so while we look for "HeroesOfTheStorm" it might launch "HeroesOfTheStorm_x64"
                //So we find the most common occurrences in the string now 
                StringUtils.LongestCommonSubstring(process.Name, newProcesWithoutExt, out var subName);
                if (string.IsNullOrWhiteSpace(subName))
                {
                    return;
                }
                if (!process.Name.ToLower().RemoveWhitespace().Equals(subName, StringComparison.CurrentCultureIgnoreCase))
                {
                    return;
                }
                lock (process)
                {
                    ManagedProcesses[kvp.Key].Id = processId;
                    ManagedProcesses[kvp.Key].Name = newProcesWithoutExt;
                    ManagedProcesses[kvp.Key].Path = ProcessUtils.GetProcessPath(processId);
                    ManagedProcesses[kvp.Key].Arguments = ProcessUtils.GetCommandLine(processId);
                    ManagedProcesses[kvp.Key]?.FoundCallback?.Invoke(true);
                };
            });
        }

        /// <summary>
        ///     Detects when a new process launches on the PC, because of URI promises we will also try and update a root managed
        ///     process if
        ///     it is found to be starting up.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var targetInstance = (ManagementBaseObject) e.NewEvent["TargetInstance"];
                var processId = int.Parse(targetInstance["ProcessId"].ToString());
                var processParent = int.Parse(targetInstance["ParentProcessId"].ToString());
                var processName = targetInstance["Name"].ToString().Trim();
                if(processName == "?")
                {
                    try
                    {
                        processName = Path.GetFileName(ProcessUtils.GetProcessPath(processId))?.Trim();
                        if (string.IsNullOrWhiteSpace(processName))
                            processName = "Unknown";
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex.ToString());
                        processName = "Unknown";
                    }
                }

                targetInstance.Dispose();
                e.NewEvent.Dispose();
#if DEBUG
                Logger?.Debug($"{processName} ({processId}) started by {processParent}");
#endif
                PreProcessing(processName, processId);
                HandleNewProcess(processName, processId, processParent);
                HandleUnknownProcess(processName, processId, processParent);
            }

            catch (Exception ex)
            {
                Logger?.Error(ex.ToString());
            }
        }

        /// <summary>
        ///     Attempts to determine if a process is added but is dynamically tracked
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="processId"></param>
        /// <param name="processParent"></param>
        private static void HandleUnknownProcess(string processName, int processId, int processParent)
        {
            if(OnUntrackedProcessAdded == null)
            {
                return;
            }
            if(!ManagedProcesses.Values.AsParallel().Any(x => x.Id == processId || x.FindChildById(processId) != null))
            {
                var @event = new UntrackedProcessEventArgs(processName, processId, processParent);
                foreach(UntrackedProcessHandler Invoke in OnUntrackedProcessAdded.GetInvocationList())
                {
                    @event.Filters = null;
                    @event.Callback = null;
                    Invoke(null, @event);
                    if(@event.Create)
                    {
                        @event.Callback?.Invoke(GetProcessFromId(processId, @event.Filters));
                    }
                }
            }
        }



        /// <summary>
        ///     Attempts to add a new process as a child inside a tree if its parent is present.
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="processId"></param>
        /// <param name="processParent"></param>
        private static void HandleNewProcess(string processName, int processId, int processParent)
        {
            Parallel.ForEach(ManagedProcesses.Values, managed =>
            {
                if (managed.Id == processParent)
                {
                    var childProcess = CreateProcessFromId(processName, managed.Id, processId, managed.Filters);
                    if (!childProcess.IsFiltered() && managed.AddChild(childProcess))
                    {
                        managed.InvokeProcessAdd(new ProcessAddedEventArgs
                        {
                            Name = processName,
                            Id = processId,
                            ParentId = managed.Id
                        });

                        return;
                    }
                }
                var child = FindChildById(processParent, managed.Children);
                if (child == null)
                {
                    return;
                }
                var grandChild = CreateProcessFromId(processName, child.Id, processId, child.Filters);
                if (!grandChild.IsFiltered() && child.AddChild(grandChild))
                {
                    managed.InvokeProcessAdd(new ProcessAddedEventArgs
                    {
                        Name = processName,
                        Id = processId,
                        ParentId = child.Id
                    });
                }
            });
        }
    }
}
