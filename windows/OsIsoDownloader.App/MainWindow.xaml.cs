// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OsIsoDownloader.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OsIsoDownloader.App
{
    public sealed class Choice
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public Node Node;
        public FileEntry File;
    }

    public sealed partial class MainWindow : Window
    {
        private const string RemoteCatalog = "https://raw.githubusercontent.com/dorfandiysoftware-glitch/os-iso-downloader/main/catalog/catalog.json";

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        private readonly ObservableCollection<DownloadItem> _items = new ObservableCollection<DownloadItem>();
        private readonly Stack<Node> _path = new Stack<Node>();
        private Catalog _catalog;
        private string _folder;

        public MainWindow()
        {
            InitializeComponent();
            Title = "OS ISO Downloader";
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBar);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 700));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("os-iso-downloader/0.1");
            Downloads.ItemsSource = _items;
            _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ISO");
            FolderText.Text = _folder;
            _ = LoadCatalogAsync();
        }

        private async Task LoadCatalogAsync()
        {
            string json = null;
            string from = "";
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    json = await _http.GetStringAsync(RemoteCatalog, cts.Token);
                    from = "каталог з GitHub";
                }
            }
            catch (Exception)
            {
                json = null;
            }
            if (json == null)
            {
                try
                {
                    json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json"));
                    from = "вбудований каталог (немає зв'язку з GitHub)";
                }
                catch (Exception e)
                {
                    StepText.Text = "Не вдалося завантажити каталог: " + e.Message;
                    return;
                }
            }
            try
            {
                _catalog = Catalog.Parse(json);
            }
            catch (Exception e)
            {
                StepText.Text = "Каталог пошкоджений: " + e.Message;
                return;
            }
            CatalogText.Text = "Джерело: " + from + ", оновлено " + _catalog.Updated;
            ShowLevel();
        }

        // ---- wizard ----

        private void ShowLevel()
        {
            var list = new List<Choice>();
            BackButton.IsEnabled = _path.Count > 0;
            if (_path.Count == 0)
            {
                PathText.Text = "Оберіть систему";
                StepText.Text = "";
                foreach (Node n in _catalog.Systems)
                {
                    list.Add(new Choice { Title = n.Name, Subtitle = n.Note ?? "", Node = n });
                }
            }
            else
            {
                Node cur = _path.Peek();
                PathText.Text = PathTitle();
                StepText.Text = cur.Note ?? "";
                if (cur.HasChildren)
                {
                    foreach (Node n in cur.Children)
                    {
                        list.Add(new Choice { Title = n.Name, Subtitle = n.Note ?? "", Node = n });
                    }
                }
                else
                {
                    foreach (FileEntry f in cur.Files)
                    {
                        list.Add(new Choice { Title = f.Name, Subtitle = Describe(f), File = f });
                    }
                }
            }
            Choices.ItemsSource = list;
        }

        private string PathTitle()
        {
            var parts = _path.ToArray();
            Array.Reverse(parts);
            var names = new List<string>();
            foreach (Node n in parts)
            {
                names.Add(n.Name);
            }
            return string.Join(" › ", names);
        }

        private static string Describe(FileEntry f)
        {
            var bits = new List<string>();
            if (f.IsGuide) bits.Add("відкриє сторінку");
            if (f.IsArchived) bits.Add("СТАРИЙ образ");
            if (!string.IsNullOrEmpty(f.Arch)) bits.Add(f.Arch);
            if (f.Checksum != null) bits.Add("з перевіркою хешу");
            if (!f.Verified) bits.Add("посилання ще не підтверджене");
            if (!string.IsNullOrEmpty(f.Note)) bits.Add(f.Note);
            return string.Join(" · ", bits);
        }

        private void OnBack(object sender, RoutedEventArgs e)
        {
            if (_path.Count > 0)
            {
                _path.Pop();
                ShowLevel();
            }
        }

        private async void OnChoice(object sender, ItemClickEventArgs e)
        {
            var c = (Choice)e.ClickedItem;
            if (c.Node != null)
            {
                _path.Push(c.Node);
                ShowLevel();
                return;
            }
            FileEntry f = c.File;
            if (f.IsGuide)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(f.Url));
                return;
            }
            Start(f, _path.Peek().Name + " — " + f.Name);
        }

        // ---- downloads ----

        private async void OnPickFolder(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _folder = folder.Path;
                FolderText.Text = _folder;
            }
        }

        private void Start(FileEntry entry, string title)
        {
            var item = new DownloadItem { Entry = entry, Title = title, Folder = _folder };
            _items.Insert(0, item);
            _ = RunAsync(item);
        }

        private async Task RunAsync(DownloadItem item)
        {
            item.Cts = new CancellationTokenSource();
            item.ActionLabel = "Пауза";
            try
            {
                if (item.Resolved == null)
                {
                    item.Status = "Шукаю актуальний файл…";
                    item.Resolved = await Resolver.ResolveAsync(_http, item.Entry, item.Cts.Token);
                    item.Title = item.Title + "  (" + item.Resolved.FileName + ")";
                }
                var progress = new Progress<DownloadProgress>(p =>
                {
                    if (p.Total > 0)
                    {
                        item.Percent = 100.0 * p.Received / p.Total;
                    }
                    item.Status = DownloadItem.Size(p.Received)
                        + (p.Total > 0 ? " з " + DownloadItem.Size(p.Total) : "")
                        + "  ·  " + DownloadItem.Size((long)p.BytesPerSecond) + "/с";
                });
                DownloadResult r = await Downloader.RunAsync(_http, item.Resolved, item.Folder, progress, s =>
                {
                    item.State = s;
                    if (s == DownloadState.Verifying)
                    {
                        DispatcherQueue.TryEnqueue(() => item.Status = "Перевіряю контрольну суму…");
                    }
                }, item.Cts.Token);
                item.Percent = 100;
                item.Path = r.Path;
                item.State = DownloadState.Done;
                item.ActionLabel = "Показати";
                if (r.HashOk == true)
                {
                    item.Status = "Готово, хеш збігається ✓  " + r.Path;
                }
                else if (r.HashOk == false)
                {
                    item.Status = "УВАГА: хеш НЕ збігається! Очікувався " + item.Resolved.Hash + ", отримано " + r.Hash + ". Файл: " + r.Path;
                }
                else
                {
                    item.Status = "Готово, але хешу для перевірки немає. SHA-256: " + r.Hash + "  " + r.Path;
                    if (r.Hash == null)
                    {
                        item.Status = "Готово (без перевірки хешу). " + r.Path;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (item.State == DownloadState.Cancelled)
                {
                    item.Status = "Скасовано";
                }
                else
                {
                    item.State = DownloadState.Paused;
                    item.Status = "Пауза (частина збережена, можна продовжити)";
                    item.ActionLabel = "Продовжити";
                }
            }
            catch (Exception ex)
            {
                item.State = DownloadState.Failed;
                item.Status = "Помилка: " + ex.Message;
                item.ActionLabel = "Повторити";
            }
        }

        private void OnItemAction(object sender, RoutedEventArgs e)
        {
            var item = (DownloadItem)((Button)sender).Tag;
            switch (item.State)
            {
                case DownloadState.Downloading:
                case DownloadState.Waiting:
                    item.State = DownloadState.Paused;
                    item.Cts?.Cancel();
                    break;
                case DownloadState.Paused:
                case DownloadState.Failed:
                    item.State = DownloadState.Waiting;
                    _ = RunAsync(item);
                    break;
                case DownloadState.Done:
                    if (item.Path != null)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                    }
                    break;
            }
        }

        private void OnItemCancel(object sender, RoutedEventArgs e)
        {
            var item = (DownloadItem)((Button)sender).Tag;
            if (item.State != DownloadState.Done)
            {
                item.State = DownloadState.Cancelled;
                item.Cts?.Cancel();
            }
            _items.Remove(item);
        }
    }
}
