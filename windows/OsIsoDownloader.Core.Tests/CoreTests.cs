// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
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
    }
}
