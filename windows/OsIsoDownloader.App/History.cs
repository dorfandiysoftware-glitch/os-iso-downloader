// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OsIsoDownloader.Core;

namespace OsIsoDownloader.App
{
    public sealed class HistoryEntry
    {
        public string Title { get; set; }
        public string Path { get; set; }
        public VmHint Vm { get; set; }
    }

    /// <summary>Remembers finished downloads between runs.</summary>
    public static class History
    {
        private static string FilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsIsoDownloader");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "history.json");
        }

        public static List<HistoryEntry> Load()
        {
            try
            {
                string f = FilePath();
                if (File.Exists(f))
                {
                    var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(f));
                    if (list != null)
                    {
                        return list;
                    }
                }
            }
            catch (Exception)
            {
            }
            return new List<HistoryEntry>();
        }

        public static void Save(IEnumerable<DownloadItem> items)
        {
            try
            {
                var list = new List<HistoryEntry>();
                foreach (DownloadItem i in items)
                {
                    if (i.State == DownloadState.Done && i.Path != null)
                    {
                        list.Add(new HistoryEntry { Title = i.Title, Path = i.Path, Vm = i.Vm });
                    }
                }
                File.WriteAllText(FilePath(), JsonSerializer.Serialize(list));
            }
            catch (Exception)
            {
            }
        }
    }
}
