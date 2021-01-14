using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Warden.Windows
{
    /// <summary>
    ///     Provides a means to watch for process exits.
    /// </summary>
    /// <remarks>
    ///     This class enables a non-elevated process to wait for processes in the same session to exit (even ones ran as
    ///     Administrator) without requiring elevation. For
    ///     tracking services or processes in a different user session the waiting process will need to be elevated.
    /// </remarks>
    internal sealed class ProcessHook : IDisposable
    {
        /// <summary>
        ///     Creates a new <see cref="ProcessHook"/> instance from the specified <paramref name="processId"/>.
        /// </summary>
        /// <param name="processId">The ID of the running process.</param>
        /// <exception cref="UnauthorizedAccessException">
        ///     Thrown when the AppDomain lacks the necessary elevation to obtain a
        ///     handle on the target process.
        /// </exception>
        public ProcessHook(int processId)
        {
            ProcessId = processId;
            _handle = ProcessNative.OpenProcessHandle(ProcessNative.ProcessAccessFlags.QueryLimitedInformation | ProcessNative.ProcessAccessFlags.Synchronize, processId);
            if (_handle.IsInvalid)
            {
                throw new UnauthorizedAccessException($"Unable to access handle of process: {Marshal.GetLastWin32Error()}");
            }
        }

        /// <summary>
        ///     The ID of the watched process.
        /// </summary>
        public int ProcessId { get; }

        /// <summary>
        ///     The exit code of the process.
        /// </summary>
        public int ExitCode { get; private set; }

        /// <summary>
        ///     Determines if the current process has exited.
        /// </summary>
        public bool HasExited
        {
            get
            {
                if (!_exited)
                {
                    if (_handle.IsInvalid)
                    {
                        _exited = true;
                    }
                    else
                    {
                        // Although this is the wrong way to check whether the process has exited,
                        // it was historically the way we checked for it, and a lot of code then took a dependency on
                        // the fact that this would always be set before the pipes were closed, so they would read
                        // the exit code out after calling ReadToEnd() or standard output or standard error. In order
                        // to allow 259 to function as a valid exit code and to break as few people as possible that
                        // took the ReadToEnd dependency, we check for an exit code before doing the more correct
                        // check to see if we have been signaled.
                        if (ProcessNative.GetExitCodeProcess(_handle, out var exitCode) && exitCode != ProcessNative.STILL_ACTIVE)
                        {
                            ExitCode = exitCode;
                            _exited = true;
                        }
                        else
                        {
                            // The best check for exit is that the kernel process object handle is invalid, 
                            // or that it is valid and signaled.  Checking if the exit code != STILL_ACTIVE 
                            // does not guarantee the process is closed,
                            // since some process could return an actual STILL_ACTIVE exit code (259).
                            if (!_signaled)
                            {
                                using ProcessWaitHandle wh = new(_handle);
                                _signaled = wh.WaitOne(0, false);
                            }
                            if (_signaled)
                            {
                                if (ProcessNative.GetExitCodeProcess(_handle, out var lpExitCode))
                                {
                                    ExitCode = lpExitCode;
                                }
                                _exited = true;
                            }
                        }
                    }

                    if (_exited)
                    {
                        RaiseOnExited();
                    }
                }
                return _exited;
            }
        }

        /// <summary>
        ///     A managed wrapper around the watched process handle.
        /// </summary>
        private readonly SafeProcessHandle _handle;

        /// <summary>
        ///     Indicates if the currently watched process has exited.
        /// </summary>
        private bool _exited;

        /// <summary>
        ///     An event handler that is invoked upon process exit.
        /// </summary>
        private EventHandler? _onExited;

        private bool _raisedOnExited;

        /// <summary>
        ///     Represents a handle that has been registered with the thread pool.
        /// </summary>
        private RegisteredWaitHandle? _registeredWaitHandle;

        /// <summary>
        ///     Indicates if the currently watched process has signaled that it has exited.
        /// </summary>
        private bool _signaled;

        /// <summary>
        ///     A wait handle to the watched processes kernel object handle.
        /// </summary>
        private ProcessWaitHandle? _waitHandle;

        /// <summary>
        ///     Indicates if the current watcher is active.
        /// </summary>
        private bool _watchingForExit;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Sets the period of time to wait for the associated process to exit, and blocks the current thread of execution
        ///     until the time has elapsed or the process has exited.
        /// </summary>
        /// <param name="milliseconds">
        ///     The amount of time, in milliseconds, to wait for the associated process to exit. A value of
        ///     0 specifies an immediate return, and a value of -1 specifies an infinite wait.
        /// </param>
        /// <returns>true if the associated process has exited; otherwise, false.</returns>
        public bool WaitForExit(int milliseconds)
        {
            bool exited;
            using var handle = ProcessNative.OpenProcessHandle(ProcessNative.ProcessAccessFlags.Synchronize, ProcessId);
            if (handle.IsInvalid)
            {
                exited = true;
            }
            else
            {
                using var processWaitHandle = new ProcessWaitHandle(handle);
                if (processWaitHandle.WaitOne(milliseconds, false))
                {
                    exited = true;
                    _signaled = true;

                    if (ProcessNative.GetExitCodeProcess(handle, out var exitCode))
                    {
                        ExitCode = exitCode;
                    }
                }
                else
                {
                    exited = false;
                    _signaled = false;
                }
            }

            if (exited)
            {
                RaiseOnExited();
            }
            return exited;
        }

        /// <summary>
        ///     Ensures that the current process is being watched for exit events.
        /// </summary>
        public void Start()
        {
            if (!_watchingForExit)
            {
                lock (this)
                {
                    if (!_watchingForExit)
                    {
                        _watchingForExit = true;
                        try
                        {
                            _waitHandle = new ProcessWaitHandle(_handle);
                            _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(_waitHandle, CompletionCallback, null, -1, true);
                        }
                        catch
                        {
                            _watchingForExit = false;
                            throw;
                        }
                    }
                }
            }
        }


        /// <summary>
        ///     A callback that is fired when the wait handle is signaled.
        /// </summary>
        private void CompletionCallback(object state, bool timedout)
        {
            Stop();
            RaiseOnExited();
        }

        /// <summary>
        ///     An event handler that can be subscribed to receive exit events.
        /// </summary>
        public event EventHandler Exited
        {
            add => _onExited += value;
            remove => _onExited -= value;
        }

        /// <summary>
        ///     Stops watching for exit events and resets all active handles.
        /// </summary>
        private void Stop()
        {
            if (_watchingForExit)
            {
                lock (this)
                {
                    if (_watchingForExit)
                    {
                        _watchingForExit = false;
                        _registeredWaitHandle?.Unregister(null);
                        _waitHandle?.Dispose();
                        _waitHandle = null;
                        _registeredWaitHandle = null;
                    }
                }
            }
        }

        /// <summary>
        ///     Invokes the exit event handler.
        /// </summary>
        private void RaiseOnExited()
        {
            if (!_raisedOnExited)
            {
                lock (this)
                {
                    if (!_raisedOnExited)
                    {
                        _raisedOnExited = true;
                        _onExited?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_onExited is not null)
                {
                    foreach (var d in _onExited.GetInvocationList())
                    {
                        _onExited -= d as EventHandler;
                    }
                    _onExited = null;
                }
                _registeredWaitHandle?.Unregister(null);
                _waitHandle?.Dispose();
                _handle?.Dispose();
                _registeredWaitHandle = null;
                _waitHandle = null;
            }
        }
    }
}