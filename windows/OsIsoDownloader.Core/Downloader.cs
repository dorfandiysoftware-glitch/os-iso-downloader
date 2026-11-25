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
            IProgress<DownloadProgress> progress, Action<DownloadState> state, CancellationToken ct)
        {
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, SafeName(file.FileName));
            string part = target + ".part";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;

            state?.Invoke(DownloadState.Downloading);
            using (var req = new HttpRequestMessage(HttpMethod.Get, file.Url))
            {
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
                        return await RunAsync(http, file, folder, progress, state, ct).ConfigureAwait(false);
                    }
                    resp.EnsureSuccessStatusCode();
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

        public static async Task<string> HashFileAsync(string path, string algo, CancellationToken ct)
        {
            using (HashAlgorithm h = string.Equals(algo, "sha512", StringComparison.OrdinalIgnoreCase)
                ? (HashAlgorithm)SHA512.Create() : SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true))
            {
                byte[] bytes = await Task.Run(() => h.ComputeHash(fs), ct).ConfigureAwait(false);
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
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
