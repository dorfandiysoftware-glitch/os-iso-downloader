// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using OsIsoDownloader.Core;
using Xunit;

namespace OsIsoDownloader.Core.Tests
{
    public class CoreTests
    {
        [Fact]
        public void VersionOrderIsNumeric()
        {
            var l = new List<string> { "a-13.9.iso", "a-13.10.iso", "a-2.1.iso" };
            l.Sort(VersionCompare.Instance);
            Assert.Equal("a-13.10.iso", l[2]);
            Assert.Equal("a-2.1.iso", l[0]);
        }

        [Fact]
        public void GnuChecksum()
        {
            string t = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  one.iso\n"
                     + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb *two.iso\n";
            Assert.Equal(new string('b', 64), ChecksumParser.Find(t, "two.iso"));
            Assert.Null(ChecksumParser.Find(t, "three.iso"));
        }

        [Fact]
        public void BsdChecksum()
        {
            string t = "SHA256 (x.iso) = " + new string('C', 64) + "\n";
            Assert.Equal(new string('c', 64), ChecksumParser.Find(t, "x.iso"));
        }

        [Fact]
        public void SingleHashFile()
        {
            Assert.Equal(new string('d', 64), ChecksumParser.Find(new string('D', 64) + "\n", "any.iso"));
        }

        [Fact]
        public void SingleEntryWithAliasName()
        {
            string t = "SHA256 (openSUSE-Tumbleweed-DVD-x86_64-Snapshot20261124-Media.iso) = " + new string('e', 64) + "\n";
            Assert.Equal(new string('e', 64), ChecksumParser.Find(t, "openSUSE-Tumbleweed-DVD-x86_64-Current.iso"));
            string two = new string('a', 64) + "  one.iso\n" + new string('b', 64) + "  two.iso\n";
            Assert.Null(ChecksumParser.Find(two, "three.iso"));
        }

        [Fact]
        public void PicksHashOfTheRightLength()
        {
            string t = new string('a', 32) + "  FD14.zip\n" + new string('b', 64) + "  FD14.zip\n";
            Assert.Equal(new string('b', 64), ChecksumParser.Find(t, "FD14.zip", "sha256"));
            Assert.Equal(new string('a', 32), ChecksumParser.Find(t, "FD14.zip", "md5"));
        }

        [Fact]
        public void ArchiveOrgPick()
        {
            string json = "{\"result\":[{\"name\":\"Win8.1_English_x32.iso\",\"size\":\"3\",\"sha1\":\"AA\"},"
                + "{\"name\":\"Win8.1_English_x64.iso\",\"size\":\"4000000000\",\"sha1\":\"BB\"},"
                + "{\"name\":\"thumb.jpg\",\"size\":\"1\"}]}";
            var rule = new Resolve { Type = "archive", Id = "win-8.1", Pattern = @"Win8\.1_English_x64\.iso" };
            ResolvedFile r = Resolver.PickFromArchive(json, rule);
            Assert.Equal("https://archive.org/download/win-8.1/Win8.1_English_x64.iso", r.Url);
            Assert.Equal("sha1", r.HashAlgo);
            Assert.Equal("bb", r.Hash);
            Assert.Equal(4000000000L, r.Size);
        }

        [Fact]
        public void ArchiveNameWithSpacesIsEscaped()
        {
            string json = "{\"result\":[{\"name\":\"MS-DOS 6.22.iso\",\"size\":\"2\"}]}";
            ResolvedFile r = Resolver.PickFromArchive(json, new Resolve { Type = "archive", Id = "x", Pattern = @".*\.iso" });
            Assert.EndsWith("/MS-DOS%206.22.iso", r.Url);
            Assert.Equal("MS-DOS 6.22.iso", r.FileName);
        }

        [Fact]
        public void ZipExtractReturnsIso()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oid-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string zip = System.IO.Path.Combine(dir, "pack.zip");
            using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                z.CreateEntry("readme.txt");
                z.CreateEntry("FD14.iso");
            }
            string iso = Downloader.ExtractZip(zip, true);
            Assert.EndsWith("FD14.iso", iso);
            Assert.False(System.IO.File.Exists(zip));
            System.IO.Directory.Delete(dir, true);
        }

        [Fact]
        public void PicksNewestFromListing()
        {
            string html = "<a href=\"debian-13.9.0-amd64-netinst.iso\">x</a>"
                        + "<a href=\"debian-13.10.0-amd64-netinst.iso\">x</a>"
                        + "<a href=\"debian-13.10.0-arm64-netinst.iso\">x</a>"
                        + "<a href=\"SHA256SUMS\">x</a>";
            var rule = new Resolve { Base = "https://e.org/iso/", Pattern = @"debian-[\d.]+-amd64-netinst\.iso" };
            Assert.Equal("https://e.org/iso/debian-13.10.0-amd64-netinst.iso", Resolver.PickNewest(html, rule));
        }

        [Fact]
        public void ExcludeWorks()
        {
            string html = "<a href=\"f-15.1-amd64-disc1.iso\">x</a><a href=\"f-15.1-amd64-bootonly.iso\">x</a>";
            var rule = new Resolve { Base = "https://e.org/", Pattern = @"f-.*\.iso", Exclude = "bootonly" };
            Assert.EndsWith("disc1.iso", Resolver.PickNewest(html, rule));
        }

        [Fact]
        public void NoMatchThrows()
        {
            var rule = new Resolve { Base = "https://e.org/", Pattern = "zzz" };
            Assert.Throws<InvalidOperationException>(() => Resolver.PickNewest("<a href=\"a\">", rule));
        }

        [Fact]
        public void CatalogParses()
        {
            string json = "{\"schema\":1,\"systems\":[{\"id\":\"x\",\"name\":\"X\",\"files\":[{\"name\":\"f\",\"url\":\"https://a/b.iso\"}]}]}";
            Catalog c = Catalog.Parse(json);
            Assert.Single(c.Systems);
            Assert.Equal("iso", c.Systems[0].Files[0].Kind);
        }

        [Fact]
        public void WrongSchemaRejected()
        {
            Assert.Throws<FormatException>(() => Catalog.Parse("{\"schema\":2,\"systems\":[]}"));
        }

        [Fact]
        public void FileNameFromUrl()
        {
            Assert.Equal("a b.iso", Resolver.FileNameOf("https://x/y/a%20b.iso?mirror=1"));
        }

        private const string FakeCatalog = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0""><dict><key>Products</key><dict>
<key>012-34567</key><dict><key>PostDate</key><date>2025-09-15T17:00:00Z</date>
<key>Packages</key><array>
<dict><key>URL</key><string>http://swcdn.apple.com/content/downloads/1/2/012-34567/abc/Other.pkg</string><key>Size</key><integer>10</integer></dict>
<dict><key>URL</key><string>http://swcdn.apple.com/content/downloads/1/2/012-34567/abc/InstallAssistant.pkg</string><key>Size</key><integer>15000000000</integer><key>Digest</key><string>0123456789abcdef0123456789abcdef01234567</string></dict>
</array><key>Distributions</key><dict><key>English</key><string>http://swcdn.apple.com/x.English.dist</string></dict></dict>
<key>099-00000</key><dict><key>Packages</key><array><dict><key>URL</key><string>http://x/Update.pkg</string><key>Size</key><integer>5</integer></dict></array></dict>
</dict></dict></plist>";

        [Fact]
        public void AppleCatalogFindsInstallers()
        {
            var list = AppleCatalog.Extract(FakeCatalog);
            Assert.Single(list);
            AppleInstaller a = list[0];
            Assert.Equal("012-34567", a.ProductId);
            Assert.StartsWith("https://", a.Url);
            Assert.EndsWith("InstallAssistant.pkg", a.Url);
            Assert.Equal(15000000000L, a.Size);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", a.Sha1);
            Assert.Equal(2025, a.PostDate.Value.Year);
        }

        [Fact]
        public void AppleDistGivesNameVersionBuild()
        {
            var a = new AppleInstaller();
            AppleCatalog.ApplyDist(a, "<auxinfo><dict><key>BUILD</key><string>25A354</string><key>VERSION</key><string>26.0</string></dict></auxinfo>"
                + "\"SU_TITLE\" = \"macOS Tahoe\";");
            Assert.Equal("macOS Tahoe 26.0 (25A354)", a.DisplayName);
            var b = new AppleInstaller();
            AppleCatalog.ApplyDist(b, "<key>VERSION</key><string>14.6.1</string><key>BUILD</key><string>23G93</string>");
            Assert.Equal("macOS Sonoma 14.6.1 (23G93)", b.DisplayName);
        }

        [Fact]
        public void RecoveryInfoParses()
        {
            string t = "AP: 001\nAU: https://oscdn.apple.com/x/BaseSystem.dmg\nAH: abc\nAT: tok1\nCU: https://oscdn.apple.com/x/BaseSystem.chunklist\nCH: def\nCT: tok2\n";
            RecoveryImage i = MacRecovery.ParseInfo(t);
            Assert.EndsWith("BaseSystem.dmg", i.ImageUrl);
            Assert.Equal("tok2", i.ChunklistToken);
            Assert.Throws<InvalidOperationException>(() => MacRecovery.ParseInfo("AP: 1\n"));
        }

        [Fact]
        public void RecoveryBodyHasExpectedFields()
        {
            string b = MacRecovery.BuildBody("Mac-X", MacRecovery.MlbZero, "default", new Random(1));
            string[] lines = b.Split('\n');
            Assert.StartsWith("cid=", lines[0]);
            Assert.Equal(4 + 16, lines[0].Length);
            Assert.Equal("sn=00000000000000000", lines[1]);
            Assert.Equal("bid=Mac-X", lines[2]);
            Assert.Equal(2 + 64, lines[3].Length);
            Assert.Equal("os=default", lines[5]);
        }

        private static void Be32(byte[] b, int o, uint v) { System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), v); }
        private static void Be64(byte[] b, int o, long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(o, 8), v); }

        [Fact]
        public void DmgRoundTrip()
        {
            // raw chunk (1 sector), zlib chunk (2 sectors), zero chunk (1 sector), raw chunk (1 sector)
            byte[] a = new byte[512];
            for (int i = 0; i < a.Length; i++) a[i] = (byte)(i * 7);
            byte[] bplain = new byte[1024];
            for (int i = 0; i < bplain.Length; i++) bplain[i] = (byte)(i % 13);
            byte[] bcomp;
            using (var ms = new System.IO.MemoryStream())
            {
                using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    z.Write(bplain, 0, bplain.Length);
                }
                bcomp = ms.ToArray();
            }
            byte[] d = new byte[512];
            for (int i = 0; i < d.Length; i++) d[i] = 0xEE;

            var fork = new System.IO.MemoryStream();
            long offA = fork.Position; fork.Write(a, 0, a.Length);
            long offB = fork.Position; fork.Write(bcomp, 0, bcomp.Length);
            long offD = fork.Position; fork.Write(d, 0, d.Length);

            var chunks = new[]
            {
                new[] { 1L, 0, 1, offA, 512 },
                new[] { 0x80000005L, 1, 2, offB, bcomp.Length },
                new[] { 0L, 3, 1, 0, 0 },
                new[] { 1L, 4, 1, offD, 512 },
                new[] { 0xffffffffL, 5, 0, 0, 0 },
            };
            byte[] mish = new byte[204 + chunks.Length * 40];
            mish[0] = (byte)'m'; mish[1] = (byte)'i'; mish[2] = (byte)'s'; mish[3] = (byte)'h';
            Be32(mish, 4, 1);
            Be64(mish, 8, 0);
            Be64(mish, 16, 5);
            Be32(mish, 200, (uint)chunks.Length);
            for (int i = 0; i < chunks.Length; i++)
            {
                int p = 204 + i * 40;
                Be32(mish, p, (uint)chunks[i][0]);
                Be64(mish, p + 8, chunks[i][1]);
                Be64(mish, p + 16, chunks[i][2]);
                Be64(mish, p + 24, chunks[i][3]);
                Be64(mish, p + 32, chunks[i][4]);
            }
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict><key>resource-fork</key><dict><key>blkx</key><array><dict><key>Data</key><data>"
                + Convert.ToBase64String(mish) + "</data><key>ID</key><string>0</string></dict></array></dict></dict></plist>";
            byte[] xmlBytes = System.Text.Encoding.UTF8.GetBytes(xml);
            long xmlOff = fork.Length;
            fork.Write(xmlBytes, 0, xmlBytes.Length);
            byte[] koly = new byte[512];
            koly[0] = (byte)'k'; koly[1] = (byte)'o'; koly[2] = (byte)'l'; koly[3] = (byte)'y';
            Be64(koly, 24, 0);
            Be64(koly, 32, xmlOff);
            Be64(koly, 216, xmlOff);
            Be64(koly, 224, xmlBytes.Length);
            fork.Write(koly, 0, koly.Length);

            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oid-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string dmg = System.IO.Path.Combine(dir, "t.dmg");
            string raw = System.IO.Path.Combine(dir, "t.img");
            System.IO.File.WriteAllBytes(dmg, fork.ToArray());
            DmgConverter.ConvertAsync(dmg, raw, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            byte[] outb = System.IO.File.ReadAllBytes(raw);
            Assert.Equal(5 * 512, outb.Length);
            Assert.Equal(a, outb.Take(512).ToArray());
            Assert.Equal(bplain, outb.Skip(512).Take(1024).ToArray());
            Assert.All(outb.Skip(1536).Take(512), x => Assert.Equal(0, x));
            Assert.Equal(d, outb.Skip(2048).Take(512).ToArray());
            System.IO.Directory.Delete(dir, true);
        }

        [Fact]
        public void GitHubAssetPick()
        {
            string json = "{\"assets\":[{\"name\":\"OpenCore-1.0.5-DEBUG.zip\",\"browser_download_url\":\"https://x/d.zip\",\"size\":5},"
                + "{\"name\":\"OpenCore-1.0.5-RELEASE.zip\",\"browser_download_url\":\"https://x/r.zip\",\"size\":7,\"digest\":\"sha256:AB12\"}]}";
            ResolvedFile r = Resolver.PickFromGitHub(json, new Resolve { Type = "github", Id = "a/b", Pattern = @"OpenCore-[\d.]+-RELEASE\.zip" });
            Assert.Equal("https://x/r.zip", r.Url);
            Assert.Equal("ab12", r.Hash);
            Assert.Equal(7, r.Size);
        }

        [Fact]
        public void IpswListParses()
        {
            string json = "{\"firmwares\":[{\"version\":\"15.1\",\"buildid\":\"24B83\",\"url\":\"https://updates.cdn-apple.com/a/UniversalMac_15.1_24B83_Restore.ipsw\",\"filesize\":17000000000,\"sha256sum\":\"" + new string('a', 64) + "\",\"releasedate\":\"2024-10-28T00:00:00Z\"},"
                + "{\"version\":\"14.6\",\"buildid\":\"23G80\",\"url\":\"https://x/y.ipsw\",\"filesize\":1}]}";
            var l = IpswCatalog.Parse(json);
            Assert.Equal(2, l.Count);
            Assert.Equal("macOS Sequoia 15.1 (24B83)", l[0].DisplayName);
            Assert.Equal(new string('a', 64), l[0].Sha256);
            Assert.Equal(17000000000L, l[0].Size);
        }

        [Fact]
        public void MicrosoftLanguagesAndLinks()
        {
            var l = MsDownload.ParseLanguages("{\"Skus\":[{\"Id\":\"3321\",\"Language\":\"English\",\"LocalizedLanguage\":\"English\"},{\"Id\":\"3322\",\"Language\":\"Ukrainian\",\"LocalizedLanguage\":\"Українська\"}]}");
            Assert.Equal(2, l.Count);
            Assert.Equal("3322", l[1].SkuId);
            var k = MsDownload.ParseLinks("{\"ProductDownloadOptions\":[{\"Name\":\"x\",\"Uri\":\"https://software.download.prss.microsoft.com/db/Win11_English_x64.iso?t=1&P1=2\",\"DownloadType\":1}]}");
            Assert.Equal("Win11_English_x64.iso", k[0].FileName);
            Assert.Equal(1, k[0].Type);
        }

        [Fact]
        public void MicrosoftBlockedMessage()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => MsDownload.ParseLinks("{\"ValidationContainer\":{\"Errors\":[{\"Type\":9,\"Value\":\"x\"}]}}"));
            Assert.Contains("715-123130", ex.Message);
        }

        [Fact]
        public void UupBuildsAndLanguages()
        {
            string j = "{\"response\":{\"builds\":{\"7\":{\"title\":\"Windows 11 Insider Preview 29712.1000\",\"build\":\"29712.1000\",\"arch\":\"amd64\",\"created\":200,\"uuid\":\"u-new\"},\"3\":{\"title\":\"old\",\"build\":\"1\",\"arch\":\"arm64\",\"created\":100,\"uuid\":\"u-old\"},\"4\":{\"title\":\"x\",\"build\":\"2\",\"arch\":\"sparc\",\"created\":300,\"uuid\":\"u-x\"}}}}";
            var b = UupDump.ParseBuilds(j, 10);
            Assert.Equal(2, b.Count);
            Assert.Equal("u-new", b[0].Uuid);
            Assert.Single(UupDump.ParseBuilds(j, 1));
            var l = UupDump.ParseLangs("{\"response\":{\"langList\":[\"de-de\",\"neutral\",\"uk-ua\",\"en-us\",\"models\"],\"langFancyNames\":{\"de-de\":\"German\",\"uk-ua\":\"Ukrainian\",\"en-us\":\"English (United States)\"}}}");
            Assert.Equal(new[] { "en-us", "uk-ua", "de-de" }, l.Select(x => x.Code).ToArray());
            Assert.Equal("https://uupdump.net/get.php?id=abc&pack=en-us&edition=professional&autodl=2", UupDump.PackageUrl("uup:abc:en-us:professional"));
        }

        [Fact]
        public void ChromeOsRecoveryList()
        {
            var l = CrosCatalog.Parse("[{\"channel\":\"STABLE\",\"name\":\"Dell Chromebook 13\",\"manufacturer\":\"Dell\",\"url\":\"https://dl.google.com/x.bin.zip\",\"sha1\":\"aa\",\"filesize\":5},{\"channel\":\"LTS\",\"url\":\"https://dl.google.com/y.zip\"}]");
            Assert.Single(l);
            Assert.Equal("Dell", l[0].Manufacturer);
        }
    }
}
