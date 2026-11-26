// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OsIsoDownloader.Core;
using Xunit;

namespace OsIsoDownloader.Core.Tests
{
    public class DownloaderTests
    {
        private sealed class FakeServer : HttpMessageHandler
        {
            public byte[] Data;
            public bool Ranges = true;
            public int FailFirst;          // number of range requests (not the probe) that fail with 503
            public int Concurrent, MaxConcurrent, Requests;
            private int failed;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                Interlocked.Increment(ref Requests);
                var range = req.Headers.Range?.Ranges;
                if (!Ranges || range == null || range.Count == 0)
                {
                    var full = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Data) };
                    full.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    return full;
                }
                long from = 0, to = 0;
                foreach (var r in range) { from = r.From ?? 0; to = r.To ?? Data.Length - 1; }
                bool probe = from == 0 && to == 0;
                if (!probe && Interlocked.Increment(ref failed) <= FailFirst)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                int now = Interlocked.Increment(ref Concurrent);
                int max;
                while ((max = MaxConcurrent) < now && Interlocked.CompareExchange(ref MaxConcurrent, now, max) != max) { }
                await Task.Delay(30, ct);
                Interlocked.Decrement(ref Concurrent);
                to = Math.Min(to, Data.Length - 1);
                var part = new byte[to - from + 1];
                Array.Copy(Data, from, part, 0, part.Length);
                var resp = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part) };
                resp.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, Data.Length);
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return resp;
            }
        }

        private static byte[] Blob(int n)
        {
            var b = new byte[n];
            new Random(7).NextBytes(b);
            return b;
        }

        private static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "oid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        private static ResolvedFile File_(byte[] data)
        {
            return new ResolvedFile
            {
                Url = "http://test/x.iso",
                FileName = "x.iso",
                HashAlgo = "sha256",
                Hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                Size = data.Length
            };
        }

        [Fact]
        public async Task SegmentedUsesSeveralConnectionsAndVerifies()
        {
            Downloader.SegmentedMinSize = 1000; Downloader.ChunkSize = 4096; Downloader.RetryDelayMs = 1;
            var srv = new FakeServer { Data = Blob(100_000) };
            string dir = TempDir();
            var r = await Downloader.RunAsync(new HttpClient(srv), File_(srv.Data), dir, null, null, CancellationToken.None, 6);
            Assert.True(r.HashOk);
            Assert.Equal(srv.Data, File.ReadAllBytes(r.Path));
            Assert.True(srv.MaxConcurrent > 1, "max concurrent = " + srv.MaxConcurrent);
            Assert.False(File.Exists(r.Path + ".part.seg"));
        }

        [Fact]
        public async Task FallsBackWithoutRangeSupport()
        {
            Downloader.SegmentedMinSize = 1000; Downloader.ChunkSize = 4096; Downloader.RetryDelayMs = 1;
            var srv = new FakeServer { Data = Blob(50_000), Ranges = false };
            var r = await Downloader.RunAsync(new HttpClient(srv), File_(srv.Data), TempDir(), null, null, CancellationToken.None, 6);
            Assert.True(r.HashOk);
            Assert.Equal(1, srv.MaxConcurrent == 0 ? 1 : srv.MaxConcurrent);
        }

        [Fact]
        public async Task RetriesFailedChunks()
        {
            Downloader.SegmentedMinSize = 1000; Downloader.ChunkSize = 4096; Downloader.RetryDelayMs = 1;
            var srv = new FakeServer { Data = Blob(60_000), FailFirst = 5 };
            var r = await Downloader.RunAsync(new HttpClient(srv), File_(srv.Data), TempDir(), null, null, CancellationToken.None, 4);
            Assert.True(r.HashOk);
        }

        [Fact]
        public async Task ResumesFromSegmentState()
        {
            Downloader.SegmentedMinSize = 1000; Downloader.ChunkSize = 4096; Downloader.RetryDelayMs = 1;
            var data = Blob(40_960); // exactly 10 chunks
            string dir = TempDir();
            // first attempt: every chunk fails permanently -> exception, state stays
            var bad = new FakeServer { Data = data, FailFirst = int.MaxValue };
            await Assert.ThrowsAnyAsync<Exception>(() => Downloader.RunAsync(new HttpClient(bad), File_(data), dir, null, null, CancellationToken.None, 2));
            Assert.True(File.Exists(Path.Combine(dir, "x.iso.part.seg")));
            var good = new FakeServer { Data = data };
            var r = await Downloader.RunAsync(new HttpClient(good), File_(data), dir, null, null, CancellationToken.None, 2);
            Assert.True(r.HashOk);
        }

        [Fact]
        public void ConnectionsPerHost()
        {
            Assert.Equal(8, Downloader.ConnectionsFor("https://ia800000.us.archive.org/x/y.iso"));
            Assert.Equal(1, Downloader.ConnectionsFor("https://example.org/a.iso"));
        }
    }
}
