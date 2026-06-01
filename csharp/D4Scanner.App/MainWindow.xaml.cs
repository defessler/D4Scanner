using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    LogWatcher? _watcher;
    System.Threading.Timer? _targetPoll;
    TargetBuild? _target;
    LiveBuild _live = new();
    string _log = TargetLoader.DefaultLogPath();
    string? _targetPath;
    DateTime _targetMtime;
    double _minRollPct = 50;
    string? _lastUrl;
    string? _selectedKey;    // which slot/category tile is expanded in the detail panel
    VisionResult? _vision;   // paragon/skills/aspects from the vision channel (merged with live gear)

    List<BuildEntry> _buildIndex = new();  // maxroll guide list for autocomplete
    string? _pickedSlug;                   // slug chosen from autocomplete (vs. free text in the box)
    bool _settingText;                     // guard: programmatic UrlBox edits shouldn't trigger autocomplete
    string? _profile;                      // active profile name (for re-import)
    string? _lastImportInput;              // the slug/url last imported (for profile re-import)
    string _detailView = "compare";        // "compare" (3-pane) | "list"

    string SettingsPath => Path.Combine(
        Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "app.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        SetUrlText(_lastUrl ?? "");
        ImportBtn.Click += async (_, _) => await DoImport();
        VisionBtn.Click += async (_, _) => await DoVision();
        TargetBtn.Click += (_, _) => PickTarget();
        LogBtn.Click += (_, _) => PickLog();
        TopmostBtn.Click += (_, _) => { Topmost = !Topmost; TopmostBtn.Content = Topmost ? "Unpin" : "Pin"; };
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseBtn.Click += (_, _) => Close();
        ThreshSlider.ValueChanged += (_, _) =>
        {
            _minRollPct = ThreshSlider.Value;
            ThreshLbl.Text = ((int)_minRollPct) + "%";
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
        UrlBox.PreviewKeyDown += UrlBox_PreviewKeyDown;
        AcList.PreviewKeyDown += AcList_PreviewKeyDown;
        AcList.MouseLeftButtonUp += async (_, _) => { ChooseAutocomplete(); await DoImport(); };
        ProfileBtn.Click += (_, _) => ProfilePopup.IsOpen = !ProfilePopup.IsOpen;

        Loaded += (_, _) => { StartWatching(); UrlBox.Focus(); };
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

    void UpdateAutocomplete()
    {
        var q = UrlBox.Text.Trim();
        if (q.Length < 2 || LooksLikeUrl(q) || _buildIndex.Count == 0) { AcPopup.IsOpen = false; return; }
        var hits = BuildIndex.Search(_buildIndex, q, 8);
        AcList.Items.Clear();
        foreach (var b in hits)
        {
            var sp = new StackPanel { Tag = b };
            sp.Children.Add(TB(b.Title, Ink, 14, false));
            if (!string.IsNullOrEmpty(b.Class))
                sp.Children.Add(TB(b.Class, Soft, 11.5, false, new Thickness(0, 1, 0, 0)));
            AcList.Items.Add(new ListBoxItem { Content = sp, Tag = b });
        }
        AcPopup.IsOpen = AcList.Items.Count > 0;
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
                new Dictionary<string, string?> { ["target"] = _targetPath, ["log"] = _log, ["url"] = _lastUrl, ["detailView"] = _detailView }));
        }
        catch { }
    }

    async void LoadBuildIndex()
    {
        try { _buildIndex = await BuildIndex.LoadAsync(); }
        catch { _buildIndex = new(); }
    }

    void StartWatching()
    {
        LoadBuildIndex();
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
        if (_target?.Profile == profile || string.IsNullOrEmpty(_lastImportInput)) return;
        _profile = profile;
        await ImportFrom(_lastImportInput!, profile);
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
        if (input.Length == 0) { Status.Text = "type a build name to search, or paste a Maxroll URL"; return Task.CompletedTask;
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
        Body.Children.Add(SummaryStrip(r));
        Body.Children.Add(SlotGrid(sections));
        var sel = sections.FirstOrDefault(s => s.Key == _selectedKey);
        if (sel != null) Body.Children.Add(DetailPanel(sel));

        Status.Text = $"● live  ·  log: {_log}  ·  target: {Path.GetFileName(_targetPath)}";
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

    UIElement SlotTile(Section s)
    {
        var (glyph, col) = Look(s.Status);
        bool selected = s.Key == _selectedKey;
        double pct = s.Total > 0 ? 100.0 * s.Matched / s.Total : 0;

        var sp = new StackPanel();
        var top = new DockPanel();
        top.Children.Add(Right(TB(s.Total > 0 ? $"{s.Matched}/{s.Total}" : "", Soft, 12.5, false)));
        // gear slots show a (status-tinted) equipment icon; non-gear categories keep the diamond marker
        FrameworkElement marker = (s.Gear != null ? SlotIcon(SlotKey(s.Label), col, 24) : null)
                                  ?? TB(glyph, col, 13.5, true);
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

        if (s.Gear != null) GearDetail(sp, s.Gear);
        else if (s.Cat != null) foreach (var g in s.Cat.Groups) GroupRows(sp, g);

        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(22, 16, 22, 20),
            Margin = new Thickness(0, 4, 0, 8), Child = sp,
        };
    }

    void GearDetail(StackPanel sp, Group g)
    {
        var it = g.LiveItems.Count > 0 ? g.LiveItems[0] : null;
        if (it != null)
        {
            // tooltip-style header: rarity-colored serif caps name + a muted subtitle
            sp.Children.Add(TBs(it.Name.ToUpperInvariant(), RarityBrush(it.Rarity), 16, true, new Thickness(0, 0, 0, 1)));
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(it.Rarity)) parts.Add(it.Rarity!);
            if (it.ItemPower != null) parts.Add("Item Power " + it.ItemPower);
            sp.Children.Add(TB(string.Join("   ·   ", parts), Soft, 12, false, new Thickness(0, 0, 0, 12)));
        }
        else sp.Children.Add(TB("— no item captured for this slot —", Miss, 13, true, new Thickness(0, 0, 0, 10)));

        if (_detailView == "list") foreach (var i in g.Items) sp.Children.Add(AffixRow(i));
        else CompareTable(sp, g);

        if (g.Extras.Count > 0)
            sp.Children.Add(TB("also on your item:  " + string.Join("   ·   ", g.Extras), Soft, 11.5, false, new Thickness(0, 12, 0, 0)));
    }

    // ---- compare view: Yours | Δ | Target ----
    Grid CompareCols()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // affix
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });                  // yours
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });                  // delta
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });                  // target
        return g;
    }

    void CompareTable(StackPanel sp, Group g)
    {
        var head = CompareCols(); head.Margin = new Thickness(0, 2, 0, 5);
        void H(int c, string t, HorizontalAlignment a)
        { var x = TB(t, Faint, 11, true); x.HorizontalAlignment = a; Grid.SetColumn(x, c); head.Children.Add(x); }
        H(0, "AFFIX", HorizontalAlignment.Left); H(1, "YOURS", HorizontalAlignment.Left);
        H(2, "Δ", HorizontalAlignment.Left); H(3, "TARGET", HorizontalAlignment.Left);
        sp.Children.Add(new Border { Child = head, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 5) });

        foreach (var i in g.Items) sp.Children.Add(CompareRow(i));
    }

    UIElement CompareRow(ReqItem i)
    {
        var (glyph, col) = Look(i.Status);
        var g = CompareCols(); g.Margin = new Thickness(0, 6, 0, 6);

        // affix label + marker
        var lab = new DockPanel();
        var mk = TB(glyph, col, 13, true, new Thickness(0, 0, 8, 0)); DockPanel.SetDock(mk, Dock.Left); lab.Children.Add(mk);
        lab.Children.Add(TB(i.Label, i.Status == "met" ? Soft : Ink, 13.5, false));
        Grid.SetColumn(lab, 0); g.Children.Add(lab);

        // yours: value + a small roll bar
        var yours = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        yours.Children.Add(TB(i.Status == "missing" ? "—" : (i.Val ?? "ok"), i.Status == "missing" ? Faint : Ink, 13.5, true));
        if (i.Status != "missing" && i.RollPct != null)
        {
            var bar = RollBar(i.RollPct.Value, col, 138, 7);
            bar.HorizontalAlignment = HorizontalAlignment.Left; bar.Margin = new Thickness(0, 4, 0, 0);
            yours.Children.Add(bar);
        }
        Grid.SetColumn(yours, 1); g.Children.Add(yours);

        // delta vs the target threshold
        var (arrow, word) = i.Status == "met" ? ("▲", "ok") : i.Status == "under" ? ("▼", "low") : ("✕", "missing");
        var d = TB(arrow + "  " + word, col, 12.5, true); d.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(d, 2); g.Children.Add(d);

        // target requirement
        var tt = TB(i.Need ?? (i.Status == "missing" ? "required" : "any roll"), Soft, 12.5, false);
        tt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tt, 3); g.Children.Add(tt);
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
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26 * UI) });           // mark
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                   // bar
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                   // value

        var mark = TB(glyph, col, 14, true); Grid.SetColumn(mark, 0); row.Children.Add(mark);
        var lbl = TB(i.Label, i.Status == "met" ? Soft : Ink, 14, false); Grid.SetColumn(lbl, 1); row.Children.Add(lbl);

        if (i.Status != "missing" && i.RollPct != null)
        {
            var bar = RollBar(i.RollPct.Value, col);
            bar.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(bar, 2); row.Children.Add(bar);
        }

        string vtext = i.Status == "missing"
            ? "—"
            : (i.Val ?? "ok")
              + (i.RollPct != null ? $"  {Math.Round(i.RollPct.Value)}%" : "")
              + (i.Status == "under" && i.Need != null ? "  " + i.Need : "");
        var val = TB(vtext, i.Status == "missing" ? Soft : col, 12.5, i.Status != "missing", new Thickness(8, 0, 0, 0));
        val.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(val, 3); row.Children.Add(val);
        return row;
    }

    // a roll-quality bar: track + left-aligned fill at pct% of width
    FrameworkElement RollBar(double pct, Brush fill, double w = 170, double h = 14)
    {
        var g = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Background = Line, CornerRadius = new CornerRadius(h / 2) });
        g.Children.Add(new Border
        {
            Width = Math.Max(3, w * Math.Clamp(pct, 0, 100) / 100.0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = fill, CornerRadius = new CornerRadius(h / 2),
        });
        return g;
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
}
