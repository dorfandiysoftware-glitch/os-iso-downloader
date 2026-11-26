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
        public static string Find(string text, string fileName, string algo = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            int want = HexLength(algo);
            string only = null;
            int lines = 0;
            int entries = 0;
            string lastHash = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("-----")
                        || line.StartsWith("Hash:"))
                {
                    continue;
                }
                Match bsd = Regex.Match(line, @"^[A-Za-z0-9-]+\s*\((.+)\)\s*=\s*([0-9A-Fa-f]{32,})$");
                if (bsd.Success && want > 0 && bsd.Groups[2].Value.Length != want)
                {
                    continue;
                }
                if (bsd.Success)
                {
                    lines++;
                    entries++;
                    lastHash = bsd.Groups[2].Value.ToLowerInvariant();
                    if (SameName(bsd.Groups[1].Value, fileName))
                    {
                        return bsd.Groups[2].Value.ToLowerInvariant();
                    }
                    continue;
                }
                Match gnu = Regex.Match(line, @"^([0-9A-Fa-f]{32,})\s+\*?(.+)$");
                if (gnu.Success && want > 0 && gnu.Groups[1].Value.Length != want)
                {
                    continue;
                }
                if (gnu.Success)
                {
                    lines++;
                    entries++;
                    lastHash = gnu.Groups[1].Value.ToLowerInvariant();
                    if (SameName(gnu.Groups[2].Value, fileName))
                    {
                        return gnu.Groups[1].Value.ToLowerInvariant();
                    }
                    continue;
                }
                if (Regex.IsMatch(line, @"^[0-9A-Fa-f]{32,}$") && (want == 0 || line.Length == want))
                {
                    lines++;
                    only = line.ToLowerInvariant();
                }
            }
            if (lines == 1 && only != null)
            {
                return only;
            }
            // A file with a single entry whose name differs (e.g. a "-Current.iso" alias of a dated file):
            // the hash is still the one for the file that was published there.
            return entries == 1 && lines == 1 ? lastHash : null;
        }

        private static int HexLength(string algo)
        {
            switch ((algo ?? "").ToLowerInvariant())
            {
                case "md5":
                    return 32;
                case "sha1":
                    return 40;
                case "sha256":
                    return 64;
                case "sha512":
                    return 128;
                default:
                    return 0;
            }
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
