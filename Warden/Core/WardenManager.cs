using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Management;
using Warden.Core.Exceptions;
using Warden.Core.Utils;
using static Warden.Core.WardenProcess;

namespace Warden.Core
{
    public static class WardenManager
    {
        private static ManagementEventWatcher _processStartEvent;
        private static ManagementEventWatcher _processStopEvent;

        public static ConcurrentDictionary<Guid, WardenProcess> ManagedProcesses = new ConcurrentDictionary<Guid, WardenProcess>();

        public static bool Initialized;
        private static bool _killTressOnExit;


        /// <summary>
        ///     Creates the Warden service which monitors processes on the computer.
        /// </summary>
        /// <param name="killTressOnExit"></param>
        public static void Initialize(bool killTressOnExit = false)
        {
            _killTressOnExit = killTressOnExit;
            try
            {
                ShutdownUtils.RegisterEvents();
                _processStartEvent =
                    new ManagementEventWatcher(new WqlEventQuery {EventClassName = "Win32_ProcessStartTrace"});
                _processStopEvent =
                    new ManagementEventWatcher(new WqlEventQuery {EventClassName = "Win32_ProcessStopTrace"});
                _processStartEvent.EventArrived += ProcessStarted;
                _processStopEvent.EventArrived += ProcessStopped;
                _processStartEvent.Start();
                _processStopEvent.Start();
                Initialized = true;
            }
            catch (Exception ex)
            {
                throw new WardenException(ex.Message);
            }
        }

        /// <summary>
        ///     Flushes a top level process.
        /// </summary>
        /// <param name="processId"></param>
        public static void Flush(int processId)
        {
            try
            {
                WardenProcess process;
                var key = ManagedProcesses.FirstOrDefault(x => x.Value.Id == processId).Key;
                ManagedProcesses.TryRemove(key, out process);
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
            var processId = int.Parse(e.NewEvent.Properties["ProcessID"].Value.ToString());
            HandleStoppedProcess(processId);
        }

        /// <summary>
        ///     Attempt to update the state of the process if its found in a tree.
        /// </summary>
        /// <param name="processId"></param>
        private static void HandleStoppedProcess(int processId)
        {
            foreach (var managed in ManagedProcesses.Values)
            {
                if (managed.Id == processId)
                {
                    managed.UpdateState(ProcessState.Dead);
                    break;
                }
                var child = FindChildById(processId, managed.Children);
                child?.UpdateState(ProcessState.Dead);
            }
        }

        /// <summary>
        ///     Shutdown the Warden service
        /// </summary>
        public static void Stop()
        {
            _processStartEvent?.Stop();
            _processStopEvent?.Stop();
            if (_killTressOnExit)
            {
                foreach (var managed in ManagedProcesses.Values)
                {
                    managed.Kill();
                }
            }
            ManagedProcesses.Clear();
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
            var processName = e.NewEvent.Properties["ProcessName"].Value.ToString();
            var processId = int.Parse(e.NewEvent.Properties["ProcessID"].Value.ToString());
            var processParent = int.Parse(e.NewEvent.Properties["ParentProcessID"].Value.ToString());
            //needed for uri promises
            foreach (var managed in ManagedProcesses.Values)
            {
                if (managed.Id <= 100000 || !managed.Name.Equals(Path.GetFileNameWithoutExtension(processName)))
                {
                    continue;
                }
                managed.Id = processId;
                managed.Name = processName;
            }
            HandleNewProcess(processName, processId, processParent);
        }

        /// <summary>
        ///     Attempts to add a new process as a child inside a tree if its parent is present.
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="processId"></param>
        /// <param name="processParent"></param>
        private static void HandleNewProcess(string processName, int processId, int processParent)
        {
            foreach (var managed in ManagedProcesses.Values)
            {
                if (managed.Id == processParent)
                {
                    if (managed.AddChild(CreateProcessFromId(processName, managed.Id, processId)))
                    {
                        managed.InvokeProcessAdd(new ProcessAddedEventArgs
                        {
                           Name = processName,
                           Id = processId,
                           ParentId = managed.Id
                        });
                        break;
                    }
                }
                var child = FindChildById(processParent, managed.Children);
                if (child == null)
                {
                    continue;
                }
                if (child.AddChild(CreateProcessFromId(processName, child.Id, processId)))
                {
                    managed.InvokeProcessAdd(new ProcessAddedEventArgs
                    {
                        Name = processName,
                        Id = processId,
                        ParentId = child.Id
                    });
                    break;
                }
            }
        }
    }
}