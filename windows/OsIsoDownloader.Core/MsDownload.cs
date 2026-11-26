// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    public sealed class MsLanguage
    {
        public string Name { get; set; }
        public string SkuId { get; set; }
    }

    public sealed class MsLink
    {
        public string Name { get; set; }
        public string Url { get; set; }
        /// <summary>0 = 32-bit, 1 = 64-bit, 2 = ARM64.</summary>
        public int Type { get; set; }
        public string FileName { get; set; }
    }

    /// <summary>
    /// The same requests the official Microsoft download page makes: it registers a browser session,
    /// asks for the languages of an edition and then for the one-day links of a language.
    /// </summary>
    public static class MsDownload
    {
        private const string Org = "y6jn8c31";
        private const string Profile = "606624d44113";
        private const string Instance = "560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175";
        private const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0";
        private const string Api = "https://www.microsoft.com/software-download-connector/api/";

        public static int EditionFor(string product, bool arm)
        {
            switch (product)
            {
                case "11": return arm ? 3816 : 3813;
                case "10": return 2618;
                default: throw new ArgumentException("Unknown product " + product);
            }
        }

        public static string PageFor(string product)
        {
            return product == "10" ? "https://www.microsoft.com/software-download/windows10ISO" : "https://www.microsoft.com/software-download/windows11";
        }

        private static async Task<string> GetAsync(HttpClient http, string url, string referer, CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", Ua);
                if (referer != null)
                {
                    req.Headers.TryAddWithoutValidation("Referer", referer);
                }
                using (HttpResponseMessage r = await http.SendAsync(req, ct).ConfigureAwait(false))
                {
                    return await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
            }
        }

        public static async Task<string> NewSessionAsync(HttpClient http, CancellationToken ct)
        {
            string sid = Guid.NewGuid().ToString();
            await GetAsync(http, "https://vlscppe.microsoft.com/tags?org_id=" + Org + "&session_id=" + sid, null, ct).ConfigureAwait(false);
            string js = await GetAsync(http, "https://ov-df.microsoft.com/mdt.js?instanceId=" + Instance + "&PageId=si&session_id=" + sid, null, ct).ConfigureAwait(false);
            Match w = Regex.Match(js, @"[?&]w=([A-F0-9]+)");
            Match rt = Regex.Match(js, "rticks\\=\"\\+?(\\d+)");
            if (!w.Success || !rt.Success)
            {
                throw new InvalidOperationException("Microsoft змінив сторінку завантаження — не вдалося відкрити сеанс.");
            }
            long mdt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await GetAsync(http, "https://ov-df.microsoft.com/?session_id=" + sid + "&CustomerId=" + Instance + "&PageId=si&w=" + w.Groups[1].Value
                + "&mdt=" + mdt + "&rticks=" + rt.Groups[1].Value, null, ct).ConfigureAwait(false);
            return sid;
        }

        public static string ErrorOf(JsonElement root)
        {
            if (root.TryGetProperty("ValidationContainer", out JsonElement vc) && vc.TryGetProperty("Errors", out JsonElement errs)
                && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            {
                string type = null, msg = null;
                JsonElement e = errs[0];
                if (e.TryGetProperty("Type", out JsonElement t)) type = t.ToString();
                if (e.TryGetProperty("Value", out JsonElement v)) msg = v.GetString();
                if (type == "9")
                {
                    return "Microsoft заблокував запит з цієї мережі (код 715-123130). Спробуй без VPN або пізніше.";
                }
                return "Microsoft відповів помилкою: " + (msg ?? type ?? "невідомо");
            }
            return null;
        }

        public static List<MsLanguage> ParseLanguages(string json)
        {
            var list = new List<MsLanguage>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                string err = ErrorOf(doc.RootElement);
                if (err != null)
                {
                    throw new InvalidOperationException(err);
                }
                if (doc.RootElement.TryGetProperty("Skus", out JsonElement skus))
                {
                    foreach (JsonElement s in skus.EnumerateArray())
                    {
                        string name = s.TryGetProperty("LocalizedLanguage", out JsonElement ll) && ll.ValueKind == JsonValueKind.String ? ll.GetString() : null;
                        string eng = s.TryGetProperty("Language", out JsonElement l) ? l.GetString() : null;
                        string id = s.GetProperty("Id").GetString();
                        list.Add(new MsLanguage { Name = eng ?? name, SkuId = id });
                    }
                }
            }
            return list;
        }

        public static List<MsLink> ParseLinks(string json)
        {
            var list = new List<MsLink>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                string err = ErrorOf(doc.RootElement);
                if (err != null)
                {
                    throw new InvalidOperationException(err);
                }
                if (doc.RootElement.TryGetProperty("ProductDownloadOptions", out JsonElement opts))
                {
                    foreach (JsonElement o in opts.EnumerateArray())
                    {
                        string uri = o.GetProperty("Uri").GetString();
                        string path = new Uri(uri).AbsolutePath;
                        list.Add(new MsLink
                        {
                            Name = o.TryGetProperty("Name", out JsonElement n) ? n.GetString() : null,
                            Url = uri,
                            Type = o.TryGetProperty("DownloadType", out JsonElement dt) ? dt.GetInt32() : 1,
                            FileName = Uri.UnescapeDataString(path.Substring(path.LastIndexOf('/') + 1)),
                        });
                    }
                }
            }
            return list;
        }

        public static async Task<List<MsLanguage>> GetLanguagesAsync(HttpClient http, string product, bool arm, CancellationToken ct)
        {
            string sid = await NewSessionAsync(http, ct).ConfigureAwait(false);
            string url = Api + "getskuinformationbyproductedition?profile=" + Profile + "&productEditionId=" + EditionFor(product, arm)
                + "&SKU=undefined&friendlyFileName=undefined&Locale=en-US&sessionID=" + sid;
            return ParseLanguages(await GetAsync(http, url, null, ct).ConfigureAwait(false));
        }

        public static async Task<List<MsLink>> GetLinksAsync(HttpClient http, string product, string skuId, CancellationToken ct)
        {
            string sid = await NewSessionAsync(http, ct).ConfigureAwait(false);
            string url = Api + "GetProductDownloadLinksBySku?profile=" + Profile + "&productEditionId=undefined&SKU=" + Uri.EscapeDataString(skuId)
                + "&friendlyFileName=undefined&Locale=en-US&sessionID=" + sid;
            return ParseLinks(await GetAsync(http, url, PageFor(product), ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Resolves "ms:{product}:{arch}:{language}" (arch = x86, x64 or arm64) into a fresh one-day link.
        /// The language is looked up again in a new session, because Microsoft ties SKU ids to the session.
        /// </summary>
        public static async Task<ResolvedFile> ResolveAsync(HttpClient http, string spec, CancellationToken ct)
        {
            string[] p = spec.Substring(3).Split(new[] { ':' }, 3);
            if (p.Length != 3)
            {
                throw new FormatException("Bad Microsoft spec " + spec);
            }
            string product = p[0], arch = p[1], lang = p[2];
            bool arm = arch == "arm64";
            int type = arch == "x86" ? 0 : arm ? 2 : 1;
            List<MsLanguage> langs = await GetLanguagesAsync(http, product, arm, ct).ConfigureAwait(false);
            MsLanguage l = langs.Find(x => string.Equals(x.Name, lang, StringComparison.OrdinalIgnoreCase));
            if (l == null)
            {
                throw new InvalidOperationException("Мови «" + lang + "» немає у списку Microsoft.");
            }
            List<MsLink> links = await GetLinksAsync(http, product, l.SkuId, ct).ConfigureAwait(false);
            MsLink link = links.Find(x => x.Type == type);
            if (link == null)
            {
                throw new InvalidOperationException("Microsoft не дала посилання для цієї архітектури.");
            }
            return new ResolvedFile { Url = link.Url, FileName = link.FileName };
        }
    }
}
