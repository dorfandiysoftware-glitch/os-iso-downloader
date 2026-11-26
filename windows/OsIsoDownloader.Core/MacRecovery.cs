// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    public sealed class RecoveryPreset
    {
        public string Name { get; set; }
        public string BoardId { get; set; }
    }

    public sealed class RecoveryImage
    {
        public string ImageUrl { get; set; }
        public string ImageToken { get; set; }
        public string ImageHash { get; set; }
        public string ChunklistUrl { get; set; }
        public string ChunklistToken { get; set; }
    }

    /// <summary>
    /// Apple's Internet Recovery service (the same one every Mac uses to start recovery over the network).
    /// It gives out BaseSystem.dmg, a small bootable recovery system that then installs macOS from Apple.
    /// </summary>
    public static class MacRecovery
    {
        private const string Host = "http://osrecovery.apple.com";
        public const string MlbZero = "00000000000000000";

        /// <summary>Board ids as listed in the OpenCore (Dortania) install guide.</summary>
        public static readonly RecoveryPreset[] Presets =
        {
            new RecoveryPreset { Name = "macOS Big Sur (11)", BoardId = "Mac-42FD25EABCABB274" },
            new RecoveryPreset { Name = "macOS Monterey (12)", BoardId = "Mac-E43C1C25D4880AD6" },
            new RecoveryPreset { Name = "macOS Catalina (10.15)", BoardId = "Mac-00BE6ED71E35EB86" },
        };

        public static HttpClient CreateClient()
        {
            var h = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("InternetRecovery/1.0");
            return h;
        }

        public static string RandomHex(Random rng, int length)
        {
            const string hex = "0123456789ABCDEF";
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(hex[rng.Next(16)]);
            }
            return sb.ToString();
        }

        public static string BuildBody(string boardId, string mlb, string os, Random rng)
        {
            return "cid=" + RandomHex(rng, 16) + "\nsn=" + mlb + "\nbid=" + boardId + "\nk=" + RandomHex(rng, 64)
                + "\nfg=" + RandomHex(rng, 64) + "\nos=" + os;
        }

        public static RecoveryImage ParseInfo(string text)
        {
            var d = new Dictionary<string, string>();
            foreach (string raw in text.Split('\n'))
            {
                int i = raw.IndexOf(':');
                if (i > 0)
                {
                    d[raw.Substring(0, i).Trim()] = raw.Substring(i + 1).Trim();
                }
            }
            foreach (string k in new[] { "AU", "AT", "CU", "CT" })
            {
                if (!d.ContainsKey(k))
                {
                    throw new InvalidOperationException("Apple не повернув очікувану відповідь (немає поля " + k + ").");
                }
            }
            string hash;
            d.TryGetValue("AH", out hash);
            return new RecoveryImage
            {
                ImageUrl = d["AU"], ImageToken = d["AT"], ImageHash = hash, ChunklistUrl = d["CU"], ChunklistToken = d["CT"],
            };
        }

        public static async Task<RecoveryImage> GetImageAsync(HttpClient http, string boardId, string os, CancellationToken ct)
        {
            string session = null;
            using (var r = await http.GetAsync(Host + "/", ct).ConfigureAwait(false))
            {
                IEnumerable<string> cookies;
                if (r.Headers.TryGetValues("Set-Cookie", out cookies))
                {
                    foreach (string c in cookies)
                    {
                        if (c.StartsWith("session=", StringComparison.OrdinalIgnoreCase))
                        {
                            session = c.Split(';')[0];
                        }
                    }
                }
            }
            if (session == null)
            {
                throw new InvalidOperationException("Apple не видав сесію для відновлення.");
            }
            var req = new HttpRequestMessage(HttpMethod.Post, Host + "/InstallationPayload/RecoveryImage");
            req.Headers.TryAddWithoutValidation("Cookie", session);
            req.Content = new StringContent(BuildBody(boardId, MlbZero, os, new Random()), Encoding.ASCII, "text/plain");
            using (var resp = await http.SendAsync(req, ct).ConfigureAwait(false))
            {
                string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Apple відповів " + (int)resp.StatusCode + ": " + text.Trim());
                }
                return ParseInfo(text);
            }
        }

        /// <summary>The headers Apple wants when the file itself is downloaded.</summary>
        public static Dictionary<string, string> AssetHeaders(string token)
        {
            return new Dictionary<string, string> { { "Cookie", "AssetToken=" + token } };
        }

        /// <summary>
        /// Checks the image against Apple's chunklist (a SHA-256 for every chunk of the file).
        /// Returns null when everything matches, otherwise a message.
        /// </summary>
        public static async Task<string> VerifyChunklistAsync(string imagePath, string chunklistPath, IProgress<double> progress, CancellationToken ct)
        {
            byte[] cl = File.ReadAllBytes(chunklistPath);
            if (cl.Length < 36 || cl[0] != 'C' || cl[1] != 'N' || cl[2] != 'K' || cl[3] != 'L')
            {
                return "Файл chunklist має невідомий формат.";
            }
            long count = BinaryPrimitives.ReadInt64LittleEndian(cl.AsSpan(12, 8));
            long off = BinaryPrimitives.ReadInt64LittleEndian(cl.AsSpan(20, 8));
            if (count <= 0 || off + count * 36 > cl.Length)
            {
                return "Файл chunklist пошкоджений.";
            }
            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true))
            using (var sha = SHA256.Create())
            {
                byte[] buf = new byte[1 << 20];
                long pos = 0;
                for (long i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    int rec = (int)(off + i * 36);
                    uint len = BinaryPrimitives.ReadUInt32LittleEndian(cl.AsSpan(rec, 4));
                    if (buf.Length < len)
                    {
                        buf = new byte[len];
                    }
                    int got = 0;
                    while (got < len)
                    {
                        int n = await fs.ReadAsync(buf, got, (int)len - got, ct).ConfigureAwait(false);
                        if (n <= 0)
                        {
                            return "Файл коротший, ніж у chunklist (шматок " + i + ").";
                        }
                        got += n;
                    }
                    byte[] hash = sha.ComputeHash(buf, 0, (int)len);
                    if (!hash.AsSpan().SequenceEqual(cl.AsSpan(rec + 4, 32)))
                    {
                        return "Контрольна сума шматка " + i + " не збігається.";
                    }
                    pos += len;
                    if ((i & 63) == 0)
                    {
                        progress?.Report((double)pos / fs.Length);
                    }
                }
                if (pos != fs.Length)
                {
                    return "Розмір файлу (" + fs.Length + ") не збігається з chunklist (" + pos + ").";
                }
            }
            return null;
        }
    }

    public sealed class DmgChunk
    {
        public uint Type;
        public long Sector;
        public long Sectors;
        public long Offset;
        public long Length;
    }

    /// <summary>Reads Apple's UDIF disk images (.dmg) and writes the raw disk inside.</summary>
    public static class DmgConverter
    {
        public const uint TypeZero = 0, TypeRaw = 1, TypeIgnore = 2, TypeAdc = 0x80000004, TypeZlib = 0x80000005,
            TypeBzip2 = 0x80000006, TypeLzfse = 0x80000007, TypeComment = 0x7ffffffe, TypeEnd = 0xffffffff;

        public sealed class Layout
        {
            public long DataForkOffset;
            public long TotalSectors;
            public List<DmgChunk> Chunks = new List<DmgChunk>();
        }

        public static Layout ReadLayout(Stream s)
        {
            if (s.Length < 512)
            {
                throw new InvalidDataException("Файл замалий для .dmg.");
            }
            byte[] koly = new byte[512];
            s.Seek(-512, SeekOrigin.End);
            ReadFully(s, koly);
            if (koly[0] != 'k' || koly[1] != 'o' || koly[2] != 'l' || koly[3] != 'y')
            {
                throw new InvalidDataException("Це не образ UDIF (.dmg): немає підпису koly.");
            }
            var layout = new Layout { DataForkOffset = BinaryPrimitives.ReadInt64BigEndian(koly.AsSpan(24, 8)) };
            long xmlOff = BinaryPrimitives.ReadInt64BigEndian(koly.AsSpan(216, 8));
            long xmlLen = BinaryPrimitives.ReadInt64BigEndian(koly.AsSpan(224, 8));
            byte[] xml = new byte[xmlLen];
            s.Seek(xmlOff, SeekOrigin.Begin);
            ReadFully(s, xml);
            var root = Plist.Parse(Encoding.UTF8.GetString(xml)) as Dictionary<string, object>;
            Dictionary<string, object> rf = Plist.Dict(root, "resource-fork");
            object blkx;
            if (rf == null || !rf.TryGetValue("blkx", out blkx) || !(blkx is List<object>))
            {
                throw new InvalidDataException("У .dmg немає таблиці розділів (blkx).");
            }
            foreach (object o in (List<object>)blkx)
            {
                string b64 = Plist.Str(o, "Data");
                if (b64 == null)
                {
                    continue;
                }
                byte[] mish = Convert.FromBase64String(b64);
                if (mish.Length < 204 || mish[0] != 'm' || mish[1] != 'i' || mish[2] != 's' || mish[3] != 'h')
                {
                    continue;
                }
                long first = BinaryPrimitives.ReadInt64BigEndian(mish.AsSpan(8, 8));
                uint n = BinaryPrimitives.ReadUInt32BigEndian(mish.AsSpan(200, 4));
                for (int i = 0; i < n; i++)
                {
                    int p = 204 + i * 40;
                    if (p + 40 > mish.Length)
                    {
                        break;
                    }
                    var c = new DmgChunk
                    {
                        Type = BinaryPrimitives.ReadUInt32BigEndian(mish.AsSpan(p, 4)),
                        Sector = first + BinaryPrimitives.ReadInt64BigEndian(mish.AsSpan(p + 8, 8)),
                        Sectors = BinaryPrimitives.ReadInt64BigEndian(mish.AsSpan(p + 16, 8)),
                        Offset = BinaryPrimitives.ReadInt64BigEndian(mish.AsSpan(p + 24, 8)),
                        Length = BinaryPrimitives.ReadInt64BigEndian(mish.AsSpan(p + 32, 8)),
                    };
                    if (c.Type == TypeComment || c.Type == TypeEnd)
                    {
                        continue;
                    }
                    layout.Chunks.Add(c);
                    layout.TotalSectors = Math.Max(layout.TotalSectors, c.Sector + c.Sectors);
                }
            }
            return layout;
        }

        public static Dictionary<uint, int> TypeCounts(Layout l)
        {
            var d = new Dictionary<uint, int>();
            foreach (DmgChunk c in l.Chunks)
            {
                int n;
                d.TryGetValue(c.Type, out n);
                d[c.Type] = n + 1;
            }
            return d;
        }

        public static string TypeName(uint t)
        {
            switch (t)
            {
                case TypeZero: return "zero";
                case TypeRaw: return "raw";
                case TypeIgnore: return "ignore";
                case TypeAdc: return "adc";
                case TypeZlib: return "zlib";
                case TypeBzip2: return "bzip2";
                case TypeLzfse: return "lzfse";
                default: return "0x" + t.ToString("x");
            }
        }

        public static async Task ConvertAsync(string dmgPath, string rawPath, IProgress<double> progress, CancellationToken ct)
        {
            using (var src = new FileStream(dmgPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true))
            {
                Layout l = ReadLayout(src);
                foreach (uint t in TypeCounts(l).Keys)
                {
                    if (t != TypeZero && t != TypeRaw && t != TypeIgnore && t != TypeZlib)
                    {
                        throw new NotSupportedException("Цей .dmg стиснутий методом «" + TypeName(t) + "», який поки що не підтримується.");
                    }
                }
                long totalWork = Math.Max(1, l.Chunks.Sum(c => c.Length));
                long done = 0;
                using (var dst = new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                {
                    dst.SetLength(l.TotalSectors * 512);
                    foreach (DmgChunk c in l.Chunks.OrderBy(x => x.Sector))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (c.Type == TypeZero || c.Type == TypeIgnore || c.Length == 0)
                        {
                            continue;
                        }
                        byte[] comp = new byte[c.Length];
                        src.Seek(l.DataForkOffset + c.Offset, SeekOrigin.Begin);
                        await src.ReadExactlyAsync(comp, 0, comp.Length, ct).ConfigureAwait(false);
                        byte[] data;
                        if (c.Type == TypeRaw)
                        {
                            data = comp;
                        }
                        else
                        {
                            data = new byte[c.Sectors * 512];
                            using (var z = new ZLibStream(new MemoryStream(comp), CompressionMode.Decompress))
                            {
                                await z.ReadExactlyAsync(data, 0, data.Length, ct).ConfigureAwait(false);
                            }
                        }
                        dst.Seek(c.Sector * 512, SeekOrigin.Begin);
                        await dst.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);
                        done += c.Length;
                        progress?.Report((double)done / totalWork);
                    }
                }
            }
        }

        private static void ReadFully(Stream s, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf, got, buf.Length - got);
                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }
                got += n;
            }
        }
    }
}
