using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Claims;
using System.Security.Principal;

namespace Warden.Core
{
    /// <summary>
    /// Impersonates the currently logged in and active user and runs programs under their context
    /// </summary>
    public class WardenImpersonator : IDisposable
    {
        private readonly WindowsImpersonationContext _context;
        private readonly WindowsIdentity _identity;


        public WardenImpersonator()
        {
            _identity = GetIdentity();
            _context = _identity.Impersonate();
        }

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

        private const int TokenDuplicate = 2;
        private const int TokenQuery = 0X00000008;
        private const int TokenImpersonate = 0X00000004;

        private WindowsIdentity GetIdentity()
        {
            var hToken = IntPtr.Zero;

            var runningProcesses = Process.GetProcesses();
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            var sameAsthisSession =
                (from c in runningProcesses where c.SessionId == currentSessionId select c).ToArray();
            var proc = sameAsthisSession[0];

            if (OpenProcessToken(proc.Handle,
                    TokenQuery | TokenImpersonate | TokenDuplicate,
                    ref hToken) != 0)
            {
                var newId = new WindowsIdentity(hToken);
                try
                {
                    const int securityImpersonation = 2;
                    var dupeTokenHandle = DupeToken(hToken,
                        securityImpersonation);
                    if (IntPtr.Zero == dupeTokenHandle)
                    {
                        var s = $"Token duplication failed {Marshal.GetLastWin32Error()}, privilege not held";
                        throw new Exception(s);
                    }

                    return newId;
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            {
                var s = $"OpenProcess Failed {Marshal.GetLastWin32Error()}, privilege not held";
                throw new Exception(s);
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
