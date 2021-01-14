using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Warden.Windows;

namespace Warden.Utils
{
    /// <summary>
    ///     Static methods that help parse and validate strings.
    /// </summary>
    internal static class StringHelpers
    {
        
        /// <summary>
        ///     Determines if the specified string  is a universal naming convention (UNC) path.
        /// </summary>
        internal static bool IsUncPath(string path) => Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsUnc;

        /// <summary>
        ///     Determines if a string is a wellformed absolute URI .
        /// </summary>
        internal static bool IsValidUri(this string path) => Uri.IsWellFormedUriString(path, UriKind.Absolute) || Regex.IsMatch(path, "^\\w+://", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Gets the scheme of a string containing a uri.
        /// </summary>
        /// <remarks>
        ///  <see cref="Uri.IsWellFormedUriString"/> will fail on uris such as this "spotify://album:27ftYHLeunzcSzb33Wk1hf" even though Windows is capable of launching it still.
        ///  To work around that we use regex to extra the scheme as a fallback.
        /// </remarks>
        internal static string GetUriScheme(this string uri)
        {
            if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
            {
                return new Uri(uri).Scheme;
            }
            if (Regex.Match(uri, @"^[^:]+(?=:\/\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase) is {Success: true} match)
            {
                return match.Value.Trim();
            }
            return string.Empty;
        }

        /// <summary>
        ///     Attempts to normalize a given directory or file path to a consistent standard by removing potential escaped characters, inconsistent directory delimiters, and more.
        /// </summary>
        internal static string NormalizePath(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            // if the input is a valid URI return so we don't mess it up.
            if (IsValidUri(path) && !IsUncPath(path))
            {
                return !path.EndsWith("//") ? $"{path}//" : path;
                //throw new WardenException($"\"{path}\" does not meet the requirements for a Warden path. Only valid application URIs or the fully qualified name of a file or directory are allowed.");
            }
            // work around an issue where the below code doesn't like directories that are just "C:"
            if (path.Length == 2 && path.EndsWith(":"))
            {
                path = $"{path}\\";
            }
            return Path.GetFullPath(new Uri(path).LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }


        /// <summary>
        /// Parses and properly formats a command-line string.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="processImage"></param>
        /// <returns></returns>
        internal static string FormatCommandLine(this string value, string? processImage = null) => ProcessNative.FormatCommandLine(value, processImage);
        
        /// <summary>
        ///     Escape commandline arguments so they may be used safely.
        /// </summary>
        /// <remarks>
        ///     See
        ///     https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/
        /// </remarks>
        /// <param name="args">The arguments to concatenate.</param>
        /// <returns>The escaped arguments, concatenated.</returns>
        internal static string EscapeArguments(this string[] args) => string.Join(" ", args.Select(EscapeSingleArg));
        

        /// <summary>
        ///     Escapes a single command-line argument.
        /// </summary>
        private static string EscapeSingleArg(string arg)
        {
            var sb = new StringBuilder();

            var needsQuotes = ShouldSurroundWithQuotes(arg);
            var isQuoted = needsQuotes || IsSurroundedWithQuotes(arg);

            if (needsQuotes)
            {
                sb.Append('"');
            }

            for (var i = 0; i < arg.Length; ++i)
            {
                var backslashes = 0;

                // Consume all backslashes
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == arg.Length && isQuoted)
                {
                    // Escape any backslashes at the end of the arg when the argument is also quoted.
                    // This ensures the outside quote is interpreted as an argument delimiter
                    sb.Append('\\', 2 * backslashes);
                }
                else if (i == arg.Length)
                {
                    // At then end of the arg, which isn't quoted,
                    // just add the backslashes, no need to escape
                    sb.Append('\\', backslashes);
                }
                else if (arg[i] == '"')
                {
                    // Escape any preceding backslashes and the quote
                    sb.Append('\\', 2 * backslashes + 1);
                    sb.Append('"');
                }
                else
                {
                    // Output any consumed backslashes and the character
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }

            if (needsQuotes)
            {
                sb.Append('"');
            }

            return sb.ToString();
        }


        /// <summary>
        ///     Determines if the specified string should be surrounded with double quotes.
        /// </summary>
        private static bool ShouldSurroundWithQuotes(string value) => !IsSurroundedWithQuotes(value) && ContainsWhitespace(value);


        /// <summary>
        ///     Determines if the specified string is surrounded with double quotes.
        /// </summary>
        private static bool IsSurroundedWithQuotes(string value) => value.Length switch
        {
            <= 1 => false,
            _ => value[0] == '"' && value[value.Length - 1] == '"'
        };


        /// <summary>
        ///     Determines if the specified string contains whitespace.
        /// </summary>
        private static bool ContainsWhitespace(string value)
            => value.IndexOfAny(new[] {' ', '\t', '\n'}) >= 0;


        /// <summary>
        ///     Find the longest string that is a substring of the <paramref name="source"/> and <paramref name="target"/> strings.
        /// </summary>
        internal static int LongestCommonSubstring(string source, string target, out string sequence)
        {
            sequence = string.Empty;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            {
                return 0;
            }

            source = source.ToLower().Trim();
            target = target.ToLower().Trim();
            var num = new int[source.Length, target.Length];
            var maxlen = 0;
            var lastSubsBegin = 0;
            var sequenceBuilder = new StringBuilder();

            for (var i = 0; i < source.Length; i++)
            {
                for (var j = 0; j < target.Length; j++)
                {
                    if (source[i] != target[j])
                    {
                        num[i, j] = 0;
                    }
                    else
                    {
                        if (i == 0 || j == 0)
                        {
                            num[i, j] = 1;
                        }
                        else
                        {
                            num[i, j] = 1 + num[i - 1, j - 1];
                        }

                        if (num[i, j] <= maxlen)
                        {
                            continue;
                        }
                        maxlen = num[i, j];
                        var thisSubsBegin = i - num[i, j] + 1;
                        if (lastSubsBegin == thisSubsBegin)
                        {
                            sequenceBuilder.Append(source[i]);
                        }
                        else
                        {
                            lastSubsBegin = thisSubsBegin;
                            sequenceBuilder.Length = 0;
                            sequenceBuilder.Append(source.Substring(lastSubsBegin, i + 1 - lastSubsBegin));
                        }
                    }
                }
            }
            sequence = sequenceBuilder.ToString().Trim();
            return maxlen;
        }
    }
}