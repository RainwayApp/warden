using System;
using System.Security.Principal;
using Warden.Windows;

namespace Warden.Core
{
    /// <summary>
    /// A class that helps processes created by <see cref="WardenProcess.StartAsUser"/> execute code as the interactive user. 
    /// </summary>
    public static class WardenImpersonator
    {
        private static bool _needsImpersonation;
        private static WindowsIdentity? _identity;

        static WardenImpersonator()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the static impersonation context by fetching the token for the active user session.
        /// </summary>
        private static void Initialize()
        {
            _needsImpersonation = WindowsIdentity.GetCurrent().IsSystem;
            if (_needsImpersonation)
            {
                _identity = InteractiveProcessCreator.GetInteractiveSessionUserIdentity();
            }
        }

        /// <summary>
        /// Gets the username of the active user session
        /// </summary>
        /// <returns></returns>
        public static string Username
        {
            get
            {
                if (!_needsImpersonation)
                {
                    return WindowsIdentity.GetCurrent().Name;
                }
                EnsureIdentity();
                return _identity!.Name;
            }
        }

        /// <summary>
        /// Ensures an identity is associated with the impersonator.
        /// </summary>
        private static void EnsureIdentity()
        {
            if (_identity == null)
            {
                throw new NullReferenceException("No identity is associated with the Warden Impersonator.");
            }
        }

        /// <summary>
        /// Determines if the calling thread is executing as a different Windows user.
        /// </summary>
        public static bool IsThreadImpersonating => WindowsIdentity.GetCurrent().ImpersonationLevel == TokenImpersonationLevel.Impersonation;

        /// <summary>
        /// Determines if the current session requires impersonation.
        /// If the entry process is not running under SYSTEM this is false.
        /// </summary>
        /// <returns>Whether impersonation should be used.</returns>
        public static bool NeedsImpersonation => _needsImpersonation;

        /// <summary>
        /// Execute a function under the context of the interactive user.
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
            EnsureIdentity();
            return WindowsIdentity.RunImpersonated(_identity!.AccessToken, function);
        }

        /// <summary>
        /// Perform an action under the context of the interactive user.
        /// </summary>
        /// <param name="action">The action to perform.</param>
        public static void RunAsUser(Action action)
        {
            if (!_needsImpersonation)
            {
                action();
                return;
            }
            EnsureIdentity();
            WindowsIdentity.RunImpersonated(_identity!.AccessToken, action);
        }
    }
}
