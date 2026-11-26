// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    /// <summary>What is really downloaded: the final address, the file name and the expected hash.</summary>
    public sealed class ResolvedFile
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        /// <summary>"sha256" or "sha512", null when there is nothing to check against.</summary>
        public string HashAlgo { get; set; }
        public string Hash { get; set; }
        public long Size { get; set; }
    }

    public static class Resolver
    {
        public static async Task<ResolvedFile> ResolveAsync(HttpClient http, FileEntry entry, CancellationToken ct)
        {
            string url = entry.Url;
            ResolvedFile fromArchive = null;
            if (string.IsNullOrEmpty(url))
            {
                if (entry.Resolve == null)
                {
                    throw new InvalidOperationException("The entry has neither a link nor a rule to find it.");
                }
                if (string.Equals(entry.Resolve.Type, "archive", StringComparison.OrdinalIgnoreCase))
                {
                    string json = await http.GetStringAsync("https://archive.org/metadata/" + Uri.EscapeDataString(entry.Resolve.Id) + "/files", ct).ConfigureAwait(false);
                    fromArchive = PickFromArchive(json, entry.Resolve);
                    url = fromArchive.Url;
                }
                else
                {
                    url = await FindNewestAsync(http, entry.Resolve, ct).ConfigureAwait(false);
                }
            }
            string name = FileNameOf(url);
            var result = new ResolvedFile { Url = url, FileName = name, Size = entry.Size };
            if (fromArchive != null)
            {
                result.Size = fromArchive.Size;
                result.HashAlgo = fromArchive.HashAlgo;
                result.Hash = fromArchive.Hash;
            }
            Checksum sum = entry.Checksum;
            if (sum != null)
            {
                result.HashAlgo = (sum.Algo ?? "sha256").ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(sum.Value))
                {
                    result.Hash = sum.Value.Trim().ToLowerInvariant();
                }
                else if (!string.IsNullOrWhiteSpace(sum.Url))
                {
                    string sumUrl = sum.Url.Replace("{file}", name);
                    string text = await http.GetStringAsync(sumUrl, ct).ConfigureAwait(false);
                    result.Hash = ChecksumParser.Find(text, name);
                    if (result.Hash == null)
                    {
                        throw new InvalidOperationException("The checksum file does not list " + name + ".");
                    }
                }
            }
            return result;
        }

        /// <summary>Picks a file out of the archive.org metadata JSON ("/metadata/{id}/files").</summary>
        public static ResolvedFile PickFromArchive(string json, Resolve rule)
        {
            var pattern = new Regex("^(" + rule.Pattern + ")$", RegexOptions.IgnoreCase);
            Regex exclude = string.IsNullOrEmpty(rule.Exclude) ? null : new Regex(rule.Exclude, RegexOptions.IgnoreCase);
            var found = new List<System.Text.Json.JsonElement>();
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                System.Text.Json.JsonElement files;
                if (!doc.RootElement.TryGetProperty("result", out files))
                {
                    throw new InvalidOperationException("archive.org returned no file list for " + rule.Id + ".");
                }
                foreach (System.Text.Json.JsonElement f in files.EnumerateArray())
                {
                    string n = f.GetProperty("name").GetString();
                    if (pattern.IsMatch(n) && (exclude == null || !exclude.IsMatch(n)))
                    {
                        found.Add(f.Clone());
                    }
                }
            }
            if (found.Count == 0)
            {
                throw new InvalidOperationException("No file in archive.org item " + rule.Id + " matches " + rule.Pattern + ".");
            }
            found.Sort((a, b) => VersionCompare.Instance.Compare(a.GetProperty("name").GetString(), b.GetProperty("name").GetString()));
            System.Text.Json.JsonElement best = found[found.Count - 1];
            string name = best.GetProperty("name").GetString();
            var parts = name.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }
            var r = new ResolvedFile
            {
                Url = "https://archive.org/download/" + Uri.EscapeDataString(rule.Id) + "/" + string.Join("/", parts),
                FileName = name.Substring(name.LastIndexOf('/') + 1),
            };
            System.Text.Json.JsonElement v;
            if (best.TryGetProperty("size", out v))
            {
                long size;
                if (long.TryParse(v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : v.ToString(), out size))
                {
                    r.Size = size;
                }
            }
            if (best.TryGetProperty("sha1", out v) && !string.IsNullOrEmpty(v.GetString()))
            {
                r.HashAlgo = "sha1";
                r.Hash = v.GetString().ToLowerInvariant();
            }
            return r;
        }

        public static async Task<string> FindNewestAsync(HttpClient http, Resolve rule, CancellationToken ct)
        {
            string html = await http.GetStringAsync(rule.Base, ct).ConfigureAwait(false);
            return PickNewest(html, rule);
        }

        /// <summary>Finds the newest link in a directory page that matches the rule.</summary>
        public static string PickNewest(string html, Resolve rule)
        {
            var pattern = new Regex("^(" + rule.Pattern + ")$", RegexOptions.IgnoreCase);
            Regex exclude = string.IsNullOrEmpty(rule.Exclude) ? null : new Regex(rule.Exclude, RegexOptions.IgnoreCase);
            var names = new List<string>();
            foreach (Match m in Regex.Matches(html, "href\\s*=\\s*[\"']([^\"'#?]+)[\"']", RegexOptions.IgnoreCase))
            {
                string name = WebUtility.UrlDecode(FileNameOf(m.Groups[1].Value));
                if (pattern.IsMatch(name) && (exclude == null || !exclude.IsMatch(name)) && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
            if (names.Count == 0)
            {
                throw new InvalidOperationException("No file in " + rule.Base + " matches " + rule.Pattern + ".");
            }
            names.Sort(VersionCompare.Instance);
            string newest = names[names.Count - 1];
            string baseUrl = rule.Base.EndsWith("/") ? rule.Base : rule.Base + "/";
            return baseUrl + Uri.EscapeDataString(newest);
        }

        public static string FileNameOf(string url)
        {
            string path = url;
            int q = path.IndexOfAny(new[] { '?', '#' });
            if (q >= 0)
            {
                path = path.Substring(0, q);
            }
            path = path.TrimEnd('/');
            int slash = path.LastIndexOf('/');
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            return WebUtility.UrlDecode(name);
        }
    }
}
