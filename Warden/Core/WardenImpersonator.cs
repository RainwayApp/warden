using System;
using System.Security.Permissions;
using System.Security.Principal;
using Warden.Core.Exceptions;
using Warden.Windows;
using Warden.Windows.Win32;

namespace Warden.Core
{
    /// <summary>
    /// Impersonates the currently logged in and active user and runs programs under their context
    /// </summary>
    [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
    public static class WardenImpersonator
    {
        private static bool _needsImpersonation;
        private static WindowsIdentity _identity;

        /// <summary>
        /// Initializes the static impersonation context by fetching the token for the active user session.
        /// </summary>
        internal static void Initialize()
        {
            _needsImpersonation = WindowsIdentity.GetCurrent().IsSystem;
            if (_needsImpersonation)
            {
                _identity = Api.GetSessionUserIdentity();
            }
        }

        /// <summary>
        /// Determines if the current session requires impersonation.
        /// If the entry process is not running under SYSTEM this is false.
        /// </summary>
        /// <returns>whether impersonation should be used.</returns>
        public static bool NeedsImpersonation()
        {
            return _needsImpersonation;
        }

        /// <summary>
        /// Gets the username of the active user session
        /// </summary>
        /// <returns></returns>
        public static string Username()
        {
            if (!_needsImpersonation)
            {
                return WindowsIdentity.GetCurrent().Name;
            }
            if (_identity == null)
            {
                throw new WardenException($"The Windows Identity is null.");
            }
            return _identity.Name;
        }


        /// <summary>
        /// Execute a function under the context of the active user session.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="function">The function to execute.</param>
        /// <returns>The result of executing the function.</returns>
        public static T RunAsUser<T>(Func<T> function)
        {
            if (!_needsImpersonation)
            {
                return function();
            }
            if (_identity == null)
            {
                throw new WardenException($"The Windows Identity is null.");
            }
            return RunImpersonated(function);
        }

        /// <summary>
        /// Perform an action under the context of the active user session.
        /// </summary>
        /// <param name="action">The action to perform.</param>
        public static void RunAsUser(Action action)
        {
            if (!_needsImpersonation)
            {
                action();
                return;
            }
            if (_identity == null)
            {
                throw new WardenException($"The Windows Identity is null.");
            }
            RunImpersonated(action);
        }

        /// <summary>
        /// Establishes a impersonation context and performs an action under it
        /// </summary>
        /// <param name="action">the action to perform.</param>
        private static void RunImpersonated(Action action)
        {
            using (var context = _identity.Impersonate())
            {
                action();
            }
        }

        /// <summary>
        /// Establishes a impersonation context and executes a function under it, returning results.
        /// </summary>
        /// <typeparam name="T">the return type of the function</typeparam>
        /// <param name="function">the function to execute</param>
        /// <returns></returns>
        private static T RunImpersonated<T>(Func<T> function)
        {
            using (var context = _identity.Impersonate())
            {
                return function();
            }
        }
    }
}
