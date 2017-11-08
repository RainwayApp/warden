using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Warden.Core.Exceptions;
using Warden.Core.Launchers;
using Warden.Core.Models;
using Warden.Core.Utils;
using static Warden.Core.WardenManager;
using Warden.Properties;
using static Warden.Core.Utils.ProcessUtils;

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

    public class ProcessAddedEventArgs : EventArgs
    {
        public ProcessAddedEventArgs(string name, int parentId, int processId)
        {
            Name = name;
            ParentId = parentId;
            Id = processId;
        }

        public ProcessAddedEventArgs()
        {
        }

        public string Name { get; set; }

        public int ParentId { get; set; }

        public int Id { get; set; }
    }

    /// <summary>
    /// Provides access to local processes and their children in real-time.
    /// </summary>
    public class WardenProcess
    {
        public delegate void ChildStateUpdateHandler(object sender, StateEventArgs e);

        public delegate void StateUpdateHandler(object sender, StateEventArgs e);

        public delegate void ProcessAddedHandler(object sender, ProcessAddedEventArgs e);

        internal WardenProcess(string name, int id, string path, ProcessState state, List<string> arguments,
            ProcessTypes type, List<ProcessFilter> filters)
        {
            Filters = filters;
            Name = name;
            Id = id;
            Path = path;
            State = state;
            Arguments = arguments;
            Type = type;
            Children = new ObservableCollection<WardenProcess>();
            var epochTicks = new DateTime(1970, 1, 1).Ticks;
            var unixTime = ((DateTime.UtcNow.Ticks - epochTicks) / TimeSpan.TicksPerSecond);
            TimeStamp = unixTime;
            if (Options.ReadFileHeaders && File.Exists(Path))
            {
                Headers = new PeHeaderReader(Path);
            }
        }

        public PeHeaderReader Headers { get; set; }

        [IgnoreDataMember] public readonly List<ProcessFilter> Filters;

        public long TimeStamp { get; set; }

        public ProcessTypes Type { get; }

        public ObservableCollection<WardenProcess> Children { get; internal set; }

        public int ParentId { get; internal set; }

        public int Id { get; internal set; }

        public ProcessState State { get; private set; }

        public string Path { get; internal set; }

        public string Name { get; internal set; }

        [IgnoreDataMember]
        public Action<bool> FoundCallback { get; set; }

        public List<string> Arguments { get; internal set; }

        internal void SetParent(int parentId)
        {
            if (parentId == Id)
            {
                return;
            }
            ParentId = parentId;
        }

        /// <summary>
        /// Finds a child process by its id
        /// </summary>
        /// <param name="id"></param>
        /// <returns>The WardenProcess of the child.</returns>
        public WardenProcess FindChildById(int id)
        {
            return FindChildById(id, Children);
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
        /// This event is fired when a process is added to the main process or its children
        /// </summary>
        public event ProcessAddedHandler OnProcessAdded;

        public void InvokeProcessAdd(ProcessAddedEventArgs args)
        {
            OnProcessAdded?.Invoke(this, args);
        }

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


        internal bool IsFiltered()
        {
            if (Filters == null || Filters.Count <= 0)
            {
                return false;
            }
            return Filters.Any(filter =>
                !string.IsNullOrWhiteSpace(filter.Name) &&
                filter.Name.Equals(Name, StringComparison.CurrentCultureIgnoreCase) ||
                !string.IsNullOrWhiteSpace(filter.Path) && NormalizePath(filter.Path)
                    .Equals(Path, StringComparison.CurrentCultureIgnoreCase));
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

        private void KillLegacy()
        {
            try
            {
                var process = Process.GetProcessById(Id);
                if (process.HasExited)
                {
                    return;
                }
                //will likely be ignored by some apps
                process.Kill();
                //give it 5 seconds and move on
                process.WaitForExit(TimeSpan.FromSeconds(5).Milliseconds);
            }
            catch
            {
                //
            }
        }


        private void TaskKill()
        {
            var taskKill = new TaskKill
            {
                Arguments = new List<TaskSwitch>
                {
                    TaskSwitch.Force,
                    TaskSwitch.ProcessId.SetValue(Id.ToString()),
                    Options.DeepKill ? TaskSwitch.TerminateChildren : null
                }
            };
            taskKill.Execute(out var output, out var error);
            if (!string.IsNullOrWhiteSpace(output))
            {
                Debug.WriteLine(output?.Trim());
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.WriteLine(error?.Trim());
            }
        }

        /// <summary>
        /// Kills the process and its children
        /// </summary>
        public void Kill()
        {
            try
            {
                if (Options.UseLegacyKill)
                {
                    KillLegacy();
                }
                else
                {
                    TaskKill();
                }
                if (Children == null || Children.Count <= 0 || !Options.DeepKill)
                {
                    return;
                }
                foreach (var child in Children)
                {
                    child?.Kill();
                }
            }
            catch
            {
                //
            }
        }

        #region static class

        /// <summary>
        /// Checks if a process is currently running, if so we build the WardenProcess right away or fetch a monitored one.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="process"></param>
        /// <returns></returns>
        private static bool CheckForWardenProcess(string path, out WardenProcess process)
        {
            var runningProcess = GetProcess(path);
            if (runningProcess == null)
            {
                process = null;
                return false;
            }
            //Lets check our watch list first
            foreach (var managedProcess in ManagedProcesses)
            {
                if (managedProcess.Value.Id != runningProcess.Id)
                {
                    continue;
                }
                process = ManagedProcesses[managedProcess.Key];
                return true;
            }
            process = GetProcessFromId(runningProcess.Id);
            return process != null;
        }

        /// <summary>
        /// Launches a system URI and returns an empty Warden process set to Alive
        /// If the process is found later via its path the process ID will be updated to match so children can be added.
        /// </summary>
        /// <param name="uri">The URI that will be launched</param>
        /// <param name="path">The full path of the executable that should spawn after the URI launch.</param>
        /// <param name="arguments">Any additional arguments.</param>
        /// <param name="filters">A list of filters so certain processes are not added to the tree.</param>
        /// <returns></returns>
        public static async Task<WardenProcess> StartUri(string uri, string path, string arguments,
            List<ProcessFilter> filters, CancellationToken cancelToken)
        {
            if (!Initialized)
            {
                throw new WardenManageException(Resources.Exception_Not_Initialized);
            }
            if (CheckForWardenProcess(path, out var existingProcess))
            {
                return existingProcess;
            }
            if (string.IsNullOrWhiteSpace(arguments))
            {
                arguments = string.Empty;
            }
            //lets add it to the dictionary ahead of time in case our program launches faster than we can return
            var key = Guid.NewGuid();
            var warden = new WardenProcess(System.IO.Path.GetFileNameWithoutExtension(path),
                new Random().Next(1000000, 2000000), path, ProcessState.Alive, arguments.SplitSpace(), ProcessTypes.Uri,
                filters);
            ManagedProcesses[key] = warden;
            if (await new UriLauncher().PrepareUri(uri, path, arguments, cancelToken) != null)
            {
                return ManagedProcesses[key];
            }
            ManagedProcesses.TryRemove(key, out var t);
            return null;
        }

        /// <summary>
        /// Launches a system URI asynchronously and returns an empty Warden process set to Alive
        /// This spawns an asynchronous loop that will execute a callback if the target process is found
        /// However the function returns right away to ensure it does not block. 
        /// </summary>
        /// <param name="uri">The URI that will be launched</param>
        /// <param name="path">The full path of the executable that should spawn after the URI launch.</param>
        /// <param name="arguments">Any additional arguments.</param>
        /// <param name="filters">A list of filters so certain processes are not added to the tree.</param>
        /// <param name="callback">A callback executed on if the process launched or not.</param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public static async Task<WardenProcess> StartUriAsync(string uri, string path, string arguments,
            List<ProcessFilter> filters, Action<bool> callback, CancellationToken cancelToken)
        {
            if (!Initialized)
            {
                throw new WardenManageException(Resources.Exception_Not_Initialized);
            }
            if (CheckForWardenProcess(path, out var existingProcess))
            {
                return existingProcess;
            }
            if (string.IsNullOrWhiteSpace(arguments))
            {
                arguments = string.Empty;
            }
            //lets add it to the dictionary ahead of time in case our program launches faster than we can return
            var key = Guid.NewGuid();
            var warden = new WardenProcess(System.IO.Path.GetFileNameWithoutExtension(path),
                new Random().Next(1000000, 2000000), path, ProcessState.Alive, arguments?.SplitSpace(),
                ProcessTypes.Uri, filters)
            {
                FoundCallback = callback
            };
            ManagedProcesses[key] = warden;
            if (await new UriLauncher().PrepareUri(uri, path, arguments, cancelToken, key) != null)
            {
                return ManagedProcesses[key];
            }
            ManagedProcesses.TryRemove(key, out var t);
            return null;
        }

        /// <summary>
        /// Starts a monitored UWP process using the applications family package name and token.
        /// </summary>
        /// <param name="appId">The UWP family package name</param>
        /// <param name="appToken">The token needed to launch the app within the package</param>
        /// <param name="arguments">Any additional arguments.</param>
        /// <param name="filters">A list of filters so certain processes are not added to the tree.</param>
        /// <returns></returns>
        public static async Task<WardenProcess> StartUwp(string appId, string appToken, string arguments,
            List<ProcessFilter> filters)
        {
            if (!Initialized)
            {
                throw new WardenManageException(Resources.Exception_Not_Initialized);
            }
            if (string.IsNullOrWhiteSpace(arguments))
            {
                arguments = string.Empty;
            }
            var process = await new UwpLauncher().Launch(appId, appToken, arguments);
            if (process == null)
            {
                return null;
            }
            var key = Guid.NewGuid();
            ManagedProcesses[key] = process;
            return ManagedProcesses[key];
        }

        /// <summary>
        /// Starts a monitored process using the applications full path.
        /// This method should only be used for win32 applications 
        /// </summary>
        /// <param name="path">The full path of the executable</param>
        /// <param name="arguments">>Any additional arguments.</param>
        /// <param name="filters">A list of filters so certain processes are not added to the tree.</param>
        /// <param name="asUser">Set to true if launching a program from a service.</param>
        /// <returns></returns>
        public static async Task<WardenProcess> Start(string path, string arguments, List<ProcessFilter> filters,
            bool asUser = false)
        {
            if (!Initialized)
            {
                throw new WardenManageException(Resources.Exception_Not_Initialized);
            }
            if (CheckForWardenProcess(path, out var existingProcess))
            {
                return existingProcess;
            }
            if (string.IsNullOrWhiteSpace(arguments))
            {
                arguments = string.Empty;
            }
            var process = await new Win32Launcher().Launch(path, arguments, asUser);
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
                if (child.Children == null)
                {
                    continue;
                }
                var nested = FindChildById(id, child.Children);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        /// <summary>
        /// Attempts to create a Warden process tree from an existing system process.
        /// </summary>
        /// <param name="pId"></param>
        /// <param name="filters">A list of filters so certain processes are not added to the tree.</param>
        /// <returns></returns>
        public static WardenProcess GetProcessFromId(int pId, List<ProcessFilter> filters = null)
        {
            var process = BuildTreeById(pId, filters);
            if (process == null)
            {
                return null;
            }
            var key = Guid.NewGuid();
            ManagedProcesses[key] = process;
            return ManagedProcesses[key];
        }

        private static WardenProcess BuildTreeById(int pId, List<ProcessFilter> filters)
        {
            try
            {
                var process = Process.GetProcessById(pId);
                var processName = process.ProcessName;
                var processId = process.Id;
                var path = GetProcessPath(processId);
                var state = process.HasExited ? ProcessState.Dead : ProcessState.Alive;
                var arguments = GetCommandLine(processId);
                var type = IsWindowsApp(path) ? ProcessTypes.Uwp : ProcessTypes.Win32;
                var warden = new WardenProcess(processName, processId, path, state, arguments, type, filters);
                var children = GetChildProcesses(pId);
                foreach (var child in children)
                {
                    warden.AddChild(BuildTreeById(child.Id, filters));
                }
                return new WardenProcess(processName, processId, path, state, arguments, type, filters);
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
        /// <param name="filters">A list of filters so certain processes are not added to the tree.</param>
        /// <returns>A WardenProcess that will be added to a child list.</returns>
        internal static WardenProcess CreateProcessFromId(string name, int parentId, int id,
            List<ProcessFilter> filters)
        {
            var path = GetProcessPath(id);
            var arguments = GetCommandLine(id);
            WardenProcess warden;
            try
            {
                var process = Process.GetProcessById(id);
                var processName = process.ProcessName;
                var processId = process.Id;
                var state = process.HasExited ? ProcessState.Dead : ProcessState.Alive;
                warden = new WardenProcess(processName, processId, path, state, arguments, ProcessTypes.Win32, filters);
                warden.SetParent(parentId);
                return warden;
            }
            catch
            {
                //
            }
            warden = new WardenProcess(name, id, path, ProcessState.Dead, arguments, ProcessTypes.Win32, filters);
            warden.SetParent(parentId);
            return warden;
        }

        #endregion
    }
}
