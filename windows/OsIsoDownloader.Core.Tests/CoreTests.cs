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
    }
}
