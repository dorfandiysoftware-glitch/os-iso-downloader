// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace OsIsoDownloader.Core
{
    /// <summary>Reads Apple's XML property lists into dictionaries, lists, strings, longs, bools.</summary>
    public static class Plist
    {
        public static object Parse(string xml)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var sr = new StringReader(xml))
            using (XmlReader r = XmlReader.Create(sr, settings))
            {
                XDocument doc = XDocument.Load(r);
                XElement root = doc.Root;
                if (root == null || root.Name.LocalName != "plist")
                {
                    throw new FormatException("Not a property list.");
                }
                XElement first = root.Elements().FirstOrDefault();
                return first == null ? null : Read(first);
            }
        }

        private static object Read(XElement e)
        {
            switch (e.Name.LocalName)
            {
                case "dict":
                {
                    var d = new Dictionary<string, object>();
                    XElement[] kids = e.Elements().ToArray();
                    for (int i = 0; i + 1 < kids.Length; i += 2)
                    {
                        d[kids[i].Value] = Read(kids[i + 1]);
                    }
                    return d;
                }
                case "array":
                    return e.Elements().Select(Read).ToList();
                case "string":
                    return e.Value;
                case "integer":
                    long n;
                    return long.TryParse(e.Value, out n) ? n : 0L;
                case "real":
                    return e.Value;
                case "true":
                    return true;
                case "false":
                    return false;
                case "date":
                    DateTime dt;
                    return DateTime.TryParse(e.Value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out dt) ? (object)dt : null;
                default:
                    return e.Value;
            }
        }

        public static Dictionary<string, object> Dict(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static string Str(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }
    }

    public sealed class AppleInstaller
    {
        public string ProductId { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string Build { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        /// <summary>SHA-1 of the package if the catalog lists one, else null.</summary>
        public string Sha1 { get; set; }
        public string Sha256 { get; set; }
        public DateTime? PostDate { get; set; }
        public string DistUrl { get; set; }

        public string DisplayName
        {
            get
            {
                string t = string.IsNullOrEmpty(Title) ? "macOS" : Title;
                string v = string.IsNullOrEmpty(Version) ? "" : " " + Version;
                string b = string.IsNullOrEmpty(Build) ? "" : " (" + Build + ")";
                return t + v + b;
            }
        }
    }

    /// <summary>Lists the full macOS installers (InstallAssistant.pkg) Apple publishes in its software update catalog.</summary>
    public static class AppleCatalog
    {
        private const string Tail = "-10.16-10.15-10.14-10.13-10.12-10.11-10.10-10.9-mountainlion-lion-snowleopard-leopard.merged-1.sucatalog";

        /// <summary>The newest chain first. Each chain also lists everything older.</summary>
        public static readonly string[] CatalogUrls =
        {
            "https://swscan.apple.com/content/catalogs/others/index-27-26-15-14-13-12" + Tail + ".gz",
            "https://swscan.apple.com/content/catalogs/others/index-26-15-14-13-12" + Tail + ".gz",
            "https://swscan.apple.com/content/catalogs/others/index-16-15-14-13-12" + Tail + ".gz",
            "https://swscan.apple.com/content/catalogs/others/index-15-14-13-12" + Tail + ".gz",
        };

        public static string MarketingName(string version)
        {
            string major = (version ?? "").Split('.')[0];
            switch (major)
            {
                case "10": return "macOS (10.x)";
                case "11": return "macOS Big Sur";
                case "12": return "macOS Monterey";
                case "13": return "macOS Ventura";
                case "14": return "macOS Sonoma";
                case "15": return "macOS Sequoia";
                case "26": return "macOS Tahoe";
                default: return string.IsNullOrEmpty(major) ? "macOS" : "macOS " + major;
            }
        }

        public static async Task<List<AppleInstaller>> FetchAsync(HttpClient http, CancellationToken ct)
        {
            Exception last = null;
            foreach (string url in CatalogUrls)
            {
                try
                {
                    byte[] raw = await http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                    string xml = Unzip(raw);
                    List<AppleInstaller> list = Extract(xml);
                    if (list.Count == 0)
                    {
                        continue;
                    }
                    await FillDetailsAsync(http, list, ct).ConfigureAwait(false);
                    list.Sort((a, b) =>
                    {
                        int c = VersionCompare.Instance.Compare(b.Version ?? "", a.Version ?? "");
                        return c != 0 ? c : string.CompareOrdinal(b.Build ?? "", a.Build ?? "");
                    });
                    return list;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    last = e;
                }
            }
            throw new InvalidOperationException("Не вдалося отримати каталог Apple" + (last != null ? ": " + last.Message : "."));
        }

        private static string Unzip(byte[] raw)
        {
            if (raw.Length > 2 && raw[0] == 0x1f && raw[1] == 0x8b)
            {
                using (var gz = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
                using (var sr = new StreamReader(gz))
                {
                    return sr.ReadToEnd();
                }
            }
            return System.Text.Encoding.UTF8.GetString(raw);
        }

        /// <summary>Finds every product that carries an InstallAssistant.pkg.</summary>
        public static List<AppleInstaller> Extract(string catalogXml)
        {
            var list = new List<AppleInstaller>();
            var root = Plist.Parse(catalogXml) as Dictionary<string, object>;
            Dictionary<string, object> products = Plist.Dict(root, "Products");
            if (products == null)
            {
                return list;
            }
            foreach (KeyValuePair<string, object> kv in products)
            {
                var product = kv.Value as Dictionary<string, object>;
                object pk;
                if (product == null || !product.TryGetValue("Packages", out pk))
                {
                    continue;
                }
                var packages = pk as List<object>;
                if (packages == null)
                {
                    continue;
                }
                foreach (object o in packages)
                {
                    string url = Plist.Str(o, "URL");
                    if (url == null || !url.EndsWith("InstallAssistant.pkg", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var a = new AppleInstaller { ProductId = kv.Key, Url = url.Replace("http://", "https://") };
                    object size;
                    if (((Dictionary<string, object>)o).TryGetValue("Size", out size) && size is long)
                    {
                        a.Size = (long)size;
                    }
                    string digest = Plist.Str(o, "Digest");
                    if (digest != null && Regex.IsMatch(digest, "^[0-9A-Fa-f]{40}$"))
                    {
                        a.Sha1 = digest.ToLowerInvariant();
                    }
                    object date;
                    if (product.TryGetValue("PostDate", out date) && date is DateTime)
                    {
                        a.PostDate = (DateTime)date;
                    }
                    Dictionary<string, object> dists = Plist.Dict(product, "Distributions");
                    if (dists != null)
                    {
                        a.DistUrl = Plist.Str(dists, "English") ?? Plist.Str(dists, "en")
                            ?? dists.Values.OfType<string>().FirstOrDefault();
                    }
                    list.Add(a);
                    break;
                }
            }
            return list;
        }

        /// <summary>Takes title, version and build out of a product's distribution file.</summary>
        public static void ApplyDist(AppleInstaller a, string dist)
        {
            Match v = Regex.Match(dist, @"<key>VERSION</key>\s*<string>([^<]+)</string>");
            Match b = Regex.Match(dist, @"<key>BUILD</key>\s*<string>([^<]+)</string>");
            Match t = Regex.Match(dist, "\"SU_TITLE\"\\s*=\\s*\"([^\"]+)\"");
            if (v.Success)
            {
                a.Version = v.Groups[1].Value.Trim();
            }
            if (b.Success)
            {
                a.Build = b.Groups[1].Value.Trim();
            }
            if (t.Success)
            {
                a.Title = t.Groups[1].Value.Trim();
            }
            if (string.IsNullOrEmpty(a.Title) || a.Title == "SU_TITLE")
            {
                a.Title = MarketingName(a.Version);
            }
        }

        private static async Task FillDetailsAsync(HttpClient http, List<AppleInstaller> list, CancellationToken ct)
        {
            var gate = new SemaphoreSlim(8);
            await Task.WhenAll(list.Select(async a =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!string.IsNullOrEmpty(a.DistUrl))
                    {
                        ApplyDist(a, await http.GetStringAsync(a.DistUrl.Replace("http://", "https://"), ct).ConfigureAwait(false));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Details are optional: the installer is still listed by its product id.
                }
                finally
                {
                    gate.Release();
                }
                if (string.IsNullOrEmpty(a.Title))
                {
                    a.Title = "macOS";
                }
            })).ConfigureAwait(false);
        }
    }

    /// <summary>macOS restore images (.ipsw) for virtual Macs on Apple Silicon, listed by ipsw.me; the files are on Apple's servers.</summary>
    public static class IpswCatalog
    {
        public const string Url = "https://api.ipsw.me/v4/device/VirtualMac2,1?type=ipsw";

        public static async Task<List<AppleInstaller>> FetchAsync(HttpClient http, CancellationToken ct)
        {
            return Parse(await http.GetStringAsync(Url, ct).ConfigureAwait(false));
        }

        public static List<AppleInstaller> Parse(string json)
        {
            var list = new List<AppleInstaller>();
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                System.Text.Json.JsonElement fw;
                if (!doc.RootElement.TryGetProperty("firmwares", out fw))
                {
                    return list;
                }
                foreach (System.Text.Json.JsonElement f in fw.EnumerateArray())
                {
                    string url = Text(f, "url");
                    if (url == null || !url.EndsWith(".ipsw", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var a = new AppleInstaller { Url = url, Version = Text(f, "version"), Build = Text(f, "buildid") };
                    a.Title = AppleCatalog.MarketingName(a.Version);
                    a.Sha1 = Text(f, "sha1sum");
                    a.Sha256 = Text(f, "sha256sum");
                    System.Text.Json.JsonElement v;
                    if (f.TryGetProperty("filesize", out v) && v.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        a.Size = v.GetInt64();
                    }
                    DateTime d;
                    if (DateTime.TryParse(Text(f, "releasedate"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out d))
                    {
                        a.PostDate = d;
                    }
                    list.Add(a);
                }
            }
            list.Sort((x, y) =>
            {
                int c = VersionCompare.Instance.Compare(y.Version ?? "", x.Version ?? "");
                return c != 0 ? c : string.CompareOrdinal(y.Build ?? "", x.Build ?? "");
            });
            return list;
        }

        private static string Text(System.Text.Json.JsonElement e, string name)
        {
            System.Text.Json.JsonElement v;
            return e.TryGetProperty(name, out v) && v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString().Length > 0 ? v.GetString() : null;
        }
    }
}
