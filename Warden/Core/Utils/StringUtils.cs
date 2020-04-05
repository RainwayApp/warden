using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Warden.Core.Utils
{
    internal static class StringUtils
    {
      
        public static List<string> SplitSpace(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }
            return Regex.Matches(input, @"[\""].+?[\""]|[^ ]+")
                .Cast<Match>()
                .Select(m => m.Value)
                .ToList();
        }

        public static string RemoveWhitespace(this string input)
        {
            return new string(input.ToCharArray()
                .Where(c => !Char.IsWhiteSpace(c))
                .ToArray());
        }

        public static int LongestCommonSubstring(string source, string target, out string sequence)
        {
            sequence = string.Empty;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0;

            source = source.ToLower().RemoveWhitespace();
            target = target.ToLower().RemoveWhitespace();
            var num = new int[source.Length, target.Length];
            var maxlen = 0;
            var lastSubsBegin = 0;
            var sequenceBuilder = new StringBuilder();

            for (var i = 0; i < source.Length; i++)
            {
                for (var j = 0; j < target.Length; j++)
                {
                    if (source[i] != target[j])
                        num[i, j] = 0;
                    else
                    {
                        if ((i == 0) || (j == 0))
                            num[i, j] = 1;
                        else
                            num[i, j] = 1 + num[i - 1, j - 1];

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
                            sequenceBuilder.Append(source.Substring(lastSubsBegin, (i + 1) - lastSubsBegin));
                        }
                    }
                }
            }
            sequence = sequenceBuilder.ToString();
            return maxlen;
        }
    }
}
