using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Warden.Core;

namespace Warden.Utils
{
    /// <summary>
    /// A utility class for validating custom URIs / Windows Protocol Handlers
    /// </summary>
    public static class UriHelpers
    {
        /// <summary>
        ///     <para>Disables handle caching for all predefined registry handles for the current process.</para>
        /// </summary>
        /// <returns>
        ///     <para>If the function succeeds, the return value is ERROR_SUCCESS.</para>
        ///     <para>If the function fails, the return value is a system error code.</para>
        /// </returns>
        /// <remarks>
        ///     <para>This function does not work on a remote computer.</para>
        ///     <para>Services that change impersonation should call this function before using any of the predefined handles.</para>
        ///     <para>
        ///         For example, any access of <c>HKEY_CURRENT_USER</c> after this function is called results in open and close
        ///         operations being
        ///         performed on <c>HKEY_USERS</c>&lt;b&gt;SID_of_current_user, or on <c>HKEY_USERS.DEFAULT</c> if the current
        ///         user's hive is not
        ///         loaded. For more information on SIDs, see Security Identifiers.
        ///     </para>
        /// </remarks>
        // https://docs.microsoft.com/en-us/windows/desktop/api/winreg/nf-winreg-regdisablepredefinedcacheex LSTATUS
        // RegDisablePredefinedCacheEx( );
        [DllImport("advapi32.dll", SetLastError = false, ExactSpelling = true)]
        private static extern int RegDisablePredefinedCacheEx();

        /// <summary>
        /// Determine if a uri is registered as a protocol handler.
        /// </summary>
        /// <param name="registeredProtocol">The URI that should be registered with Windows.</param>
        /// <returns>true if the scheme is registered, otherwise false.</returns>
        internal static bool IsValidCustomProtocol(string registeredProtocol)
        {
            if (WardenImpersonator.IsThreadImpersonating)
            {
                RegDisablePredefinedCacheEx();
            }
            var scheme = registeredProtocol.GetUriScheme();
          
            if (string.IsNullOrWhiteSpace(scheme))
            {
                return false;
            }
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default);
            using var schemeKey = registry.OpenSubKey(scheme);
            return schemeKey is not null && schemeKey.GetValueNames().Contains("URL Protocol");
        }
    }
}