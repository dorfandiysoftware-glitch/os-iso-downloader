// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OsIsoDownloader.Core
{
    public enum DownloadState { Waiting, Downloading, Verifying, Done, Failed, Paused, Cancelled }

    public sealed class DownloadProgress
    {
        public long Received { get; set; }
        public long Total { get; set; }
        public double BytesPerSecond { get; set; }
    }

    public sealed class DownloadResult
    {
        public string Path { get; set; }
        /// <summary>null = nothing to compare with, true = matches, false = does not match.</summary>
        public bool? HashOk { get; set; }
        public string Hash { get; set; }
    }

    /// <summary>Downloads to "name.part", resumes with HTTP Range and checks the hash at the end.</summary>
    public static class Downloader
    {
        public static async Task<DownloadResult> RunAsync(HttpClient http, ResolvedFile file, string folder,
            IProgress<DownloadProgress> progress, Action<DownloadState> state, CancellationToken ct, int connections = 1)
        {
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, SafeName(file.FileName));
            string part = target + ".part";

            state?.Invoke(DownloadState.Downloading);
            bool segmented = false;
            if (connections > 1)
            {
                segmented = await SegmentedAsync(http, file, part, connections, progress, ct).ConfigureAwait(false);
            }
            if (!segmented)
            {
                await SingleAsync(http, file, part, progress, ct).ConfigureAwait(false);
            }

            long actual = new FileInfo(part).Length;
            if (file.Size > 0 && actual != file.Size)
            {
                throw new InvalidOperationException("Розмір файлу не збігається: очікувалось " + file.Size + " Б, отримано " + actual + " Б. Натисни «Повторити» — завантаження продовжиться.");
            }
            var result = new DownloadResult();
            if (!string.IsNullOrEmpty(file.Hash))
            {
                state?.Invoke(DownloadState.Verifying);
                result.Hash = await HashFileAsync(part, file.HashAlgo, ct).ConfigureAwait(false);
                result.HashOk = string.Equals(result.Hash, file.Hash, StringComparison.OrdinalIgnoreCase);
            }
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(part, target);
            result.Path = target;
            state?.Invoke(DownloadState.Done);
            return result;
        }

        /// <summary>Smaller files are not worth splitting.</summary>
        public static long SegmentedMinSize = 16L << 20;
        public static int ChunkSize = 8 << 20;
        public static int RetryDelayMs = 1000;

        /// <summary>How many parallel connections suit a server. archive.org limits every single connection, so it gets the most.</summary>
        public static int ConnectionsFor(string url)
        {
            string host;
            try
            {
                host = new Uri(url).Host.ToLowerInvariant();
            }
            catch (UriFormatException)
            {
                return 1;
            }
            if (host == "archive.org" || host.EndsWith(".archive.org"))
            {
                return 8;
            }
            if (host.EndsWith(".microsoft.com") || host.EndsWith(".apple.com") || host.EndsWith(".openindiana.org"))
            {
                return 4;
            }
            return 1;
        }

        private sealed class SegState
        {
            public long Total { get; set; }
            public int Chunk { get; set; }
            public bool[] Done { get; set; }
        }

        /// <summary>
        /// Downloads the file in chunks over several connections at once. Returns false (and touches nothing)
        /// when the server cannot do ranges, the size is unknown, or a single-stream part already exists.
        /// </summary>
        private static async Task<bool> SegmentedAsync(HttpClient http, ResolvedFile file, string part, int connections,
            IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            string statePath = part + ".seg";
            bool hasState = File.Exists(statePath);
            if (!hasState && File.Exists(part) && new FileInfo(part).Length > 0)
            {
                return false;
            }

            long total = 0;
            using (var probe = new HttpRequestMessage(HttpMethod.Get, file.Url))
            {
                AddHeaders(probe, file);
                probe.Headers.Range = new RangeHeaderValue(0, 0);
                using (HttpResponseMessage r = await http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (r.StatusCode != HttpStatusCode.PartialContent || r.Content.Headers.ContentRange == null || !r.Content.Headers.ContentRange.HasLength)
                    {
                        return false;
                    }
                    total = r.Content.Headers.ContentRange.Length.Value;
                    string media = r.Content.Headers.ContentType?.MediaType;
                    if (string.Equals(media, "text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            if (total < SegmentedMinSize)
            {
                return false;
            }

            int chunk = ChunkSize;
            int count = (int)((total + chunk - 1) / chunk);
            SegState st = null;
            if (hasState)
            {
                try
                {
                    st = System.Text.Json.JsonSerializer.Deserialize<SegState>(File.ReadAllText(statePath));
                }
                catch (Exception)
                {
                    st = null;
                }
                if (st == null || st.Total != total || st.Chunk != chunk || st.Done == null || st.Done.Length != count || !File.Exists(part))
                {
                    st = null;
                }
            }
            if (st == null)
            {
                st = new SegState { Total = total, Chunk = chunk, Done = new bool[count] };
            }

            long received = 0;
            for (int i = 0; i < count; i++)
            {
                if (st.Done[i])
                {
                    received += ChunkLength(i, chunk, total);
                }
            }
            object gate = new object();
            Action save = () =>
            {
                lock (gate)
                {
                    File.WriteAllText(statePath, System.Text.Json.JsonSerializer.Serialize(st));
                }
            };

            using (var fs = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 1, false))
            {
                if (fs.Length != total)
                {
                    fs.SetLength(total);
                }
                save();
                int next = -1;
                long counter = received;
                int doneSinceSave = 0;
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    Task reporter = Task.Run(async () =>
                    {
                        long last = Interlocked.Read(ref counter);
                        DateTime lastT = DateTime.UtcNow;
                        while (!stop.IsCancellationRequested)
                        {
                            try
                            {
                                await Task.Delay(500, stop.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            long now = Interlocked.Read(ref counter);
                            DateTime t = DateTime.UtcNow;
                            double speed = (now - last) / Math.Max(0.001, (t - lastT).TotalSeconds);
                            last = now;
                            lastT = t;
                            progress?.Report(new DownloadProgress { Received = now, Total = total, BytesPerSecond = speed });
                        }
                    });

                    var workers = new Task[Math.Min(connections, count)];
                    for (int w = 0; w < workers.Length; w++)
                    {
                        workers[w] = Task.Run(async () =>
                        {
                            while (true)
                            {
                                int idx = Interlocked.Increment(ref next);
                                if (idx >= count)
                                {
                                    return;
                                }
                                if (st.Done[idx])
                                {
                                    continue;
                                }
                                long from = (long)idx * chunk;
                                long len = ChunkLength(idx, chunk, total);
                                byte[] data = await GetChunkAsync(http, file, from, len, () => counter, d => Interlocked.Add(ref counter, d), stop.Token).ConfigureAwait(false);
                                RandomAccess.Write(fs.SafeFileHandle, data, from);
                                lock (gate)
                                {
                                    st.Done[idx] = true;
                                    doneSinceSave++;
                                }
                                if (doneSinceSave >= 4)
                                {
                                    doneSinceSave = 0;
                                    save();
                                }
                            }
                        });
                    }

                    try
                    {
                        await Task.WhenAll(workers).ConfigureAwait(false);
                    }
                    catch
                    {
                        stop.Cancel();
                        try { await Task.WhenAll(workers).ConfigureAwait(false); } catch { }
                        save();
                        throw;
                    }
                    finally
                    {
                        stop.Cancel();
                        try { await reporter.ConfigureAwait(false); } catch { }
                    }
                }
                progress?.Report(new DownloadProgress { Received = total, Total = total, BytesPerSecond = 0 });
            }
            File.Delete(statePath);
            return true;
        }

        private static long ChunkLength(int idx, int chunk, long total)
        {
            return Math.Min(chunk, total - (long)idx * chunk);
        }

        private static void AddHeaders(HttpRequestMessage req, ResolvedFile file)
        {
            if (file.Headers != null)
            {
                foreach (var h in file.Headers)
                {
                    req.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }
        }

        private static async Task<byte[]> GetChunkAsync(HttpClient http, ResolvedFile file, long from, long len,
            Func<long> unused, Action<long> count, CancellationToken ct)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(RetryDelayMs << Math.Min(attempt - 1, 4), ct).ConfigureAwait(false);
                }
                long counted = 0;
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Get, file.Url))
                    {
                        AddHeaders(req, file);
                        req.Headers.Range = new RangeHeaderValue(from, from + len - 1);
                        using (HttpResponseMessage r = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                        {
                            if (r.StatusCode != HttpStatusCode.PartialContent)
                            {
                                throw new HttpRequestException("Сервер відповів " + (int)r.StatusCode + " на запит частини файлу.");
                            }
                            byte[] data = new byte[len];
                            using (Stream s = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                            {
                                int got = 0;
                                while (got < len)
                                {
                                    int n = await s.ReadAsync(data, got, (int)len - got, ct).ConfigureAwait(false);
                                    if (n <= 0)
                                    {
                                        throw new EndOfStreamException("З'єднання перервалось посередині частини.");
                                    }
                                    got += n;
                                    counted += n;
                                    count(n);
                                }
                            }
                            return data;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    last = e;
                    count(-counted);
                }
            }
            throw new IOException("Не вдалося отримати частину файлу після кількох спроб: " + last?.Message, last);
        }

        private static async Task SingleAsync(HttpClient http, ResolvedFile file, string part, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            using (var req = new HttpRequestMessage(HttpMethod.Get, file.Url))
            {
                if (file.Headers != null)
                {
                    foreach (var h in file.Headers)
                    {
                        req.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }
                if (have > 0)
                {
                    req.Headers.Range = new RangeHeaderValue(have, null);
                }
                using (HttpResponseMessage resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // The part is already complete (or the file changed): start over.
                        File.Delete(part);
                        await SingleAsync(http, file, part, progress, ct).ConfigureAwait(false);
                        return;
                    }
                    resp.EnsureSuccessStatusCode();
                    string media = resp.Content.Headers.ContentType?.MediaType;
                    if (string.Equals(media, "text/html", StringComparison.OrdinalIgnoreCase)
                        && !file.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Сервер повернув веб-сторінку замість файлу. Посилання не підходить для прямого завантаження.");
                    }
                    bool resumed = resp.StatusCode == HttpStatusCode.PartialContent;
                    if (!resumed)
                    {
                        have = 0;
                    }
                    long total = (resp.Content.Headers.ContentLength ?? 0) + have;
                    if (total == 0)
                    {
                        total = file.Size;
                    }
                    using (Stream net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    using (var fs = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, true))
                    {
                        byte[] buf = new byte[1 << 17];
                        long received = have;
                        long windowBytes = 0;
                        DateTime windowStart = DateTime.UtcNow;
                        double speed = 0;
                        int n;
                        while ((n = await net.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await fs.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            received += n;
                            windowBytes += n;
                            double secs = (DateTime.UtcNow - windowStart).TotalSeconds;
                            if (secs >= 0.5)
                            {
                                speed = windowBytes / secs;
                                windowBytes = 0;
                                windowStart = DateTime.UtcNow;
                                progress?.Report(new DownloadProgress { Received = received, Total = total, BytesPerSecond = speed });
                            }
                        }
                        progress?.Report(new DownloadProgress { Received = received, Total = total, BytesPerSecond = speed });
                    }
                }
            }
        }

        public static async Task<string> HashFileAsync(string path, string algo, CancellationToken ct)
        {
            using (HashAlgorithm h = Create(algo))
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true))
            {
                byte[] bytes = await Task.Run(() => h.ComputeHash(fs), ct).ConfigureAwait(false);
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
        }

        private static HashAlgorithm Create(string algo)
        {
            switch ((algo ?? "sha256").ToLowerInvariant())
            {
                case "sha512":
                    return SHA512.Create();
                case "sha1":
                    return SHA1.Create();
                default:
                    return SHA256.Create();
            }
        }

        /// <summary>Unpacks a .zip into a folder next to it, returns the first .iso (or the first file).</summary>
        public static string ExtractZip(string zipPath, bool deleteZip)
        {
            string dir = Path.Combine(Path.GetDirectoryName(zipPath), Path.GetFileNameWithoutExtension(zipPath));
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dir, true);
            string pick = null;
            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    pick = f;
                    break;
                }
                if (pick == null)
                {
                    pick = f;
                }
            }
            if (deleteZip)
            {
                File.Delete(zipPath);
            }
            return pick ?? dir;
        }

        public static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(name) ? "download.iso" : name;
        }
    }
}
