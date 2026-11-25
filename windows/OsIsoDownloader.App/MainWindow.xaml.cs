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
using Windows.UI;
using OsIsoDownloader.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OsIsoDownloader.App
{
    public sealed class Choice
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Initials { get; set; }
        public string Star { get; set; } = "";
        public Brush Brush { get; set; }
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
        private List<Choice> _level = new List<Choice>();
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
            foreach (HistoryEntry h in History.Load())
            {
                if (File.Exists(h.Path))
                {
                    _items.Add(new DownloadItem
                    {
                        Title = h.Title, Path = h.Path, Vm = h.Vm, State = DownloadState.Done, Percent = 100,
                        Status = "Готово: " + h.Path, ActionLabel = "Показати",
                    });
                }
            }
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
                    list.Add(Tile(n.Name, n.Note ?? "", n.Id, false, n, null));
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
                        list.Add(Tile(n.Name, n.Note ?? "", n.Id, false, n, null));
                    }
                }
                else
                {
                    foreach (FileEntry f in cur.Files)
                    {
                        list.Add(Tile(f.Name, Describe(f), f.Name, f.Recommended, null, f));
                    }
                }
            }
            _level = list;
            SearchBox.Text = "";
            Choices.ItemsSource = list;
        }

        private static readonly Color[] Palette =
        {
            Color.FromArgb(255, 0, 120, 212), Color.FromArgb(255, 16, 124, 16), Color.FromArgb(255, 202, 80, 16),
            Color.FromArgb(255, 136, 23, 152), Color.FromArgb(255, 0, 153, 188), Color.FromArgb(255, 194, 57, 179),
            Color.FromArgb(255, 74, 84, 89), Color.FromArgb(255, 186, 20, 40),
        };

        private static Choice Tile(string title, string subtitle, string key, bool star, Node node, FileEntry file)
        {
            int h = 0;
            foreach (char c in key ?? title)
            {
                h = h * 31 + c;
            }
            string initials = "";
            foreach (string w in title.Split(new[] { ' ', '/', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (char.IsLetterOrDigit(w[0]) && initials.Length < 2)
                {
                    initials += char.ToUpperInvariant(w[0]);
                }
            }
            return new Choice
            {
                Title = title, Subtitle = subtitle, Node = node, File = file,
                Initials = initials.Length == 0 ? "?" : initials, Star = star ? "★" : "",
                Brush = new SolidColorBrush(Palette[(h & 0x7fffffff) % Palette.Length]),
            };
        }

        private void OnSearch(object sender, TextChangedEventArgs e)
        {
            string q = SearchBox.Text.Trim();
            if (q.Length == 0)
            {
                Choices.ItemsSource = _level;
                return;
            }
            var filtered = new List<Choice>();
            foreach (Choice c in _level)
            {
                if (c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (c.Subtitle ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(c);
                }
            }
            Choices.ItemsSource = filtered;
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
            var item = new DownloadItem { Entry = entry, Title = title, Folder = _folder, Vm = FindVm() };
            _items.Insert(0, item);
            _ = RunAsync(item);
        }

        private VmHint FindVm()
        {
            foreach (Node n in _path)
            {
                if (n.Vm != null)
                {
                    return n.Vm;
                }
            }
            return new VmHint();
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
                History.Save(_items);
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
            History.Save(_items);
        }

        // ---- virtual machines ----

        private async Task Info(string title, string text, string primary = null, string secondary = null, Action onPrimary = null, Action onSecondary = null)
        {
            var d = new ContentDialog
            {
                Title = title, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                CloseButtonText = "Закрити", XamlRoot = Root.XamlRoot,
            };
            if (primary != null)
            {
                d.PrimaryButtonText = primary;
            }
            if (secondary != null)
            {
                d.SecondaryButtonText = secondary;
            }
            ContentDialogResult r = await d.ShowAsync();
            if (r == ContentDialogResult.Primary)
            {
                onPrimary?.Invoke();
            }
            else if (r == ContentDialogResult.Secondary)
            {
                onSecondary?.Invoke();
            }
        }

        private static void Open(string url)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }

        private async void OnItemVm(object sender, RoutedEventArgs e)
        {
            var item = (DownloadItem)((Button)sender).Tag;
            string vbm = VmTools.FindVBoxManage();
            string qemu = VmTools.FindQemu("qemu-system-x86_64.exe");
            string qimg = VmTools.FindQemu("qemu-img.exe");
            bool hasQemu = qemu != null && qimg != null;
            if (vbm == null && !hasQemu)
            {
                await Info("Немає ні VirtualBox, ні QEMU",
                    "Встанови одну з програм і спробуй ще раз.", "VirtualBox", "QEMU",
                    () => Open("https://www.virtualbox.org/wiki/Downloads"),
                    () => Open("https://qemu.weilnetz.de/w64/"));
                return;
            }

            VmHint h = item.Vm ?? new VmHint();
            var name = new TextBox { Header = "Назва", Text = Path.GetFileNameWithoutExtension(item.Path) };
            var ram = new NumberBox { Header = "ОЗП, МБ", Value = h.Ram, Minimum = 256, SmallChange = 256, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var cpus = new NumberBox { Header = "Ядра", Value = h.Cpus, Minimum = 1, Maximum = 32, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var disk = new NumberBox { Header = "Диск, ГБ", Value = h.Disk, Minimum = 1, SmallChange = 5, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var efi = new CheckBox { Content = "UEFI (замість BIOS)", IsChecked = h.Efi };
            var start = new CheckBox { Content = "Запустити після створення", IsChecked = true };
            var kind = new RadioButtons { Header = "Чим створити" };
            kind.Items.Add(new RadioButton { Content = vbm != null ? "VirtualBox" : "VirtualBox (не знайдено)", IsEnabled = vbm != null });
            kind.Items.Add(new RadioButton { Content = hasQemu ? "QEMU" : "QEMU (не знайдено)", IsEnabled = hasQemu });
            kind.SelectedIndex = vbm != null ? 0 : 1;
            var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
            foreach (UIElement u in new UIElement[] { name, kind, ram, cpus, disk, efi, start })
            {
                panel.Children.Add(u);
            }
            var dlg = new ContentDialog
            {
                Title = "Створити віртуальну машину", Content = panel, PrimaryButtonText = "Створити",
                CloseButtonText = "Скасувати", DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            bool useVbox = kind.SelectedIndex == 0;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            VmSpec spec = VmSpec.From(h, name.Text, item.Path, Path.Combine(home, useVbox ? "VirtualBox VMs" : "VMs"));
            spec.RamMb = (int)ram.Value;
            spec.Cpus = (int)cpus.Value;
            spec.DiskGb = (int)disk.Value;
            spec.Efi = efi.IsChecked == true;
            string before = item.Status;
            item.Status = "Створюю віртуальну машину…";
            try
            {
                string log = useVbox
                    ? await VmTools.CreateVBoxAsync(vbm, spec, start.IsChecked == true, CancellationToken.None)
                    : await VmTools.CreateQemuAsync(qemu, qimg, spec, start.IsChecked == true, CancellationToken.None);
                item.Status = before;
                await Info("Машину створено", log.Length > 1500 ? log.Substring(log.Length - 1500) : log);
            }
            catch (Exception ex)
            {
                item.Status = before;
                await Info("Не вдалося створити машину", ex.Message);
            }
        }
    }
}
