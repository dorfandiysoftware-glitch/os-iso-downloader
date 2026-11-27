// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    public sealed class UupBuild
    {
        public string Title { get; set; }
        public string Build { get; set; }
        public string Arch { get; set; }
        public long Created { get; set; }
        public string Uuid { get; set; }
        public DateTime Date => DateTimeOffset.FromUnixTimeSeconds(Created).UtcDateTime;
    }

    public sealed class UupLang
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// UUP dump (uupdump.net) lists every Windows build Microsoft ever pushed through Windows Update,
    /// including Insider ones. It also hands out a small script package that builds the ISO on the user's PC.
    /// </summary>
    public static class UupDump
    {
        private const string Api = "https://api.uupdump.net/";

        public static List<UupBuild> ParseBuilds(string json, int max)
        {
            var list = new List<UupBuild>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("response", out JsonElement resp) || !resp.TryGetProperty("builds", out JsonElement builds))
                {
                    return list;
                }
                IEnumerable<JsonElement> items = builds.ValueKind == JsonValueKind.Object ? Values(builds) : builds.EnumerateArray();
                foreach (JsonElement b in items)
                {
                    string arch = Str(b, "arch");
                    if (arch != "amd64" && arch != "arm64" && arch != "x86")
                    {
                        continue;
                    }
                    list.Add(new UupBuild
                    {
                        Title = Str(b, "title"), Build = Str(b, "build"), Arch = arch, Uuid = Str(b, "uuid"),
                        Created = b.TryGetProperty("created", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0,
                    });
                }
            }
            list.Sort((a, b) => b.Created.CompareTo(a.Created));
            if (list.Count > max)
            {
                list.RemoveRange(max, list.Count - max);
            }
            return list;
        }

        private static IEnumerable<JsonElement> Values(JsonElement obj)
        {
            foreach (JsonProperty p in obj.EnumerateObject())
            {
                yield return p.Value;
            }
        }

        private static string Str(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        public static List<UupLang> ParseLangs(string json)
        {
            var list = new List<UupLang>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement resp = doc.RootElement.GetProperty("response");
                JsonElement fancy;
                bool hasFancy = resp.TryGetProperty("langFancyNames", out fancy) && fancy.ValueKind == JsonValueKind.Object;
                foreach (JsonElement l in resp.GetProperty("langList").EnumerateArray())
                {
                    string code = l.GetString();
                    if (code == "neutral" || code == "models")
                    {
                        continue;
                    }
                    string name = hasFancy && fancy.TryGetProperty(code, out JsonElement n) ? n.GetString() : code;
                    list.Add(new UupLang { Code = code, Name = name });
                }
            }
            // The most useful languages first.
            string[] first = { "en-us", "uk-ua", "ru-ru", "en-gb" };
            list.Sort((a, b) =>
            {
                int ia = Array.IndexOf(first, a.Code), ib = Array.IndexOf(first, b.Code);
                if (ia < 0) ia = 99;
                if (ib < 0) ib = 99;
                return ia != ib ? ia.CompareTo(ib) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static async Task<List<UupBuild>> FetchBuildsAsync(HttpClient http, string search, int max, CancellationToken ct)
        {
            string json = await http.GetStringAsync(Api + "listid.php?sortByDate=1&search=" + Uri.EscapeDataString(search), ct).ConfigureAwait(false);
            return ParseBuilds(json, max);
        }

        public static async Task<List<UupLang>> FetchLangsAsync(HttpClient http, string uuid, CancellationToken ct)
        {
            string json = await http.GetStringAsync(Api + "listlangs.php?id=" + Uri.EscapeDataString(uuid), ct).ConfigureAwait(false);
            return ParseLangs(json);
        }

        /// <summary>"uup:{uuid}:{lang}:{edition}" to the address of the script package.</summary>
        public static string PackageUrl(string spec)
        {
            string[] p = spec.Substring(4).Split(':');
            if (p.Length != 3)
            {
                throw new FormatException("Bad UUP spec " + spec);
            }
            return "https://uupdump.net/get.php?id=" + Uri.EscapeDataString(p[0]) + "&pack=" + Uri.EscapeDataString(p[1])
                + "&edition=" + Uri.EscapeDataString(p[2]) + "&autodl=2";
        }

        /// <summary>Downloads the script package, unpacks it into <paramref name="dir"/> and returns the path of the .cmd that builds the ISO.</summary>
        public static async Task<string> PreparePackageAsync(HttpClient http, string spec, string dir, CancellationToken ct)
        {
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, "uup-package.zip");
            using (var req = new HttpRequestMessage(HttpMethod.Get, PackageUrl(spec)))
            using (HttpResponseMessage r = await http.SendAsync(req, ct).ConfigureAwait(false))
            {
                if ((int)r.StatusCode == 429)
                {
                    throw new InvalidOperationException("UUP dump просить зачекати (забагато запитів). Спробуй за хвилину.");
                }
                r.EnsureSuccessStatusCode();
                byte[] data = await r.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (data.Length < 4 || data[0] != (byte)'P' || data[1] != (byte)'K')
                {
                    throw new InvalidOperationException("UUP dump повернув не пакет, а щось інше.");
                }
                File.WriteAllBytes(zip, data);
            }
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, dir, true);
            File.Delete(zip);
            string cmd = Path.Combine(dir, "uup_download_windows.cmd");
            if (!File.Exists(cmd))
            {
                throw new InvalidOperationException("У пакеті UUP dump немає uup_download_windows.cmd.");
            }
            return cmd;
        }
    }

    public sealed class CrosImage
    {
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public string Url { get; set; }
        public string Sha1 { get; set; }
        public long Size { get; set; }
    }

    /// <summary>Google's list of ChromeOS recovery images (one per Chromebook board).</summary>
    public static class CrosCatalog
    {
        public const string ListUrl = "https://dl.google.com/dl/edgedl/chromeos/recovery/recovery.json";

        public static List<CrosImage> Parse(string json)
        {
            var list = new List<CrosImage>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                foreach (JsonElement e in doc.RootElement.EnumerateArray())
                {
                    string channel = Str(e, "channel");
                    string url = Str(e, "url");
                    if (url == null || (channel != null && !string.Equals(channel, "STABLE", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    list.Add(new CrosImage
                    {
                        Name = Str(e, "name") ?? Str(e, "model") ?? Str(e, "file"),
                        Manufacturer = Str(e, "manufacturer") ?? "Інше",
                        Url = url,
                        Sha1 = Str(e, "sha1"),
                        Size = e.TryGetProperty("filesize", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0,
                    });
                }
            }
            return list;
        }

        private static string Str(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }
}
