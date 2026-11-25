// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace OsIsoDownloader.Core
{
    /// <summary>Orders names by the numbers inside them: 13.10 is newer than 13.9.</summary>
    public sealed class VersionCompare : IComparer<string>
    {
        public static readonly VersionCompare Instance = new VersionCompare();

        public int Compare(string a, string b)
        {
            int i = 0;
            int j = 0;
            a = a ?? "";
            b = b ?? "";
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i;
                    int sj = j;
                    while (i < a.Length && char.IsDigit(a[i]))
                    {
                        i++;
                    }
                    while (j < b.Length && char.IsDigit(b[j]))
                    {
                        j++;
                    }
                    string na = a.Substring(si, i - si).TrimStart('0');
                    string nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length)
                    {
                        return na.Length < nb.Length ? -1 : 1;
                    }
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0)
                    {
                        return c < 0 ? -1 : 1;
                    }
                }
                else
                {
                    int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0)
                    {
                        return c < 0 ? -1 : 1;
                    }
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
