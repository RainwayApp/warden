using System;
using System.IO;

namespace Warden.Windows
{
    /// <summary>
    ///     Encapsulates information about a system process object.
    /// </summary>
    public sealed class ProcessInfo : IEquatable<ProcessInfo>
    {
        /// <summary>
        ///     Creates a read-only record of the provided process info.
        /// </summary>
        /// <param name="id">The system-unique identifier of the process.</param>
        /// <param name="parentProcessId">The system-unique identifier of the process object that created the current process.</param>
        /// <param name="image">The name of the executable belonging to the process.</param>
        /// <param name="commandLine">The command-line string passed to the process.</param>
        /// <param name="workingDirectory">The current working directory of the process.</param>
        /// <param name="creationTime">The time at which the process object was created on the system.</param>
        public ProcessInfo(int id,
            int parentProcessId,
            string image,
            string commandLine,
            string workingDirectory,
            long creationTime)
        {
            Id = id;
            ParentProcessId = parentProcessId;
            Image = image;
            CommandLine = commandLine;
            WorkingDirectory = workingDirectory;
            Name = Path.GetFileNameWithoutExtension(Image);
            CreationDate = DateTime.FromFileTime(creationTime);
            if (ProcessNative.ProcessIdToSessionId((uint) Id, out var sessionId ))
            {
                SessionId = (int) sessionId;
            }
        }
        
        /// <summary>
        /// Gets the Terminal Services session identifier for the process.
        /// </summary>
        public int SessionId { get; }
        
        /// <summary>
        /// Gets the date that the process began executing.
        /// </summary>
        public DateTime CreationDate { get; }

        /// <summary>
        ///     Gets the working directory for the process.
        /// </summary>
        public string WorkingDirectory { get; }

        /// <summary>
        ///     The name that the system uses to identify the process to the user.
        /// </summary>
        /// <remarks>
        ///     This property holds an executable file name, such as Outlook, that does not include the .exe extension or the path.
        /// </remarks>
        public string Name { get; }

        /// <summary>
        ///     Gets the full path to the process executable.
        /// </summary>
        public string Image { get; }

        /// <summary>
        ///     Gets the command-line string passed to the process.
        /// </summary>
        public string CommandLine { get; }

        /// <summary>
        ///     The system-unique identifier of the process object that created the current process.
        /// </summary>
        /// <remarks>
        ///     Process identifier numbers are reused, so they only identify a process for the lifetime of that process. It is
        ///     possible that <see cref="ParentProcessId"/> incorrectly refers to a process that reuses a process identifier. You
        ///     can use the <see cref="CreationDate"/> property to determine whether the specified parent was created after the
        ///     process represented by this <see cref="ProcessInfo"/> instance was created.
        /// </remarks>
        public int ParentProcessId { get; }

        /// <summary>
        ///     The system-unique identifier of the process.
        /// </summary>
        /// <remarks>
        ///     Process IDs are valid from process creation time to process termination. Upon termination, that same numeric
        ///     identifier can be applied to a new process. This means that you cannot use process ID alone to monitor a particular
        ///     process.
        /// </remarks>
        public int Id { get; }


        /// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        ///     <see langword="true"/> if the current object is equal to the <paramref name="other"/> parameter; otherwise,
        ///     <see langword="false"/>.
        /// </returns>
        public bool Equals(ProcessInfo? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return Name == other.Name && Image == other.Image && ParentProcessId == other.ParentProcessId && Id == other.Id && CommandLine == other.CommandLine && WorkingDirectory == other.WorkingDirectory &&
                CreationDate == other.CreationDate && SessionId == other.SessionId;
        }

        /// <summary>Determines whether the specified object is equal to the current object.</summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>
        ///     <see langword="true"/> if the specified object  is equal to the current object; otherwise, <see langword="false"/>.
        /// </returns>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            return obj.GetType() == GetType() && Equals((ProcessInfo) obj);
        }

        /// <summary>Serves as the default hash function.</summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            var hash = 1;
            hash ^= Id.GetHashCode();
            hash ^= ParentProcessId.GetHashCode();
            hash ^= Image.GetHashCode();
            hash ^= Name.GetHashCode();
            hash ^= CommandLine.GetHashCode();
            hash ^= WorkingDirectory.GetHashCode();
            hash ^= CreationDate.GetHashCode();
            hash ^= SessionId.GetHashCode();
            return hash;
        }

        public static bool operator ==(ProcessInfo left, ProcessInfo right) => Equals(left, right);


        public static bool operator !=(ProcessInfo left, ProcessInfo right) => !Equals(left, right);

        /// <summary>Returns a string that represents the current object.</summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString() => $"ID => {Id}\nName => {Name}\nParent => {ParentProcessId}\nPath => {Image}\nCommandline => {CommandLine}\nWorking Directory => {WorkingDirectory}\nCreation Time => {CreationDate}\nSession ID => {SessionId}";
    }
}