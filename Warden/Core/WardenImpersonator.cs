using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Warden.Core.Exceptions;

namespace Warden.Core
{
    /// <summary>
    /// Impersonates the currently logged in and active user and runs programs under their context
    /// </summary>
    public class WardenImpersonator : IDisposable
    {
        private readonly WindowsImpersonationContext _context;
        private readonly WindowsIdentity _identity;

        /// <summary>
        /// 
        /// </summary>
        public IntPtr Token => _identity.Token;

        /// <summary>
        /// 
        /// </summary>
        public WardenImpersonator()
        {
            _identity = GetIdentity();
            _context = _identity.Impersonate();
        }

        /// <summary>
        /// Gets the currently impersonated users name
        /// </summary>
        /// <returns></returns>
        public string UserName()
        {
            return WindowsIdentity.GetCurrent().Name;
        }

        [DllImport("advapi32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern int OpenProcessToken(IntPtr processHandle, int desiredAccess, ref IntPtr tokenHandle);

        [DllImport("kernel32", SetLastError = true)]
        [SuppressUnmanagedCodeSecurity]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DuplicateToken(IntPtr existingTokenHandle,
            int securityImpersonationLevel, ref IntPtr duplicateTokenHandle);

        private const int TOKEN_TOKEN_DUPLICATE = 2;
        private const int TOKEN_TOKEN_QUERY = 0X00000008;
        private const int TOKEN_TOKEN_IMPERSONATE = 0X00000004;


        private IntPtr GetExplorerHandle()
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!p.ProcessName.Equals("explorer")) continue;
                    var handle = p.Handle;
                    return handle;
                }
                catch
                {
                    // Ignore forbidden processes so we can get a list of processes we do have access to
                }
            } 
            return IntPtr.Zero;
        }

        private WindowsIdentity GetIdentity()
        {
            var hToken = IntPtr.Zero;
            var explorer = GetExplorerHandle();

            if (explorer == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                if (OpenProcessToken(explorer, TOKEN_TOKEN_QUERY | TOKEN_TOKEN_IMPERSONATE | TOKEN_TOKEN_DUPLICATE, ref hToken) == 0)
                {
                    throw new WardenException("Unable to open explorer process");
                }
                var newId = new WindowsIdentity(hToken);
                const int securityImpersonation = 2;
                var dupeTokenHandle = DupeToken(hToken, securityImpersonation);
                if (IntPtr.Zero == dupeTokenHandle)
                {
                    throw new WardenException($"Token duplication failed {Marshal.GetLastWin32Error()}, privilege not held");
                }
                return newId;
            }
            finally
            {
                CloseHandle(hToken);
            }
        }

        private static IntPtr DupeToken(IntPtr token, int level)
        {
            var dupeTokenHandle = IntPtr.Zero;
            DuplicateToken(token, level, ref dupeTokenHandle);
            return dupeTokenHandle;
        }

        public void Dispose()
        {
            _context?.Undo();
            _context?.Dispose();
            _identity?.Dispose();
        }
    }
}
