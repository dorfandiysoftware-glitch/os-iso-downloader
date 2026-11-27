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
using Microsoft.UI.Xaml.Media.Imaging;
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
        public ImageSource Logo { get; set; }
        public Visibility LogoVisibility => Logo != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FallbackVisibility => Logo == null ? Visibility.Visible : Visibility.Collapsed;
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
            VmTools.QemuDir = History.LoadQemuDir();
            foreach (HistoryEntry h in History.Load())
            {
                if (File.Exists(h.Path))
                {
                    _items.Add(new DownloadItem
                    {
                        Title = h.Title, Path = h.Path, Vm = h.Vm, IsMac = h.IsMac, State = DownloadState.Done, Percent = 100,
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
                    list.Add(Tile(n.Name, n.Note ?? "", n.Id, false, n, null, n.Id));
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
                        list.Add(Tile(n.Name, n.Note ?? "", n.Id, false, n, null, n.Id));
                    }
                }
                else
                {
                    foreach (FileEntry f in cur.Files)
                    {
                        list.Add(Tile(f.Name, Describe(f), f.Name, f.Recommended, null, f, cur.Id));
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

        private static readonly Dictionary<string, BitmapImage> LogoCache = new Dictionary<string, BitmapImage>();

        private static ImageSource LogoFor(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            BitmapImage img;
            if (LogoCache.TryGetValue(id, out img))
            {
                return img;
            }
            string f = Path.Combine(AppContext.BaseDirectory, "Assets", "logos", id + ".png");
            img = File.Exists(f) ? new BitmapImage(new Uri(f)) : null;
            LogoCache[id] = img;
            return img;
        }

        private static Choice Tile(string title, string subtitle, string key, bool star, Node node, FileEntry file, string logoId)
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
                Logo = LogoFor(logoId),
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

        /// <summary>Puts a downloaded file under a child node named after its OS version, so the tree stays name > version > options.</summary>
        private static void AddGrouped(Node n, string group, FileEntry f)
        {
            string g = string.IsNullOrWhiteSpace(group) ? "macOS" : group;
            Node target = n.Children.Find(x => x.Name == g);
            if (target == null)
            {
                target = new Node { Id = n.Id + "-g" + n.Children.Count, Name = g, ChildrenTitle = "Файл" };
                n.Children.Add(target);
            }
            target.Files.Add(f);
        }

        private async Task LoadAppleAsync(Node n)
        {
            StepText.Text = "Завантажую каталог Apple… (це може тривати до хвилини)";
            Choices.ItemsSource = null;
            try
            {
                List<AppleInstaller> list = await AppleCatalog.FetchAsync(_http, CancellationToken.None);
                foreach (AppleInstaller a in list)
                {
                    AddGrouped(n, a.Title, new FileEntry
                    {
                        Name = a.DisplayName, Kind = "iso", Url = a.Url, Size = a.Size, Verified = true,
                        Source = "https://swscan.apple.com", Arch = "Intel і Apple Silicon",
                        Note = a.PostDate.HasValue ? a.PostDate.Value.ToString("yyyy-MM-dd") : null,
                        Checksum = a.Sha1 != null ? new Checksum { Algo = "sha1", Value = a.Sha1 } : null,
                    });
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати каталог Apple: " + e.Message;
            }
        }

        private static readonly int[] IosMajors = { 1, 2, 3, 4, 5, 6, 7, 16, 26, 27 };

        private async Task LoadIosDevicesAsync(Node n)
        {
            StepText.Text = "Запитую список пристроїв…";
            Choices.ItemsSource = null;
            try
            {
                string json = await _http.GetStringAsync(IosCatalog.DevicesUrl);
                foreach (IosDevice d in IosCatalog.ParseDevices(json, n.Param))
                {
                    n.Children.Add(new Node { Id = "ios-" + d.Identifier, Name = d.Name, Note = d.Identifier, ChildrenTitle = "Версія", Dynamic = "ios-fw", Param = n.Param + "|" + d.Identifier });
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати список: " + e.Message;
            }
        }

        private async Task LoadIosFirmwareAsync(Node n)
        {
            StepText.Text = "Запитую прошивки…";
            Choices.ItemsSource = null;
            try
            {
                string[] p = n.Param.Split('|');
                string json = await _http.GetStringAsync(IosCatalog.FirmwareUrl(p[1]));
                List<IosFirmware> fws = IosCatalog.ParseFirmwares(json, IosMajors);
                if (fws.Count == 0)
                {
                    StepText.Text = "Для цього пристрою немає прошивок iOS 1–7, 16, 26 чи 27.";
                    return;
                }
                foreach (IosFirmware f in fws)
                {
                    AddGrouped(n, f.GroupName(p[0]), new FileEntry
                    {
                        Name = f.Version + " (" + f.Build + ")" + (f.Signed ? " — підписана" : ""), Kind = "iso", Url = f.Url, Size = f.Size, Verified = true,
                        Source = "https://ipsw.me", Arch = p[1],
                        Note = "прошивка .ipsw" + (f.Date.HasValue ? ", " + f.Date.Value.ToString("yyyy-MM-dd") : ""),
                        Checksum = f.Sha256 != null ? new Checksum { Algo = "sha256", Value = f.Sha256 } : f.Sha1 != null ? new Checksum { Algo = "sha1", Value = f.Sha1 } : null,
                    });
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати прошивки: " + e.Message;
            }
        }

        private async Task LoadUupAsync(Node n)
        {
            StepText.Text = "Запитую у UUP dump список збірок…";
            Choices.ItemsSource = null;
            try
            {
                List<UupBuild> builds = await UupDump.FetchBuildsAsync(_http, n.Param, 80, CancellationToken.None);
                if (builds.Count == 0)
                {
                    StepText.Text = "UUP dump не знайшов збірок для «" + n.Param + "».";
                    return;
                }
                foreach (UupBuild b in builds)
                {
                    n.Children.Add(new Node
                    {
                        Id = "uup-" + b.Uuid.Substring(0, 8), Name = b.Build + " (" + b.Arch + ")", Note = b.Date.ToString("yyyy-MM-dd") + " · " + b.Title,
                        ChildrenTitle = "Мова", Dynamic = "uuplang", Param = b.Uuid + "|" + b.Arch,
                    });
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати список з UUP dump: " + e.Message;
            }
        }

        private async Task LoadUupLangAsync(Node n)
        {
            StepText.Text = "Запитую мови збірки…";
            Choices.ItemsSource = null;
            try
            {
                string[] p = n.Param.Split('|');
                List<UupLang> langs = await UupDump.FetchLangsAsync(_http, p[0], CancellationToken.None);
                foreach (UupLang l in langs)
                {
                    foreach ((string ed, string label) in new[] { ("professional", "Pro"), ("core", "Home") })
                    {
                        n.Files.Add(new FileEntry
                        {
                            Name = l.Name + " — " + label, Kind = "uup", Url = "uup:" + p[0] + ":" + l.Code + ":" + ed, Arch = p[1], Verified = true,
                            Source = "https://uupdump.net", Note = "ISO збирається на твоєму ПК з файлів Microsoft (потрібно ~20 ГБ і час)",
                        });
                    }
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати мови: " + e.Message;
            }
        }

        private async Task LoadCrosAsync(Node n)
        {
            StepText.Text = "Завантажую список образів ChromeOS…";
            Choices.ItemsSource = null;
            try
            {
                string json = await _http.GetStringAsync(CrosCatalog.ListUrl);
                foreach (CrosImage i in CrosCatalog.Parse(json))
                {
                    AddGrouped(n, i.Manufacturer, new FileEntry
                    {
                        Name = i.Name, Kind = "iso", Url = i.Url, Verified = true, Unzip = true, Source = "https://chromeos.google",
                        Note = "образ відновлення для конкретного Chromebook (.bin)",
                        Checksum = i.Sha1 != null ? new Checksum { Algo = "sha1", Value = i.Sha1 } : null,
                    });
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати список: " + e.Message;
            }
        }

        private async Task LoadMsAsync(Node n)
        {
            StepText.Text = "Запитую у Microsoft список мов…";
            Choices.ItemsSource = null;
            try
            {
                string v = n.Param;
                List<MsLanguage> langs = await MsDownload.GetLanguagesAsync(_http, v, false, CancellationToken.None);
                foreach (MsLanguage l in langs)
                {
                    string src = MsDownload.PageFor(v);
                    n.Files.Add(new FileEntry { Name = l.Name + " (64-біт)", Kind = "iso", Url = "ms:" + v + ":x64:" + l.Name, Arch = "x64", Verified = true, Source = src, Lang = l.Name });
                    if (v == "10")
                    {
                        n.Files.Add(new FileEntry { Name = l.Name + " (32-біт)", Kind = "iso", Url = "ms:" + v + ":x86:" + l.Name, Arch = "x86", Verified = true, Source = src, Lang = l.Name });
                    }
                }
                if (v == "11")
                {
                    try
                    {
                        foreach (MsLanguage l in await MsDownload.GetLanguagesAsync(_http, v, true, CancellationToken.None))
                        {
                            n.Files.Add(new FileEntry { Name = l.Name + " (ARM64)", Kind = "iso", Url = "ms:" + v + ":arm64:" + l.Name, Arch = "ARM64", Verified = true, Source = MsDownload.PageFor(v), Lang = l.Name });
                        }
                    }
                    catch (Exception)
                    {
                        // ARM64 is optional.
                    }
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати список від Microsoft: " + e.Message;
            }
        }

        private async Task LoadIpswAsync(Node n)
        {
            StepText.Text = "Завантажую список образів для Apple Silicon…";
            Choices.ItemsSource = null;
            try
            {
                List<AppleInstaller> list = await IpswCatalog.FetchAsync(_http, CancellationToken.None);
                foreach (AppleInstaller a in list)
                {
                    AddGrouped(n, a.Title, new FileEntry
                    {
                        Name = a.DisplayName + " (.ipsw)", Kind = "iso", Url = a.Url, Size = a.Size, Verified = true,
                        Source = "https://ipsw.me", Arch = "Apple Silicon",
                        Note = a.PostDate.HasValue ? a.PostDate.Value.ToString("yyyy-MM-dd") : null,
                        Checksum = a.Sha256 != null ? new Checksum { Algo = "sha256", Value = a.Sha256 }
                            : a.Sha1 != null ? new Checksum { Algo = "sha1", Value = a.Sha1 } : null,
                    });
                }
                if (_path.Count > 0 && _path.Peek() == n)
                {
                    ShowLevel();
                }
            }
            catch (Exception e)
            {
                StepText.Text = "Не вдалося отримати список: " + e.Message;
            }
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
            if (f.IsArchived) bits.Add("старий / неофіційний образ");
            if (f.Unzip) bits.Add("розпакується автоматично");
            if (!string.IsNullOrEmpty(f.Arch)) bits.Add(f.Arch);
            if (f.Size > 0) bits.Add(DownloadItem.Size(f.Size));
            if (f.Checksum != null) bits.Add("з перевіркою хешу");
            else if (f.Size > 0) bits.Add("перевірка за розміром");
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
                if (c.Node.Dynamic == "apple" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadAppleAsync(c.Node);
                }
                else if (c.Node.Dynamic == "ipsw" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadIpswAsync(c.Node);
                }
                else if (c.Node.Dynamic == "ios-devices" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadIosDevicesAsync(c.Node);
                }
                else if (c.Node.Dynamic == "ios-fw" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadIosFirmwareAsync(c.Node);
                }
                else if (c.Node.Dynamic == "uup" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadUupAsync(c.Node);
                }
                else if (c.Node.Dynamic == "uuplang" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadUupLangAsync(c.Node);
                }
                else if (c.Node.Dynamic == "cros" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadCrosAsync(c.Node);
                }
                else if (c.Node.Dynamic == "ms" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    await LoadMsAsync(c.Node);
                }
                else if (c.Node.Dynamic == "macrecovery" && c.Node.Files.Count == 0 && !c.Node.HasChildren)
                {
                    foreach (RecoveryPreset p in MacRecovery.Presets)
                    {
                        c.Node.Files.Add(new FileEntry
                        {
                            Name = p.Name, Kind = "macrecovery", Url = "macrecovery:" + p.BoardId, Verified = true,
                            Source = "http://osrecovery.apple.com", Arch = "Intel",
                            Note = "образ відновлення ≈0,5–0,7 ГБ → диск ≈3 ГБ для віртуальної машини",
                        });
                    }
                    ShowLevel();
                }
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
            item.IsMac = entry.Kind == "macrecovery";
            item.IsUup = entry.Kind == "uup";
            _items.Insert(0, item);
            Launch(item);
        }

        private void Launch(DownloadItem item)
        {
            _ = item.IsMac ? RunMacAsync(item) : item.IsUup ? RunUupAsync(item) : RunAsync(item);
        }

        private async Task RunUupAsync(DownloadItem item)
        {
            item.Cts = new CancellationTokenSource();
            CancellationToken ct = item.Cts.Token;
            item.State = DownloadState.Downloading;
            item.ActionLabel = "Скасувати";
            try
            {
                string dir = Path.Combine(item.Folder, "UUP-" + Downloader.SafeName(item.Entry.Url.Substring(4).Replace(':', '-')));
                item.Status = "Отримую пакет збірки з UUP dump…";
                string cmd = await UupDump.PreparePackageAsync(_http, item.Entry.Url, dir, ct);
                item.Status = "Йде завантаження файлів Microsoft і збірка ISO у вікні консолі (може тривати довго). Не закривай його.";
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"\"" + cmd + "\" || pause\"")
                {
                    WorkingDirectory = dir, UseShellExecute = true,
                };
                using (System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi))
                {
                    using (ct.Register(() => { try { proc.Kill(true); } catch (Exception) { } }))
                    {
                        await proc.WaitForExitAsync();
                    }
                }
                ct.ThrowIfCancellationRequested();
                string iso = null;
                foreach (string f in Directory.EnumerateFiles(dir, "*.iso"))
                {
                    iso = f;
                }
                if (iso == null)
                {
                    throw new InvalidOperationException("Збірка не створила ISO. Подивись повідомлення у вікні консолі; «Повторити» продовжить із завантажених файлів.");
                }
                item.State = DownloadState.Done;
                item.Percent = 100;
                item.Path = iso;
                item.ActionLabel = "Показати";
                item.Status = "Готово  " + iso;
                History.Save(_items);
            }
            catch (OperationCanceledException)
            {
                item.State = DownloadState.Cancelled;
                item.Status = "Скасовано";
            }
            catch (Exception ex)
            {
                item.State = DownloadState.Failed;
                item.Status = "Помилка: " + ex.Message;
                item.ActionLabel = "Повторити";
            }
        }

        private async Task RunMacAsync(DownloadItem item)
        {
            item.Cts = new CancellationTokenSource();
            CancellationToken ct = item.Cts.Token;
            item.State = DownloadState.Downloading;
            item.ActionLabel = "Пауза";
            try
            {
                string board = item.Entry.Url.Substring("macrecovery:".Length);
                string name = Downloader.SafeName(item.Entry.Name);
                string work = Path.Combine(item.Folder, name);
                string img = Path.Combine(work, name + ".img");
                HttpClient mac = MacRecovery.CreateClient();
                item.Status = "Прошу в Apple образ відновлення…";
                RecoveryImage info = await MacRecovery.GetImageAsync(mac, board, "default", ct);
                await Downloader.RunAsync(mac, new ResolvedFile { Url = info.ChunklistUrl, FileName = "BaseSystem.chunklist", Headers = MacRecovery.AssetHeaders(info.ChunklistToken) }, work, null, null, ct);
                var progress = new Progress<DownloadProgress>(p =>
                {
                    if (p.Total > 0)
                    {
                        item.Percent = 60.0 * p.Received / p.Total;
                    }
                    item.Status = "Завантаження: " + DownloadItem.Size(p.Received) + (p.Total > 0 ? " з " + DownloadItem.Size(p.Total) : "")
                        + "  ·  " + DownloadItem.Size((long)p.BytesPerSecond) + "/с";
                });
                await Downloader.RunAsync(mac, new ResolvedFile { Url = info.ImageUrl, FileName = "BaseSystem.dmg", Headers = MacRecovery.AssetHeaders(info.ImageToken) }, work, progress, null, ct);

                item.State = DownloadState.Verifying;
                item.Status = "Перевіряю образ за списком контрольних сум Apple…";
                string dmg = Path.Combine(work, "BaseSystem.dmg");
                string cl = Path.Combine(work, "BaseSystem.chunklist");
                string bad = await MacRecovery.VerifyChunklistAsync(dmg, cl, new Progress<double>(f => item.Percent = 60 + 15 * f), ct);
                if (bad != null)
                {
                    File.Delete(dmg);
                    throw new InvalidOperationException("Образ пошкоджений: " + bad + " Файл видалено, натисни «Повторити».");
                }

                item.Status = "Перетворюю .dmg у диск…";
                await DmgConverter.ConvertAsync(dmg, img, new Progress<double>(f => item.Percent = 75 + 25 * f), ct);
                File.Delete(dmg);
                File.Delete(cl);
                item.State = DownloadState.Done;
                item.Percent = 100;
                item.Path = img;
                item.ActionLabel = "Показати";
                item.Status = "Готово, перевірено за chunklist Apple ✓  " + img + "  (диск ≈3 ГБ; це образ відновлення, а не ISO)";
                History.Save(_items);
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
                    item.Status = "Пауза (частину збережено)";
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
                }, item.Cts.Token, Downloader.ConnectionsFor(item.Resolved.Url));
                string finalPath = r.Path;
                if (item.Entry != null && item.Entry.Unzip && r.HashOk != false)
                {
                    item.Status = "Розпаковую…";
                    finalPath = await Task.Run(() => r.Path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? Downloader.ExtractGzip(r.Path, true) : Downloader.ExtractZip(r.Path, true));
                }
                item.Percent = 100;
                item.Path = finalPath;
                item.State = DownloadState.Done;
                item.ActionLabel = "Показати";
                History.Save(_items);
                if (finalPath.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                {
                    item.Status = "Готово: " + finalPath + (r.HashOk == true ? " (хеш збігається ✓)" : " (розмір збігається з каталогом Apple)")
                        + ". На Mac двічі клацни .pkg, щоб отримати Install macOS.app.";
                }
                else if (r.HashOk == true)
                {
                    item.Status = "Готово, хеш збігається ✓  " + finalPath;
                }
                else if (r.HashOk == false)
                {
                    item.Status = "УВАГА: хеш НЕ збігається! Очікувався " + item.Resolved.Hash + ", отримано " + r.Hash + ". Файл: " + r.Path;
                }
                else
                {
                    item.Status = "Готово, але хешу для перевірки немає.  " + finalPath;
                    if (r.Hash == null)
                    {
                        item.Status = "Готово (без перевірки хешу). " + finalPath;
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
                    Launch(item);
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

        private async Task CreateMacVmAsync(DownloadItem item, string vbm)
        {
            if (vbm == null)
            {
                await Info("Потрібен VirtualBox", "Для macOS підтримується лише VirtualBox.", "VirtualBox", null,
                    () => Open("https://www.virtualbox.org/wiki/Downloads"));
                return;
            }
            var name = new TextBox { Header = "Назва", Text = Path.GetFileNameWithoutExtension(item.Path) };
            var ram = new NumberBox { Header = "ОЗП, МБ", Value = 4096, Minimum = 4096, SmallChange = 1024, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var cpus = new NumberBox { Header = "Ядра", Value = 2, Minimum = 1, Maximum = 32, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var disk = new NumberBox { Header = "Диск для macOS, ГБ", Value = 60, Minimum = 30, SmallChange = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var start = new CheckBox { Content = "Запустити після створення", IsChecked = true };
            var warn = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                Text = "Експериментально. Образ відновлення завантажить macOS з серверів Apple, тож потрібен інтернет у машині. "
                    + "Apple дозволяє macOS лише на своєму залізі. macOS 13 і новіші вимагають процесор з AVX2.",
            };
            var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
            foreach (UIElement u in new UIElement[] { warn, name, ram, cpus, disk, start })
            {
                panel.Children.Add(u);
            }
            var dlg = new ContentDialog
            {
                Title = "Створити віртуальну машину macOS", Content = panel, PrimaryButtonText = "Створити",
                CloseButtonText = "Скасувати", DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var spec = new VmSpec
            {
                Name = name.Text, IsoPath = item.Path, Folder = Path.Combine(home, "VirtualBox VMs"),
                RamMb = (int)ram.Value, Cpus = (int)cpus.Value, DiskGb = (int)disk.Value, VboxType = "MacOS1013_64",
            };
            string before = item.Status;
            item.Status = "Створюю віртуальну машину…";
            try
            {
                string log = await VmTools.CreateVBoxMacAsync(vbm, spec, start.IsChecked == true, CancellationToken.None);
                item.Status = before;
                await Info("Машину створено", log.Length > 1500 ? log.Substring(log.Length - 1500) : log);
            }
            catch (Exception ex)
            {
                item.Status = before;
                await Info("Не вдалося створити машину", ex.Message);
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
            if (item.IsMac)
            {
                await CreateMacVmAsync(item, vbm);
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
            kind.SelectedIndex = vbm != null ? 0 : (hasQemu ? 1 : 0);
            var pickQemu = new HyperlinkButton { Content = "Вказати шлях до QEMU…", Padding = new Thickness(0) };
            pickQemu.Visibility = hasQemu ? Visibility.Collapsed : Visibility.Visible;
            pickQemu.Click += async (s2, e2) =>
            {
                var fp = new FileOpenPicker();
                fp.FileTypeFilter.Add(".exe");
                InitializeWithWindow.Initialize(fp, WindowNative.GetWindowHandle(this));
                var file = await fp.PickSingleFileAsync();
                if (file == null)
                {
                    return;
                }
                VmTools.QemuDir = Path.GetDirectoryName(file.Path);
                qemu = VmTools.FindQemu("qemu-system-x86_64.exe");
                qimg = VmTools.FindQemu("qemu-img.exe");
                hasQemu = qemu != null && qimg != null;
                ((RadioButton)kind.Items[1]).Content = hasQemu ? "QEMU" : "QEMU (не знайдено у цій теці)";
                ((RadioButton)kind.Items[1]).IsEnabled = hasQemu;
                pickQemu.Visibility = hasQemu ? Visibility.Collapsed : Visibility.Visible;
                if (hasQemu)
                {
                    History.SaveQemuDir(VmTools.QemuDir);
                    kind.SelectedIndex = 1;
                }
            };
            var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
            foreach (UIElement u in new UIElement[] { name, kind, pickQemu, ram, cpus, disk, efi, start })
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
            if ((useVbox && vbm == null) || (!useVbox && !hasQemu))
            {
                await Info("Немає потрібної програми",
                    "Встанови VirtualBox або QEMU (або вкажи шлях до QEMU) і спробуй ще раз.", "VirtualBox", "QEMU",
                    () => Open("https://www.virtualbox.org/wiki/Downloads"),
                    () => Open("https://qemu.weilnetz.de/w64/"));
                return;
            }
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
