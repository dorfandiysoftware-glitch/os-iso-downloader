// SPDX-License-Identifier: GPL-3.0-or-later
// Runs the real Apple catalog code against Apple's servers. Used by CI.
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OsIsoDownloader.Core;

internal static class Program
{
    private static async Task<int> Main()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("os-iso-downloader/0.1");
        var list = await AppleCatalog.FetchAsync(http, CancellationToken.None);
        int withSha1 = 0;
        foreach (AppleInstaller a in list)
        {
            if (a.Sha1 != null) withSha1++;
            Console.WriteLine($"{a.DisplayName} | {a.Size / 1048576} MiB | {a.PostDate:yyyy-MM-dd} | sha1={(a.Sha1 ?? "-")} | {a.Url}");
        }
        Console.WriteLine($"TOTAL {list.Count}, with sha1: {withSha1}");
        // Check that the first link answers and has the size the catalog claims.
        var req = new HttpRequestMessage(HttpMethod.Head, list[0].Url);
        var resp = await http.SendAsync(req);
        Console.WriteLine($"HEAD {list[0].Url} -> {(int)resp.StatusCode}, length {resp.Content.Headers.ContentLength}, catalog size {list[0].Size}");
        return list.Count > 0 && resp.IsSuccessStatusCode ? 0 : 1;
    }
}
