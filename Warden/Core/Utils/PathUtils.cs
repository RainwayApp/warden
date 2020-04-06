using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Warden.Core.Exceptions;

namespace Warden.Core.Utils
{
    /// <summary>
    /// </summary>
    internal static class PathUtils
    {
        /// <summary>
        ///     Regex for matching a mapped executable path: C:\Test\Path\File.exe
        /// </summary>
        private static readonly Regex LocalProgramPath = new Regex(@"[A-Z]:\\[^\/:\*\""\?<>\|]+\.exe");

        /// <summary>
        ///     Regex for matching a network executable path: \\workspace\\domain\\File.exe
        /// </summary>
        private static readonly Regex NetworkProgramPath = new Regex(@"\\{2}[^\/:\*\?\""<>\|]+\.exe");

        /// <summary>
        ///     Our primary regex which combines <see cref="LocalProgramPath" /> and <see cref="NetworkProgramPath" />.
        /// </summary>
        private static readonly Regex ProgramPath =
            new Regex($@"{LocalProgramPath}|{NetworkProgramPath}", RegexOptions.IgnoreCase);

        /// <summary>
        ///     A behemoth regular expression for parsing a programs command line arguments.
        /// </summary>
        private static readonly Regex ProgramArguments = new Regex(@"(?:^[ \t]*((?>[^ \t""\r\n]+|""[^""]+(?:""|$))+)|(?!^)[ \t]+((?>[^ \t""\\\r\n]+|(?<!\\)(?:\\\\)*""[^""\\\r\n]*(?:\\.[^""\\\r\n]*)*""{1,2}|(?:\\(?:\\\\)*"")+|\\+(?!""))+)|([^ \t\r\n]))", RegexOptions.IgnoreCase);

        /// <summary>
        /// Attempts to locate commandline arguments 
        /// </summary>
        /// <param name="command">the string that is known to contain all of a processes arguments</param>
        /// <returns></returns>
        internal static string FindCommandLineArguments(string command)
        {
            var executable = FindFullyQualifiedName(command);
            var matches = ProgramArguments.Matches(command);
            var arguments = new StringBuilder();
            foreach (Match match in matches)
            {
                var argument = match.Value;
                if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(argument, executable, CompareOptions.IgnoreCase) >= 0)
                {
                    continue;
                }
                arguments.Append(argument);
            }
            return arguments.ToString().TrimStart();
        }

        /// <summary>
        /// Attempts to find a fully qualified file name in a string.
        /// </summary>
        /// <param name="command">the string that is known to possibly contain an executable path.</param>
        /// <returns></returns>
        internal static string FindFullyQualifiedName(string command)
        {
            var match = ProgramPath.Match(command);
            return match.Success ? NormalizePath(match.Value) : null;
        }


        /// <summary>
        /// Returns the directory information for the specified path string.
        /// </summary>
        /// <param name="path">The path of a file or directory.</param>
        /// <returns></returns>
        internal static string GetDirectoryName(string path)
        {
            return NormalizePath(new FileInfo(path).DirectoryName);
        }

        /// <summary>
        ///     Checks if a URI is absolute
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool IsValidUri(string path)
        {
            return Uri.IsWellFormedUriString(path, UriKind.Absolute);
        }

        /// <summary>
        ///     Gets whether the specified path is a universal naming convention (UNC) path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool IsUncPath(string path)
        {
            return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsUnc;
        }

        /// <summary>
        ///     Attempts to normalize a given directory or file path to a consistent standard.
        ///     We do this by removing potential escaped characters, inconsistent directory delimiters, and more.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            // if the input is a valid URI return so we don't mess it up.
            if (IsValidUri(path) && !IsUncPath(path))
            {
                return path;
               //throw new WardenException($"\"{path}\" does not meet the requirements for a Warden path. Only valid application URIs or the fully qualified name of a file or directory are allowed.");
            }
            // work around an issue where the below code doesn't like directories that are just "C:"
            if (path.Length == 2 && path.EndsWith(":"))
            {
                path = $"{path}/";
            }
            return Path.GetFullPath(new Uri(path).LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
