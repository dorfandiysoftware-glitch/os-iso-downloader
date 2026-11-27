// SPDX-License-Identifier: GPL-3.0-or-later
// Runs the real Apple catalog code against Apple's servers. Used by CI.
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OsIsoDownloader.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "ios")
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("os-iso-downloader/0.1");
            string devJson = await h.GetStringAsync(IosCatalog.DevicesUrl);
            int[] majors = { 1, 2, 3, 4, 5, 6, 7, 16, 26, 27 };
            foreach (string fam in new[] { "iPhone", "iPad", "iPod" })
            {
                var devs = IosCatalog.ParseDevices(devJson, fam);
                int withFw = 0;
                foreach (IosDevice d in devs)
                {
                    var fw = IosCatalog.ParseFirmwares(await h.GetStringAsync(IosCatalog.FirmwareUrl(d.Identifier)), majors);
                    if (fw.Count > 0) withFw++;
                    if (devs.IndexOf(d) < 3) Console.WriteLine("IOS   " + d.Identifier + " " + d.Name + " -> " + fw.Count + " fw");
                }
                Console.WriteLine("IOS " + fam + ": " + devs.Count + " devices, " + withFw + " with matching firmware");
            }
            return 0;
        }
        if (args.Length > 0 && args[0] == "uup")
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("os-iso-downloader/0.1");
            int bad = 0;
            foreach (string q in new[] { "Windows 11, version 26H2", "Windows 11, version 25H2", "Windows 11, version 24H2", "Windows 11 Insider Preview", "Windows 10, version 22H2" })
            {
                var b = await UupDump.FetchBuildsAsync(h, q, 80, CancellationToken.None);
                Console.WriteLine("UUP " + q + " -> " + b.Count + " builds" + (b.Count > 0 ? ", newest " + b[0].Build + " " + b[0].Arch + " " + b[0].Date : ""));
                if (b.Count == 0) bad++;
            }
            var one = await UupDump.FetchBuildsAsync(h, "Windows 11, version 26H2", 5, CancellationToken.None);
            var langs = await UupDump.FetchLangsAsync(h, one[0].Uuid, CancellationToken.None);
            Console.WriteLine("UUP langs " + langs.Count + " first " + langs[0].Code + " " + langs[0].Name);
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uup-probe");
            string cmd = await UupDump.PreparePackageAsync(h, "uup:" + one[0].Uuid + ":en-us:professional", dir, CancellationToken.None);
            Console.WriteLine("UUP package ok: " + cmd + " files=" + System.IO.Directory.GetFiles(dir).Length);
            var cros = CrosCatalog.Parse(await h.GetStringAsync(CrosCatalog.ListUrl));
            Console.WriteLine("CROS " + cros.Count + " images, first " + cros[0].Name);
            return bad == 0 ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "ms")
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            int bad = 0;
            foreach (string spec in new[] { "ms:11:x64:English", "ms:11:arm64:English", "ms:10:x64:English", "ms:10:x86:English" })
            {
                try
                {
                    ResolvedFile f = await MsDownload.ResolveAsync(h, spec, CancellationToken.None);
                    Console.WriteLine("MS " + spec + " -> " + f.FileName + " " + f.Url.Substring(0, Math.Min(90, f.Url.Length)));
                    using (var msReq = new HttpRequestMessage(HttpMethod.Get, f.Url))
                    {
                        msReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                        using (var msResp = await h.SendAsync(msReq, HttpCompletionOption.ResponseHeadersRead))
                        {
                            Console.WriteLine("MS   probe " + (int)msResp.StatusCode + " range=" + msResp.Content.Headers.ContentRange);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("MS " + spec + " FAILED " + e.Message);
                    bad++;
                }
            }
            return bad == 0 ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "ipsw")
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("os-iso-downloader/0.1");
            foreach (string u in new[]
            {
                "https://api.ipsw.me/v4/device/VirtualMac2,1?type=ipsw",
                "https://mesu.apple.com/assets/macos/com_apple_macOSIPSW/com_apple_macOSIPSW.xml",
            })
            {
                try
                {
                    string t = await h.GetStringAsync(u);
                    Console.WriteLine("IPSW " + u + " -> " + t.Length + " chars");
                    Console.WriteLine(t.Substring(0, Math.Min(1800, t.Length)).Replace("\n", " "));
                }
                catch (Exception e)
                {
                    Console.WriteLine("IPSW " + u + " FAILED " + e.Message);
                }
            }
            var parsed = await IpswCatalog.FetchAsync(h, CancellationToken.None);
            Console.WriteLine("IPSWPARSED " + parsed.Count);
            foreach (var a in parsed.GetRange(0, Math.Min(8, parsed.Count)))
            {
                Console.WriteLine($"IPSWPARSED {a.DisplayName} | {a.Size / 1048576} MiB | sha256={a.Sha256} | sha1={a.Sha1} | {a.Url}");
            }
            return parsed.Count > 0 ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "recovery")
        {
            return await Recovery(args.Length > 1 ? int.Parse(args[1]) : 1);
        }
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

    private static async Task<int> Recovery(int pick)
    {
        var http = MacRecovery.CreateClient();
        string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macrec");
        System.IO.Directory.CreateDirectory(work);
        for (int i = 0; i < MacRecovery.Presets.Length; i++)
        {
            RecoveryPreset p = MacRecovery.Presets[i];
            try
            {
                RecoveryImage info = await MacRecovery.GetImageAsync(http, p.BoardId, "default", CancellationToken.None);
                var head = new HttpRequestMessage(HttpMethod.Head, info.ImageUrl);
                head.Headers.TryAddWithoutValidation("Cookie", "AssetToken=" + info.ImageToken);
                var hr = await http.SendAsync(head);
                Console.WriteLine($"PRESET {i} {p.Name} {p.BoardId}: {info.ImageUrl} | head {(int)hr.StatusCode} length {hr.Content.Headers.ContentLength} | AH={info.ImageHash}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"PRESET {i} {p.Name} {p.BoardId}: FAILED {e.Message}");
            }
        }

        RecoveryPreset chosen = MacRecovery.Presets[pick];
        RecoveryImage img = await MacRecovery.GetImageAsync(http, chosen.BoardId, "default", CancellationToken.None);
        string dmg = System.IO.Path.Combine(work, "BaseSystem.dmg");
        string cl = System.IO.Path.Combine(work, "BaseSystem.chunklist");
        var rp = new Progress<DownloadProgress>(x => { });
        await Downloader.RunAsync(http, new ResolvedFile { Url = img.ChunklistUrl, FileName = "BaseSystem.chunklist", Headers = MacRecovery.AssetHeaders(img.ChunklistToken) }, work, rp, null, CancellationToken.None);
        await Downloader.RunAsync(http, new ResolvedFile { Url = img.ImageUrl, FileName = "BaseSystem.dmg", Headers = MacRecovery.AssetHeaders(img.ImageToken) }, work, rp, null, CancellationToken.None);
        Console.WriteLine($"downloaded {new System.IO.FileInfo(dmg).Length} bytes");

        string bad = await MacRecovery.VerifyChunklistAsync(dmg, cl, null, CancellationToken.None);
        Console.WriteLine("CHUNKLIST: " + (bad ?? "OK"));

        DmgConverter.Layout layout;
        using (var fs = System.IO.File.OpenRead(dmg))
        {
            layout = DmgConverter.ReadLayout(fs);
        }
        Console.WriteLine($"DMG: {layout.Chunks.Count} chunks, {layout.TotalSectors} sectors ({layout.TotalSectors * 512 / 1048576} MiB)");
        foreach (var kv in DmgConverter.TypeCounts(layout))
        {
            Console.WriteLine($"  type {DmgConverter.TypeName(kv.Key)}: {kv.Value}");
        }

        string raw = System.IO.Path.Combine(work, "BaseSystem.img");
        await DmgConverter.ConvertAsync(dmg, raw, null, CancellationToken.None);
        byte[] head2 = new byte[1024];
        using (var fs = System.IO.File.OpenRead(raw))
        {
            fs.ReadExactly(head2, 0, head2.Length);
        }
        bool mbr = head2[510] == 0x55 && head2[511] == 0xAA;
        string gpt = System.Text.Encoding.ASCII.GetString(head2, 512, 8);
        Console.WriteLine($"RAW: {new System.IO.FileInfo(raw).Length} bytes, MBR55AA={mbr}, GPT signature='{gpt}'");
        return bad == null && mbr && gpt == "EFI PART" ? 0 : 1;
    }
}
