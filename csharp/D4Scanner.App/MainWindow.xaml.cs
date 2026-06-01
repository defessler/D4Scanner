using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D4Scanner.Core;
using Microsoft.Win32;

namespace D4Scanner.App;

public partial class MainWindow : Window
{
    static Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    // Diablo IV palette — warm stone + antique gold + rarity colors
    static readonly Brush Ink = B("#E9E1D2"), Soft = B("#9C907C"), Faint = B("#6B5F4D"), Green = B("#6CBF5E"),
        Amber = B("#E0A52E"), Miss = B("#D14A35"), Card = B("#1A1714"), CardHi = B("#221E19"),
        Line = B("#2C261E"), Edge = B("#4A4031"), EdgeHi = B("#6E5E45"),
        Crimson = B("#D14A35"), Gold = B("#C8A24E"), GoldHi = B("#E6C873"), TileSel = B("#271F15");
    // item-rarity colors (match D4's itemization)
    static readonly Brush RMagic = B("#6E9BD6"), RRare = B("#E5C84A"), RLegend = B("#E08A3C"),
        RUnique = B("#C9A45C"), RMythic = B("#D1492E");
    static readonly FontFamily Serif = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Cinzel");
    const double UI = 1.55;   // intentional large-but-clean scale for the rendered body

    static Brush RarityBrush(string? rarity)
    {
        var s = (rarity ?? "").ToLowerInvariant();
        if (s.Contains("mythic")) return RMythic;
        if (s.Contains("unique")) return RUnique;
        if (s.Contains("legend")) return RLegend;
        if (s.Contains("rare")) return RRare;
        if (s.Contains("magic")) return RMagic;
        return Ink;
    }

    static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color RarityColor(string? rarity) => ((SolidColorBrush)RarityBrush(rarity)).Color;
    static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    // a horizontal transparent→color→transparent gradient (ornamental dividers / tints)
    static Brush HGrad(Color c, byte midAlpha)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(midAlpha, c.R, c.G, c.B), 0.5));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        return b;
    }

    // map a slot/group label to a slot-icon key (see Icons.Geom)
    static string SlotKey(string label)
    {
        var s = (label ?? "").ToLowerInvariant();
        if (s.Contains("helm") || s.Contains("head")) return "helm";
        if (s.Contains("chest") || s.Contains("torso") || s.Contains("body")) return "chest";
        if (s.Contains("glove") || s.Contains("hand")) return "gloves";
        if (s.Contains("pant") || s.Contains("leg")) return "pants";
        if (s.Contains("boot") || s.Contains("feet")) return "boots";
        if (s.Contains("ring")) return "ring";
        if (s.Contains("amulet") || s.Contains("neck")) return "amulet";
        if (s.Contains("off") || s.Contains("shield") || s.Contains("focus") || s.Contains("totem")) return "offhand";
        return "weapon";
    }

    // a tinted slot silhouette (game-icons.net geometry, 0..512 box, auto-scaled in a Viewbox)
    static FrameworkElement? SlotIcon(string key, Brush tint, double size)
    {
        if (!Icons.Geom.TryGetValue(key, out var d)) return null;
        Geometry g; try { g = Geometry.Parse(d); } catch { return null; }
        var path = new System.Windows.Shapes.Path { Data = g, Fill = tint };
        return new Viewbox { Width = size, Height = size, Child = path, VerticalAlignment = VerticalAlignment.Center };
    }

    // real D4 item art (runtime-fetched, cached) for a named item; null until it's downloaded
    FrameworkElement? RealIcon(string? name, double w, double h)
    {
        var path = IconResolver.Get(name, _target?.Class);
        if (path == null) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(path); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            return new Image { Source = bi, Width = w, Height = h, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        }
        catch { return null; }
    }

    // real item art if available, else the tinted slot silhouette
    FrameworkElement SlotOrItemIcon(string? itemName, string slotKey, Brush tint, double size) =>
        RealIcon(itemName, size, size) ?? SlotIcon(slotKey, tint, size) ?? TB("", tint, 1, false);

    LogWatcher? _watcher;
    System.Threading.Timer? _targetPoll;
    TargetBuild? _target;
    LiveBuild _live = new();
    string _log = TargetLoader.DefaultLogPath();
    string? _targetPath;
    DateTime _targetMtime;
    double _minRollPct = 75;
    string? _lastUrl;
    string? _selectedKey;    // which slot/category tile is expanded in the detail panel
    VisionResult? _vision;   // paragon/skills/aspects from the vision channel (merged with live gear)

    List<BuildEntry> _buildIndex = new();  // maxroll guide list for autocomplete
    string? _pickedSlug;                   // slug chosen from autocomplete (vs. free text in the box)
    bool _settingText;                     // guard: programmatic UrlBox edits shouldn't trigger autocomplete
    string? _profile;                      // active profile name (for re-import)
    string? _lastImportInput;              // the slug/url last imported (for profile re-import)
    string _detailView = "compare";        // "compare" (tooltip card) | "list"
    bool _rawView;                         // body shows the raw build details instead of the grid
    string? _classFilter;                  // active class chip in the search dropdown
    List<string> _recentSlugs = new();     // recently imported builds (search recents)
    bool _uiReady;                         // suppresses the search dropdown during the initial auto-focus

    string SettingsPath => Path.Combine(
        Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "app.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        SetUrlText("");   // start empty with the placeholder; the loaded build shows in the header
        ImportBtn.Click += async (_, _) => await DoImport();
        VisionBtn.Click += async (_, _) => await DoVision();
        TargetBtn.Click += (_, _) => PickTarget();
        LogBtn.Click += (_, _) => PickLog();
        RawBtn.Click += (_, _) => { _rawView = !_rawView; RawBtn.Content = _rawView ? "← Overview" : "Build details"; Render(); };
        TopmostBtn.Click += (_, _) => { Topmost = !Topmost; TopmostBtn.Content = Topmost ? "Unpin" : "Pin"; };
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseBtn.Click += (_, _) => Close();
        ThreshSlider.Value = _minRollPct;          // reflect the persisted threshold
        ThreshLbl.Text = ((int)_minRollPct) + "%";
        ThreshSlider.ValueChanged += (_, _) =>
        {
            _minRollPct = ThreshSlider.Value;
            ThreshLbl.Text = ((int)_minRollPct) + "%";
            SaveSettings();                        // remember the user's choice
            Render();
        };

        // ---- smart import box: placeholder + fuzzy autocomplete ----
        UrlBox.TextChanged += (_, _) =>
        {
            UrlPlaceholder.Visibility = UrlBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_settingText) return;
            _pickedSlug = null;          // user is typing free text now
            UpdateAutocomplete();
        };
        UrlBox.GotFocus += (_, _) => { if (_uiReady && UrlBox.Text.Length == 0) UpdateAutocomplete(); };
        UrlBox.PreviewKeyDown += UrlBox_PreviewKeyDown;
        AcList.PreviewKeyDown += AcList_PreviewKeyDown;
        AcList.MouseLeftButtonUp += async (_, _) => { ChooseAutocomplete(); await DoImport(); };
        ProfileBtn.Click += (_, _) => ProfilePopup.IsOpen = !ProfilePopup.IsOpen;

        Loaded += (_, _) => { StartWatching(); UrlBox.Focus(); Dispatcher.BeginInvoke(new Action(() => _uiReady = true), System.Windows.Threading.DispatcherPriority.Background); };
        Closed += (_, _) => { _watcher?.Dispose(); _targetPoll?.Dispose(); };
    }

    void SetUrlText(string text)
    {
        _settingText = true;
        UrlBox.Text = text;
        _settingText = false;
        UrlPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static bool LooksLikeUrl(string s) => s.Contains("://") || s.Contains("maxroll.gg");

    static Brush ClassColor(string? c) => (c ?? "").ToLowerInvariant() switch
    {
        "barbarian" => B("#C8553D"), "druid" => B("#6FA85B"), "necromancer" => B("#5BB7A8"),
        "rogue" => B("#C9A45C"), "sorcerer" => B("#6E9BD6"), "spiritborn" => B("#B07BD0"),
        "paladin" => B("#E0C060"), "warlock" => B("#B05B7A"), _ => B("#9C907C"),
    };

    void UpdateAutocomplete()
    {
        var q = UrlBox.Text.Trim();
        if (LooksLikeUrl(q) || _buildIndex.Count == 0) { AcPopup.IsOpen = false; return; }

        BuildClassChips();
        IEnumerable<BuildEntry> pool = _classFilter == null ? _buildIndex : _buildIndex.Where(b => b.Class == _classFilter);
        List<BuildEntry> hits;
        if (q.Length < 2)   // not typing yet → recent builds (filtered by the active class chip)
            hits = _recentSlugs.Select(s => _buildIndex.FirstOrDefault(b => b.Slug == s)).Where(b => b != null)
                    .Cast<BuildEntry>().Where(b => _classFilter == null || b.Class == _classFilter).Take(8).ToList();
        else
            hits = BuildIndex.Search(pool.ToList(), q, 8);

        AcList.Items.Clear();
        foreach (var b in hits) AcList.Items.Add(MakeAcItem(b));
        AcPopup.IsOpen = AcChips.Children.Count > 0 || AcList.Items.Count > 0;
    }

    ListBoxItem MakeAcItem(BuildEntry b)
    {
        var sp = new StackPanel { Tag = b };
        sp.Children.Add(TB(b.Title, Ink, 14, false));
        if (!string.IsNullOrEmpty(b.Class)) sp.Children.Add(TB(b.Class, ClassColor(b.Class), 11.5, false, new Thickness(0, 1, 0, 0)));
        return new ListBoxItem { Content = sp, Tag = b };
    }

    void BuildClassChips()
    {
        AcChips.Children.Clear();
        var classes = _buildIndex.Select(b => b.Class).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
        AcChips.Children.Add(ClassChip("All", null));
        foreach (var c in classes) AcChips.Children.Add(ClassChip(c!, c));
    }

    UIElement ClassChip(string label, string? cls)
    {
        bool on = _classFilter == cls;
        var t = TB(label, on ? B("#14110D") : ClassColor(cls), 12, on);
        var b = new Border
        {
            Child = t, CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 4, 11, 5), Margin = new Thickness(0, 0, 6, 6),
            Background = on ? (cls == null ? Gold : ClassColor(cls)) : B("#14110D"),
            BorderBrush = Edge, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, _) => { _classFilter = on ? null : cls; UpdateAutocomplete(); };
        return b;
    }

    async void UrlBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Down && AcPopup.IsOpen && AcList.Items.Count > 0)
        {
            AcList.SelectedIndex = 0;
            (AcList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape && AcPopup.IsOpen) { AcPopup.IsOpen = false; e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Enter) { AcPopup.IsOpen = false; e.Handled = true; await DoImport(); }
    }

    async void AcList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; ChooseAutocomplete(); await DoImport(); }
        else if (e.Key == System.Windows.Input.Key.Escape) { AcPopup.IsOpen = false; UrlBox.Focus(); e.Handled = true; }
    }

    void ChooseAutocomplete()
    {
        if ((AcList.SelectedItem ?? (AcList.Items.Count > 0 ? AcList.Items[0] : null)) is not ListBoxItem li || li.Tag is not BuildEntry b)
            return;
        SetUrlText(b.Display);
        _pickedSlug = b.Slug;
        AcPopup.IsOpen = false;
    }

    void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                if (s != null)
                {
                    if (s.TryGetValue("target", out var t)) _targetPath = t;
                    if (s.TryGetValue("log", out var l) && !string.IsNullOrEmpty(l)) _log = l;
                    if (s.TryGetValue("url", out var u)) _lastUrl = u;
                    if (s.TryGetValue("detailView", out var dv) && !string.IsNullOrEmpty(dv)) _detailView = dv;
                    if (s.TryGetValue("minRoll", out var mr) && double.TryParse(mr, System.Globalization.CultureInfo.InvariantCulture, out var mrv))
                        _minRollPct = Math.Clamp(mrv, 0, 100);
                    if (s.TryGetValue("src", out var sr) && !string.IsNullOrEmpty(sr)) _lastImportInput = sr;
                    if (s.TryGetValue("recent", out var rc) && !string.IsNullOrEmpty(rc)) _recentSlugs = rc.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
        }
        catch { }
        if (_targetPath == null || !File.Exists(_targetPath))
            foreach (var g in new[] { @"D:\Projects\D4Scanner\target.json", Path.Combine(AppContext.BaseDirectory, "target.json") })
                if (File.Exists(g)) { _targetPath = g; break; }
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new Dictionary<string, string?> { ["target"] = _targetPath, ["log"] = _log, ["url"] = _lastUrl, ["detailView"] = _detailView, ["src"] = _lastImportInput, ["recent"] = string.Join("|", _recentSlugs), ["minRoll"] = ((int)_minRollPct).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }
        catch { }
    }

    async void LoadBuildIndex()
    {
        try { _buildIndex = await BuildIndex.LoadAsync(); }
        catch { _buildIndex = new(); }
    }

    bool _iconRefreshQueued;
    async void LoadIconIndex()
    {
        try { await IconResolver.LoadIndexAsync(); Dispatcher.Invoke(Render); } catch { }
    }
    void OnIconReady()
    {
        // coalesce many icon downloads into a single background re-render
        if (_iconRefreshQueued) return;
        _iconRefreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { _iconRefreshQueued = false; Render(); });
    }

    void StartWatching()
    {
        LoadBuildIndex();
        IconResolver.Changed -= OnIconReady; IconResolver.Changed += OnIconReady;
        LoadIconIndex();
        ReloadTarget();
        LoadVision();
        _watcher?.Dispose();
        _watcher = new LogWatcher(_log, equippedOnly: true);
        _watcher.Updated += b => Dispatcher.Invoke(() => { _live = b; Render(); });
        _watcher.Start();
        _live = _watcher.Build;

        _targetPoll?.Dispose();
        _targetPoll = new System.Threading.Timer(_ =>
        {
            try
            {
                if (_targetPath != null && File.Exists(_targetPath) &&
                    File.GetLastWriteTimeUtc(_targetPath) != _targetMtime)
                    Dispatcher.Invoke(() => { ReloadTarget(); Render(); });
            }
            catch { }
        }, null, 1000, 1000);

        Render();
    }

    void ReloadTarget()
    {
        if (_targetPath != null && File.Exists(_targetPath))
            try
            {
                _target = TargetLoader.Load(_targetPath);
                _targetMtime = File.GetLastWriteTimeUtc(_targetPath);
                _profile = _target.Profile;
                ApplyProfileUi();
            }
            catch { _target = null; }
    }

    // show the profile dropdown (and its options) when the loaded build exposes more than one profile
    void ApplyProfileUi()
    {
        var profiles = _target?.Profiles ?? new();
        if (profiles.Count <= 1) { ProfileBtn.Visibility = Visibility.Collapsed; ProfilePopup.IsOpen = false; return; }

        ProfileBtn.Visibility = Visibility.Visible;
        ProfileBtn.Content = "Profile: " + (_target!.Profile ?? profiles[^1]);
        ProfileList.Items.Clear();
        foreach (var p in profiles)
        {
            var item = new ListBoxItem { Content = TB(p, Ink, 14, false), Tag = p, IsSelected = p == _target.Profile };
            item.MouseLeftButtonUp += async (_, _) => { ProfilePopup.IsOpen = false; await SwitchProfile(p); };
            ProfileList.Items.Add(item);
        }
    }

    async Task SwitchProfile(string profile)
    {
        if (_target?.Profile == profile) return;        // already on it
        var src = _lastImportInput ?? ResolveSrc();      // works even when the build was loaded from disk
        if (string.IsNullOrEmpty(src))
        {
            Status.Text = "re-import this build (paste its URL/slug) to switch profiles";
            return;
        }
        _profile = profile;
        await ImportFrom(src!, profile);
    }

    // best-effort recovery of the import source from the remembered URL/last build name
    string? ResolveSrc()
    {
        var u = (_lastUrl ?? "").Trim();
        if (u.Length == 0) return null;
        if (LooksLikeUrl(u)) return u;
        var m = _buildIndex.FirstOrDefault(b => b.Display == u || b.Title == u);
        return m?.Slug ?? u;
    }

    void PickTarget()
    {
        var d = new OpenFileDialog { Filter = "Target build JSON|*.json|All files|*.*", Title = "Pick your target.json" };
        if (d.ShowDialog() == true) { _targetPath = d.FileName; SaveSettings(); ReloadTarget(); Render(); }
    }

    // Import button / Enter: import whatever the box resolves to (an autocomplete-picked slug, or free text).
    Task DoImport()
    {
        AcPopup.IsOpen = false;
        var input = _pickedSlug ?? (UrlBox.Text ?? "").Trim();
        if (input.Length == 0) { Status.Text = "type a build name to search, or paste a Maxroll URL"; return Task.CompletedTask; }
        // resolve a typed/pre-filled build name (e.g. "Flurry · Rogue") back to its slug
        if (_pickedSlug == null && !LooksLikeUrl(input))
        {
            var m = _buildIndex.FirstOrDefault(b => b.Display == input || b.Title == input);
            if (m != null) input = m.Slug;
        }
        return ImportFrom(input, _profile);
    }

    async Task ImportFrom(string input, string? profile)
    {
        var prev = ImportBtn.Content;
        ImportBtn.IsEnabled = false; ImportBtn.Content = "…"; Status.Text = "importing build…";
        try
        {
            var t = await MaxrollImporter.ImportAsync(input, profile, s => Dispatcher.Invoke(() => Status.Text = s));
            var path = Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "target.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(t, D4Scanner.Core.Json.Opts));
            _target = t; _targetPath = path; _targetMtime = File.GetLastWriteTimeUtc(path);
            _lastUrl = UrlBox.Text.Trim(); _lastImportInput = input; _profile = t.Profile;
            if (!LooksLikeUrl(input))   // remember the build slug for the search "recents"
            {
                _recentSlugs.Remove(input); _recentSlugs.Insert(0, input);
                if (_recentSlugs.Count > 6) _recentSlugs.RemoveRange(6, _recentSlugs.Count - 6);
            }
            SaveSettings();
            ApplyProfileUi();
            Render();
            Status.Text = $"imported: {t.Name}" + (t.Profile != null ? $" [{t.Profile}]" : "")
                        + $" ({t.Gear.Count} gear, {t.Uniques.Count} uniques)";
        }
        catch (Exception ex)
        {
            Status.Text = "import failed — " + ex.Message;
            MessageBox.Show(ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { ImportBtn.IsEnabled = true; ImportBtn.Content = prev; }
    }

    // gear comes live from the TTS log; paragon/skills/aspects come from the vision channel
    LiveBuild EffectiveLive() => new()
    {
        Gear = _live.Gear,
        Skills = _vision?.Skills ?? new(),
        Paragon = _vision?.Paragon ?? new(),
        Aspects = _vision?.Aspects ?? new(),
    };

    string VisionPath => Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "vision.json");
    void LoadVision()
    {
        try { if (File.Exists(VisionPath)) _vision = JsonSerializer.Deserialize<VisionResult>(File.ReadAllText(VisionPath), D4Scanner.Core.Json.Opts); }
        catch { }
    }
    void SaveVision()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(VisionPath)!); File.WriteAllText(VisionPath, JsonSerializer.Serialize(_vision, D4Scanner.Core.Json.Opts)); }
        catch { }
    }

    async Task DoVision()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("Set the ANTHROPIC_API_KEY environment variable, then restart the app.\n\n" +
                "This reads paragon boards, glyph levels, skills and aspects from screenshots via Claude vision.",
                "API key needed", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var d = new OpenFileDialog
        {
            Title = "Pick screenshots — paragon boards, glyph tooltips, skill tree",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp|All files|*.*",
            Multiselect = true,
        };
        if (d.ShowDialog() != true || d.FileNames.Length == 0) return;
        var prev = VisionBtn.Content; VisionBtn.IsEnabled = false; VisionBtn.Content = "…"; Status.Text = "reading screenshots…";
        try
        {
            var res = await VisionCapture.CaptureAsync(d.FileNames, key!, null, s => Dispatcher.Invoke(() => Status.Text = s));
            _vision = res; SaveVision(); Render();
            Status.Text = $"vision: {res.Skills.Count} skills, {res.Paragon.Count} boards, {res.Aspects.Count} aspects";
        }
        catch (Exception ex)
        {
            Status.Text = "vision failed — " + ex.Message;
            MessageBox.Show(ex.Message, "Vision capture failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { VisionBtn.IsEnabled = true; VisionBtn.Content = prev; }
    }

    void PickLog()
    {
        var d = new OpenFileDialog { Filter = "TTS log|*.log;*.txt|All files|*.*", Title = "Pick the d4_tts.log" };
        if (d.ShowDialog() == true) { _log = d.FileName; SaveSettings(); StartWatching(); }
    }

    // ---- rendering ----
    static TextBlock TB(string text, Brush brush, double size, bool bold, Thickness? m = null) => new()
    {
        Text = text, Foreground = brush, FontSize = size * UI,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Margin = m ?? new Thickness(0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };

    // serif variant — for build name, item names, slot/section headers (the D4 "carved" look)
    static TextBlock TBs(string text, Brush brush, double size, bool bold, Thickness? m = null)
    {
        var t = TB(text, brush, size, bold, m);
        t.FontFamily = Serif;
        return t;
    }

    static UIElement Right(FrameworkElement e) { DockPanel.SetDock(e, Dock.Right); return e; }

    sealed class Section
    {
        public string Key = "", Label = "";
        public int Matched, Total, Under;
        public Group? Gear;     // a gear slot
        public Category? Cat;   // a whole non-gear category
        public string Status => (Total - Matched) > 0 ? "missing" : Under > 0 ? "under" : "met";
    }

    // D4-style markers: filled diamond when present, hollow diamond when missing (like an empty socket)
    (string glyph, Brush col) Look(string status) =>
        status == "met" ? ("◆", Green) : status == "under" ? ("◆", Amber) : ("◇", Miss);

    static string ShortName(string id) => id switch
    {
        "gear" => "Gear", "uniques" => "Uniques", "skills" => "Skills",
        "paragon" => "Paragon", "aspects" => "Aspects", _ => id,
    };

    void Render()
    {
        if (_target == null)
        {
            BuildName.Text = "D4Scanner — Live Build Tracker";
            OverallPct.Text = "—";
            OverallCount.Text = "No target loaded — paste a Maxroll URL and Import, or pick a target.json";
            OverallBar.Value = 0; Body.Children.Clear();
            Status.Text = $"log: {_log}";
            return;
        }
        var r = DiffEngine.Diff(_target, EffectiveLive(), _minRollPct);
        BuildName.Text = r.TargetName + (r.TargetClass != null ? "  ·  " + r.TargetClass : "");
        OverallPct.Text = r.Pct + "%";
        OverallCount.Text = $"{r.Matched} / {r.Total} met  ·  {_live.Gear.Count} equipped items"
            + (_vision != null ? "  ·  + vision" : "")
            + (r.Under > 0 ? $"  ·  ⚠ {r.Under} under-rolled" : "");
        OverallBar.Value = r.Pct;

        if (_rawView)
        {
            Body.Children.Clear();
            Body.Children.Add(RawView());
            Status.Text = $"build details  ·  target: {Path.GetFileName(_targetPath)}";
            return;
        }

        // build sections: one per gear slot + one per non-gear category.
        // gear slots get a unique key by index (several slots share the label "Weapon",
        // so keying by label would select/highlight all of them at once), and any
        // duplicated label is numbered — "Weapon 1", "Weapon 2", "Weapon 3".
        var sections = new List<Section>();
        var gearGroups = r.Categories.FirstOrDefault(c => c.Id == "gear")?.Groups ?? new List<Group>();
        var dupLabels = gearGroups.GroupBy(g => g.Name).Where(grp => grp.Count() > 1).Select(grp => grp.Key).ToHashSet();
        var seen = new Dictionary<string, int>();
        for (int gi = 0; gi < gearGroups.Count; gi++)
        {
            var g = gearGroups[gi];
            string label = g.Name;
            if (dupLabels.Contains(g.Name)) { int n = seen.GetValueOrDefault(g.Name) + 1; seen[g.Name] = n; label = $"{g.Name} {n}"; }
            sections.Add(new Section { Key = "gear:" + gi, Label = label, Matched = g.Matched, Total = g.Total, Under = g.Under, Gear = g });
        }
        foreach (var c in r.Categories)
            if (c.Id != "gear")
                sections.Add(new Section { Key = "cat:" + c.Id, Label = ShortName(c.Id), Matched = c.Matched, Total = c.Total, Under = c.Under, Cat = c });

        // keep selection if still present, else default to the first thing needing work
        if (_selectedKey == null || sections.All(s => s.Key != _selectedKey))
            _selectedKey = (sections.FirstOrDefault(s => s.Status != "met") ?? sections.FirstOrDefault())?.Key;

        Body.Children.Clear();
        if (!CaptureSetup.Installed()) Body.Children.Add(CaptureBanner());
        Body.Children.Add(SummaryStrip(r));
        var guide = GuidancePanel(r); if (guide != null) Body.Children.Add(guide);
        Body.Children.Add(PaperDoll(sections));
        var sel = sections.FirstOrDefault(s => s.Key == _selectedKey);
        if (sel != null) Body.Children.Add(DetailPanel(sel));

        Status.Text = $"● live  ·  log: {_log}  ·  target: {Path.GetFileName(_targetPath)}";
    }

    // shown when the TTS capture shim isn't installed — one-click install into the Diablo IV folder
    UIElement CaptureBanner()
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var btn = new Button { Content = "Install capture DLL", Style = (Style)FindResource("Primary"), Padding = new Thickness(18, 7, 18, 7), VerticalAlignment = VerticalAlignment.Center };
        btn.Click += (_, _) =>
        {
            btn.IsEnabled = false; var prev = btn.Content; btn.Content = "installing…";
            var (ok, msg) = CaptureSetup.Install();
            MessageBox.Show(msg, ok ? "Capture set up" : "Couldn't set up capture",
                MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            btn.IsEnabled = true; btn.Content = prev; Render();
        };
        DockPanel.SetDock(btn, Dock.Right); dp.Children.Add(btn);

        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(TBs("Capture DLL not installed", Amber, 13.5, true));
        txt.Children.Add(TB("Diablo IV can’t send item data yet. Install the TTS shim into your Diablo IV folder to start scanning.",
            Soft, 12, false, new Thickness(0, 2, 0, 0)));
        dp.Children.Add(txt);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1E, 0xE0, 0xA5, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE0, 0xA5, 0x2E)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12), Child = dp,
        };
    }

    // "Do Next" guidance for an in-progress build: the highest-impact missing/under requirements,
    // with a Mobalytics-style headline for the top one. Null when the build is already complete.
    UIElement? GuidancePanel(DiffReport r)
    {
        var items = new List<(string slot, ReqItem i)>();
        var gearCat = r.Categories.FirstOrDefault(c => c.Id == "gear");
        if (gearCat != null)
            foreach (var g in gearCat.Groups)
                foreach (var i in g.Items)
                    if (i.Status != "met") items.Add((g.Name, i));
        if (items.Count == 0) return null;

        var top = items.OrderBy(x => x.i.Status == "missing" ? 0 : 1).ThenBy(x => x.slot).Take(6).ToList();

        var sp = new StackPanel();
        sp.Children.Add(TBs("DO NEXT", Gold, 13, true, new Thickness(0, 0, 0, 6)));
        var h = top[0];
        string headline = h.i.Status == "missing"
            ? $"You still need {h.i.Label} on your {h.slot}" + (h.i.Need != null ? $"  ({h.i.Need})" : "")
            : $"Improve {h.i.Label} on your {h.slot} — you're at {h.i.Val}  ({h.i.Need})";
        var hl = TB(headline, Ink, 14, false, new Thickness(0, 0, 0, 9)); hl.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(hl);

        int n = 1;
        foreach (var (slot, i) in top)
        {
            var (_, col) = Look(i.Status);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var numT = TB(n.ToString(), B("#14110D"), 10.5, true);
            numT.HorizontalAlignment = HorizontalAlignment.Center; numT.TextAlignment = TextAlignment.Center;
            var numB = new Border { Background = col, CornerRadius = new CornerRadius(3), Width = 20, Height = 18, Margin = new Thickness(0, 0, 10, 0), Child = numT };
            DockPanel.SetDock(numB, Dock.Left); row.Children.Add(numB);
            if (i.Need != null) { var nd = TB(i.Need, Soft, 12, false, new Thickness(8, 0, 0, 0)); DockPanel.SetDock(nd, Dock.Right); row.Children.Add(nd); }
            row.Children.Add(TB($"{slot}  —  {i.Label}", i.Status == "missing" ? Ink : Soft, 12.5, false));
            sp.Children.Add(row);
            n++;
        }
        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14), Child = sp,
        };
    }

    UIElement SummaryStrip(DiffReport r)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var c in r.Categories)
        {
            var tb = TB($"{ShortName(c.Id)}  {c.Matched}/{c.Total}" + (c.Under > 0 ? $"  ⚠{c.Under}" : ""),
                c.Under > 0 ? Amber : (c.Matched == c.Total ? Green : Soft), 13, true);
            wp.Children.Add(new Border
            {
                Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(0, 0, 10, 8), Child = tb,
            });
        }
        return wp;
    }

    UIElement SlotGrid(List<Section> sections)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var s in sections) wp.Children.Add(SlotTile(s));
        return wp;
    }

    // Diablo IV character-screen layout: armor down the left, weapons/jewelry down the right,
    // build-wide categories (uniques / skills / paragon) in the middle.
    UIElement PaperDoll(List<Section> sections)
    {
        var gear = sections.Where(s => s.Gear != null).ToList();
        var cats = sections.Where(s => s.Cat != null).ToList();

        // equipment priority: missing first, then under-rolled, then met; number 1..N
        var ordered = gear
            .OrderBy(s => s.Status == "missing" ? 0 : s.Status == "under" ? 1 : 2)
            .ThenByDescending(s => s.Total - s.Matched)
            .ToList();
        var prio = new Dictionary<Section, int>();
        for (int i = 0; i < ordered.Count; i++) prio[ordered[i]] = i + 1;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var used = new HashSet<Section>();
        var left = new StackPanel();
        var right = new StackPanel();
        void Place(StackPanel col, string[] order, bool alignRight)
        {
            foreach (var k in order)
                foreach (var s in gear.Where(x => !used.Contains(x) && SlotKey(x.Label) == k))
                { col.Children.Add(SlotCell(s, prio.GetValueOrDefault(s), alignRight)); used.Add(s); }
        }
        Place(left, new[] { "helm", "chest", "gloves", "pants", "boots", "weapon" }, false);
        Place(right, new[] { "amulet", "ring", "offhand" }, true);
        foreach (var s in gear.Where(x => !used.Contains(x))) right.Children.Add(SlotCell(s, prio.GetValueOrDefault(s), true));

        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 2, 20, 0), MinWidth = 160 };
        if (cats.Count > 0) center.Children.Add(TBs("BUILD", Faint, 11, true, new Thickness(2, 0, 0, 6)));
        foreach (var s in cats) center.Children.Add(CatCell(s));

        Grid.SetColumn(left, 0); grid.Children.Add(left);
        Grid.SetColumn(center, 1); grid.Children.Add(center);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        return grid;
    }

    // slim framed cell for a build-wide category (uniques / skills / paragon), matching the slot cells
    UIElement CatCell(Section s)
    {
        var (glyph, col) = Look(s.Status);
        bool selected = s.Key == _selectedKey;
        var dp = new DockPanel { Width = 196 };
        var cnt = TB($"{s.Matched}/{s.Total}", Soft, 12, false); cnt.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(cnt, Dock.Right); dp.Children.Add(cnt);
        var mk = TB(glyph, col, 12.5, true, new Thickness(0, 0, 9, 0)); mk.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(mk, Dock.Left); dp.Children.Add(mk);
        var nm = TBs(s.Label, Ink, 13, true); nm.VerticalAlignment = VerticalAlignment.Center; dp.Children.Add(nm);
        var b = new Border
        {
            Child = dp, Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 0, 0, 9), CornerRadius = new CornerRadius(5),
            Background = selected ? TileSel : Card, BorderBrush = selected ? Gold : Edge, BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, _) => { _selectedKey = s.Key; Render(); };
        return b;
    }

    // what the build wants in a slot: a targeted unique, else the wanted aspect, else "Any <slot>"
    (string name, Brush col, string? iconName) WantedFor(Section s)
    {
        var key = SlotKey(s.Label);
        var u = _target?.Uniques.FirstOrDefault(x => SlotKey(x.Slot ?? "") == key);
        if (u != null) return (u.Name, u.Mythic ? RMythic : RUnique, u.Name);
        if (!string.IsNullOrEmpty(s.Gear?.WantAspect)) return (s.Gear!.WantAspect!, RLegend, null);
        return ("Any " + s.Label, Soft, null);
    }

    // a framed item icon with a status-colored border and a priority number badge
    FrameworkElement IconBox(Section s, int num)
    {
        var (_, scol) = Look(s.Status);
        var (_, _, iconName) = WantedFor(s);
        var grid = new Grid { Width = 48, Height = 48 };
        grid.Children.Add(new Border { Background = B("#14110D"), BorderBrush = scol, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(4) });
        var art = SlotOrItemIcon(iconName, SlotKey(s.Label), Soft, 40);
        art.Margin = new Thickness(4); art.HorizontalAlignment = HorizontalAlignment.Center; art.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(art);
        if (num > 0)
        {
            var badge = new Border
            {
                Background = scol, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -5), Child = TB(num.ToString(), B("#14110D"), 10.5, true),
            };
            grid.Children.Add(badge);
        }
        if (s.Gear != null && s.Gear.UpgradeItems.Count > 0)   // an upgrade is sitting in your bags
            grid.Children.Add(new Border
            {
                Background = Green, CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-4, -5, 0, 0), Child = TB("↑", B("#14110D"), 10.5, true),
            });
        return grid;
    }

    // a Mobalytics-style slot row: icon + (slot label / wanted item name), clickable to open the compare
    UIElement SlotCell(Section s, int num, bool alignRight)
    {
        var (name, ncol, _) = WantedFor(s);
        bool selected = s.Key == _selectedKey;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var lbl = TB(s.Label, Soft, 11.5, false);
        var nm = TB(name, ncol, 13.5, true); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = 200;
        if (alignRight)
        {
            lbl.HorizontalAlignment = nm.HorizontalAlignment = HorizontalAlignment.Right;
            lbl.TextAlignment = nm.TextAlignment = TextAlignment.Right;
        }
        text.Children.Add(lbl); text.Children.Add(nm);

        var icon = IconBox(s, num);
        var dp = new DockPanel { Width = 286 };
        if (alignRight) { DockPanel.SetDock(icon, Dock.Right); icon.Margin = new Thickness(12, 0, 0, 0); }
        else { DockPanel.SetDock(icon, Dock.Left); icon.Margin = new Thickness(0, 0, 12, 0); }
        dp.Children.Add(icon); dp.Children.Add(text);

        var b = new Border
        {
            Child = dp, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 0, 9), CornerRadius = new CornerRadius(5),
            Background = selected ? TileSel : System.Windows.Media.Brushes.Transparent,
            BorderBrush = selected ? Gold : System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseEnter += (_, _) => { if (!selected) b.Background = Card; };
        b.MouseLeave += (_, _) => { if (!selected) b.Background = System.Windows.Media.Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => { _selectedKey = s.Key; Render(); };
        return b;
    }

    UIElement SlotTile(Section s)
    {
        var (glyph, col) = Look(s.Status);
        bool selected = s.Key == _selectedKey;
        double pct = s.Total > 0 ? 100.0 * s.Matched / s.Total : 0;

        var sp = new StackPanel();
        var top = new DockPanel();
        top.Children.Add(Right(TB(s.Total > 0 ? $"{s.Matched}/{s.Total}" : "", Soft, 12.5, false)));
        // gear slots show real item art (or a status-tinted silhouette); categories keep the diamond
        string? eqName = s.Gear != null && s.Gear.LiveItems.Count > 0 ? s.Gear.LiveItems[0].Name : null;
        FrameworkElement marker = s.Gear != null ? SlotOrItemIcon(eqName, SlotKey(s.Label), col, 26)
                                                 : TB(glyph, col, 13.5, true);
        marker.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(marker, Dock.Left); top.Children.Add(marker);
        top.Children.Add(TBs(s.Label, Ink, 14, true));
        sp.Children.Add(top);
        sp.Children.Add(MiniBar(pct, s.Status == "missing" ? Crimson : col));

        var b = new Border
        {
            Background = selected ? TileSel : Card,
            BorderBrush = selected ? Gold : Edge, BorderThickness = new Thickness(selected ? 1.5 : 1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(16, 13, 16, 14),
            Margin = new Thickness(0, 0, 11, 11), Width = 208, Child = sp,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseEnter += (_, _) => { if (!selected) b.Background = CardHi; };
        b.MouseLeave += (_, _) => { if (!selected) b.Background = Card; };
        b.MouseLeftButtonUp += (_, _) => { _selectedKey = s.Key; Render(); };
        return b;
    }

    UIElement MiniBar(double pct, Brush fill)
    {
        double w = 176, h = 6;
        var g = new Grid { Width = w, Height = h, Margin = new Thickness(0, 11, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(new Border { Background = Line, CornerRadius = new CornerRadius(3) });
        g.Children.Add(new Border
        {
            Width = Math.Max(3, w * Math.Clamp(pct, 0, 100) / 100.0),
            HorizontalAlignment = HorizontalAlignment.Left, Background = fill, CornerRadius = new CornerRadius(3),
        });
        return g;
    }

    UIElement DetailPanel(Section s)
    {
        var sp = new StackPanel();
        var (glyph, col) = Look(s.Status);

        // header: slot icon + serif title, with the Compare/List toggle (gear only) docked right
        var hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (s.Gear != null) { var tg = ViewToggle(); DockPanel.SetDock(tg, Dock.Right); hdr.Children.Add(tg); }
        var hi = s.Gear != null ? SlotIcon(SlotKey(s.Label), col, 30) : null;
        if (hi != null) { hi.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(hi, Dock.Left); hdr.Children.Add(hi); }
        hdr.Children.Add(TBs((hi == null ? glyph + "  " : "") + s.Label + $"     {s.Matched} / {s.Total} met"
            + (s.Under > 0 ? $"  ·  ⚠ {s.Under} under-rolled" : ""), col, 17, true));
        sp.Children.Add(hdr);

        if (s.Gear != null) GearDetail(sp, s.Gear, s.Label);
        else if (s.Cat != null)
        {
            if (s.Cat.Id == "skills") SkillsView(sp, s.Cat);
            else if (s.Cat.Id == "paragon") ParagonView(sp, s.Cat);
            else foreach (var g in s.Cat.Groups) GroupRows(sp, g);
        }

        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(22, 16, 22, 20),
            Margin = new Thickness(0, 4, 0, 8), Child = sp,
        };
    }

    void GearDetail(StackPanel sp, Group g, string label)
    {
        var it = g.LiveItems.Count > 0 ? g.LiveItems[0] : null;
        if (_detailView == "list")
        {
            ItemHeader(sp, it);
            foreach (var i in g.Items) sp.Children.Add(AffixRow(i));
            if (g.Extras.Count > 0)
                sp.Children.Add(TB("also on your item:  " + string.Join("   ·   ", g.Extras), Soft, 11.5, false, new Thickness(0, 12, 0, 0)));
        }
        else
        {
            sp.Children.Add(CompareCard(g, it, label));
            if (g.UpgradeItems.Count > 0) sp.Children.Add(StashUpgrades(g.UpgradeItems));
        }
    }

    // green "better in your bags" block: non-equipped items that beat the equipped one for this slot
    UIElement StashUpgrades(List<string> ups)
    {
        var inner = new StackPanel();
        inner.Children.Add(TBs("↑  BETTER IN YOUR BAGS", Green, 12, true, new Thickness(0, 0, 0, 5)));
        foreach (var u in ups) inner.Children.Add(TB("◆  " + u, Ink, 12.5, false, new Thickness(0, 2, 0, 2)));
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1E, 0x6C, 0xBF, 0x5E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x6C, 0xBF, 0x5E)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 10, 0, 0), Child = inner,
        };
    }

    void ItemHeader(StackPanel sp, GearLiveItem? it)
    {
        if (it != null)
        {
            sp.Children.Add(TBs(it.Name.ToUpperInvariant(), RarityBrush(it.Rarity), 16, true, new Thickness(0, 0, 0, 1)));
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(it.Rarity)) parts.Add(it.Rarity!);
            if (it.ItemPower != null) parts.Add("Item Power " + it.ItemPower);
            sp.Children.Add(TB(string.Join("   ·   ", parts), Soft, 12, false, new Thickness(0, 0, 0, 12)));
        }
        else sp.Children.Add(TB("— no item captured for this slot —", Miss, 13, true, new Thickness(0, 0, 0, 10)));
    }

    UIElement Divider(Color c, byte alpha) =>
        new Border { Height = 1, Margin = new Thickness(0, 2, 0, 9), Background = HGrad(c, alpha) };

    // a small "tempered" (forged) badge, à la Maxroll's anvil marker
    TextBlock TemperedBadge()
    {
        var t = TB("⚒", Amber, 12, true);
        t.Margin = new Thickness(6, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center;
        t.ToolTip = "tempered affix";
        return t;
    }

    // wanted gems / runes for the slot (live socket contents aren't readable via TTS — target-only)
    UIElement SocketsBox(List<string> sockets)
    {
        var inner = new StackPanel();
        inner.Children.Add(TB("SOCKETS", Faint, 9.5, true, new Thickness(0, 0, 0, 2)));
        foreach (var s in sockets) inner.Children.Add(TB("◆  " + s, B("#6E9BD6"), 12, false, new Thickness(0, 1, 0, 1)));
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x6E, 0x9B, 0xD6)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x6E, 0x9B, 0xD6)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 8, 0, 0), Child = inner,
        };
    }

    // the item's legendary aspect / special ability, shown in a D4-style orange box
    UIElement AspectBox(string aspect)
    {
        var inner = new StackPanel();
        inner.Children.Add(TB("ASPECT / POWER", Faint, 9.5, true, new Thickness(0, 0, 0, 2)));
        var t = TB(aspect, RLegend, 12.5, false); t.TextWrapping = TextWrapping.Wrap;
        inner.Children.Add(t);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1E, 0xE0, 0x8A, 0x3C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE0, 0x8A, 0x3C)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 9, 0, 0), Child = inner,
        };
    }

    static string Sub(GearLiveItem it) =>
        string.Join("   ·   ", new[] { it.Rarity ?? "", it.ItemPower != null ? "Item Power " + it.ItemPower : "" }.Where(x => x.Length > 0));

    // ---- compare view: equipped item beside what the build wants (D4 hold-to-compare idiom) ----
    UIElement CompareCard(Group g, GearLiveItem? it, string label)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // EQUIPPED (left): your item + your rolls, colored by how they compare to the target
        var eq = new StackPanel();
        foreach (var i in g.Items) eq.Children.Add(EquippedRow(i));
        if (!string.IsNullOrEmpty(it?.Aspect)) eq.Children.Add(AspectBox(it!.Aspect!));
        if (g.Extras.Count > 0)
        {
            eq.Children.Add(Divider(RarityColor(it?.Rarity), 0x44));
            eq.Children.Add(TB("also: " + string.Join("   ·   ", g.Extras), Soft, 11, false));
        }
        var left = TooltipPanel("EQUIPPED",
            it != null ? it.Name.ToUpperInvariant() : "— EMPTY SLOT —",
            it != null ? RarityBrush(it.Rarity) : Miss,
            it != null ? Sub(it) : "nothing scanned in this slot yet",
            RarityColor(it?.Rarity), eq, it?.Name, SlotKey(label));

        // BUILD WANTS (right): the wanted item (a slot unique, if any) + the wanted affixes/thresholds
        var wantUnique = _target?.Uniques.FirstOrDefault(u => SlotKey(u.Slot ?? "") == SlotKey(label));
        bool myth = wantUnique?.Mythic == true;
        Color wrc = wantUnique != null ? (myth ? Col("#D1492E") : Col("#C9A45C")) : Col("#C8A24E");
        Brush wbr = wantUnique != null ? (myth ? RMythic : RUnique) : Gold;
        var wp = new StackPanel();
        foreach (var i in g.Items) wp.Children.Add(WantedRow(i));
        if (!string.IsNullOrEmpty(g.WantAspect)) wp.Children.Add(AspectBox(g.WantAspect!));
        if (g.WantSockets.Count > 0) wp.Children.Add(SocketsBox(g.WantSockets));
        var right = TooltipPanel("BUILD WANTS",
            wantUnique != null ? wantUnique.Name.ToUpperInvariant() : "ANY " + label.ToUpperInvariant(),
            wbr,
            wantUnique != null ? (myth ? "Mythic Unique" : "Unique") : "any item with these affixes",
            wrc, wp, wantUnique?.Name, SlotKey(label));

        Grid.SetColumn(left, 0); grid.Children.Add(left);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        return grid;
    }

    UIElement TooltipPanel(string title, string header, Brush headerBrush, string sub, Color rarity, StackPanel rows,
                           string? iconName, string slotKey)
    {
        // header band: real item art (or slot silhouette) beside the title/name/subtitle
        var head = new StackPanel();
        head.Children.Add(TB(title, Faint, 10.5, true, new Thickness(0, 0, 0, 4)));
        head.Children.Add(TBs(header, headerBrush, 15.5, true, new Thickness(0, 0, 0, 1)));
        head.Children.Add(TB(sub, Soft, 11, false));

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var icon = SlotOrItemIcon(iconName, slotKey, headerBrush, 50);
        var iconBox = new Border { Width = 52, Height = 64, Child = icon, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(iconBox, Dock.Left); top.Children.Add(iconBox);
        head.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(head);

        var inner = new StackPanel();
        inner.Children.Add(top);
        inner.Children.Add(Divider(rarity, 0xAA));
        inner.Children.Add(rows);
        return new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0x22, rarity.R, rarity.G, rarity.B), Col("#1A1714"), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, rarity.R, rarity.G, rarity.B)),
            BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(18, 14, 18, 16), VerticalAlignment = VerticalAlignment.Top, Child = inner,
        };
    }

    // left panel row: your rolled value + roll quality, colored by how it compares to the target
    UIElement EquippedRow(ReqItem i)
    {
        var (glyph, col) = Look(i.Status);
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mk = TB(glyph, col, 12.5, true); mk.VerticalAlignment = VerticalAlignment.Top; mk.Margin = new Thickness(0, 1, 0, 0);
        Grid.SetColumn(mk, 0); g.Children.Add(mk);
        var mid = new StackPanel();
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (i.Status != "missing" && !string.IsNullOrEmpty(i.Val)) line.Children.Add(TB(i.Val + "  ", col, 13, true));
        line.Children.Add(TB(i.Status == "missing" ? "— " + i.Label : i.Label, i.Status == "missing" ? Faint : Ink, 13, false));
        if (i.Tempered) line.Children.Add(TemperedBadge());
        mid.Children.Add(line);
        if (i.Status != "missing" && i.RollPct != null)
        {
            var bar = RollBar(i.RollPct.Value, col, 200, 7, _minRollPct); bar.Margin = new Thickness(0, 4, 0, 0);
            mid.Children.Add(bar);
        }
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        return g;
    }

    // right panel row: what the build asks for + its threshold
    UIElement WantedRow(ReqItem i)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mk = TB("◆", Gold, 12.5, true); mk.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(mk, 0); g.Children.Add(mk);
        var nmline = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nmline.Children.Add(TB(i.Label, Ink, 13, false));
        if (i.Tempered) nmline.Children.Add(TemperedBadge());
        Grid.SetColumn(nmline, 1); g.Children.Add(nmline);
        var nd = TB(i.Need ?? "any roll", Soft, 11.5, false); nd.VerticalAlignment = VerticalAlignment.Center; nd.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(nd, 2); g.Children.Add(nd);
        return g;
    }

    // segmented Compare/List toggle for the detail header
    FrameworkElement ViewToggle()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(SegBtn("Compare", "compare"));
        p.Children.Add(SegBtn("List", "list"));
        return new Border
        {
            Background = B("#0E0C0A"), BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center, Child = p,
        };
    }

    FrameworkElement SegBtn(string text, string mode)
    {
        bool on = _detailView == mode;
        var t = TB(text, on ? B("#1B1408") : Soft, 12.5, on);
        var b = new Border
        {
            Background = on ? Gold : System.Windows.Media.Brushes.Transparent, CornerRadius = new CornerRadius(3),
            Padding = new Thickness(13, 5, 13, 5), Child = t, Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseLeftButtonUp += (_, _) => { if (_detailView != mode) { _detailView = mode; SaveSettings(); Render(); } };
        return b;
    }

    UIElement AffixRow(ReqItem i)
    {
        var (glyph, col) = Look(i.Status);
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });               // mark
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });              // bar (fixed → aligned)
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });              // value (fixed → aligned)

        var mark = TB(glyph, col, 14, true); mark.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(mark, 0); row.Children.Add(mark);
        var lbl = TB(i.Label, i.Status == "met" ? Soft : Ink, 14, false); lbl.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lbl, 1); row.Children.Add(lbl);

        if (i.Status != "missing" && i.RollPct != null)
        {
            var bar = RollBar(i.RollPct.Value, col, 190, 12, _minRollPct);
            bar.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(bar, 2); row.Children.Add(bar);
        }

        string vtext = i.Status == "missing"
            ? "—"
            : (i.Val ?? "ok")
              + (i.RollPct != null ? $"   {Math.Round(i.RollPct.Value)}%" : "")
              + (i.Status == "under" && i.Need != null ? "   " + i.Need : "");
        var val = TB(vtext, i.Status == "missing" ? Soft : col, 12.5, i.Status != "missing");
        val.HorizontalAlignment = HorizontalAlignment.Right; val.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(val, 3); row.Children.Add(val);
        return row;
    }

    // a roll-quality bar: inset track + gradient fill at pct% of width, with an optional
    // target-threshold tick so you can see how far the roll is from where the build wants it.
    FrameworkElement RollBar(double pct, Brush fill, double w = 190, double h = 12, double? threshold = null)
    {
        var g = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(new Border { Background = B("#241F18"), BorderBrush = B("#100E0B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(h / 2) });

        var fc = ((SolidColorBrush)fill).Color;
        var grad = new LinearGradientBrush(Lighten(fc, 0.22), fc, 0);
        g.Children.Add(new Border
        {
            Width = Math.Max(4, w * Math.Clamp(pct, 0, 100) / 100.0),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0),
            Background = grad, CornerRadius = new CornerRadius(h / 2),
        });

        if (threshold is double t && t > 0 && t < 100)
            g.Children.Add(new Border
            {
                Width = 2, Height = h + 5, Background = Gold, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(w * t / 100.0 - 1, -2.5, 0, 0), Opacity = 0.85,
            });
        return g;
    }

    // ---- game-styled skills & paragon ----
    static string Monogram(string name)
    {
        var w = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return "?";
        return w.Length == 1 ? w[0][..Math.Min(2, w[0].Length)].ToUpperInvariant()
                             : ("" + w[0][0] + w[^1][0]).ToUpperInvariant();
    }

    // a square icon tile (skill / glyph): real art or a monogram, status border, optional rank/level badge
    UIElement IconTile(ReqItem i)
    {
        var col = i.Done ? Green : Miss;
        string name = i.Label; string? badge = null;
        var m = System.Text.RegularExpressions.Regex.Match(i.Label, @"^(.*?)\s+(\d+)\s*/\s*(\d+)$");
        if (m.Success) { name = m.Groups[1].Value.Trim(); badge = m.Groups[2].Value + "/" + m.Groups[3].Value; }

        var box = new Grid { Width = 54, Height = 54, HorizontalAlignment = HorizontalAlignment.Center };
        box.Children.Add(new Border { Background = B("#14110D"), BorderBrush = col, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(6) });
        var art = RealIcon(name, 44, 44);
        if (art != null) { art.Margin = new Thickness(4); box.Children.Add(art); }
        else { var mono = TB(Monogram(name), col, 17, true); mono.HorizontalAlignment = HorizontalAlignment.Center; mono.VerticalAlignment = VerticalAlignment.Center; box.Children.Add(mono); }
        if (badge != null)
        {
            var bt = TB(badge, B("#14110D"), 9.5, true);
            box.Children.Add(new Border { Background = col, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -4), Child = bt });
        }
        var cap = TB(name, i.Done ? Soft : Ink, 11, false);
        cap.TextWrapping = TextWrapping.Wrap; cap.TextAlignment = TextAlignment.Center; cap.Width = 82; cap.Margin = new Thickness(0, 5, 0, 0);
        var stack = new StackPanel { Width = 90, Margin = new Thickness(0, 0, 6, 12) };
        stack.Children.Add(box); stack.Children.Add(cap);
        return stack;
    }

    UIElement Chip(ReqItem i)
    {
        var col = i.Done ? Green : Miss;
        var sp2 = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = TB("◆", col, 11, true, new Thickness(0, 0, 7, 0)); dot.VerticalAlignment = VerticalAlignment.Center;
        var t = TB(i.Label, i.Done ? Soft : Ink, 12.5, false); t.VerticalAlignment = VerticalAlignment.Center;
        sp2.Children.Add(dot); sp2.Children.Add(t);
        return new Border { Child = sp2, Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8) };
    }

    void SkillsView(StackPanel sp, Category cat)
    {
        foreach (var g in cat.Groups)
        {
            sp.Children.Add(TBs(g.Name.ToUpperInvariant(), Gold, 11.5, true, new Thickness(0, 10, 0, 6)));
            if (g.Name == "Key Passives")
            {
                var wrap = new WrapPanel();
                foreach (var i in g.Items) wrap.Children.Add(Chip(i));
                sp.Children.Add(wrap);
            }
            else
            {
                var bar = new WrapPanel();
                foreach (var i in g.Items) bar.Children.Add(IconTile(i));
                sp.Children.Add(bar);
            }
        }
    }

    void ParagonView(StackPanel sp, Category cat)
    {
        foreach (var g in cat.Groups)
        {
            sp.Children.Add(TBs(g.Name.ToUpperInvariant(), Gold, 11.5, true, new Thickness(0, 10, 0, 6)));
            var wrap = new WrapPanel();
            foreach (var i in g.Items) wrap.Children.Add(g.Name == "Glyphs" ? IconTile(i) : Chip(i));
            sp.Children.Add(wrap);
        }
    }

    void GroupRows(StackPanel sp, Group g)
    {
        sp.Children.Add(TBs($"{g.Name.ToUpperInvariant()}   {g.Matched}/{g.Total}", Gold, 11.5, true, new Thickness(0, 10, 0, 4)));
        foreach (var i in g.Items)
        {
            var (glyph, col) = Look(i.Status);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            if (i.Have != null) row.Children.Add(Right(TB("have: " + i.Have, Miss, 12, false, new Thickness(8, 0, 0, 0))));
            var mark = TB(glyph + "  ", col, 13, true); DockPanel.SetDock(mark, Dock.Left); row.Children.Add(mark);
            row.Children.Add(TB(i.Label, i.Status == "met" ? Soft : Ink, 13, false));
            sp.Children.Add(row);
        }
    }

    // ---- raw build details ----
    UIElement RawView()
    {
        var t = _target!;
        var sp = new StackPanel();
        sp.Children.Add(TBs("BUILD DETAILS", Gold, 16, true, new Thickness(0, 0, 0, 2)));
        var meta = new[] { t.Class, t.Profile, t.Source }.Where(x => !string.IsNullOrEmpty(x));
        sp.Children.Add(TB(t.Name + (meta.Any() ? "   ·   " + string.Join("   ·   ", meta) : ""), Soft, 12.5, false, new Thickness(0, 0, 0, 4)));

        RawHeader(sp, "GEAR & AFFIX TARGETS");
        foreach (var ge in t.Gear)
        {
            sp.Children.Add(TBs((ge.Label ?? ge.Slot).ToUpperInvariant(), Ink, 12.5, true, new Thickness(0, 9, 0, 2)));
            if (ge.Affixes.Count == 0) sp.Children.Add(RawLine("—"));
            foreach (var a in ge.Affixes)
            {
                string thr = a.MinPercent != null ? $"   ≥ {a.MinPercent}% roll" : a.Min != null ? $"   ≥ {a.Min}" : "";
                sp.Children.Add(RawLine("◆  " + a.Name + thr));
            }
        }
        if (t.Uniques.Count > 0)
        {
            RawHeader(sp, "UNIQUES");
            foreach (var u in t.Uniques)
                sp.Children.Add(RawLine("◆  " + u.Name + (u.Slot != null ? "   —   " + u.Slot : "") + (u.Mythic ? "   (Mythic)" : "")));
        }
        if (t.Aspects.Count > 0) { RawHeader(sp, "ASPECTS"); foreach (var a in t.Aspects) sp.Children.Add(RawLine("◆  " + a)); }
        if (t.Skills.Count > 0)
        {
            RawHeader(sp, "SKILLS");
            foreach (var s in t.Skills) sp.Children.Add(RawLine("◆  " + s.Name + (s.Rank != null ? $"   (rank {s.Rank})" : "")));
        }
        if (t.KeyPassives.Count > 0) { RawHeader(sp, "KEY PASSIVES"); foreach (var k in t.KeyPassives) sp.Children.Add(RawLine("◆  " + k)); }
        if (t.Paragon != null && (t.Paragon.Boards.Count > 0 || t.Paragon.Glyphs.Count > 0))
        {
            RawHeader(sp, "PARAGON");
            foreach (var b in t.Paragon.Boards) sp.Children.Add(RawLine("▸  " + b));
            foreach (var gl in t.Paragon.Glyphs)
                sp.Children.Add(RawLine("◆  glyph: " + gl.Name + (gl.Level != null ? $"   (lvl {gl.Level})" : "")));
        }

        RawHeader(sp, "RAW JSON");
        string json; try { json = JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true }); } catch { json = "(unavailable)"; }
        sp.Children.Add(new TextBox
        {
            Text = json, IsReadOnly = true, IsReadOnlyCaretVisible = false,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"), FontSize = 12, Foreground = Soft,
            Background = B("#0D0B09"), BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(12),
            TextWrapping = TextWrapping.NoWrap, MaxHeight = 460, Margin = new Thickness(0, 4, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(22, 18, 22, 20), Child = sp,
        };
    }

    void RawHeader(StackPanel sp, string t) => sp.Children.Add(TBs(t, Gold, 12.5, true, new Thickness(0, 16, 0, 4)));
    UIElement RawLine(string text) => TB(text, Soft, 12.5, false, new Thickness(14, 2, 0, 2));
}
