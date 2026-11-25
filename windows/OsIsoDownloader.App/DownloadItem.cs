// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using Microsoft.UI.Xaml;
using OsIsoDownloader.Core;

namespace OsIsoDownloader.App
{
    public sealed class DownloadItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public FileEntry Entry;
        public string Folder;
        public CancellationTokenSource Cts;
        public ResolvedFile Resolved;
        public bool Resumable = true;
        public VmHint Vm;

        private string _title;
        private string _status = "У черзі";
        private double _percent;
        private string _detail = "";
        private string _actionLabel = "Пауза";
        private string _path;

        public string Title { get => _title; set { _title = value; Raise("Title"); } }
        public string Status { get => _status; set { _status = value; Raise("Status"); } }
        public double Percent { get => _percent; set { _percent = value; Raise("Percent"); } }
        public string Detail { get => _detail; set { _detail = value; Raise("Detail"); } }
        public string ActionLabel { get => _actionLabel; set { _actionLabel = value; Raise("ActionLabel"); } }
        public string Path { get => _path; set { _path = value; Raise("Path"); Raise("VmVisibility"); } }
        public Visibility VmVisibility =>
            _path != null && _path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        public DownloadState State = DownloadState.Waiting;

        private void Raise(string n)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public static string Size(long bytes)
        {
            string[] u = { "Б", "КБ", "МБ", "ГБ" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return v.ToString(i == 0 ? "0" : "0.0") + " " + u[i];
        }
    }
}
