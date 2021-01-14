using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Warden.Utils;

namespace Warden.Windows
{
    /// <summary>
    ///     Windows API methods for working with processes.
    /// </summary>
    internal static class ProcessNative
    {
        /// <summary>
        /// The process identifier of the calling process.
        /// </summary>
        internal static uint CurrentProcessId = GetCurrentProcessId();
        
        /// <summary>
        /// Creates a new <see cref="ProcessInfo"/> record from the specified <paramref name="processId"/>.
        /// </summary>
        /// <param name="processId">The system-unique identifier of a process resource.</param>
        /// <exception cref="ArgumentException">The process specified by the <paramref name="processId"/> parameter is not running or can't be accessed.</exception>
        /// <returns>A <see cref="ProcessInfo"/> record that is associated with the <paramref name="processId"/> parameter.</returns>
        internal static ProcessInfo GetProcessInfoById(int processId)
        {
            foreach (var info in GetProcesses())
            {
                if (info.Id == processId)
                {
                    return info;
                }
            }
            throw new ArgumentException($"The process specified by the processId parameter ('{processId}') is not running or can't be accessed.");
        }

        /// <summary>
        /// Terminates the system process object with the specified <paramref name="processId"/>.
        /// </summary>
        /// <param name="processId"></param>
        /// <param name="exitCode"></param>
        internal static void TerminateProcess(int processId, int exitCode)
        {
            if (IsProcessRunning(processId))
            {
                using var processHandle = OpenProcessHandle(ProcessAccessFlags.Terminate | ProcessAccessFlags.Synchronize, processId);
                if (processHandle.IsInvalid)
                {
                    throw new UnauthorizedAccessException($"Unable to access handle of process: {Marshal.GetLastWin32Error()}");
                }
                if (!TerminateProcess(processHandle, exitCode))
                {
                    throw new Win32Exception("Unable to terminate process.");
                }
                WaitForSingleObject(processHandle, 0xFFFFFFFF);
            }
        }

        /// <summary>
        ///     Returns an enumerator of active and accessible system process object information.
        /// </summary>
        /// <returns></returns>
        internal static IEnumerable<ProcessInfo> GetProcesses() => GetProcesses(GetProcessIds());

        /// <summary>
        ///     Opens a safe handle to the current process.
        /// </summary>
        /// <param name="access">The access flags that control the handle permissions.</param>
        /// <param name="processId">The ID of the process object to open.</param>
        /// <returns>A handle to the current process.</returns>
        internal static SafeProcessHandle OpenProcessHandle(ProcessAccessFlags access, int processId) => OpenProcess(access, false, processId);

        /// <summary>
        ///     Determines if the specified <paramref name="processId"/> is running on the system.
        /// </summary>
        /// <param name="processId">The system-unique identifier of a process resource.</param>
        /// <returns>true if the process is running, otherwise false.</returns>
        internal static bool IsProcessRunning(int processId)
        {
            var processIds = GetProcessIds();
            // ReSharper disable once LoopCanBeConvertedToQuery
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < processIds.Length; i++)
            {
                if (processIds[i] == processId)
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Retrieves the name of the specified parent process.
        /// </summary>
        /// <param name="parentProcessId">The system-unique identifier of the parent process resource.</param>
        /// <returns> Returns the name of the parent process that created the calling application.</returns>
        /// <remarks>
        ///  Because a parent process may be in another session <see cref="OpenProcessHandle"/> will fail. This method safely retrieves process information albeit more slowly.
        /// </remarks>
        internal static string? GetParentProcessName(int parentProcessId)
        {
            var snapshotHandle = IntPtr.Zero;
            try
            {
                snapshotHandle = CreateToolhelp32Snapshot(SnapshotFlags.Process | SnapshotFlags.NoHeaps, (uint) parentProcessId);
                var processEntry = new ProcessEntry32W();
                processEntry.Size = Marshal.SizeOf<ProcessEntry32W>();
                if (Process32FirstW(snapshotHandle, ref processEntry))
                {
                    do
                    {
                        if (processEntry.ProcessID == parentProcessId)
                        {
                            return string.IsNullOrWhiteSpace(processEntry.szExeFile) ? null : Path.GetFileNameWithoutExtension(processEntry.szExeFile);
                        }
                    }
                    while (Process32NextW(snapshotHandle, ref processEntry));
                }
            }
            finally
            {
                CloseHandle(snapshotHandle);
            }
            return null;
        }

        /// <summary>
        /// Returns the <see cref="ProcessInfo"/> associated with the currently active process.
        /// </summary>
        /// <returns> Returns the <see cref="ProcessInfo"/> of the calling application.</returns>
        internal static ProcessInfo GetCurrentProcessInfo() => GetProcesses().FirstOrDefault(process => process.Id == CurrentProcessId)!;

        internal static IEnumerable<ProcessInfo> GetProcesses(uint[] processIds)
        {
            const int imageBufferSize = 2048;
            var imageBuffer = new StringBuilder(imageBufferSize);

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < processIds.Length; i++)
            {
                var processId = processIds[i];
                // Obtain a process handle with only ProcessQueryLimitedInformation and ProcessVMRead access.
                // anything greater and the calling process must be elevated.
                using var processHandle = OpenProcessHandle(ProcessAccessFlags.QueryLimitedInformation | ProcessAccessFlags.VMRead, (int) processId);
                if (processHandle.IsInvalid)
                {
                    continue;
                }
                // Keep this here as the reference pointer needs to be unique.
                var capacity = imageBufferSize;
                // Try to get the processes fully qualified executable name
                if (QueryFullProcessImageName(processHandle, NamePathFormat.Default, imageBuffer, ref capacity))
                {
                    var imageName = imageBuffer.ToString();
                    if (string.IsNullOrWhiteSpace(imageName))
                    {
                        continue;
                    }
                    // NtQueryInformationProcess writes the requested information into this.
                    var basicInfo = new ProcessBasicInformation();
                    // Retrieves a pointer to a PEB structure that can be used to determine whether the specified process is being debugged,
                    // and a unique value used by the system to identify the specified process.
                    var status = NtQueryInformationProcess(processHandle, 0, ref basicInfo, Marshal.SizeOf<ProcessBasicInformation>(), out _);
                    if (status != 0)
                    {
                        continue;
                    }
                    if (!GetProcessTimes(processHandle, out var creationTime, out _, out _, out _))
                    {
                        // We rely on accurate creation time to determine process relationships
                        continue;
                    }
                    var commandLine = string.Empty;
                    var workingDirectory = string.Empty;
                    if (basicInfo.PebBaseAddress != IntPtr.Zero)
                    {
                        var peb = ReadProcessMemory<PEB>(processHandle, basicInfo.PebBaseAddress);
                        if (peb is not null)
                        {
                            var processParameters = ReadProcessMemory<RtlUserProcessParameters>(processHandle, peb.Value.ProcessParameters);
                            if (processParameters is not null)
                            {
                                commandLine = FormatCommandLine(ReadUnicodeString(processHandle, processParameters.Value.CommandLine), imageName);
                                workingDirectory = ReadUnicodeString(processHandle, processParameters.Value.CurrentDirectory);
                            }
                        }
                    }
                    yield return new ProcessInfo((int) processId, basicInfo.InheritedFromUniqueProcessId.ToInt32(), imageName, commandLine, workingDirectory, creationTime);
                }
            }
        }

        /// <summary>
        ///     Parses and formats the command-line string.
        /// </summary>
        /// <param name="commandLine"></param>
        /// <param name="processImage"></param>
        /// <returns></returns>
        internal static string FormatCommandLine(string commandLine, string? processImage = null)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return string.Empty;
            }
            var argsPointer = CommandLineToArgvW(commandLine, out var argumentCount);
            try
            {
                if (argsPointer == IntPtr.Zero)
                {
                    return string.Empty;
                }
                if (argumentCount == 0)
                {
                    return string.Empty;
                }
                var args = new string[argumentCount];
                for (var i = 0; i < args.Length; i++)
                {
                    var p = Marshal.ReadIntPtr(argsPointer, i * IntPtr.Size);
                    args[i] = Marshal.PtrToStringUni(p) ?? string.Empty;
                }
                if (args.Length == 0)
                {
                    return string.Empty;
                }
                if (!string.IsNullOrWhiteSpace(processImage))
                {
                    if (args.Length == 1 && args[0].Equals(processImage, StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Empty;
                    }
                    if (args.Length > 1 && args[0].Equals(processImage, StringComparison.OrdinalIgnoreCase))
                    {
                        return args.Skip(1).ToArray().EscapeArguments();
                    }
                }
                return args.EscapeArguments();
            }
            finally
            {
                Marshal.FreeHGlobal(argsPointer);
            }
        }

        /// <summary>
        ///     Reads a string from another process.
        /// </summary>
        /// <param name="handle">
        ///     A handle to the process with memory that is being read. The handle must have
        ///     <see cref="ProcessAccessFlags.VMRead"/> access to the process.
        /// </param>
        /// <param name="unicodeString">The unicode structure containing the location of the wide chars.</param>
        /// <returns>If the string is read it is returned; otherwise <see cref="string.Empty"/> is used.</returns>
        private static string ReadUnicodeString(SafeProcessHandle handle, UnicodeString unicodeString)
        {
            var maxlength = unicodeString.MaximumLength;
            var memoryBuffer = Marshal.AllocHGlobal(maxlength);
            try
            {
                if (ReadProcessMemory(handle, unicodeString.Buffer, memoryBuffer, maxlength, out _))
                {
                    return Marshal.PtrToStringUni(memoryBuffer) ?? string.Empty;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(memoryBuffer);
            }
            return string.Empty;
        }

        /// <summary>
        ///     Reads a struct from another process at the specified address.
        /// </summary>
        /// <typeparam name="TStruct">The type of data structure to read.</typeparam>
        /// <param name="handle">
        ///     A handle to the process with memory that is being read. The handle must have
        ///     <see cref="ProcessAccessFlags.VMRead"/> access to the process.
        /// </param>
        /// <param name="baseAddress">
        ///     A pointer to the base address in the specified process from which to read. Before any data
        ///     transfer occurs, the system verifies that all data in the base address and memory of the specified size is
        ///     accessible for read access, and if it is not accessible the function fails.
        /// </param>
        /// <returns>If the read succeeds, the return value is an instance of <see cref="TStruct"/>. Otherwise null.</returns>
        private static TStruct? ReadProcessMemory<TStruct>(SafeProcessHandle handle, IntPtr baseAddress) where TStruct : struct
        {
            var structSize = Marshal.SizeOf<TStruct>();
            var memoryBuffer = Marshal.AllocHGlobal(structSize);
            try
            {
                if (ReadProcessMemory(handle, baseAddress, memoryBuffer, (uint) structSize, out var bytesRead) && bytesRead == structSize)
                {
                    return Marshal.PtrToStructure<TStruct>(memoryBuffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(memoryBuffer);
            }
            return null;
        }

        /// <summary>
        ///     Retrieves the process identifier for each process object in the system.
        /// </summary>
        private static uint[] GetProcessIds()
        {
            var processIds = new uint[256];
            uint size;
            for (;;)
            {
                if (!EnumProcesses(processIds, processIds.Length * 4, out size))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                if (size == processIds.Length * 4)
                {
                    processIds = new uint[processIds.Length * 2];
                    continue;
                }
                break;
            }
            var ids = new uint[size / 4];
            Array.Copy(processIds, ids, ids.Length);
            return ids;
        }

    #region Windows API Constants


        private enum NamePathFormat : uint
        {
            /// <summary>
            ///     The name should use the Win32 path format.
            /// </summary>
            Default = 0,

            /// <summary>
            ///     The name should use the native system path format.
            /// </summary>
            ProcessNameNative = 0x00000001
        }

        /// <summary>
        ///     The Microsoft Windows security model enables you to control access to process objects using these flags.
        /// </summary>
        [Flags]
        internal enum ProcessAccessFlags : uint
        {
            /// <summary>
            ///     Required to retrieve certain information about a process
            /// </summary>
            QueryLimitedInformation = 0x00001000,
            /// <summary>
            ///     Required to retrieve certain information about a process
            /// </summary>
            Terminate = 0x0001,

            /// <summary>
            ///     Required to read memory in a process
            /// </summary>
            VMRead = 0x0010,

            /// <summary>
            ///     Required to wait for the process to terminate using the wait functions.
            /// </summary>
            Synchronize = 0x00100000,
            /// <summary>
            /// The maximum allowed access.
            /// </summary>
            MaximumAllowed = 0x2000000,
        }

        /// <summary>
        ///     A legacy Windows exit code that tells us the process is still active.
        /// </summary>
        internal const int STILL_ACTIVE = 259;

        [Flags]
        private enum SnapshotFlags : uint
        {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            All = (HeapList | Process | Thread | Module),
            Inherit = 0x80000000,
            NoHeaps = 0x40000000
        }

        #endregion


        #region Windows API Functions


        /// <summary>
        ///     Retrieves the process identifier for each process object in the system.
        /// </summary>
        /// <param name="processIds">A pointer to an array that receives the list of process identifiers.</param>
        /// <param name="size">The size of the <paramref name="processIds"/> array, in bytes.</param>
        /// <param name="needed">The number of bytes returned in the <paramref name="processIds"/> array.</param>
        /// <returns>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero.</returns>
        [DllImport("psapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool EnumProcesses([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U4)] [In] [Out]
            uint[] processIds,
            int size,
            [MarshalAs(UnmanagedType.U4)] out uint needed);

        /// <summary>
        ///     Opens an existing local process object.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess([MarshalAs(UnmanagedType.U4)] ProcessAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        /// <summary>
        ///     Retrieves the termination status of the specified process.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess([In] SafeProcessHandle hProcess, [Out] out int lpExitCode);


        /// <summary>
        ///     Retrieves information about the specified process.
        /// </summary>
        /// <param name="processHandle">A handle to the process for which information is to be retrieved.</param>
        /// <param name="processInformationClass">The type of process information to be retrieved. </param>
        /// <param name="processInformation">
        ///     A pointer to a buffer supplied by the calling application into which the function
        ///     writes the requested information.
        /// </param>
        /// <param name="processInformationLength">The size of the buffer pointed to by the ProcessInformation parameter, in bytes.</param>
        /// <param name="returnLength">A pointer to a variable in which the function returns the size of the requested information.</param>
        /// <returns>The function returns an NTSTATUS success or error code.</returns>
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(SafeProcessHandle processHandle,
            int processInformationClass,
            ref ProcessBasicInformation processInformation,
            int processInformationLength,
            out int returnLength);


        /// <summary>
        ///     Retrieves the full name of the executable image for the specified process.
        /// </summary>
        /// <param name="hProcess">
        ///     A handle to the process. This handle must be created with the PROCESS_QUERY_INFORMATION or
        ///     PROCESS_QUERY_LIMITED_INFORMATION access right.
        /// </param>
        /// <param name="dwFlags">The format the name should use.</param>
        /// <param name="lpExeName">The path to the executable image. If the function succeeds, this string is null-terminated.</param>
        /// <param name="lpdwSize">
        ///     On input, specifies the size of the lpExeName buffer, in characters. On success, receives the
        ///     number of characters written to the buffer, not including the null-terminating character.
        /// </param>
        /// <returns>If the function succeeds, the return value is nonzero.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName([In] SafeProcessHandle hProcess, [In] NamePathFormat dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

        /// <summary>
        ///     Retrieves the process identifier of the calling process.
        /// </summary>
        /// <returns></returns>
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        /// <summary>
        ///     Reads memory from another process.
        /// </summary>
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(SafeProcessHandle hProcess,
            IntPtr lpBaseAddress,
            [Out] IntPtr lpBuffer,
            uint nSize,
            out uint lpNumberOfBytesRead);

        /// <summary>
        ///     Parses a Unicode command line string and returns an array of pointers to the command line arguments, along with a
        ///     count of such arguments, in a way that is similar to the standard C run-time argv and argc values.
        /// </summary>
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        /// <summary>
        ///     Retrieves timing information for the specified process.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetProcessTimes(SafeProcessHandle handle,
            out long creation,
            out long exit,
            out long kernel,
            out long user);


        /// <summary>
        /// Terminates the specified process and all of its threads.
        /// </summary>
        /// <param name="processHandle">A handle to the process to be terminated.</param>
        /// <param name="exitCode">The exit code to be used by the process and threads terminated as a result of this call. </param>
        /// <returns>If the function succeeds, the return value is nonzero.</returns>
        [DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool TerminateProcess(SafeProcessHandle processHandle, int exitCode);
        /// <summary>
        /// Waits until the specified object is in the signaled state or the time-out interval elapses.
        /// </summary>
        /// <param name="handle">A handle to the object. </param>
        /// <param name="timeout">The time-out interval, in milliseconds.</param>
        /// <returns></returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeout);

        /// <summary>
        /// Retrieves the Remote Desktop Services session associated with a specified process.
        /// </summary>
        [DllImport("kernel32.dll")]
        internal static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        /// <summary>
        ///  Opens the access token associated with a process.
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(SafeProcessHandle processHandle, int desiredAccess, out SafeAccessTokenHandle tokenHandle);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32W entry);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32W entry);

        /// <summary>
        ///     Closes an open object handle.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        #endregion


        #region Windows API Structs

        /// <summary>
        ///     The UNICODE_STRING structure is used to define Unicode strings.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct UnicodeString
        {
            /// <summary>
            ///     The length, in bytes, of the string stored in <see cref="Buffer"/>.
            /// </summary>
            internal readonly ushort Length;

            /// <summary>
            ///     The length, in bytes, of <see cref="Buffer"/>.
            /// </summary>
            internal readonly ushort MaximumLength;

            /// <summary>
            ///     Pointer to a buffer used to contain a string of wide characters.
            /// </summary>
            internal readonly IntPtr Buffer;
        }

        /// <summary>
        ///     Contains process parameter information.
        ///     https://docs.microsoft.com/en-us/windows/win32/api/winternl/ns-winternl-rtl_user_process_parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct RtlUserProcessParameters
        {
            internal readonly uint MaximumLength;
            internal readonly uint Length;
            internal readonly uint Flags;
            internal readonly uint DebugFlags;
            internal readonly IntPtr ConsoleHandle;
            internal readonly uint ConsoleFlags;
            internal readonly IntPtr StandardInput;
            internal readonly IntPtr StandardOutput;
            internal readonly IntPtr StandardError;

            /// <summary>
            ///     The current working directory of the process.
            /// </summary>
            internal readonly UnicodeString CurrentDirectory;

            internal readonly IntPtr CurrentDirectoryHandle;
            internal readonly UnicodeString DllPath;
            internal readonly UnicodeString ImagePathName;

            /// <summary>
            ///     The command-line string passed to the process.
            /// </summary>
            internal readonly UnicodeString CommandLine;
        }


        /// <summary>
        ///     Contains process information.
        ///     https://docs.microsoft.com/en-us/windows/win32/api/winternl/ns-winternl-peb
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        // ReSharper disable once InconsistentNaming
        private readonly struct PEB
        {
            /// <summary>
            ///     Reserved for internal use by the operating system. Skips a bunch of offsets.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            internal readonly IntPtr[] Reserved;

            /// <summary>
            ///     A pointer to an <see cref="RtlUserProcessParameters"/> structure that contains process parameter information such
            ///     as the command line.
            /// </summary>
            internal readonly IntPtr ProcessParameters;
        }

        /// <summary>
        ///     Used in the NtQueryInformationProcess call.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct ProcessBasicInformation
        {
            internal readonly IntPtr ExitStatus;
            internal readonly IntPtr PebBaseAddress;
            internal readonly IntPtr AffinityMask;
            internal readonly IntPtr BasePriority;
            internal readonly IntPtr UniqueProcessId;
            internal readonly IntPtr InheritedFromUniqueProcessId;
        }

        /// <summary>
        /// Describes an entry from a list of the processes residing in the system address space when a snapshot was taken.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry32W
        {
            const int MAX_PATH = 260;
            public int Size;
            public int Usage;
            public int ProcessID;
            public IntPtr DefaultHeapID;
            public int ModuleID;
            public int Threads;
            public int ParentProcessID;
            public int PriClassBase;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            internal string szExeFile;
        }

        #endregion
    }
}