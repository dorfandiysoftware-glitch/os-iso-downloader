// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    public sealed class IosDevice
    {
        public string Name { get; set; }
        public string Identifier { get; set; }
    }

    public sealed class IosFirmware
    {
        public string Identifier { get; set; }
        public string Version { get; set; }
        public string Build { get; set; }
        public string Url { get; set; }
        public string Sha1 { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public bool Signed { get; set; }
        public DateTime? Date { get; set; }
        public int Major => int.TryParse(Version.Split('.')[0], out int m) ? m : 0;

        /// <summary>The name Apple used for that release on this device family.</summary>
        public string GroupName(string family)
        {
            int m = Major;
            if (family == "AppleTV")
            {
                return (m >= 9 ? "tvOS " : "Apple TV ") + m;
            }
            if (family == "iPad" && m >= 13)
            {
                return "iPadOS " + m;
            }
            return (m <= 3 ? "iPhone OS " : "iOS ") + m;
        }
    }

    /// <summary>Firmware (.ipsw) lists of iPhone, iPad and iPod touch from api.ipsw.me, which mirrors Apple's own download links.</summary>
    public static class IosCatalog
    {
        public const string DevicesUrl = "https://api.ipsw.me/v4/devices";

        public static string FirmwareUrl(string identifier)
        {
            return "https://api.ipsw.me/v4/device/" + Uri.EscapeDataString(identifier) + "?type=ipsw";
        }

        /// <summary>family: "iPhone", "iPad" or "iPod".</summary>
        public static List<IosDevice> ParseDevices(string json, string family)
        {
            var list = new List<IosDevice>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                foreach (JsonElement d in doc.RootElement.EnumerateArray())
                {
                    string id = Str(d, "identifier");
                    if (id == null || !id.StartsWith(family, StringComparison.Ordinal) || id.Length <= family.Length || !char.IsDigit(id[family.Length]))
                    {
                        continue;
                    }
                    list.Add(new IosDevice { Identifier = id, Name = Str(d, "name") ?? id });
                }
            }
            return list;
        }

        public static List<IosFirmware> ParseFirmwares(string json, ICollection<int> majors)
        {
            var list = new List<IosFirmware>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("firmwares", out JsonElement fw))
                {
                    return list;
                }
                foreach (JsonElement f in fw.EnumerateArray())
                {
                    string url = Str(f, "url");
                    string ver = Str(f, "version");
                    if (url == null || ver == null || !url.EndsWith(".ipsw", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var x = new IosFirmware
                    {
                        Identifier = Str(f, "identifier"), Version = ver, Build = Str(f, "buildid"), Url = url,
                        Sha1 = Str(f, "sha1sum"), Sha256 = Str(f, "sha256sum"),
                        Signed = f.TryGetProperty("signed", out JsonElement s) && s.ValueKind == JsonValueKind.True,
                        Size = f.TryGetProperty("filesize", out JsonElement z) && z.ValueKind == JsonValueKind.Number ? z.GetInt64() : 0,
                    };
                    if (DateTime.TryParse(Str(f, "releasedate"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime d))
                    {
                        x.Date = d;
                    }
                    if (majors == null || majors.Contains(x.Major))
                    {
                        list.Add(x);
                    }
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
