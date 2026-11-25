// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Text.RegularExpressions;

namespace OsIsoDownloader.Core
{
    /// <summary>Reads the hash of one file out of the usual checksum files.</summary>
    public static class ChecksumParser
    {
        /// <summary>
        /// Understands "hash  name" and "hash *name" (GNU), "SHA256 (name) = hash" (BSD), and a
        /// file holding only the hash.
        /// </summary>
        /// <returns>the lower-case hex hash, or null if the file is not mentioned</returns>
        public static string Find(string text, string fileName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            string only = null;
            int lines = 0;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("-----")
                        || line.StartsWith("Hash:"))
                {
                    continue;
                }
                lines++;
                Match bsd = Regex.Match(line, @"^[A-Za-z0-9-]+\s*\((.+)\)\s*=\s*([0-9A-Fa-f]{32,})$");
                if (bsd.Success)
                {
                    if (SameName(bsd.Groups[1].Value, fileName))
                    {
                        return bsd.Groups[2].Value.ToLowerInvariant();
                    }
                    continue;
                }
                Match gnu = Regex.Match(line, @"^([0-9A-Fa-f]{32,})\s+\*?(.+)$");
                if (gnu.Success)
                {
                    if (SameName(gnu.Groups[2].Value, fileName))
                    {
                        return gnu.Groups[1].Value.ToLowerInvariant();
                    }
                    continue;
                }
                if (Regex.IsMatch(line, @"^[0-9A-Fa-f]{32,}$"))
                {
                    only = line.ToLowerInvariant();
                }
            }
            return lines == 1 ? only : null;
        }

        private static bool SameName(string listed, string fileName)
        {
            string a = listed.Trim().Replace('\\', '/');
            int slash = a.LastIndexOf('/');
            if (slash >= 0)
            {
                a = a.Substring(slash + 1);
            }
            return string.Equals(a, fileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
