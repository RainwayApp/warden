using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Launchers;
using Warden.Core.Utils;
using static Warden.Core.WardenManager;

namespace Warden.Core
{
    public class StateEventArgs : EventArgs
    {
        public StateEventArgs(int processId, ProcessState state)
        {
            State = state;
            Id = processId;
        }

        public ProcessState State { get; }

        public int Id { get; }
    }

    /// <summary>
    /// Provides access to local processes and their children in real-time.
    /// </summary>
    public class WardenProcess
    {
        public delegate void ChildStateUpdateHandler(object sender, StateEventArgs e);
        public delegate void StateUpdateHandler(object sender, StateEventArgs e);

        internal WardenProcess(string name, int id, string path, ProcessState state, string arguments, ProcessTypes uwp)
        {
            Name = name;
            Id = id;
            Path = path;
            State = state;
            Arguments = arguments;
            Type = uwp;
            Children = new ObservableCollection<WardenProcess>();
            long epochTicks = new DateTime(1970, 1, 1).Ticks;
            long unixTime = ((DateTime.UtcNow.Ticks - epochTicks) / TimeSpan.TicksPerSecond);
            TimeStamp = unixTime;
        }

        public long TimeStamp { get; set; }

        public ProcessTypes Type { get; }

        public ObservableCollection<WardenProcess> Children { get; internal set; }

        public int ParentId { get; internal set; }

        public int Id { get; internal set; }

        public ProcessState State { get; private set; }

        public string Path { get; }

        public string Name { get; internal set; }

        public string Arguments { get; }

        internal void SetParent(int parentId)
        {
            if (parentId == Id)
            {
                return;
            }
            ParentId = parentId;
        }

        /// <summary>
        /// Adds a child to a collection.
        /// </summary>
        /// <param name="child"></param>
        /// <returns></returns>
        internal bool AddChild(WardenProcess child)
        {
            if (child == null)
            {
                return false;
            }
            if (Children == null)
            {
                Children = new ObservableCollection<WardenProcess>();
            }
            child.OnChildStateChange += OnChildOnStateChange;
            Children.Add(child);
            return true;
        }

        private void OnChildOnStateChange(object sender, StateEventArgs stateEventArgs)
        {
            OnChildStateChange?.Invoke(this, stateEventArgs);
        }

        /// <summary>
        /// Updates the state of a process and fires events.
        /// </summary>
        /// <param name="state"></param>
        internal void UpdateState(ProcessState state)
        {
            State = state;
            OnStateChange?.Invoke(this, new StateEventArgs(Id, State));
            if (ParentId > 0)
            {
                OnChildStateChange?.Invoke(this, new StateEventArgs(Id, State));
            }
        }


        /// <summary>
        /// This event is fired when the process state has changed.
        /// </summary>
        public event StateUpdateHandler OnStateChange;
        /// <summary>
        /// This event is fired when a child for the current process has a state change.
        /// </summary>
        public event ChildStateUpdateHandler OnChildStateChange;


        /// <summary>
        /// Crawls a process tree and updates the states.
        /// </summary>
        public void RefreshTree()
        {
            try
            {
                var p = Process.GetProcessById(Id);
                p.Refresh();
                State = p.HasExited ? ProcessState.Dead : ProcessState.Alive;
            }
            catch
            {
                State = ProcessState.Dead;
            }
            if (Children != null)
            {
                RefreshChildren(Children);
            }
        }
        /// <summary>
        /// Updates the children of a process.
        /// </summary>
        /// <param name="children"></param>
        private void RefreshChildren(ObservableCollection<WardenProcess> children)
        {
            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }
                try
                {
                    var p = Process.GetProcessById(child.Id);
                    p.Refresh();
                    child.State = p.HasExited ? ProcessState.Dead : ProcessState.Alive;
                }
                catch
                {
                    child.State = ProcessState.Dead;
                }
                if (child.Children != null)
                {
                    RefreshChildren(child.Children);
                }
            }
        }

        /// <summary>
        /// Checks if the tree contains any applications that are alive.
        /// </summary>
        /// <returns></returns>
        public bool IsTreeActive()
        {
            if (State == ProcessState.Alive)
            {
                return true;
            }
            return Children != null && CheckChildren(Children);
        }
        /// <summary>
        /// Checks if any of the children are alive.
        /// </summary>
        /// <param name="children"></param>
        /// <returns></returns>
        private bool CheckChildren(ObservableCollection<WardenProcess> children)
        {
            if (children == null)
            {
                return false;
            }
            foreach (var child in children)
            {
                if (child.State == ProcessState.Alive)
                {
                    return true;
                }
                if (child.Children == null)
                {
                    continue;
                }
                if (CheckChildren(child.Children))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Kills the process and its children
        /// </summary>
        public void Kill()
        {
            try
            {
                var process = Process.GetProcessById(Id);
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                } 
            }
            catch 
            {
               //
            }
            if (Children != null)
            {
                KillChildren(Children);
            }
        }
        /// <summary>
        /// Loops over the tree killing all children
        /// </summary>
        /// <param name="children"></param>
        private void KillChildren(ObservableCollection<WardenProcess> children)
        {
            foreach (var child in children)
            {
                try
                {
                    var process = Process.GetProcessById(child.Id);
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                }
                catch
                {
                    //
                }
                if (child.Children != null)
                {
                    KillChildren(child.Children);
                }
            }
        }
        #region static class


        /// <summary>
        /// Launches a system URI and returns an empty Warden process set to Alive
        /// If the process is found later via its path the process ID will be updated to match so children can be added.
        /// </summary>
        /// <param name="uri">The URI that will be launched</param>
        /// <param name="path">The full path of the executable that should spawn after the URI launch.</param>
        /// <param name="arguments">Any additional arguments.</param>
        /// <returns></returns>
        public static async Task<WardenProcess> StartUri(string uri, string path, string arguments)
        {
            if (!Initialized)
            {
                throw new WardenManageException("Warden is not initialized.");
            }
            var process = await new UriLauncher().LaunchUri(uri, path, arguments);
            if (process == null)
            {
                return null;
            }
            var key = Guid.NewGuid();
            ManagedProcesses[key] = process;
            return ManagedProcesses[key];
        }

        /// <summary>
        /// Starts a Warden process using the applications full path. 
        /// This method can handle both Win32 applications and UWP.
        /// </summary>
        /// <param name="path">The full path of the executable or UWP family package name</param>
        /// <param name="arguments">>Any additional arguments.</param>
        /// <param name="type">The type of application you wish to launch.</param>
        /// <returns></returns>
        public static async Task<WardenProcess> Start(string path, string arguments, ProcessTypes type)
        {
            if (!Initialized)
            {
                throw new WardenManageException("Warden is not initialized.");
            }
            WardenProcess process;
            switch (type)
            {
                case ProcessTypes.Uwp:
                    process = await new UwpLauncher().Launch(path, arguments);
                    break;
                case ProcessTypes.Win32:
                    process = await new Win32Launcher().Launch(path, arguments);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
            if (process == null)
            {
                return null;
            }
            var key = Guid.NewGuid();
            ManagedProcesses[key] = process;
            return ManagedProcesses[key];
        }


        /// <summary>
        /// Finds a process in the tree using recursion 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="children"></param>
        /// <returns></returns>
        internal static WardenProcess FindChildById(int id, ObservableCollection<WardenProcess> children)
        {
            if (children == null)
            {
                return null;
            }
            foreach (var child in children)
            {
                if (child.Id == id)
                {
                    return child;
                }
                if (child.Children != null)
                {
                    return FindChildById(id, child.Children);
                }
            }
            return null;
        }

        /// <summary>
        /// Attempts to create a Warden process tree from an existing system process.
        /// </summary>
        /// <param name="pId"></param>
        /// <returns></returns>
        public static WardenProcess GetProcessFromId(int pId)
        {
            var process = BuildTreeById(pId);
            if (process == null)
            {
                return null;
            }
            var key = Guid.NewGuid();
            ManagedProcesses[key] = process;
            return ManagedProcesses[key];
        }
     
        private static WardenProcess BuildTreeById(int pId)
        {
            try
            {
                var process = Process.GetProcessById(pId);
                var processName = process.ProcessName;
                var processId = process.Id;
                var path = ProcessUtils.GetProcessPath(processId);
                var state = process.HasExited ? ProcessState.Dead : ProcessState.Alive;
                var arguments = ProcessUtils.GetCommandLine(processId);
                var type = ProcessUtils.IsWindowsApp(path) ? ProcessTypes.Uwp : ProcessTypes.Win32;
                var warden = new WardenProcess(processName, processId, path, state, arguments, type);
                var children = ProcessUtils.GetChildProcesses(pId);
                foreach (var child in children)
                {
                    warden.AddChild(BuildTreeById(child.Id));
                }
                return new WardenProcess(processName, processId, path, state, arguments, type);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a WardenProcess from a process id and sets a parent.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="parentId"></param>
        /// <param name="id"></param>
        /// <returns>A WardenProcess that will be added to a child list.</returns>
        internal static WardenProcess CreateProcessFromId(string name, int parentId, int id)
        {
            var path = ProcessUtils.GetProcessPath(id);
            var arguments = ProcessUtils.GetCommandLine(id);
            WardenProcess warden;
            try
            {
                var process = Process.GetProcessById(id);
                var processName = process.ProcessName;
                var processId = process.Id;
                var state = process.HasExited ? ProcessState.Dead : ProcessState.Alive;
                warden = new WardenProcess(processName, processId, path, state, arguments, ProcessTypes.Win32);
                warden.SetParent(parentId);
                return warden;
            }
            catch 
            {
              //
            }
            warden = new WardenProcess(name, id, path, ProcessState.Dead, arguments, ProcessTypes.Win32);
            warden.SetParent(parentId);
            return warden;
        }

        #endregion
    }
}