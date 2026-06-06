using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D4Scanner.App.Capture;
using D4Scanner.Core;
using Microsoft.Win32;

namespace D4Scanner.App;

public partial class MainWindow : Window
{
    static Brush B(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    // Diablo IV palette — predominantly dark/cool with selective amber for accents only
    // Backgrounds and panels stay dark neutral; amber/gold used only for values, headers, highlights
    static readonly Brush Ink = B("#E8E4DC"),      // warm-tinted near-white for primary text
                          Soft = B("#9C9890"),      // grey with slight warmth for secondary text
                          Faint = B("#64615A"),     // dim grey for placeholder/disabled
                          Green = B("#4FB05A"),
                          Amber = B("#D4A730"),     // D4 amber — stat values and highlights ONLY
                          Miss = B("#CC3030"),      // red for missing/error
                          Card = B("#16151A"),      // near-black with very slight purple-grey (D4 panels)
                          CardHi = B("#1E1D24"),    // slightly lighter panel hover
                          Line = B("#252430"),      // dark separator
                          Edge = B("#383540"),      // dark border
                          EdgeHi = B("#524F5E"),    // lighter border for focus
                          Steel = B("#6E8CA8"),     // cool info blue
                          Crimson = B("#CC3030"),
                          Gold = B("#D4A730"),      // amber gold — used sparingly for section headers / CTAs
                          GoldHi = B("#F0C04A"),    // bright gold for active state
                          TileSel = B("#22202C");   // subtle purple-dark selected tile
    // item-rarity colors (tuned to match D4's in-game slot coloring)
    static readonly Brush RMagic  = B("#4A8FE0"), RRare   = B("#D4A730"), RLegend = B("#E8711A"),
                          RUnique = B("#C4935A"), RMythic = B("#C92B2B"), RAncestral = B("#66D0F8");
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

    static bool IsAncestral(string? rarity) =>
        (rarity ?? "").Contains("ancestral", StringComparison.OrdinalIgnoreCase);

    static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color RarityColor(string? rarity) => ((SolidColorBrush)RarityBrush(rarity)).Color;
    static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    // Composite a semi-transparent rarity colour onto the dark base (#16171B) to get a fully opaque result.
    // Used for the tooltip gradient so the panel is never transparent regardless of what's behind the popup.
    static Color BlendOntoBase(Color rc, byte alpha)
    {
        float a = alpha / 255f;
        return Color.FromRgb(
            (byte)(rc.R * a + 0x16 * (1 - a)),
            (byte)(rc.G * a + 0x17 * (1 - a)),
            (byte)(rc.B * a + 0x1B * (1 - a)));
    }

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

    // weapon base type from a build item id, e.g. "2HCrossbow_Legendary_…" → "Crossbow", "1HSword_…" → "Sword"
    static string? WeaponTypeLabel(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var head = itemId!.Split('_')[0];
        if (head.StartsWith("1H") || head.StartsWith("2H")) head = head[2..];
        if (head.Length < 2 || !char.IsLetter(head[0])) return null;
        return head;
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
    FrameworkElement? RealIcon(string? name, double w, double h, string? id = null, long? image = null)
    {
        var path = IconResolver.Get(name, id, image, _target?.Class);
        if (path == null) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(path); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            return new Image { Source = bi, Width = w, Height = h, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        }
        catch { return null; }
    }

    // real item art if available, else the tinted slot silhouette (separate w/h for portrait slots)
    FrameworkElement SlotOrItemIcon(string? itemName, string slotKey, Brush tint, double w, double h, string? id = null, long? image = null) =>
        RealIcon(itemName, w, h, id, image) ?? SlotIcon(slotKey, tint, Math.Max(w, h)) ?? TB("", tint, 1, false);
    FrameworkElement SlotOrItemIcon(string? itemName, string slotKey, Brush tint, double size, string? id = null, long? image = null) =>
        SlotOrItemIcon(itemName, slotKey, tint, size, size, id, image);

    LogWatcher? _watcher;
    OcrCaptureEngine? _captureEngine;
    bool _useTts = true;
    bool _useCapture = false;
    LogToJsonlConverter? _jsonl;   // mirrors the TTS log to a one-item-per-line .jsonl side-car
    System.Threading.Timer? _targetPoll;
    TargetBuild? _target;
    LiveBuild _live = new();
    string _log = TargetLoader.DefaultLogPath();
    string? _targetPath;
    DateTime _targetMtime;
    double _minRollPct = 75;
    double _uiScale = 1.0;    // body zoom factor (Ctrl +/-/0), on top of the base UI scale
    string? _lastUrl;
    string? _selectedKey;    // which slot/category tile is expanded in the detail panel
    string? _focusKey;       // when set, DO NEXT shows only this slot/category (one-thing-at-a-time focus)
    string _dollView = "mine";     // paper doll previews "mine" (equipped) or "target" (build wants)
    bool _stepsView;               // the full searchable/paged Next-Steps screen
    string _stepsSearch = "";
    int? _stepsTier;               // active effort-tier filter (null = all)
    int _stepsPage;
    StackPanel? _stepsResultsPanel;
    TextBlock? _stepsPageLbl;
    List<BuildEntry> _buildIndex = new();  // maxroll guide list for autocomplete
    string? _pickedSlug;                   // slug chosen from autocomplete (vs. free text in the box)
    bool _settingText;                     // guard: programmatic UrlBox edits shouldn't trigger autocomplete
    string? _profile;                      // active profile name (for re-import)
    string? _lastImportInput;              // the slug/url last imported (for profile re-import)
    string _detailView = "compare";        // "compare" (tooltip card) | "list"
    bool _rawView;                         // body shows the raw build details instead of the grid
    bool _activitiesOpen;                  // guidance-rail Activities accordion expanded?
    bool _narrow;                          // window below the two-column breakpoint → stack doll + rail
    bool _debugMode;                       // show diagnostic info (last scan time, slot names, etc.)
    bool _shimNeedsUpgrade;                // cached at StartWatching; cleared when user installs this session
    const double TwoColMin = 1080;         // below this width the overview reflows to a single column
    string? _classFilter;                  // active class chip in the search dropdown
    List<string> _recentSlugs = new();     // recently imported builds (search recents)
    bool _uiReady;                         // suppresses the search dropdown during the initial auto-focus
    readonly HashSet<string> _pinned = new();   // slot keys pinned for side-by-side compare
    readonly Dictionary<string, Section> _inventorySections = new();  // synthetic sections for pinned inventory items
    readonly System.Windows.Controls.Primitives.Popup _hoverPopup = new()
    { AllowsTransparency = true, StaysOpen = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };

    long _logSkipToPos;    // when > 0, next LogWatcher starts here (skips old log data after a live-cache clear)

    // auto-updater state
    System.Threading.Timer? _updateTimer;
    string? _pendingUpdateTag;          // tag of a downloaded update ready to apply
    string? _skipUpdateVersion;         // user-chosen "remind me later" version (persisted)
    bool _updateModalOpen;

    string SettingsPath => Path.Combine(
        Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "app.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        SetUrlText("");   // start empty with the placeholder; the loaded build shows in the header
        ImportBtn.Click += async (_, _) => await DoImport();
        TargetBtn.Click += (_, _) => ShowBuilds();
        // LogBtn removed from header — log actions live in Settings
        RawBtn.Click += (_, _) => { _rawView = !_rawView; RawBtn.Content = _rawView ? "← Overview" : "Build details"; Render(); };
        TopmostBtn.Click += (_, _) => { Topmost = !Topmost; TopmostBtn.Content = Topmost ? "Unpin" : "Pin"; };
        MinBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseBtn.Click += (_, _) => Close();
        InstallCaptureBtn.Click += (_, _) => RunInstall(InstallCaptureBtn);
        OpenSrcBtn.Click += (_, _) => OpenSource();
        HelpBtn.Click += (_, _) => ToggleHelp();
        // NextBtn removed from header — "View all steps" link lives inside the DO NEXT panel
        SettingsBtn.Click += (_, _) => ShowSettings();
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
        UrlBox.GotFocus += (_, _) =>
        {
            // Restore the search hint as placeholder (hides the loaded-build-name display)
            UrlPlaceholder.Text = "Search builds  (e.g. dance of knives)  or paste a Maxroll URL…";
            UrlPlaceholder.Foreground = Faint;
            if (_uiReady && UrlBox.Text.Length == 0) UpdateAutocomplete();
        };
        UrlBox.LostFocus += (_, _) => UpdateBuildNamePlaceholder();
        // Select all on click when the box is not already focused (makes switching builds fast)
        UrlBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!UrlBox.IsKeyboardFocused) { UrlBox.Focus(); UrlBox.SelectAll(); e.Handled = true; }
        };
        UrlBox.PreviewKeyDown += UrlBox_PreviewKeyDown;
        AcList.PreviewKeyDown += AcList_PreviewKeyDown;
        AcList.MouseLeftButtonUp += async (_, _) => { ChooseAutocomplete(); await DoImport(); };
        ProfileBtn.Click += (_, _) => ProfilePopup.IsOpen = !ProfilePopup.IsOpen;

        Loaded += (_, _) =>
        {
            // Clamp window to screen work area so it never starts off-screen on 1080p displays
            var wa = SystemParameters.WorkArea;
            if (Height > wa.Height) Height = wa.Height;
            if (Width  > wa.Width)  Width  = wa.Width;
            StartWatching(); UrlBox.Focus();
            Dispatcher.BeginInvoke(new Action(() => _uiReady = true), System.Windows.Threading.DispatcherPriority.Background);
        };
        CheckUpdatesBtn.Click += (_, _) => ShowUpdateModal(_pendingUpdateTag);
        Closing += (_, _) => { SaveLive(); SaveSettings(); };   // persist gear state + window size
        Closed += (_, _) => { _watcher?.Dispose(); _captureEngine?.Dispose(); _jsonl?.Dispose(); _targetPoll?.Dispose(); _updateTimer?.Dispose(); };
        // responsive reflow: re-render only when crossing the two-column width breakpoint
        SizeChanged += (_, _) =>
        {
            bool n = ActualWidth < TwoColMin;
            if (n != _narrow) { _narrow = n; if (_target != null && !_rawView && !_stepsView) Render(); }
        };
        KeyDown += Window_KeyDown;
        ApplyZoom();
    }

    void SetUrlText(string text)
    {
        _settingText = true;
        UrlBox.Text = text;
        _settingText = false;
        UrlPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Show the loaded build name as placeholder text when the box is not focused and empty.
    // This replaces the large BuildName header while keeping the search box as the primary UI.
    void UpdateBuildNamePlaceholder()
    {
        if (UrlBox.IsKeyboardFocused) return;   // don't overwrite what the user is typing
        if (UrlBox.Text.Length > 0) return;     // showing import input — leave it
        if (_target != null)
        {
            UrlPlaceholder.Text = _target.Name + (_target.Class != null ? "  ·  " + _target.Class : "");
            UrlPlaceholder.Foreground = B("#9DA1AB");   // Soft — distinct from dim search hint
        }
        else
        {
            UrlPlaceholder.Text = "Search builds  (e.g. dance of knives)  or paste a Maxroll URL…";
            UrlPlaceholder.Foreground = Faint;
        }
        UrlPlaceholder.Visibility = Visibility.Visible;
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
        var t = TB(label, on ? B("#0C0C0F") : ClassColor(cls), 12, on);
        var b = new Border
        {
            Child = t, CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 4, 11, 5), Margin = new Thickness(0, 0, 6, 6),
            Background = on ? (cls == null ? Gold : ClassColor(cls)) : B("#0C0C0F"),
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
                    if (s.TryGetValue("zoom", out var z) && double.TryParse(z, System.Globalization.CultureInfo.InvariantCulture, out var zv))
                        _uiScale = Math.Clamp(zv, 0.7, 1.6);
                    if (s.TryGetValue("gameDir", out var gd) && !string.IsNullOrEmpty(gd) && File.Exists(Path.Combine(gd, "Diablo IV.exe")))
                        CaptureSetup.UserGameDir = gd;
                    if (s.TryGetValue("debug", out var dbg)) _debugMode = dbg == "1";
                    if (s.TryGetValue("useTts", out var ut)) _useTts = ut != "0";
                    if (s.TryGetValue("useCapture", out var uc)) _useCapture = uc == "1";
                    if (s.TryGetValue("skipUpdateVersion", out var suv) && !string.IsNullOrEmpty(suv)) _skipUpdateVersion = suv;
                    if (s.TryGetValue("logSkipPos", out var lsp) && long.TryParse(lsp, out var lspv) && lspv > 0) _logSkipToPos = lspv;
                    // remembered window size (position is not restored, to avoid landing off-screen)
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (s.TryGetValue("winW", out var ww) && double.TryParse(ww, inv, out var wwv) &&
                        s.TryGetValue("winH", out var wh) && double.TryParse(wh, inv, out var whv) &&
                        wwv >= MinWidth && whv >= MinHeight)
                    {
                        Width = Math.Min(wwv, SystemParameters.VirtualScreenWidth);
                        Height = Math.Min(whv, SystemParameters.VirtualScreenHeight);
                    }
                    if (s.TryGetValue("winMax", out var wm) && wm == "1") WindowState = WindowState.Maximized;
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
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            bool mx = WindowState == WindowState.Maximized;
            double sw = mx && !RestoreBounds.IsEmpty ? RestoreBounds.Width : (ActualWidth > 0 ? ActualWidth : Width);
            double sh = mx && !RestoreBounds.IsEmpty ? RestoreBounds.Height : (ActualHeight > 0 ? ActualHeight : Height);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new Dictionary<string, string?> { ["target"] = _targetPath, ["log"] = _log, ["url"] = _lastUrl, ["detailView"] = _detailView, ["src"] = _lastImportInput, ["recent"] = string.Join("|", _recentSlugs), ["minRoll"] = ((int)_minRollPct).ToString(inv), ["zoom"] = _uiScale.ToString(inv), ["winW"] = sw.ToString(inv), ["winH"] = sh.ToString(inv), ["winMax"] = mx ? "1" : "0", ["gameDir"] = CaptureSetup.UserGameDir, ["debug"] = _debugMode ? "1" : "0", ["useTts"] = _useTts ? "1" : "0", ["useCapture"] = _useCapture ? "1" : "0", ["skipUpdateVersion"] = _skipUpdateVersion, ["logSkipPos"] = _logSkipToPos > 0 ? _logSkipToPos.ToString() : null }));
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
        // point the game-data icon extractor at the local install (falls back to probing common paths)
        try { GameDataIcons.GameDir = CaptureSetup.GameDir(); } catch { }
        try { await IconResolver.LoadIndexAsync(); Dispatcher.Invoke(Render); } catch { }
    }
    void OnIconReady()
    {
        // coalesce many icon downloads into a single background re-render
        if (_iconRefreshQueued) return;
        _iconRefreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { _iconRefreshQueued = false; Render(); });
    }

    // live gear changed: swap it in, re-render, and surface what was newly equipped.
    void OnLiveUpdate(LiveBuild b)
    {
        var merged = new LiveBuild
        {
            Gear      = MergeGear(_live.Gear, b.Gear),
            Inventory = b.Inventory,
        };
        var added = _live.Gear.Count == 0 ? new List<string>() : NewlyEquipped(_live, merged);
        _live = merged;
        SaveLive();
        Render();
        if (added.Count == 1) { Toast($"Equipped  {added[0]}"); AppLog($"equipped: {added[0]}"); }
        else if (added.Count > 1) { Toast($"Gear updated — {added.Count} new items"); AppLog($"gear update: {added.Count} new items — {string.Join(", ", added)}"); }
        // First successful update after a cache clear: retire the skip position so future launches
        // don't stay pinned at the old log offset after the user has refreshed their gear.
        if (_logSkipToPos > 0 && merged.Gear.Count > 0) { _logSkipToPos = 0; SaveSettings(); }
        // Show last scanned item in status bar
        var newest = merged.Gear.OrderByDescending(g => g.LastScannedTicks).FirstOrDefault();
        if (newest != null) StatusDetail.Text = $"last: {newest.Name}  ·  {System.IO.Path.GetFileName(_log)}";
    }

    static List<string> NewlyEquipped(LiveBuild oldB, LiveBuild newB)
    {
        var had = new HashSet<string>(oldB.Gear.Select(g => g.Name ?? "").Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        return newB.Gear.Select(g => g.Name ?? "").Where(n => n.Length > 0 && !had.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    void StartWatching()
    {
        AppLog($"D4Scanner {Updater.RunningVersion()} started — log: {_log}");
        LoadBuildIndex();
        IconResolver.Changed -= OnIconReady; IconResolver.Changed += OnIconReady;
        LoadIconIndex();
        // No longer auto-upgrades the DLL silently — the user is prompted via UpgradeBanner() in Render().
        // Cache the result so we don't re-check on every Render() after the user installs this session.
        _shimNeedsUpgrade = CaptureSetup.Installed() && CaptureSetup.NeedsUpgrade();
        CheckForUpdatesAsync();        // check on every launch
        _updateTimer?.Dispose();
        _updateTimer = new System.Threading.Timer(
            _ => Dispatcher.Invoke(CheckForUpdatesAsync),
            null,
            TimeSpan.FromHours(4),     // first periodic check: 4 hours after launch
            TimeSpan.FromHours(4));    // then every 4 hours while running
        ReloadTarget();
        LoadLive();   // seed paper doll immediately from last-known state
        _watcher?.Dispose(); _watcher = null;
        if (_useTts)
        {
            // Pass _logSkipToPos so a live-cache clear starts reading from the end of the old data.
            _watcher = new LogWatcher(_log, equippedOnly: true, startPos: _logSkipToPos);
            _logSkipToPos = 0;   // consume the skip once
            _watcher.Updated += b => Dispatcher.Invoke(() => OnLiveUpdate(b));
            _watcher.CharacterPanelDetected += () =>
                Task.Delay(800).ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    // Auto-capture portrait after a short delay so the panel finishes animating
                    var err = CaptureCharacterPortrait();
                    if (err == null) { Render(); AppLog("portrait auto-captured from character panel"); }
                }));
            _watcher.Start();
            _jsonl?.Dispose();
            _jsonl = new LogToJsonlConverter(_log);
            _jsonl.Start();
            _live = new LiveBuild
            {
                Gear      = MergeGear(_live.Gear, _watcher.Build.Gear),
                Inventory = _watcher.Build.Inventory,
            };
        }
        _captureEngine?.Dispose(); _captureEngine = null;
        if (_useCapture)
        {
            _captureEngine = new OcrCaptureEngine();
            _captureEngine.Updated += b => Dispatcher.Invoke(() => OnLiveUpdate(b));
            _captureEngine.Start();
        }

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

    string BuildsDir => Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "builds");
    static string SafeFile(string? name)
    {
        var s = new string((name ?? "build").Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray()).Trim();
        return s.Length == 0 ? "build" : s;
    }

    // dropdown of saved builds — switch which build you're comparing against (plus an "open a file" option)
    void ShowBuilds()
    {
        BuildsList.Items.Clear();
        var files = Directory.Exists(BuildsDir) ? Directory.GetFiles(BuildsDir, "*.json").ToList() : new List<string>();
        if (_targetPath != null && File.Exists(_targetPath) && !files.Any(f => string.Equals(f, _targetPath, StringComparison.OrdinalIgnoreCase)))
            files.Insert(0, _targetPath);   // include an active build saved outside the builds dir (e.g. a legacy target.json)
        foreach (var f in files)
        {
            string name;
            try { name = JsonSerializer.Deserialize<TargetBuild>(File.ReadAllText(f), D4Scanner.Core.Json.Opts)?.Name ?? Path.GetFileNameWithoutExtension(f); }
            catch { name = Path.GetFileNameWithoutExtension(f); }
            bool active = string.Equals(f, _targetPath, StringComparison.OrdinalIgnoreCase);
            var item = new ListBoxItem { Content = TB((active ? "● " : "") + name, active ? Ink : Soft, 13.5, active), Tag = f };
            var fp = f;
            item.MouseLeftButtonUp += (_, _) =>
            {
                BuildsPopup.IsOpen = false;
                if (string.Equals(fp, _targetPath, StringComparison.OrdinalIgnoreCase)) return;
                _targetPath = fp; _lastImportInput = null; _lastUrl = null;   // switched build: source comes from its own metadata
                _pinned.Clear(); _focusKey = null;
                // Invalidate persisted gear — it was for the previous build
                _live = new LiveBuild();
                try { if (File.Exists(LivePath)) File.Delete(LivePath); } catch { }
                SaveSettings(); ReloadTarget(); ApplyProfileUi(); Render();
                Toast($"Switched to  {name}");
            };
            BuildsList.Items.Add(item);
        }
        var open = new ListBoxItem { Content = TB("＋ Open a .json file…", Steel, 13, false) };
        open.MouseLeftButtonUp += (_, _) => { BuildsPopup.IsOpen = false; PickTarget(); };
        BuildsList.Items.Add(open);
        BuildsPopup.IsOpen = true;
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
            Directory.CreateDirectory(BuildsDir);
            var path = Path.Combine(BuildsDir, SafeFile(t.Name) + ".json");   // one file per build (kept for comparison switching)
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
            Toast($"Imported  {t.Name}");
        }
        catch (Exception ex)
        {
            Status.Text = "import failed — " + ex.Message;
            MessageBox.Show(ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { ImportBtn.IsEnabled = true; ImportBtn.Content = prev; }
    }

    LiveBuild EffectiveLive() => _live;

    // Persist the last-known gear state so the paper doll shows immediately on next launch
    // without requiring the user to re-hover all their equipped items.  Mirrors vision.json.
    string LivePath => Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "live.json");
    void LoadLive()
    {
        // Try live.json first (persisted gear state from last session)
        try
        {
            if (File.Exists(LivePath))
            {
                var lb = JsonSerializer.Deserialize<LiveBuild>(File.ReadAllText(LivePath), D4Scanner.Core.Json.Opts);
                if (lb != null) _live = lb;
            }
        }
        catch { }

        // Note: .jsonl fast-startup removed. The .jsonl is append-only across ALL sessions
        // and seeded stale gear from previous sessions while skipping current log entries.
        // LogWatcher re-reads from the beginning and is authoritative.
    }
    void SaveLive()
    {
        if (_live.Gear.Count == 0) return;   // never overwrite good persisted data with empty
        try { File.WriteAllText(LivePath, JsonSerializer.Serialize(_live, D4Scanner.Core.Json.Opts)); }
        catch { }
    }

    // Merge fresh scan results into the persisted live state. Tts items win over Ocr items per slot:
    // if the incoming batch has only Ocr for a slot where persisted has a Tts item, keep the Tts item.
    static List<Item> MergeGear(List<Item> persisted, List<Item> fresh)
    {
        if (fresh.Count == 0) return persisted;
        var freshBySlot = fresh
            .GroupBy(it => DiffEngine.SlotBaseName(it.Slot ?? ""), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var result = new List<Item>();
        var handledSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in freshBySlot)
        {
            bool hasTtsFresh = kv.Value.Any(it => it.Source == ItemSource.Tts);
            var persistedForSlot = persisted.Where(it => DiffEngine.SlotBaseName(it.Slot ?? "") == kv.Key).ToList();
            bool hasTtsPersisted = persistedForSlot.Any(it => it.Source == ItemSource.Tts);
            if (!hasTtsFresh && hasTtsPersisted)
                result.AddRange(persistedForSlot);  // keep existing Tts, ignore incoming Ocr
            else
                result.AddRange(kv.Value);          // fresh wins (Tts fresh, or no Tts conflict)
            handledSlots.Add(kv.Key);
        }
        result.AddRange(persisted.Where(it => !handledSlots.Contains(DiffEngine.SlotBaseName(it.Slot ?? ""))));
        return result;
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
    static T Center<T>(T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Center; return e; }

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
        "paragon" => "Paragon", "aspects" => "Aspects", "mercenary" => "Mercenary", _ => id,
    };

    // Render the whole window to a PNG without showing it — for headless inspection of the live UI.
    internal void HeadlessRender(string outPng, int w = 1300, int h = 2100)
    {
        try { GameDataIcons.GameDir = CaptureSetup.GameDir(); } catch { }
        _uiReady = true;
        _narrow = w < TwoColMin;   // headless has no ActualWidth yet, so seed the reflow from the requested width
        ReloadTarget();
        try { _live = LogWatcher.BuildFromFile(_log, equippedOnly: true); } catch { }
        // render-test seams (env-gated, no effect in normal use): exercise states the harness can't click to
        var seed = System.Environment.GetEnvironmentVariable("D4_RENDER_STATE");
        if (seed == "pin") { _pinned.Add("gear:0"); _pinned.Add("gear:1"); }
        else if (seed == "focus") _focusKey = "gear:0";
        else if (seed == "steps") _stepsView = true;
        else if (seed == "raw") _rawView = true;
        Render();
        if (seed == "help" || System.Environment.GetEnvironmentVariable("D4_RENDER_HELP") == "1") ToggleHelp();

        var content = (FrameworkElement)Content;
        var size = new Size(w, h);
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(content);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var full = Path.GetFullPath(outPng);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var fs = File.Create(full);
        enc.Save(fs);
    }

    void Render()
    {
        InstallCaptureBtn.Visibility = Visibility.Collapsed;   // setup via Settings only
        OpenSrcBtn.Visibility = SourceUrl() != null ? Visibility.Visible : Visibility.Collapsed;
        if (_target == null)
        {
            UpdateBuildNamePlaceholder();
            OverallPct.Text = "—";
            OverallCount.Text = "No build loaded yet — import one to start your guide";
            OverallBar.Value = 0; Body.Children.Clear();
            if (!CaptureSetup.Installed()) Body.Children.Add(CaptureBanner());
        else if (_shimNeedsUpgrade) Body.Children.Add(UpgradeBanner());
            Body.Children.Add(WelcomeCard());
            Status.Text = $"waiting for first scan  ·  {Updater.RunningVersion()}";
            return;
        }
        var r = DiffEngine.Diff(_target, EffectiveLive(), _minRollPct);
        UpdateBuildNamePlaceholder();
        OverallPct.Text = r.Pct + "%";

        OverallCount.Text = $"{r.Matched} / {r.Total} met  ·  {_live.Gear.Count} equipped items"
            + (r.Under > 0 ? $"  ·  ⚠ {r.Under} under-rolled" : "");
        OverallBar.Value = r.Pct;

        if (_rawView)
        {
            Body.Children.Clear();
            Body.Children.Add(RawView());
            Status.Text = $"build details  ·  target: {Path.GetFileName(_targetPath)}";
            return;
        }

        if (_stepsView)
        {
            Body.Children.Clear();
            Body.Children.Add(NextStepsView(r));
            Status.Text = $"next steps  ·  target: {Path.GetFileName(_targetPath)}";
            return;
        }

        // build sections: one per gear slot + one per non-gear category.
        // gear slots get a unique key by index (several slots share the label "Weapon",
        // so keying by label would select/highlight all of them at once), and any
        // duplicated label is numbered — "Weapon 1", "Weapon 2", "Weapon 3".
        var sections = new List<Section>();
        var gearGroups = r.Categories.FirstOrDefault(c => c.Id == "gear")?.Groups ?? new List<Group>();
        // base label per gear: weapon slots get a type name (Crossbow / Sword / Dagger / Bow …) from the build
        // item id so each weapon — including the bow — is its own distinct slot, not a generic "Weapon".
        string BaseLabel(int gi)
        {
            var g = gearGroups[gi];
            if (SlotKey(g.Name) == "weapon" && _target != null && gi < _target.Gear.Count
                && WeaponTypeLabel(_target.Gear[gi].ItemId) is string wl) return wl;
            return g.Name;
        }
        var baseLabels = Enumerable.Range(0, gearGroups.Count).Select(BaseLabel).ToList();
        var dupLabels = baseLabels.GroupBy(x => x).Where(grp => grp.Count() > 1).Select(grp => grp.Key).ToHashSet();
        var seen = new Dictionary<string, int>();
        for (int gi = 0; gi < gearGroups.Count; gi++)
        {
            var g = gearGroups[gi];
            string label = baseLabels[gi];
            if (dupLabels.Contains(label)) { int n = seen.GetValueOrDefault(label) + 1; seen[label] = n; label = $"{label} {n}"; }
            sections.Add(new Section { Key = "gear:" + gi, Label = label, Matched = g.Matched, Total = g.Total, Under = g.Under, Gear = g });
        }

        // give the doll a tile for every target unique that doesn't have its own gear section.
        // Key by unique name (not slot key) so multi-weapon builds (crossbow + sword + unique dagger)
        // each get their own tile — we don't block by slot key since a slot can hold several weapons.
        var synthesizedUniques = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var liveNow = EffectiveLive();
        // Track which live items were already assigned to gear sections by DiffEngine so the unique
        // section fallback doesn't show the same weapon for both gear:N and the unique tile.
        var assignedLiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sec in sections)
            if (sec.Gear != null)
                foreach (var li in sec.Gear.LiveItems) assignedLiveNames.Add(li.Name);
        foreach (var u in _target?.Uniques ?? new())
        {
            var sk = SlotKey(u.Slot ?? "");
            if (sk.Length == 0 || !synthesizedUniques.Add(u.Name)) continue;   // skip if no slot or already added
            string slotLabel = string.IsNullOrEmpty(u.Slot) ? u.Name : char.ToUpperInvariant(u.Slot![0]) + u.Slot[1..];
            var eq = liveNow.Gear.FirstOrDefault(it => DiffEngine.PhraseMatch(u.Name, it.Name))   // prefer exact unique match
                  ?? liveNow.Gear.FirstOrDefault(it => DiffEngine.SlotBaseName(it.Slot) == DiffEngine.SlotBaseName(u.Slot ?? "")
                         && !assignedLiveNames.Contains(it.Name)     // don't re-use a weapon already shown on a gear slot
                         && !synthesizedUniques.Any(n => !n.Equals(u.Name, StringComparison.OrdinalIgnoreCase) && DiffEngine.PhraseMatch(n, it.Name)));
            bool have = liveNow.Gear.Any(it => DiffEngine.PhraseMatch(u.Name, it.Name));
            var grp = new Group { Name = slotLabel, Kind = "gear", Total = 1, Matched = have ? 1 : 0 };
            if (eq != null) grp.LiveItems.Add(new GearLiveItem { Name = eq.Name, Rarity = eq.Rarity, ItemPower = eq.ItemPower, IsUnique = eq.IsUnique, IsAncestral = eq.IsAncestral, Aspect = eq.Aspect });
            sections.Add(new Section { Key = "uni:" + DiffEngine.Normalize(u.Name), Label = slotLabel, Matched = have ? 1 : 0, Total = 1, Gear = grp });
        }

        foreach (var c in r.Categories)
            if (c.Id != "gear" && c.Id != "skills" && c.Id != "paragon" && c.Id != "mercenary")
                sections.Add(new Section { Key = "cat:" + c.Id, Label = ShortName(c.Id), Matched = c.Matched, Total = c.Total, Under = c.Under, Cat = c });

        // keep selection if still present, else default to the first thing needing work
        if (_selectedKey == null || sections.All(s => s.Key != _selectedKey))
            _selectedKey = (sections.FirstOrDefault(s => s.Status != "met") ?? sections.FirstOrDefault())?.Key;

        Body.Children.Clear();
        if (_shimNeedsUpgrade) Body.Children.Add(UpgradeBanner());
        Body.Children.Add(SummaryStrip(r));
        Body.Children.Add(IconLegend());

        // quick compare actions: pin every slot that still needs work, or clear what's pinned
        var gapKeys = sections.Where(s => s.Gear != null && s.Status != "met").Select(s => s.Key).ToList();
        if (gapKeys.Count > 0 || _pinned.Count > 0)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 13) };
            if (gapKeys.Count > 0)
            {
                var all = MakeLink($"⊞  Compare all {gapKeys.Count} gaps", Steel);
                all.MouseLeftButtonUp += (_, _) => { _pinned.Clear(); foreach (var k in gapKeys) _pinned.Add(k); Render(); Toast($"Pinned {gapKeys.Count} gaps"); };
                actions.Children.Add(all);
            }
            if (_pinned.Count > 0)
            {
                var clr = MakeLink("✕  Clear pins", Soft); clr.Margin = new Thickness(18, 0, 0, 0);
                clr.MouseLeftButtonUp += (_, _) => { _pinned.Clear(); Render(); Toast("Cleared pins"); };
                actions.Children.Add(clr);
            }
            Body.Children.Add(actions);
        }

        // glanceable layout: the paper doll (visual anchor) beside the guidance rail (do-this-next + activities)
        // so the core signal sits above the fold. On narrow windows the two columns stack instead.
        if (ActualWidth > 50) _narrow = ActualWidth < TwoColMin;
        var doll = PaperDoll(sections, r.TargetClass, r.Pct);
        ((FrameworkElement)doll).VerticalAlignment = VerticalAlignment.Top;
        var rail = new StackPanel();
        var guide = GuidancePanel(r); if (guide != null) rail.Children.Add(guide);
        var acts = ActivitiesPanel(r); if (acts != null) rail.Children.Add(acts);
        var exp = MakeLink("⤓  Export loot filter", Steel); exp.Margin = new Thickness(2, 2, 0, 0);
        exp.MouseLeftButtonUp += (_, _) => ExportLootFilter();
        rail.Children.Add(exp);

        if (_narrow)
        {
            ((FrameworkElement)doll).HorizontalAlignment = HorizontalAlignment.Center;
            rail.Margin = new Thickness(0, 14, 0, 0);
            var stack = new StackPanel();
            stack.Children.Add(doll); stack.Children.Add(rail);
            Body.Children.Add(stack);
        }
        else
        {
            rail.Margin = new Thickness(22, 0, 0, 0); rail.MinWidth = 320;
            var cols = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // doll: natural width
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // rail: fills the rest
            Grid.SetColumn(doll, 0); cols.Children.Add(doll);
            Grid.SetColumn(rail, 1); cols.Children.Add(rail);
            Body.Children.Add(cols);
        }

        // compare deck (in the space freed by the two-column layout): pinned slots, each a FULL side-by-side
        // compare. A header lets you clear all at once; each panel can be unpinned or focused.
        var pinnedSecs = _pinned.ToList()
            .Select(k => sections.FirstOrDefault(x => x.Key == k && x.Gear != null)
                      ?? (_inventorySections.TryGetValue(k, out var inv) ? inv : null))
            .Where(p => p != null).Cast<Section>().ToList();
        if (pinnedSecs.Count > 0)
        {
            var deckHdr = new DockPanel { Margin = new Thickness(0, 8, 0, 9) };
            var clearAll = MakeLink("✕  clear all", Soft); clearAll.HorizontalAlignment = HorizontalAlignment.Right;
            clearAll.MouseLeftButtonUp += (_, _) => { _pinned.Clear(); Render(); Toast("Cleared pins"); };
            DockPanel.SetDock(clearAll, Dock.Right); deckHdr.Children.Add(clearAll);
            deckHdr.Children.Add(TBs($"COMPARING  ·  {pinnedSecs.Count} pinned", Gold, 13, true));
            Body.Children.Add(deckHdr);

            foreach (var p in pinnedSecs)
            {
                var bar = new DockPanel { Margin = new Thickness(0, 4, 2, 2) };
                var keyC = p.Key;
                var unpin = MakeLink("✕ unpin", Soft); unpin.HorizontalAlignment = HorizontalAlignment.Right;
                unpin.MouseLeftButtonUp += (_, _) => { _pinned.Remove(keyC); Render(); };
                DockPanel.SetDock(unpin, Dock.Right); bar.Children.Add(unpin);
                if (keyC.StartsWith("gear:"))   // focus only works for affix-gear sections (uniques have no step key)
                {
                    var focus = MakeLink("◎ focus", Steel); focus.HorizontalAlignment = HorizontalAlignment.Right; focus.Margin = new Thickness(0, 0, 16, 0);
                    focus.MouseLeftButtonUp += (_, _) => { _focusKey = keyC; _selectedKey = keyC; Render(); };
                    DockPanel.SetDock(focus, Dock.Right); bar.Children.Add(focus);
                }
                bar.Children.Add(TBs(p.Label, Soft, 11.5, true));
                Body.Children.Add(bar);
                Body.Children.Add(DetailPanel(p));
            }
        }
        // a selected category (Uniques/Skills/Paragon) shows its detail
        var selCat = sections.FirstOrDefault(x => x.Key == _selectedKey && x.Cat != null);
        if (selCat != null) Body.Children.Add(DetailPanel(selCat));

        int gearCount = _live.Gear.Count;
        string ago = "";
        long latestTick = _live.Gear.Count > 0 ? _live.Gear.Max(g => g.LastScannedTicks) : 0;
        if (latestTick > 0)
        {
            var since = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - latestTick);
            ago = since.TotalSeconds < 120 ? $" · last scan {(int)since.TotalSeconds}s ago"
                : since.TotalMinutes < 60  ? $" · last scan {(int)since.TotalMinutes}m ago"
                : $" · last scan {since.TotalHours:0.0}h ago";
        }
        string src = _useTts && _useCapture ? "TTS+OCR" : _useTts ? "TTS" : _useCapture ? "OCR" : "offline";
        Status.Text = $"● live  ·  {gearCount} items  ·  {src}{ago}";
        StatusDetail.Text = $"{Updater.RunningVersion()}  ·  {Path.GetFileName(_log)}";
    }

    // shown when the TTS capture shim isn't installed — one-click install into the Diablo IV folder
    UIElement CaptureBanner()
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var btn = new Button { Content = "Install capture DLL", Style = (Style)FindResource("Primary"), Padding = new Thickness(18, 7, 18, 7), VerticalAlignment = VerticalAlignment.Center };
        btn.Click += (_, _) => RunInstall(btn);
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

    UIElement UpgradeBanner()
    {
        int installedVer = CaptureSetup.InstalledShimVersion();
        string oldLabel = installedVer <= 0 ? "an older version" : $"v{installedVer}";

        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var btn = new Button { Content = "Update capture DLL", Style = (Style)FindResource("Primary"), Padding = new Thickness(18, 7, 18, 7), VerticalAlignment = VerticalAlignment.Center };
        btn.Click += (_, _) => RunInstall(btn);
        DockPanel.SetDock(btn, Dock.Right); dp.Children.Add(btn);

        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(TBs("Capture DLL update available", Amber, 13.5, true));
        txt.Children.Add(TB($"You have {oldLabel} installed. Update to v{CaptureSetup.CurrentShimVersion} to get timestamps, deduplication, and better session tracking.",
            Soft, 12, false, new Thickness(0, 2, 0, 0)));
        dp.Children.Add(txt);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xE0, 0xA5, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE0, 0xA5, 0x2E)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12), Child = dp,
        };
    }

    // the reconstructed Maxroll URL of the build we're comparing against (null if unknown)
    string? SourceUrl()
    {
        var u = MaxrollImporter.BuildUrl(_lastImportInput) ?? MaxrollImporter.BuildUrl(_lastUrl);
        if (u != null) return u;
        var src = _target?.Source;   // a switched build carries its planner id in Source
        return src != null && src != "maxroll" ? MaxrollImporter.BuildUrl(src) : null;
    }

    void OpenSource()
    {
        var url = SourceUrl();
        if (url == null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // export the target as a loot filter: a readable markdown checklist + a Diablo4Companion-shaped JSON
    void ExportLootFilter()
    {
        if (_target == null) return;
        var safe = new string((_target.Name ?? "build").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var dlg = new SaveFileDialog { FileName = safe + "-loot-filter.md", Filter = "Markdown|*.md|All files|*.*", Title = "Export loot filter" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, LootFilter.Markdown(_target));
            var jsonPath = Path.ChangeExtension(dlg.FileName, ".companion.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(LootFilter.CompanionPreset(_target), new JsonSerializerOptions { WriteIndented = true }));
            Status.Text = "exported: " + dlg.FileName;
            Toast("Loot filter exported");
        }
        catch (Exception e) { MessageBox.Show("Export failed: " + e.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // shared install action for the banner button and the footer button
    void RunInstall(System.Windows.Controls.Control? btn = null)
    {
        // if the game can't be auto-detected, ask the user to locate Diablo IV.exe before installing
        if (CaptureSetup.GameDir() == null)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Locate Diablo IV.exe",
                Filter = "Diablo IV|Diablo IV.exe|All executables|*.exe",
                FileName = "Diablo IV.exe",
            };
            if (dlg.ShowDialog() != true) return;
            var dir = Path.GetDirectoryName(dlg.FileName);
            if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "Diablo IV.exe")))
            {
                MessageBox.Show("That doesn't look like the Diablo IV folder — please select Diablo IV.exe inside the game's install folder.",
                    "Wrong file", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CaptureSetup.UserGameDir = dir;
            GameDataIcons.GameDir = dir;
            SaveSettings();   // persist so the next launch remembers it
        }

        object? prevContent = null;
        if (btn is System.Windows.Controls.ContentControl cc)
            { btn.IsEnabled = false; prevContent = cc.Content; cc.Content = "installing…"; }
        else if (btn != null)
            btn.IsEnabled = false;
        var (ok, msg) = CaptureSetup.Install();
        MessageBox.Show(msg, ok ? "Capture set up" : "Couldn't set up capture",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (btn is System.Windows.Controls.ContentControl cc2)
            { btn.IsEnabled = true; cc2.Content = prevContent; }
        else if (btn != null)
            btn.IsEnabled = true;
        if (ok) { _useTts = true; _shimNeedsUpgrade = false; SaveSettings(); }
        Render();
    }

    // transient, non-blocking confirmation (bottom-center, auto-fades). Use instead of MessageBox for routine success.
    string AppLogPath => Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "d4scanner_app.log");

    void AppLog(string msg)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
            File.AppendAllText(AppLogPath, line + Environment.NewLine);
            // Trim to last 2000 lines to prevent unbounded growth
            var path = AppLogPath;
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length > 2500) File.WriteAllLines(path, lines[^2000..]);
            }
            catch { }
        }
        catch { }
    }

    void Toast(string msg)
    {
        var card = new Border
        {
            Background = B("#20222A"), BorderBrush = EdgeHi, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(18, 9, 18, 10), Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0, Child = TB(msg, Ink, 13, true),
        };
        ToastHost.Children.Add(card);
        card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
            fade.Completed += (_, _) => { if (ToastHost.Children.Contains(card)) ToastHost.Children.Remove(card); };
            card.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    // character portrait: look for character.png / character.jpg in the d4scanner dir so the user can
    // place a screenshot there and have it appear in the doll center behind their class emblem.
    Image? LoadCharacterImage()
    {
        var dir = Path.GetDirectoryName(TargetLoader.DefaultLogPath()) ?? "";
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var p = Path.Combine(dir, "character" + ext);
            if (!File.Exists(p)) continue;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri(p); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                return new Image { Source = bi };
            }
            catch { }
        }
        return null;
    }

    // a small clickable text link (caller wires MouseLeftButtonUp)
    TextBlock MakeLink(string text, Brush color)
    {
        var t = TB(text, color, 12, false);
        t.Cursor = System.Windows.Input.Cursors.Hand;
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    // body zoom (Ctrl +/-/0): a LayoutTransform on the scrollable body, so the header/status stay fixed
    void ApplyZoom() => Body.LayoutTransform = _uiScale == 1.0 ? System.Windows.Media.Transform.Identity : new ScaleTransform(_uiScale, _uiScale);
    void Zoom(double delta)
    {
        var z = Math.Clamp(Math.Round(_uiScale + delta, 2), 0.7, 1.6);
        if (Math.Abs(z - _uiScale) < 0.001) return;
        _uiScale = z; ApplyZoom(); SaveSettings();
        Toast($"Zoom  {(int)Math.Round(_uiScale * 100)}%");
    }

    // ---- auto-updater ----

    async void CheckForUpdatesAsync()
    {
        var latest = await Updater.GetLatestTagAsync();
        if (latest == null) return;
        if (!Updater.IsNewer(latest, Updater.RunningVersion())) return;   // already current
        if (latest == _skipUpdateVersion) return;                         // user skipped this version

        // Already staged from a prior check?
        if (Updater.FindStagedUpdate().HasValue) { ShowUpdateReady(latest); return; }

        // Download in background; ~70 MB — show Toast when done
        bool ok = await Task.Run(() => Updater.DownloadUpdateAsync(latest));
        if (ok) Dispatcher.Invoke(() => ShowUpdateReady(latest));
    }

    void ShowUpdateReady(string tag)
    {
        _pendingUpdateTag = tag;
        CheckUpdatesBtn.Content = $"↑ Update {tag}";
        CheckUpdatesBtn.Style = (Style)FindResource("Primary");
        AppLog($"update {tag} staged and ready to install");
        Toast($"{tag} ready — click Update in the status bar to install");
    }

    // ── Markdown renderer ──────────────────────────────────────────────────────
    UIElement RenderMarkdown(string md)
    {
        var sp = new StackPanel();
        foreach (var raw in md.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) { sp.Children.Add(new Border { Height = 5 }); continue; }
            // skip auto-generated "Full Changelog" link lines
            if (line.StartsWith("**Full Changelog")) continue;

            string text = line;
            double size = 12.5; bool bold = false; Brush col = Ink;
            var margin = new Thickness(0, 2, 0, 2);

            if (line.StartsWith("### ")) { text = line[4..]; size = 13;   bold = true; col = Ink;  margin = new Thickness(0, 8, 0, 2); }
            else if (line.StartsWith("## "))  { text = line[3..]; size = 14;   bold = true; col = Gold; margin = new Thickness(0, 10, 0, 3); }
            else if (line.StartsWith("# "))   { text = line[2..]; size = 14.5; bold = true; col = Gold; margin = new Thickness(0, 10, 0, 4); }
            else if (line.StartsWith("- ") || line.StartsWith("* ")) { text = "•  " + line[2..]; col = Ink; margin = new Thickness(10, 1, 0, 1); }
            else if (line.StartsWith("> "))   { text = line[2..]; col = Soft; margin = new Thickness(14, 1, 0, 1); }

            // strip inline **bold** and `code` markers for plain rendering
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`",       "$1");

            var tb = new TextBlock { Text = text, Foreground = col, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin };
            if (bold) tb.FontWeight = FontWeights.SemiBold;
            sp.Children.Add(tb);
        }
        return sp;
    }

    // ── Update modal (state-machine) ───────────────────────────────────────────
    async void ShowUpdateModal(string? knownTag = null)
    {
        if (_updateModalOpen) return;
        _updateModalOpen = true;

        var host = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)) };
        void CloseModal() { _updateModalOpen = false; RootLayer.Children.Remove(host); }
        host.MouseLeftButtonDown += (_, e) => { if (e.Source == host) CloseModal(); };
        RootLayer.Children.Add(host);

        // Outer container — no fixed Width, just min/max so it breathes
        var wa = SystemParameters.WorkArea;
        double panelW = Math.Min(780, wa.Width * 0.58);

        var sp = new StackPanel();

        // Title bar with close button
        var hd = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var xBtn = new Border
        {
            Child = TB("✕", Soft, 14, false), Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4), Cursor = System.Windows.Input.Cursors.Hand,
        };
        xBtn.MouseLeftButtonUp += (_, _) => CloseModal();
        xBtn.MouseEnter += (_, _) => xBtn.Background = B("#2A2430");
        xBtn.MouseLeave += (_, _) => xBtn.Background = System.Windows.Media.Brushes.Transparent;
        DockPanel.SetDock(xBtn, Dock.Right); hd.Children.Add(xBtn);
        var titleSp = new StackPanel { Orientation = Orientation.Horizontal };
        titleSp.Children.Add(TB("◆ ", Gold, 16, true));
        titleSp.Children.Add(TBs("D4Scanner Update", Ink, 17, true));
        hd.Children.Add(titleSp);
        sp.Children.Add(hd);

        var body = new StackPanel();  // swapped between states
        sp.Children.Add(body);

        var scroll = new ScrollViewer
        {
            Content = sp,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = wa.Height * 0.85,
        };
        host.Children.Add(new Border
        {
            Background = B("#1A1921"),
            BorderBrush = EdgeHi, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(32, 28, 32, 30),
            Width = panelW,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = scroll,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7,
            },
        });

        // ── helpers ──
        void SetBody(UIElement el) { body.Children.Clear(); body.Children.Add(el); }

        // Version pill: "v0.9.0  →  v0.9.1"
        UIElement VersionRow(string from, string to) =>
            new Border
            {
                Background = B("#0E0D12"), BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(18, 10, 18, 10),
                Margin = new Thickness(0, 0, 0, 20),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = {
                        TB(from, Soft, 14, false),
                        TB("   →   ", Faint, 14, false),
                        TB(to, Gold, 14, true),
                    }
                },
            };

        // Styled notes area
        UIElement NotesBlock(string mdText) => new Border
        {
            Background = B("#0E0D12"), BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 22),
            Child = new ScrollViewer
            {
                Content = RenderMarkdown(mdText),
                MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };

        // Button row helper
        (Button primary, Button secondary) BtnRow(string primaryLabel, string secondaryLabel)
        {
            var p = new Button { Content = primaryLabel, Style = (Style)FindResource("Primary"), Padding = new Thickness(24, 10, 24, 10), FontSize = 14 };
            var s = new Button { Content = secondaryLabel, Padding = new Thickness(18, 10, 18, 10), FontSize = 14 };
            return (p, s);
        }

        // ── Checking ──
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8),
            Children = { TB("Checking for updates", Soft, 13.5, false) }
        });

        var running = Updater.RunningVersion();
        var info = await Updater.GetLatestReleaseInfoAsync();
        string? tag = knownTag ?? info?.tag;
        string notes = string.IsNullOrWhiteSpace(info?.body) ? "" : info!.Value.body;

        // ── Up to date ──
        if (tag == null || !Updater.IsNewer(tag, running))
        {
            var upSp = new StackPanel();
            upSp.Children.Add(new Border
            {
                Background = B("#0E1A10"), BorderBrush = B("#2E4F33"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 20),
                Child = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { TB("✓  ", Green, 15, true), TB($"You're on the latest version  ({running})", Green, 14, false) } },
            });
            if (!string.IsNullOrEmpty(notes)) upSp.Children.Add(NotesBlock(notes));
            var ok = new Button { Content = "Close", Padding = new Thickness(20, 10, 20, 10), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 14 };
            ok.Click += (_, _) => CloseModal();
            upSp.Children.Add(ok);
            SetBody(upSp);
            return;
        }

        // ── Available / Ready-to-install ──
        bool alreadyStaged = Updater.FindStagedUpdate().HasValue;

        void ShowReadyState()
        {
            var rdSp = new StackPanel();
            rdSp.Children.Add(VersionRow(running, tag!));
            if (!string.IsNullOrEmpty(notes)) rdSp.Children.Add(NotesBlock(notes));
            rdSp.Children.Add(new Border
            {
                Background = B("#0E1A10"), BorderBrush = B("#2E4F33"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 20),
                Child = TB("✓  Downloaded and ready to install", Green, 13, false),
            });
            var (instBtn, laterBtn) = BtnRow("Install & Restart", "Later");
            laterBtn.Click += (_, _) => CloseModal();
            instBtn.Click += (_, _) =>
            {
                var spinners = new[] { "⠋","⠙","⠹","⠸","⠼","⠴","⠦","⠧","⠇","⠏" };
                var spinLbl = TBs("⠋  Applying update and restarting…", Ink, 14, false, new Thickness(0, 12, 0, 12));
                spinLbl.HorizontalAlignment = HorizontalAlignment.Center;
                SetBody(spinLbl);
                int frame = 0;
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                t.Tick += (_, _) => { frame = (frame + 1) % spinners.Length; spinLbl.Text = $"{spinners[frame]}  Applying update and restarting…"; };
                t.Start();
                Task.Delay(1400).ContinueWith(_ => Dispatcher.Invoke(() => { t.Stop(); RestartToApplyUpdate(); }));
            };
            var rdBtns = new DockPanel();
            DockPanel.SetDock(laterBtn, Dock.Left); rdBtns.Children.Add(laterBtn);
            DockPanel.SetDock(instBtn, Dock.Right); rdBtns.Children.Add(instBtn);
            rdSp.Children.Add(rdBtns);
            SetBody(rdSp);
        }

        if (alreadyStaged) { ShowReadyState(); return; }

        // ── Available: show notes + Download button ──
        var avSp = new StackPanel();
        avSp.Children.Add(VersionRow(running, tag!));
        if (!string.IsNullOrEmpty(notes)) avSp.Children.Add(NotesBlock(notes));
        var (dlBtn, notNowBtn) = BtnRow("Download ↓", "Not now");
        notNowBtn.Click += (_, _) => CloseModal();
        var avBtns = new DockPanel();
        DockPanel.SetDock(notNowBtn, Dock.Left); avBtns.Children.Add(notNowBtn);
        DockPanel.SetDock(dlBtn, Dock.Right); avBtns.Children.Add(dlBtn);
        avSp.Children.Add(avBtns);
        SetBody(avSp);

        var dlTcs = new TaskCompletionSource();
        dlBtn.Click += (_, _) => dlTcs.TrySetResult();
        await dlTcs.Task;

        // ── Downloading ──
        var dlSp = new StackPanel();
        dlSp.Children.Add(TB("Downloading…", Soft, 13, false, new Thickness(0, 0, 0, 14)));
        var bar = new ProgressBar { Height = 12, Minimum = 0, Maximum = 100, Value = 0, Margin = new Thickness(0, 0, 0, 8) };
        var pctRow = new DockPanel();
        var pctLbl = TB("0 %", Faint, 12, false); pctLbl.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(pctLbl, Dock.Right); pctRow.Children.Add(pctLbl);
        pctRow.Children.Add(TB("", Faint, 12, false));
        dlSp.Children.Add(bar); dlSp.Children.Add(pctRow);
        SetBody(dlSp);

        var prog = new Progress<double>(p => Dispatcher.Invoke(() => { bar.Value = p; pctLbl.Text = $"{(int)p} %"; }));
        bool ok2 = await Task.Run(() => Updater.DownloadUpdateAsync(tag, prog));

        if (ok2) { ShowUpdateReady(tag!); ShowReadyState(); }
        else
        {
            var errSp = new StackPanel();
            errSp.Children.Add(new Border
            {
                Background = B("#1A0D0D"), BorderBrush = B("#5C2020"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 20),
                Child = TB("Download failed — check your connection and try again.", B("#E07070"), 13, false),
            });
            var closeBtn = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 10, 20, 10), FontSize = 14 };
            closeBtn.Click += (_, _) => CloseModal();
            errSp.Children.Add(closeBtn);
            SetBody(errSp);
        }
    }

    void RestartToApplyUpdate()
    {
        // The staged update will be applied on the next launch by App.xaml.cs.
        // Restart immediately; the startup code handles the swap and old-file cleanup.
        var exe = System.Environment.ProcessPath
               ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    void GoOverview() { _stepsView = false; _rawView = false; RawBtn.Content = "Build details"; Render(); }

    // keyboard-shortcut cheatsheet: a centered overlay over a dimmed backdrop, toggled by "?" / F1 / button
    void ToggleHelp()
    {
        if (HelpHost.Visibility == Visibility.Visible) { HelpHost.Visibility = Visibility.Collapsed; return; }
        HelpHost.Children.Clear();
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB4, 0, 0, 0)) };
        backdrop.MouseLeftButtonDown += (_, _) => HelpHost.Visibility = Visibility.Collapsed;
        HelpHost.Children.Add(backdrop);

        var sp = new StackPanel();
        var hd = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var x = MakeLink("✕", Soft); x.FontSize = 15; x.HorizontalAlignment = HorizontalAlignment.Right;
        x.MouseLeftButtonUp += (_, _) => HelpHost.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(x, Dock.Right); hd.Children.Add(x);
        hd.Children.Add(TBs("Keyboard shortcuts", Gold, 16, true));
        sp.Children.Add(hd);

        void Row(string keys, string desc)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var kb = new Border { Background = B("#0C0C0F"), BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 2, 9, 3), MinWidth = 132, VerticalAlignment = VerticalAlignment.Top, Child = TB(keys, Ink, 12.5, true) };
            DockPanel.SetDock(kb, Dock.Left); kb.Margin = new Thickness(0, 0, 14, 0); row.Children.Add(kb);
            var d = TB(desc, Soft, 12.5, false); d.VerticalAlignment = VerticalAlignment.Center; d.TextWrapping = TextWrapping.Wrap; row.Children.Add(d);
            sp.Children.Add(row);
        }
        Row("Alt + O", "Overview");
        Row("Alt + N", "Next Steps");
        Row("Alt + B", "Build details");
        Row("Alt + I", "Item inventory (all scanned items)");
        Row("/", "Jump to the build search box");
        Row("Ctrl  + / − / 0", "Zoom in · out · reset");
        Row("Esc", "Close popup → clear focus → clear pins → back to overview");
        Row("? · F1", "Show / hide this list");

        var panel = new Border
        {
            Background = Card, BorderBrush = EdgeHi, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 20, 24, 22), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = sp,
        };
        HelpHost.Children.Add(panel);
        HelpHost.Visibility = Visibility.Visible;
    }

    void ShowSettings()
    {
        if (SettingsHost.Visibility == Visibility.Visible) { SettingsHost.Visibility = Visibility.Collapsed; return; }
        SettingsHost.Children.Clear();
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) };
        backdrop.MouseLeftButtonDown += (_, _) => SettingsHost.Visibility = Visibility.Collapsed;
        SettingsHost.Children.Add(backdrop);

        void Close() => SettingsHost.Visibility = Visibility.Collapsed;

        // ── content StackPanel — no fixed Width; the outer Border controls sizing ─
        var sp = new StackPanel();

        var hd = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        var xb = new Border
        {
            Child = TB("✕", Soft, 14, false), Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4), Cursor = System.Windows.Input.Cursors.Hand,
        };
        xb.MouseLeftButtonUp += (_, _) => Close();
        xb.MouseEnter += (_, _) => xb.Background = B("#2A2430");
        xb.MouseLeave += (_, _) => xb.Background = System.Windows.Media.Brushes.Transparent;
        DockPanel.SetDock(xb, Dock.Right); hd.Children.Add(xb);
        var titleSp2 = new StackPanel { Orientation = Orientation.Horizontal };
        titleSp2.Children.Add(TB("⚙ ", Gold, 16, true));
        titleSp2.Children.Add(TBs("Settings", Ink, 17, true));
        hd.Children.Add(titleSp2);
        sp.Children.Add(hd);

        // ── helpers ──────────────────────────────────────────────────────────
        void Section(string title) =>
            sp.Children.Add(new Border
            {
                BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 6, 0, 14), Padding = new Thickness(0, 0, 0, 4),
                Child = TB(title, Faint, 10, true),
            });

        void ToggleRow(string label, string desc, bool isChecked, Action<bool> onChange, UIElement? extra = null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var chk = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
            chk.Checked   += (_, _) => onChange(true);
            chk.Unchecked += (_, _) => onChange(false);
            DockPanel.SetDock(chk, Dock.Left); row.Children.Add(chk);
            var col = new StackPanel();
            if (extra != null) { var h = new DockPanel(); DockPanel.SetDock(extra, Dock.Right); h.Children.Add(extra); h.Children.Add(TBs(label, Ink, 13, true)); col.Children.Add(h); }
            else col.Children.Add(TBs(label, Ink, 13, true));
            var d = TB(desc, Soft, 11.5, false); d.TextWrapping = TextWrapping.Wrap; col.Children.Add(d);
            row.Children.Add(col); sp.Children.Add(row);
        }

        // ── CAPTURE section ───────────────────────────────────────────────────
        Section("CAPTURE");

        ToggleRow("Screen-reader (TTS)", "Reads gear tooltips via D4's accessibility output (most accurate). Requires the capture DLL and D4 Accessibility settings.",
            _useTts, on =>
            {
                if (on) { _useTts = true; SaveSettings(); if (!CaptureSetup.Installed()) RunInstall(null); else StartWatching(); }
                else
                {
                    var confirm = MessageBox.Show("Turning off TTS capture removes the DLL shim, its certificate, and PATH entry.\n\nContinue?",
                        "Remove TTS capture", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) { /* re-check won't fire since we're in lambda */ return; }
                    var (ok, msg) = CaptureSetup.Uninstall();
                    _useTts = false; SaveSettings(); StartWatching();
                    MessageBox.Show(msg, ok ? "TTS capture removed" : "TTS capture removal", MessageBoxButton.OK,
                        ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            });

        // OCR toggle with inline status feedback
        var ocrRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var ocrChk = new CheckBox { IsChecked = _useCapture, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        DockPanel.SetDock(ocrChk, Dock.Left); ocrRow.Children.Add(ocrChk);
        var ocrCol = new StackPanel();
        var ocrHdrRow = new DockPanel();
        var ocrStatusLbl = TB(_useCapture ? "active" : "", Faint, 11, false);
        ocrStatusLbl.VerticalAlignment = VerticalAlignment.Center; ocrStatusLbl.Margin = new Thickness(10, 0, 0, 0);
        var scanNowBtn = new Button { Content = "Scan now", Padding = new Thickness(10, 2, 10, 2), IsEnabled = _useCapture, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        scanNowBtn.Click += async (_, _) => { if (_captureEngine != null) { scanNowBtn.Content = "…"; await _captureEngine.ScanNowAsync(); scanNowBtn.Content = "Scan now"; } };
        DockPanel.SetDock(scanNowBtn, Dock.Right); ocrHdrRow.Children.Add(scanNowBtn);
        ocrHdrRow.Children.Add(TBs("Screen capture (OCR)", Ink, 13, true));
        ocrCol.Children.Add(ocrHdrRow);
        var ocrDescLbl = TB("Captures the game window via Windows OCR — free, no API key, no DLL. Works in borderless and exclusive fullscreen.", Soft, 11.5, false);
        ocrDescLbl.TextWrapping = TextWrapping.Wrap; ocrCol.Children.Add(ocrDescLbl);
        ocrCol.Children.Add(ocrStatusLbl);
        ocrRow.Children.Add(ocrCol);
        sp.Children.Add(ocrRow);
        ocrChk.Checked += (_, _) =>
        {
            _useCapture = true; scanNowBtn.IsEnabled = true;
            ocrStatusLbl.Text = "starting…"; ocrStatusLbl.Foreground = Soft;
            SaveSettings(); StartWatching();
            Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() =>
                ocrStatusLbl.Text = _captureEngine != null ? "active" : "inactive"));
        };
        ocrChk.Unchecked += (_, _) =>
        {
            _useCapture = false; scanNowBtn.IsEnabled = false;
            ocrStatusLbl.Text = ""; SaveSettings(); StartWatching();
        };

        // character portrait capture
        sp.Children.Add(TBs("Character portrait", Ink, 13, true, new Thickness(0, 0, 0, 4)));
        var portDesc = TB("Open your character panel in D4 (press C) then click Capture. The portrait appears behind the paper doll.", Soft, 11.5, false);
        portDesc.TextWrapping = TextWrapping.Wrap; portDesc.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(portDesc);
        var portRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var portStatus = TB("", Soft, 11.5, false); portStatus.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(portStatus, Dock.Left); portRow.Children.Add(portStatus);
        var portBtn = new Button { Content = "Capture", Style = (Style)FindResource("Primary"), Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Right };
        portBtn.Click += (_, _) =>
        {
            portBtn.IsEnabled = false; portBtn.Content = "…";
            var err = CaptureCharacterPortrait();
            portBtn.Content = err == null ? "✓" : "Capture";
            portStatus.Text = err ?? "Saved";
            portStatus.Foreground = err == null ? Green : Miss;
            portBtn.IsEnabled = true;
            if (err == null) Render();
        };
        portRow.Children.Add(portBtn); sp.Children.Add(portRow);

        // ── DISPLAY section ───────────────────────────────────────────────────
        Section("DISPLAY");

        // affix roll quality slider
        sp.Children.Add(TBs("Affix roll quality threshold", Ink, 13, true, new Thickness(0, 0, 0, 4)));
        var thrDesc = TB("Affixes whose roll falls below this % of their range are flagged ⚠ under-rolled.", Soft, 11.5, false);
        thrDesc.TextWrapping = TextWrapping.Wrap; thrDesc.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(thrDesc);
        var thrRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var thrLbl = TBs(((int)_minRollPct) + "%", Crimson, 13.5, true);
        thrLbl.VerticalAlignment = VerticalAlignment.Center; thrLbl.MinWidth = 38; thrLbl.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(thrLbl, Dock.Right); thrRow.Children.Add(thrLbl);
        var thrSlider = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 100, Value = _minRollPct, TickFrequency = 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        thrSlider.ValueChanged += (_, _) => { _minRollPct = thrSlider.Value; ThreshSlider.Value = _minRollPct; ThreshLbl.Text = thrLbl.Text = ((int)_minRollPct) + "%"; SaveSettings(); Render(); };
        thrRow.Children.Add(thrSlider); sp.Children.Add(thrRow);

        ToggleRow("Debug info", "Show last-scan time and slot key diagnostics on each paper-doll cell.", _debugMode, on => { _debugMode = on; SaveSettings(); Render(); });

        // ── LOG section ───────────────────────────────────────────────────────
        Section("LOG");
        var logRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var openLogBtn = new Button { Content = "Open log file", Padding = new Thickness(14, 6, 14, 6) };
        openLogBtn.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_log) { UseShellExecute = true }); } catch { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"/select,\"{_log}\"") { UseShellExecute = true }); } catch { } } };
        var openAppLogBtn = new Button { Content = "Open app log", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
        openAppLogBtn.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLogPath) { UseShellExecute = true }); } catch { } };
        DockPanel.SetDock(openLogBtn, Dock.Left); logRow.Children.Add(openLogBtn);
        DockPanel.SetDock(openAppLogBtn, Dock.Left); logRow.Children.Add(openAppLogBtn);
        sp.Children.Add(logRow);
        var diagRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var diagBtn = new Button { Content = "🩺 Diagnose capture", Style = (Style)FindResource("Primary"), Padding = new Thickness(14, 6, 14, 6) };
        diagBtn.Click += (_, _) => ShowTtsDiagnostics();
        DockPanel.SetDock(diagBtn, Dock.Left); diagRow.Children.Add(diagBtn);
        var diagHint = TB("See exactly what TTS parsed, how each item was classified, and why anything was dropped.", Faint, 11, false);
        diagHint.TextWrapping = TextWrapping.Wrap; diagHint.VerticalAlignment = VerticalAlignment.Center; diagHint.Margin = new Thickness(12, 0, 0, 0);
        diagRow.Children.Add(diagHint);
        sp.Children.Add(diagRow);
        var logPathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var logPathLbl = TB(System.IO.Path.GetFileName(_log), Faint, 11, false); logPathLbl.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(logPathLbl, Dock.Left); logPathRow.Children.Add(logPathLbl);
        var changeLogBtn = new Button { Content = "Change…", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12 };
        changeLogBtn.Click += (_, _) => { Close(); PickLog(); };
        logPathRow.Children.Add(changeLogBtn); sp.Children.Add(logPathRow);

        // ── CACHE section ─────────────────────────────────────────────────────
        Section("CACHE");
        var cacheDesc = TB("Clear cached data. Icons and build data re-download automatically on next use.", Soft, 11.5, false);
        cacheDesc.TextWrapping = TextWrapping.Wrap; cacheDesc.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(cacheDesc);

        var iconDir     = Path.Combine(IconResolver.CacheDir, "icons");
        var gameIconDir = Path.Combine(iconDir, "game");
        var liveJsonPath = LivePath;
        var cacheFiles = new (string label, string detail, Func<bool> exists, Action clear)[]
        {
            ("Game item icons",  $"{CountFiles(gameIconDir)} files — extracted from your D4 install",
                () => Directory.Exists(gameIconDir) && Directory.GetFiles(gameIconDir, "*.png").Length > 0,
                () => { try { foreach (var f in Directory.GetFiles(gameIconDir, "*.png")) File.Delete(f); } catch { } }),
            ("Build index",      "Maxroll guide list — re-fetched on next launch",
                () => File.Exists(IconResolver.IndexPath),
                () => { try { File.Delete(IconResolver.IndexPath); } catch { } }),
            ("Maxroll data",     "Planner item/affix data — re-fetched on next import",
                () => File.Exists(Path.Combine(IconResolver.CacheDir, "maxroll_data.min.json")),
                () => { try { File.Delete(Path.Combine(IconResolver.CacheDir, "maxroll_data.min.json")); } catch { } }),
            ("Live gear cache",  "Last-known equipped items — hover new items to rebuild from scratch",
                () => File.Exists(liveJsonPath) || _live.Gear.Count > 0,
                () =>
                {
                    // Persist the skip position so restarts also skip the old log data
                    try { if (File.Exists(_log)) _logSkipToPos = new FileInfo(_log).Length; } catch { }
                    try { File.Delete(liveJsonPath); } catch { }
                    _live = new();
                    SaveSettings();   // persist _logSkipToPos to app.json
                    Dispatcher.Invoke(StartWatching);
                }),
        };

        var checks = new CheckBox[cacheFiles.Length];
        for (int i = 0; i < cacheFiles.Length; i++)
        {
            var (label, detail, exists, _) = cacheFiles[i];
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var chk = checks[i] = new CheckBox { IsChecked = false, IsEnabled = exists(), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
            DockPanel.SetDock(chk, Dock.Left); row.Children.Add(chk);
            var col = new StackPanel();
            col.Children.Add(TB(label, exists() ? Ink : Faint, 13, true));
            col.Children.Add(TB(detail, Soft, 11, false));
            row.Children.Add(col); sp.Children.Add(row);
        }

        var cacheRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6) };
        cancelBtn.Click += (_, _) => Close();
        var clearBtn = new Button { Content = "Clear selected", Style = (Style)FindResource("Primary"), Padding = new Thickness(14, 6, 14, 6) };
        clearBtn.Click += (_, _) =>
        {
            for (int i = 0; i < cacheFiles.Length; i++) if (checks[i].IsChecked == true) cacheFiles[i].clear();
            Close(); Render();
            Toast("Cache cleared");
        };
        DockPanel.SetDock(cancelBtn, Dock.Right); cancelBtn.Margin = new Thickness(8, 0, 0, 0); cacheRow.Children.Add(cancelBtn);
        DockPanel.SetDock(clearBtn,  Dock.Right); cacheRow.Children.Add(clearBtn);
        sp.Children.Add(cacheRow);

        // ── panel + scrollviewer ──────────────────────────────────────────────
        var waS = SystemParameters.WorkArea;
        double settingsW = Math.Min(780, waS.Width * 0.62);
        var maxH = waS.Height * 0.84;
        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = maxH };
        var panel = new Border
        {
            Background = B("#1A1921"),
            BorderBrush = EdgeHi, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(32, 28, 32, 30),
            Width = settingsW,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = scroll,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7,
            },
        };
        SettingsHost.Children.Add(panel);
        SettingsHost.Visibility = Visibility.Visible;
    }

    static int CountFiles(string dir, string pat = "*") { try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pat, SearchOption.AllDirectories).Length : 0; } catch { return 0; } }

    // ---- TTS capture diagnostics ----
    // Re-runs the full parse → classify → dedup pipeline over the live log and shows every stage,
    // so "the log has data but items don't update" becomes a self-diagnosable, screenshot-free report.
    void ShowTtsDiagnostics()
    {
        SettingsHost.Children.Clear();
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) };
        backdrop.MouseLeftButtonDown += (_, _) => SettingsHost.Visibility = Visibility.Collapsed;
        SettingsHost.Children.Add(backdrop);
        void Close() => SettingsHost.Visibility = Visibility.Collapsed;

        var rep = LogWatcher.Diagnose(_log);
        var sp = new StackPanel();

        void Hdr(string title) => sp.Children.Add(new Border
        {
            BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 16, 0, 10), Padding = new Thickness(0, 0, 0, 4), Child = TB(title, Faint, 10, true),
        });
        static string Rel(DateTime utc)
        {
            var ts = DateTime.UtcNow - utc;
            if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s ago";
            if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m ago";
            if (ts.TotalHours   < 24) return $"{(int)ts.TotalHours}h ago";
            return $"{(int)ts.TotalDays}d ago";
        }

        // ── header ──
        var hd = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var xb = new Border { Child = TB("✕", Soft, 14, false), Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(4), Cursor = System.Windows.Input.Cursors.Hand };
        xb.MouseLeftButtonUp += (_, _) => Close();
        xb.MouseEnter += (_, _) => xb.Background = B("#2A2430");
        xb.MouseLeave += (_, _) => xb.Background = System.Windows.Media.Brushes.Transparent;
        DockPanel.SetDock(xb, Dock.Right); hd.Children.Add(xb);
        var titleSp = new StackPanel { Orientation = Orientation.Horizontal };
        titleSp.Children.Add(TB("🩺 ", Gold, 16, true));
        titleSp.Children.Add(TBs("TTS capture diagnostics", Ink, 17, true));
        hd.Children.Add(titleSp);
        sp.Children.Add(hd);

        // ── status ──
        bool watching = _useTts && _watcher != null;
        string rel = rep.LogExists ? Rel(rep.LastModifiedUtc) : "—";
        sp.Children.Add(TB((watching ? "● Watching" : "○ Not watching")
            + $"     last write {rel}     {rep.TotalLines:#,0} lines     {rep.SessionMarkers} session(s)",
            watching ? Green : Faint, 12, false));
        var pathLine = TB(rep.LogExists ? rep.LogPath : rep.LogPath + "   (not found)", Faint, 10.5, false);
        pathLine.TextWrapping = TextWrapping.Wrap; pathLine.Margin = new Thickness(0, 2, 0, 12);
        sp.Children.Add(pathLine);

        int eq = rep.Items.Count(i => i.Equipped);
        sp.Children.Add(TBs($"Parsed {rep.Items.Count}   →   {eq} equipped   →   {rep.FinalEquipped.Count} displayed", Ink, 13.5, true, new Thickness(0, 0, 0, 4)));
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        void Leg(Brush c, string t) { legend.Children.Add(TB("● ", c, 11, false)); legend.Children.Add(TB(t + "     ", Faint, 10.5, false)); }
        Leg(Green, "displayed"); Leg(Gold, "equipped · superseded"); Leg(Faint, "not equipped");
        sp.Children.Add(legend);

        // ── parsed items ──
        Hdr("PARSED ITEMS");
        if (rep.Items.Count == 0)
            sp.Children.Add(TB("No items parsed yet. Open D4's character panel (press C) and hover your gear.", Soft, 12, false));
        foreach (var it in rep.Items)
        {
            var dot = it.InFinal ? Green : it.Equipped ? Gold : Faint;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var bullet = TB("●", dot, 12, false); bullet.VerticalAlignment = VerticalAlignment.Top; bullet.Margin = new Thickness(0, 1, 8, 0);
            DockPanel.SetDock(bullet, Dock.Left); row.Children.Add(bullet);
            var col = new StackPanel();
            var l1 = new StackPanel { Orientation = Orientation.Horizontal };
            l1.Children.Add(TBs(it.Name.Length > 0 ? it.Name : "(unnamed)", Ink, 12.5, true));
            l1.Children.Add(TB($"   {it.Slot}" + (it.ItemPower != null ? $" · {it.ItemPower}" : "") + $" · {it.Affixes.Count} affix", Soft, 11, false));
            col.Children.Add(l1);
            string sub = $"panel={it.Panel ?? "—"} · {it.Context}";
            if (!it.InFinal && it.DropReason != null) sub += $"   ✕ {it.DropReason}";
            var l2 = TB(sub, it.InFinal ? Faint : Gold, 10.5, false); l2.TextWrapping = TextWrapping.Wrap;
            col.Children.Add(l2);
            row.Children.Add(col); sp.Children.Add(row);
        }

        // ── raw log tail ──
        Hdr("RAW LOG TAIL (timestamps stripped)");
        var tailText = string.Join("\n", rep.RawTail.Select(GearParser.Clean).Where(s => s.Length > 0).TakeLast(40));
        var tailBox = new TextBox
        {
            Text = tailText.Length > 0 ? tailText : "(empty)", IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"), FontSize = 10.5, Foreground = Soft,
            Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
            TextWrapping = TextWrapping.NoWrap, MaxHeight = 180, Padding = new Thickness(10, 8, 10, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        sp.Children.Add(new Border { Background = B("#12121A"), CornerRadius = new CornerRadius(6), Child = tailBox, Margin = new Thickness(0, 0, 0, 14) });

        // ── buttons ──
        var btnRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var closeBtn = new Button { Content = "Close", Padding = new Thickness(14, 6, 14, 6) };
        closeBtn.Click += (_, _) => Close();
        var refreshBtn = new Button { Content = "↻ Refresh", Style = (Style)FindResource("Primary"), Padding = new Thickness(14, 6, 14, 6) };
        refreshBtn.Click += (_, _) => ShowTtsDiagnostics();
        var openBtn = new Button { Content = "Open log file", Padding = new Thickness(14, 6, 14, 6) };
        openBtn.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_log) { UseShellExecute = true }); } catch { } };
        DockPanel.SetDock(closeBtn, Dock.Right); closeBtn.Margin = new Thickness(8, 0, 0, 0); btnRow.Children.Add(closeBtn);
        DockPanel.SetDock(refreshBtn, Dock.Right); btnRow.Children.Add(refreshBtn);
        DockPanel.SetDock(openBtn, Dock.Left); btnRow.Children.Add(openBtn);
        sp.Children.Add(btnRow);

        // ── panel + scroll ──
        var wa = SystemParameters.WorkArea;
        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = wa.Height * 0.86 };
        var panel = new Border
        {
            Background = B("#1A1921"), BorderBrush = EdgeHi, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(32, 28, 32, 30), Width = Math.Min(820, wa.Width * 0.66),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = scroll,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 },
        };
        SettingsHost.Children.Add(panel);
        SettingsHost.Visibility = Visibility.Visible;
    }

    // ---- Item Inventory Modal ----
    // Shows all scanned items (equipped + bag) with slot, name, age, and a delete button per entry.
    // Build a synthetic Section from a raw scanned Item so it can be hovered / pinned.
    Section ItemToSection(Item item)
    {
        var key = $"inv:{DiffEngine.Normalize(item.Name)}:{item.Slot}";
        var grp = new Group { Name = item.Slot ?? "item", Kind = "gear" };
        foreach (var aff in item.Affixes)
        {
            double? pct = null;
            if (aff.Min.HasValue && aff.Max.HasValue && aff.Max > aff.Min)
                pct = Math.Max(0, Math.Min(100, ((aff.Value ?? 0) - aff.Min.Value) / (aff.Max.Value - aff.Min.Value) * 100));
            string val = aff.Value.HasValue
                ? (aff.IsPercent ? $"+{aff.Value:0.#}%" : aff.IsMultiplier ? $"x{aff.Value:0.##}" : $"+{aff.Value:#,0.##}")
                : "";
            string need = aff.Min.HasValue ? $"≥ {aff.Min:#,0.##}" : pct.HasValue ? $"{pct:0}% roll" : "";
            grp.Items.Add(new ReqItem
            {
                Label = aff.Text, Val = val, Need = need,
                Done = true, Status = string.IsNullOrEmpty(need) ? "met" : (pct >= 75 ? "met" : "under"),
                RollPct = pct, Tempered = false,
            });
        }
        grp.Matched = grp.Total = grp.Items.Count;
        grp.LiveItems.Add(new GearLiveItem
        {
            Name = item.Name, Rarity = item.Rarity, ItemPower = item.ItemPower,
            IsUnique = item.IsUnique, IsAncestral = item.IsAncestral, Aspect = item.Aspect,
        });
        var sec = new Section
        {
            Key = key, Label = item.Slot ?? item.ItemType ?? "item",
            Gear = grp, Matched = grp.Matched, Total = grp.Total, Under = 0,
        };
        _inventorySections[key] = sec;
        return sec;
    }

    void ShowInventoryModal()
    {
        var overlay = new Grid { IsHitTestVisible = true };
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0xBB, 0, 0, 0)) };
        backdrop.MouseLeftButtonDown += (_, _) => { RootLayer.Children.Remove(overlay); _hoverPopup.IsOpen = false; };
        overlay.Children.Add(backdrop);

        void Close() { RootLayer.Children.Remove(overlay); _hoverPopup.IsOpen = false; }

        var live = EffectiveLive();
        var allItems = live.Gear.Concat(live.Inventory).ToList();
        var now = DateTime.UtcNow.Ticks;

        // ---- header ----
        var sp = new StackPanel { MinWidth = 700, MaxWidth = 1000 };
        var hd = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var xb = MakeLink("✕", Soft); xb.FontSize = 15; DockPanel.SetDock(xb, Dock.Right);
        xb.MouseLeftButtonUp += (_, _) => Close();
        hd.Children.Add(xb);
        hd.Children.Add(TBs($"All scanned items  ·  {allItems.Count}", Gold, 17, true));
        sp.Children.Add(hd);
        sp.Children.Add(TB("Hover for compare · click to pin · ✕ to delete stale entries", Soft, 12, false, new Thickness(0, 0, 0, 12)));

        // ---- item grid ----
        var scroll = new ScrollViewer { MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

        foreach (var item in allItems.OrderBy(i => i.Slot ?? "").ThenByDescending(i => i.ItemPower ?? 0).ThenBy(i => i.Name))
        {
            var sec = ItemToSection(item);
            var rcol = RarityBrush(item.Rarity);
            var rc = ((SolidColorBrush)rcol).Color;
            bool pinned = _pinned.Contains(sec.Key);
            bool eq = item.Equipped;

            // age string
            string age = item.LastScannedTicks > 0
                ? TimeSpan.FromTicks(now - item.LastScannedTicks) is var ts
                  ? ts.TotalSeconds < 120 ? $"{(int)ts.TotalSeconds}s ago"
                  : ts.TotalMinutes < 60  ? $"{(int)ts.TotalMinutes}m ago"
                  : $"{ts.TotalHours:0.0}h ago"
                  : "?"
                : "?";

            // portrait icon with rarity overlay (same as paper doll)
            var iconGrid = new Grid { Width = 54, Height = 76, Margin = new Thickness(0, 0, 10, 0) };
            iconGrid.Children.Add(new Border { Background = B("#080809"), CornerRadius = new CornerRadius(4) });
            var ovl = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0.1, 1) };
            ovl.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, rc.R, rc.G, rc.B), 0));
            ovl.GradientStops.Add(new GradientStop(Color.FromArgb(0x05, rc.R, rc.G, rc.B), 1));
            iconGrid.Children.Add(new Border { Background = ovl, CornerRadius = new CornerRadius(4) });
            iconGrid.Children.Add(new Border { BorderBrush = rcol, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(3.5), Margin = new Thickness(1) });
            if (IsAncestral(item.Rarity))
                iconGrid.Children.Add(new Border { BorderBrush = RAncestral, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2.5), Margin = new Thickness(3), Opacity = 0.5 });
            var art = SlotOrItemIcon(item.Name, SlotKey(item.Slot ?? ""), rcol, 42, 62);
            art.HorizontalAlignment = HorizontalAlignment.Center; art.VerticalAlignment = VerticalAlignment.Center;
            iconGrid.Children.Add(art);

            // item details
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 160 };
            var slotLbl = string.IsNullOrEmpty(item.Slot) ? "" : char.ToUpper(item.Slot[0]) + item.Slot[1..];
            details.Children.Add(TB(slotLbl + (eq ? "  ●" : "  ○"), Faint, 10, false));
            var nameBlock = TB(item.Name, rcol, 12.5, true); nameBlock.TextWrapping = TextWrapping.Wrap;
            details.Children.Add(nameBlock);
            if (item.ItemPower > 0)
                details.Children.Add(TB($"IP {item.ItemPower}" + (item.MasterworkRank > 0 ? $"  MW {item.MasterworkRank}" : ""), Soft, 10.5, false, new Thickness(0, 2, 0, 2)));
            // top 3 affixes
            foreach (var aff in item.Affixes.Take(3))
            {
                var av = aff.Value.HasValue ? (aff.IsPercent ? $"+{aff.Value:0.#}%" : $"+{aff.Value:#,0.##}") : "";
                details.Children.Add(TB($"{aff.Text}  {av}", Soft, 9.5, false));
            }
            if (item.Affixes.Count > 3) details.Children.Add(TB($"…+{item.Affixes.Count - 3} more", Faint, 9, false));
            details.Children.Add(TB(age, Faint, 9, false, new Thickness(0, 3, 0, 0)));

            // card container
            var dp = new DockPanel();
            DockPanel.SetDock(iconGrid, Dock.Left); dp.Children.Add(iconGrid); dp.Children.Add(details);

            // delete button overlay (top-right)
            var delGrid = new Grid();
            delGrid.Children.Add(dp);
            var delBtn = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0E, 0x0E, 0x11)),
                BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 2), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0), Child = TB("✕", Soft, 9.5, true), Visibility = Visibility.Collapsed,
            };
            delGrid.Children.Add(delBtn);
            var capturedItem = item; var capturedSec = sec;
            delBtn.MouseLeftButtonUp += (_, _) =>
            {
                _live.Gear.Remove(capturedItem); _live.Inventory.Remove(capturedItem);
                _pinned.Remove(capturedSec.Key); _inventorySections.Remove(capturedSec.Key);
                SaveLive(); Close(); Render(); ShowInventoryModal();
            };

            var card = new Border
            {
                Child = delGrid,
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(6), Width = 260,
                Background = pinned ? TileSel : Card,
                BorderBrush = pinned ? Gold : new SolidColorBrush(Color.FromArgb(0x60, rc.R, rc.G, rc.B)),
                BorderThickness = new Thickness(pinned ? 1.5 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            card.MouseEnter += (_, _) =>
            {
                if (!pinned) card.Background = CardHi;
                delBtn.Visibility = Visibility.Visible;
                ShowHover(capturedSec, card);
            };
            card.MouseLeave += (_, _) =>
            {
                if (!pinned) card.Background = Card;
                delBtn.Visibility = Visibility.Collapsed;
                _hoverPopup.IsOpen = false;
            };
            // Tiny inline "Pinned ✓" / "Unpinned" label that fades in/out on the card — modal stays open
            var pinLabel = new TextBlock
            {
                FontSize = 9.5, FontWeight = System.Windows.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 0), Opacity = 0,
            };
            delGrid.Children.Add(pinLabel);

            card.MouseLeftButtonUp += (_, _) =>
            {
                bool wasPinned = _pinned.Remove(capturedSec.Key);
                if (!wasPinned) _pinned.Add(capturedSec.Key);
                _hoverPopup.IsOpen = false;

                // Update card visuals in-place (avoid closing the modal)
                bool nowPinned = !wasPinned;
                card.Background = nowPinned ? TileSel : Card;
                card.BorderBrush = nowPinned ? Gold : new SolidColorBrush(Color.FromArgb(0x60, rc.R, rc.G, rc.B));
                card.BorderThickness = new Thickness(nowPinned ? 1.5 : 1);
                pinLabel.Text = nowPinned ? "Pinned ✓" : "Unpinned";
                pinLabel.Foreground = nowPinned ? Gold : Soft;

                // Fade in then out
                pinLabel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
                var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(1200) };
                pinLabel.BeginAnimation(OpacityProperty, fade);

                Render();   // update compare deck in the main window behind the modal
            };
            wrap.Children.Add(card);
        }
        scroll.Content = wrap;
        sp.Children.Add(scroll);

        var panel = new Border
        {
            Background = B("#0D0D10"), BorderBrush = EdgeHi, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(22, 18, 22, 20),
            MaxWidth = 1060, MaxHeight = 700,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = sp, ClipToBounds = true,
        };
        overlay.Children.Add(panel);
        RootLayer.Children.Add(overlay);
    }

    // ---- character portrait capture ----
    // Uses WGC (works in exclusive fullscreen) with PrintWindow as the internal fallback.
    // CaptureCharacterPortrait() is synchronous (called from a button click), so it runs
    // GrabAsync() synchronously via GetAwaiter().GetResult() — this is safe because the
    // button handler is on the UI thread and the async path only blocks briefly.

    string? CaptureCharacterPortrait()
    {
        var d4 = System.Diagnostics.Process.GetProcessesByName("Diablo IV").FirstOrDefault();
        if (d4 == null || d4.MainWindowHandle == IntPtr.Zero)
            return "Diablo IV is not running. Launch the game and open the character panel (C key), then try again.";

        System.Drawing.Bitmap? bmp;
        try { bmp = D4Scanner.App.Capture.WindowsGraphicsCapture.GrabAsync().GetAwaiter().GetResult(); }
        catch { bmp = null; }

        if (bmp == null)
            return "Capture failed. Make sure D4 is not minimized and try again.";

        using (bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            if (w < 100 || h < 100) return "D4 window is too small to capture.";
            // D4 character panel: the model stands in the centre-left third of the screen.
            // Relative coords tested against 1080p and 1440p: model occupies roughly
            // x = 13-40% of width, y = 5-91% of height.
            int cropW = Math.Max(200, w * 28 / 100);
            int cropH = Math.Max(400, h * 87 / 100);
            int cropX = Math.Max(0, w * 13 / 100);
            int cropY = Math.Max(0, h *  5 / 100);
            cropW = Math.Min(cropW, w - cropX); cropH = Math.Min(cropH, h - cropY);
            using var crop = bmp.Clone(new System.Drawing.Rectangle(cropX, cropY, cropW, cropH),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var dest = Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "character.png");
            crop.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
        }
        return null;
    }

    void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var mods = System.Windows.Input.Keyboard.Modifiers;
        bool ctrl = (mods & System.Windows.Input.ModifierKeys.Control) != 0;
        bool alt = (mods & System.Windows.Input.ModifierKeys.Alt) != 0;
        bool shift = (mods & System.Windows.Input.ModifierKeys.Shift) != 0;
        var k = alt ? e.SystemKey : e.Key;   // Alt routes the real key to SystemKey

        if (k == System.Windows.Input.Key.F1 || (k == System.Windows.Input.Key.Oem2 && shift && !UrlBox.IsFocused)) { ToggleHelp(); e.Handled = true; return; }
        if (HelpHost.Visibility == Visibility.Visible && k == System.Windows.Input.Key.Escape) { HelpHost.Visibility = Visibility.Collapsed; e.Handled = true; return; }
        if (SettingsHost.Visibility == Visibility.Visible && k == System.Windows.Input.Key.Escape) { SettingsHost.Visibility = Visibility.Collapsed; e.Handled = true; return; }
        if (ctrl && (k == System.Windows.Input.Key.OemPlus || k == System.Windows.Input.Key.Add)) { Zoom(0.1); e.Handled = true; }
        else if (ctrl && (k == System.Windows.Input.Key.OemMinus || k == System.Windows.Input.Key.Subtract)) { Zoom(-0.1); e.Handled = true; }
        else if (ctrl && (k == System.Windows.Input.Key.D0 || k == System.Windows.Input.Key.NumPad0)) { Zoom(1.0 - _uiScale); e.Handled = true; }
        else if (alt && k == System.Windows.Input.Key.O && _target != null) { GoOverview(); e.Handled = true; }
        else if (alt && k == System.Windows.Input.Key.N && _target != null) { _stepsView = !_stepsView; if (_stepsView) _rawView = false; Render(); e.Handled = true; }
        else if (alt && k == System.Windows.Input.Key.B && _target != null) { _rawView = !_rawView; if (_rawView) _stepsView = false; RawBtn.Content = _rawView ? "← Overview" : "Build details"; Render(); e.Handled = true; }
        else if (alt && k == System.Windows.Input.Key.I) { ShowInventoryModal(); e.Handled = true; }
        else if (k == System.Windows.Input.Key.Oem2 && !UrlBox.IsFocused) { UrlBox.Focus(); UrlBox.SelectAll(); e.Handled = true; }   // "/" focuses search
        else if (k == System.Windows.Input.Key.Escape)
        {
            if (AcPopup.IsOpen) { AcPopup.IsOpen = false; UrlBox.Focus(); }
            else if (BuildsPopup.IsOpen) BuildsPopup.IsOpen = false;
            else if (ProfilePopup.IsOpen) ProfilePopup.IsOpen = false;
            else if (_focusKey != null) { _focusKey = null; Render(); }
            else if (_pinned.Count > 0) { _pinned.Clear(); Render(); Toast("Cleared pins"); }
            else if (_stepsView || _rawView) GoOverview();
        }
    }

    Brush VerbColor(string verb) => verb switch
    {
        "EQUIP" => Green, "FIND" => RUnique, "IMPRINT" => RLegend,
        "SKILL" or "PARAGON" or "CAPTURE" or "MERC" => Steel, "TEMPER" or "RE-TEMPER" or "IMPROVE" => Amber, _ => Ink,
    };

    // First-run setup card: two steps with live checkmarks.
    // Step 2 presents OCR and TTS as distinct choices with clear trade-offs.
    UIElement WelcomeCard()
    {
        bool s1 = _target != null;
        bool s2 = CaptureSetup.Installed() || _useCapture;
        int doneCount = (s1 ? 1 : 0) + (s2 ? 1 : 0);

        var sp = new StackPanel();
        var hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var prog = TB($"{doneCount} / 2", Soft, 12, false); prog.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(prog, Dock.Right); hdr.Children.Add(prog);
        hdr.Children.Add(TBs("Set up your live guide", Gold, 15, true));
        sp.Children.Add(hdr);
        sp.Children.Add(TB("Two quick steps — checkmarks fill in as you complete each one.", Soft, 12, false, new Thickness(0, 0, 0, 12)));

        // Step helper
        void Step(bool done, UIElement content)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var mark = TB(done ? "✓" : "○", done ? Green : Faint, 15, true);
            mark.TextAlignment = TextAlignment.Center; mark.Width = 24; mark.Margin = new Thickness(0, 1, 12, 0); mark.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(mark, Dock.Left); row.Children.Add(mark);
            row.Children.Add(content);
            sp.Children.Add(row);
        }

        // Step 1: Import
        var s1Content = new StackPanel();
        s1Content.Children.Add(TBs("Import a build", s1 ? Soft : Ink, 13.5, true));
        var s1Desc = TB("Paste a Maxroll build-guide URL above, or just type the build name and hit Import.", Soft, 12, false, new Thickness(0, 1, 0, 0));
        s1Desc.TextWrapping = TextWrapping.Wrap; s1Content.Children.Add(s1Desc);
        Step(s1, s1Content);

        // Step 2: Gear capture — explain the two options clearly
        var s2Content = new StackPanel();
        s2Content.Children.Add(TBs("Enable gear capture", s2 ? Soft : Ink, 13.5, true));
        var s2Intro = TB("D4Scanner needs to read your equipped items. Pick one method — or use both for best coverage:", Soft, 12, false, new Thickness(0, 3, 0, 8));
        s2Intro.TextWrapping = TextWrapping.Wrap; s2Content.Children.Add(s2Intro);

        if (!s2)
        {
            // Option cards
            void OptionCard(string icon, string title, string desc, string btnLabel, Action<Button> onClick)
            {
                var card = new Border
                {
                    Background = B("#111014"), BorderBrush = Edge, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 6),
                };
                var inner = new DockPanel();
                var btn = new Button { Content = btnLabel, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Top };
                btn.Click += (_, _) => onClick(btn);
                DockPanel.SetDock(btn, Dock.Right); btn.Margin = new Thickness(10, 0, 0, 0); inner.Children.Add(btn);
                var txt = new StackPanel();
                var hd2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
                hd2.Children.Add(TB(icon + "  ", Faint, 13, false));
                hd2.Children.Add(TBs(title, Ink, 13, true));
                txt.Children.Add(hd2);
                var d = TB(desc, Soft, 11.5, false); d.TextWrapping = TextWrapping.Wrap; txt.Children.Add(d);
                inner.Children.Add(txt);
                card.Child = inner;
                s2Content.Children.Add(card);
            }

            OptionCard("⌨", "Screen-reader (TTS)",
                "Most accurate. D4 voices item text which is logged and parsed. Requires a one-click DLL install and D4 accessibility settings.",
                "Install DLL", b => RunInstall(b));

            OptionCard("👁", "Screen capture (OCR)",
                "No install needed — grabs the game window every 20 s and reads tooltip text via Windows OCR. Works in borderless and exclusive fullscreen.",
                "Enable OCR", _ => { _useCapture = true; SaveSettings(); StartWatching(); Render(); });
        }
        else
        {
            var active = new StackPanel { Orientation = Orientation.Horizontal };
            if (CaptureSetup.Installed()) active.Children.Add(TB("✓ TTS", Green, 12, false, new Thickness(0, 0, 12, 0)));
            if (_useCapture) active.Children.Add(TB("✓ OCR", Green, 12, false));
            s2Content.Children.Add(active);
        }
        Step(s2, s2Content);

        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(20, 16, 20, 18), Margin = new Thickness(0, 0, 0, 14), Child = sp,
        };
    }

    // "Do Next" guidance: the prioritized, build-wide plan is produced by Core (BuildGuide.Steps) — every
    // actionable gap across gear, uniques, aspects, skills and paragon, grouped by effort and ordered by
    // impact. This method only renders it (verb colors, focus filter, click-to-focus). Leads with free wins
    // (equip a better item you already own). Null when the build is complete.
    UIElement? GuidancePanel(DiffReport r)
    {
        var acts = BuildGuide.Steps(r);
        if (acts.Count == 0)
        {
            // closed the loop — guide all the way to the finish line
            var done = new StackPanel();
            done.Children.Add(TBs("BUILD COMPLETE", Green, 13, true, new Thickness(0, 0, 0, 4)));
            done.Children.Add(TB("Every target is met — gear, affixes, uniques, aspects, skills and paragon. Nice work.",
                Soft, 12.5, false));
            return new Border
            {
                Background = Card, BorderBrush = B("#2E4F33"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14), Child = done,
            };
        }
        // focus mode: show only the picked slot/category (cleared automatically once it's fully met)
        var view = _focusKey != null ? acts.Where(a => a.FocusKey == _focusKey).ToList() : acts;
        if (view.Count == 0) { view = acts; _focusKey = null; }
        bool focused = _focusKey != null;
        var top = view.Take(9).ToList();   // BuildGuide.Steps is already impact-ordered

        var sp = new StackPanel();

        // header: "DO NEXT" + step count + (focused: focus label + clear)
        var hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var allStepsLink = TB($"all {acts.Count} steps →", Steel, 11, false);
        allStepsLink.VerticalAlignment = VerticalAlignment.Center;
        allStepsLink.Cursor = System.Windows.Input.Cursors.Hand;
        allStepsLink.MouseLeftButtonUp += (_, _) => { _stepsView = true; Render(); };
        DockPanel.SetDock(allStepsLink, Dock.Right); hdr.Children.Add(allStepsLink);
        var cntLbl = TB($"{view.Count} steps  ", Faint, 11.5, false);
        cntLbl.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(cntLbl, Dock.Right); hdr.Children.Add(cntLbl);
        hdr.Children.Add(TBs("DO NEXT", Gold, 13.5, true));
        sp.Children.Add(hdr);

        if (focused)
        {
            var fb = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var clear = TB("✕ all", Steel, 11, false); clear.Cursor = System.Windows.Input.Cursors.Hand;
            clear.MouseLeftButtonUp += (_, _) => { _focusKey = null; Render(); };
            DockPanel.SetDock(clear, Dock.Right); fb.Children.Add(clear);
            fb.Children.Add(TB(BuildGuide.FocusLabel(r, _focusKey!), Faint, 11, false));
            sp.Children.Add(fb);
        }

        IEnumerable<GuideStep> rows = top;
        if (!focused)
        {
            sp.Children.Add(HeroCard(top[0]));
            rows = top.Skip(1);
        }
        else
        {
            var hl = TB(top[0].Headline ?? top[0].Text, Ink, 14, false, new Thickness(0, 0, 0, 8));
            hl.TextWrapping = TextWrapping.Wrap; sp.Children.Add(hl);
        }

        // compact step rows — no tier labels, just a thin rule between tiers for breathing room
        int? lastTier = null;
        foreach (var a in rows)
        {
            if (a.Tier != lastTier && lastTier != null)
                sp.Children.Add(new Border { Height = 1, Background = Edge, Margin = new Thickness(0, 5, 0, 5) });
            lastTier = a.Tier;
            sp.Children.Add(StepRow(a));
        }
        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14), Child = sp,
        };
    }

    // the single most important next action, rendered prominently (accent-tinted, larger) so the one thing
    // to do next is unmistakable. Clicking focuses that slot/category.
    FrameworkElement HeroCard(GuideStep a)
    {
        var inner = new StackPanel();
        inner.Children.Add(TBs("DO THIS FIRST", VerbColor(a.Verb), 9.5, true, new Thickness(0, 0, 0, 5)));
        var hl = TB(a.Headline ?? a.Text, Ink, 16, true); hl.TextWrapping = TextWrapping.Wrap; inner.Children.Add(hl);

        var sub = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (a.Detail != null) { var d = TB(a.Detail, Soft, 11.5, false); d.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(d, Dock.Right); sub.Children.Add(d); }
        var vt = TB(a.Verb, B("#0C0C0F"), 10, true); vt.TextAlignment = TextAlignment.Center;
        var vb = new Border { Background = VerbColor(a.Verb), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 2, 8, 3), MinWidth = 62, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Child = vt };
        DockPanel.SetDock(vb, Dock.Left); sub.Children.Add(vb);
        var tx = TB(a.Text, Soft, 12, false); tx.VerticalAlignment = VerticalAlignment.Center; tx.TextWrapping = TextWrapping.Wrap; sub.Children.Add(tx);
        inner.Children.Add(sub);

        var card = new Border
        {
            Background = B("#1A1416"), BorderBrush = B("#7A3338"), BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(0, 0, 0, 12), Child = inner,
        };
        if (a.FocusKey is string fk)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => { if (fk.StartsWith("gear:")) _pinned.Add(fk); _selectedKey = fk; _focusKey = fk; _stepsView = false; Render(); };
        }
        return card;
    }

    // build-specific "go do this" list: which activities + crafters get the loot the build still needs.
    // Rendered as a collapsible accordion so it doesn't crowd the guidance rail (collapsed by default).
    FrameworkElement? ActivitiesPanel(DiffReport r)
    {
        var acts = Activities.Recommend(r);
        var sp = new StackPanel();

        // clickable header row: chevron + title + count
        var hdr = new DockPanel { Cursor = System.Windows.Input.Cursors.Hand };
        var chev = TBs(_activitiesOpen ? "▾" : "▸", Gold, 13, true); chev.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(chev, Dock.Left); hdr.Children.Add(chev);
        if (acts.Count > 0) { var cnt = TB(acts.Count.ToString(), Soft, 11.5, false); cnt.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(cnt, Dock.Right); hdr.Children.Add(cnt); }
        hdr.Children.Add(TBs("RECOMMENDED ACTIVITIES", Gold, 13, true));
        hdr.MouseLeftButtonUp += (_, _) => { _activitiesOpen = !_activitiesOpen; Render(); };
        sp.Children.Add(hdr);

        if (_activitiesOpen)
        {
            if (acts.Count == 0)
            {
                var none = TB("You're set on loot — nothing specific to farm right now. Remaining steps are crafting & equipping.", Soft, 12.5, false);
                none.TextWrapping = TextWrapping.Wrap; none.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(none);
            }
            foreach (var a in acts)
            {
                var row = new StackPanel { Margin = new Thickness(0, a == acts[0] ? 11 : 9, 0, 0) };
                row.Children.Add(TBs(a.Title, Ink, 13, true));
                var d = TB(a.Detail, Soft, 12, false, new Thickness(0, 2, 0, 0)); d.TextWrapping = TextWrapping.Wrap;
                row.Children.Add(d);
                sp.Children.Add(row);
            }
        }
        return new Border { Child = sp, Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(18, 13, 18, _activitiesOpen ? 14 : 13), Margin = new Thickness(0, 0, 0, 14) };
    }

    // one Do-Next row: verb chip + text + detail; click to jump to that slot/category (shared by DO NEXT + Next Steps)
    FrameworkElement StepRow(GuideStep a)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent };
        var vt = TB(a.Verb, B("#0C0C0F"), 9.5, true); vt.TextAlignment = TextAlignment.Center;
        var vb = new Border { Background = VerbColor(a.Verb), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 10, 0), MinWidth = 62, VerticalAlignment = VerticalAlignment.Center, Child = vt };
        DockPanel.SetDock(vb, Dock.Left); row.Children.Add(vb);
        if (a.Detail != null) { var d = TB(a.Detail, Soft, 11.5, false, new Thickness(10, 0, 0, 0)); d.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(d, Dock.Right); row.Children.Add(d); }
        var tx = TB(a.Text, Ink, 12.5, false); tx.VerticalAlignment = VerticalAlignment.Center; tx.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(tx);
        if (a.FocusKey is string fk)
        {
            row.Cursor = System.Windows.Input.Cursors.Hand;
            row.MouseEnter += (_, _) => row.Background = CardHi;
            row.MouseLeave += (_, _) => row.Background = System.Windows.Media.Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => { if (fk.StartsWith("gear:")) _pinned.Add(fk); _selectedKey = fk; _focusKey = fk; _stepsView = false; Render(); };
        }
        return row;
    }

    // the full Next-Steps screen: searchable, filterable by effort tier, paged 10-at-a-time
    UIElement NextStepsView(DiffReport r)
    {
        var all = BuildGuide.Steps(r);
        var root = new StackPanel();

        var hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var back = TB("← Overview", Steel, 13, false); back.Cursor = System.Windows.Input.Cursors.Hand; back.VerticalAlignment = VerticalAlignment.Center;
        back.MouseLeftButtonUp += (_, _) => { _stepsView = false; Render(); };
        DockPanel.SetDock(back, Dock.Right); hdr.Children.Add(back);
        var exp = TB("⤓ Export loot filter", Steel, 12.5, false); exp.Cursor = System.Windows.Input.Cursors.Hand; exp.VerticalAlignment = VerticalAlignment.Center; exp.Margin = new Thickness(0, 0, 18, 0);
        exp.MouseLeftButtonUp += (_, _) => ExportLootFilter();
        DockPanel.SetDock(exp, Dock.Right); hdr.Children.Add(exp);
        hdr.Children.Add(TBs("NEXT STEPS", Gold, 16, true));
        root.Children.Add(hdr);

        if (all.Count == 0)
        {
            root.Children.Add(TB("Build complete — no steps remaining.", Soft, 13, false));
        }
        else
        {
            var search = new TextBox { Text = _stepsSearch, Margin = new Thickness(0, 0, 0, 10) };
            search.TextChanged += (_, _) => { _stepsSearch = search.Text; _stepsPage = 0; RefreshSteps(all); };
            root.Children.Add(search);

            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            void Chip(int? tier, string label)
            {
                bool on = _stepsTier == tier;
                var b = new Border { Child = TB(label, on ? Ink : Soft, 12, on), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 7, 7), CornerRadius = new CornerRadius(12), Background = on ? TileSel : Card, BorderBrush = on ? Gold : Edge, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
                b.MouseLeftButtonUp += (_, _) => { _stepsTier = tier; _stepsPage = 0; RefreshSteps(all); };
                chips.Children.Add(b);
            }
            Chip(null, "All");
            foreach (var t in all.Select(a => a.Tier).Distinct().OrderBy(x => x))
                Chip(t, BuildGuide.TierLabel(t).Split('·')[0].Trim());
            root.Children.Add(chips);

            _stepsResultsPanel = new StackPanel();
            root.Children.Add(_stepsResultsPanel);

            var pager = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var prev = PagerBtn("‹ Prev", () => { if (_stepsPage > 0) { _stepsPage--; RefreshSteps(all); } });
            var next = PagerBtn("Next ›", () => { _stepsPage++; RefreshSteps(all); });
            DockPanel.SetDock(prev, Dock.Left); DockPanel.SetDock(next, Dock.Right);
            _stepsPageLbl = TB("", Soft, 12, false); _stepsPageLbl.HorizontalAlignment = HorizontalAlignment.Center; _stepsPageLbl.VerticalAlignment = VerticalAlignment.Center;
            pager.Children.Add(prev); pager.Children.Add(next); pager.Children.Add(_stepsPageLbl);
            root.Children.Add(pager);

            RefreshSteps(all);

            var ap = ActivitiesPanel(r);
            if (ap != null) { ap.Margin = new Thickness(0, 18, 0, 0); root.Children.Add(ap); }
        }
        return new Border
        {
            Child = root, Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(20, 16, 20, 18),
        };
    }

    // repopulate just the results list + page label (so the search box keeps focus while typing)
    void RefreshSteps(List<GuideStep> all)
    {
        if (_stepsResultsPanel == null) return;
        var q = (_stepsSearch ?? "").Trim();
        var filtered = all
            .Where(a => _stepsTier == null || a.Tier == _stepsTier)
            .Where(a => q.Length == 0 || (a.Text + " " + a.Verb + " " + (a.Detail ?? "")).Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        const int per = 10;
        int pages = Math.Max(1, (filtered.Count + per - 1) / per);
        _stepsPage = Math.Clamp(_stepsPage, 0, pages - 1);

        _stepsResultsPanel.Children.Clear();
        if (filtered.Count == 0)
            _stepsResultsPanel.Children.Add(TB("No steps match your search / filter.", Soft, 12.5, false));
        else
        {
            int? lastTier = null;
            foreach (var a in filtered.Skip(_stepsPage * per).Take(per))
            {
                if (a.Tier != lastTier) { _stepsResultsPanel.Children.Add(TBs(BuildGuide.TierLabel(a.Tier), Faint, 10, true, new Thickness(0, lastTier == null ? 0 : 11, 0, 5))); lastTier = a.Tier; }
                _stepsResultsPanel.Children.Add(StepRow(a));
            }
        }
        if (_stepsPageLbl != null)
            _stepsPageLbl.Text = $"{filtered.Count} step{(filtered.Count == 1 ? "" : "s")}  ·  page {_stepsPage + 1} of {pages}";
    }

    FrameworkElement PagerBtn(string label, Action onClick)
    {
        var b = new Border { Child = TB(label, Ink, 12, false), Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(4), Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    // Small icon-key legend shown above the paper doll so first-time users understand the badges
    UIElement IconLegend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        void Entry(string icon, Brush iconBg, string desc)
        {
            var badge = new Border
            {
                Background = iconBg, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 2), Margin = new Thickness(0, 0, 5, 0),
                Child = TB(icon, B("#0C0C0F"), 10, true),
            };
            row.Children.Add(badge);
            row.Children.Add(TB(desc, Faint, 11.5, false, new Thickness(0, 0, 16, 0)));
        }
        Entry("↑", Green, "upgrade in bags");
        Entry("#1", Amber, "priority");
        Entry("⚠", Amber, "under-rolled");
        return row;
    }

    UIElement SummaryStrip(DiffReport r)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var c in r.Categories.Where(c => c.Id != "skills" && c.Id != "paragon" && c.Id != "mercenary"))
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

    // Diablo IV character-screen layout: armor down the left, weapons/jewelry down the right,
    // build-wide categories (uniques / skills / paragon) in the middle.
    UIElement PaperDoll(List<Section> sections, string? className, int pct)
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
        Place(left, new[] { "helm", "chest", "gloves", "pants", "boots" }, false);   // armor down the left
        Place(right, new[] { "amulet", "ring" }, true);                              // jewelry down the right

        // weapons (+ offhand) sit across the bottom, matching the in-game character screen.
        // Pre-compute live item names already shown by gear: sections so we can skip uni: duplicates.
        var shownWeaponLiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in gear.Where(x => x.Key.StartsWith("gear:") && SlotKey(x.Label) is "weapon" or "offhand"))
            foreach (var li in s.Gear?.LiveItems ?? new())
                if (!string.IsNullOrEmpty(li.Name)) shownWeaponLiveNames.Add(li.Name);

        var weaponsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        void PlaceWeapons(string[] order)
        {
            foreach (var k in order)
                foreach (var s in gear.Where(x => !used.Contains(x) && SlotKey(x.Label) == k))
                {
                    // Skip a uni: section if the same live weapon is already shown via a gear: section.
                    // "Etna's Lost Dagger" appears as both gear:N (Sword) and uni:etna... — only show once.
                    if (s.Key.StartsWith("uni:") && s.Gear?.LiveItems.Count > 0
                        && shownWeaponLiveNames.Contains(s.Gear.LiveItems[0].Name))
                    { used.Add(s); continue; }
                    weaponsRow.Children.Add(SlotCell(s, prio.GetValueOrDefault(s), false)); used.Add(s);
                }
        }
        PlaceWeapons(new[] { "weapon", "offhand" });
        foreach (var s in gear.Where(x => !used.Contains(x))) weaponsRow.Children.Add(SlotCell(s, prio.GetValueOrDefault(s), false));

        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 2, 20, 0), MinWidth = 160 };

        // Center column: portrait if captured, otherwise just spacing for build categories below.
        // The class name, LIVE/offline indicator, and progress minibar have moved to the status bar.
        var charImg = LoadCharacterImage();
        if (charImg != null)
        {
            charImg.Stretch = Stretch.UniformToFill;
            charImg.VerticalAlignment = VerticalAlignment.Top;
            charImg.HorizontalAlignment = HorizontalAlignment.Center;

            const double pw = 172, ph = 290;

            // Layer 0: dark stone background
            // Layer 1: portrait image (clipped via the containing grid's Clip)
            // Layer 2: bottom-fade gradient — image dissolves into card background colour
            // Layer 3: subtle amber rim (matches rarity-frame aesthetic)
            var bgCol = Color.FromRgb(0x16, 0x15, 0x1A);
            var fadeGrad = new LinearGradientBrush { StartPoint = new Point(0.5, 0.52), EndPoint = new Point(0.5, 1.0) };
            fadeGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0,    bgCol.R, bgCol.G, bgCol.B), 0));
            fadeGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, bgCol.R, bgCol.G, bgCol.B), 0.78));
            fadeGrad.GradientStops.Add(new GradientStop(bgCol,                                           1));

            var pg = new Grid
            {
                Width = pw, Height = ph,
                Background = B("#0A090D"),
                Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, pw, ph), 6, 6),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            pg.Children.Add(charImg);
            pg.Children.Add(new Border { Background = fadeGrad });
            pg.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xD4, 0xA7, 0x30)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            });
            center.Children.Add(pg);
        }

        // skills/paragon/mercenary hidden for now; uniques shown only if any have no doll slot
        bool allUniquesMapped = _target == null || _target.Uniques.All(u => SlotKey(u.Slot ?? "").Length > 0);
        var shownCats = cats.Where(s => s.Total > 0
            && s.Cat?.Id != "skills" && s.Cat?.Id != "paragon" && s.Cat?.Id != "mercenary"
            && (s.Cat?.Id != "uniques" || !allUniquesMapped)).ToList();
        if (shownCats.Count > 0) center.Children.Add(TBs("BUILD", Faint, 11, true, new Thickness(2, 0, 0, 6)));
        foreach (var s in shownCats) center.Children.Add(CatCell(s));

        Grid.SetColumn(left, 0); grid.Children.Add(left);
        Grid.SetColumn(center, 1); grid.Children.Add(center);
        Grid.SetColumn(right, 2); grid.Children.Add(right);

        var dollStack = new StackPanel();
        dollStack.Children.Add(grid);
        if (weaponsRow.Children.Count > 0) dollStack.Children.Add(weaponsRow);

        // Simple StackPanel wrap — no backdrop (portrait is now a framed element in the center column)
        var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        wrap.Children.Add(DollToggle());
        wrap.Children.Add(dollStack);
        return wrap;
    }

    // tab toggle above the doll: preview the build's wanted gear ("Target") vs your equipped gear ("My gear")
    UIElement DollToggle()
    {
        // D4-style tab strip: active tab has a thick amber underline + elevated background;
        // inactive tabs are muted with a subtle hover effect
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };

        // baseline rule the full strip sits on
        var baseRule = new Border
        {
            Height = 2, Background = Edge,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var wrapper = new Grid();
        wrapper.Children.Add(baseRule);
        wrapper.Children.Add(strip);

        void Tab(string key, string label, string icon)
        {
            bool on = _dollView == key;
            var iconTB = TB(icon + " ", on ? Gold : Faint, 11.5, false);
            var labelTB = TB(label, on ? Ink : Soft, 12.5, on);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(iconTB); content.Children.Add(labelTB);

            // bottom accent bar — amber when active, invisible when not
            var bar = new Border
            {
                Height = 3, CornerRadius = new CornerRadius(1.5, 1.5, 0, 0),
                Background = on ? Gold : System.Windows.Media.Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 4, 0),
            };

            var cell = new Grid { Margin = new Thickness(2, 0, 2, 0) };
            cell.Children.Add(new Border
            {
                Child = content, Padding = new Thickness(14, 7, 14, 9),
                Background = on ? B("#22201C") : System.Windows.Media.Brushes.Transparent,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
            });
            cell.Children.Add(bar);
            cell.Cursor = System.Windows.Input.Cursors.Hand;

            cell.MouseLeftButtonUp += (_, e) =>
            {
                if (key == "all") { ShowInventoryModal(); e.Handled = true; return; }
                _dollView = key; Render();
            };
            if (!on)
            {
                var bg = (Border)cell.Children[0];
                cell.MouseEnter += (_, _) => { bg.Background = B("#1C1B18"); iconTB.Foreground = Soft; };
                cell.MouseLeave += (_, _) => { bg.Background = System.Windows.Media.Brushes.Transparent; iconTB.Foreground = Faint; };
            }
            strip.Children.Add(cell);
        }

        Tab("mine",   "My Gear",   "◆");
        Tab("target", "Target",    "◎");
        Tab("all",    "All Items",  "⊞");
        return wrapper;
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

    // the exact TargetGear backing a gear section (sections are keyed "gear:<index>" in target.Gear order),
    // so multi-instance slots (the three weapons, two rings) keep their own icon instead of all sharing the first.
    TargetGear? TargetGearOf(Section s)
    {
        if (s.Key.StartsWith("gear:") && int.TryParse(s.Key.AsSpan(5), out var gi) && _target != null && gi >= 0 && gi < _target.Gear.Count)
            return _target.Gear[gi];
        var key = SlotKey(s.Label);
        return _target?.Gear.FirstOrDefault(g => SlotKey(g.Slot) == key);
    }

    // what the build wants in a slot: a targeted unique, else the wanted aspect, else "Any <slot>".
    // also returns the item id/image so icon sources keyed by id/image can resolve it.
    (string name, Brush col, string? iconName, string? id, long? image) WantedFor(Section s)
    {
        // Gear affix sections (gear:N) never show a unique — the unique has its own separate tile (uni:*).
        // Showing a unique here caused ALL weapon/ring slots to show the same unique (e.g. both the
        // Crossbow and Sword sections both matched "Etna's Lost Dagger" via SlotKey=="weapon").
        // Unique sections (uni:*) match by the normalized unique name embedded in the section key.
        if (s.Key.StartsWith("uni:"))
        {
            var normalizedKey = s.Key.Substring(4);   // strip "uni:" to get the normalized unique name
            var u = _target?.Uniques.FirstOrDefault(x => DiffEngine.Normalize(x.Name) == normalizedKey);
            if (u != null) return (u.Name, u.Mythic ? RMythic : RUnique, u.Name, u.ItemId, u.Image);
        }
        var tg = TargetGearOf(s);
        // Aspect — the build wants a specific legendary power; use the template icon for art if available
        if (!string.IsNullOrEmpty(s.Gear?.WantAspect)) return (s.Gear!.WantAspect!, RLegend, null, tg?.ItemId, tg?.Image);
        // "Any <slot>" — no specific item; pass null icon handles so the generic slot silhouette is shown
        return ("Any " + s.Label, Soft, null, null, null);
    }

    // Borrow an image handle for a live item so GameDataIcons can render the real game art.
    // Priority: exact unique name match → exact target gear ItemId match → name-only fallback.
    (string name, Brush col, string? iconName, string? id, long? image) EquippedFor(Section s)
    {
        var it = s.Gear != null && s.Gear.LiveItems.Count > 0 ? s.Gear.LiveItems[0] : null;
        if (it == null) return ("(empty)", Faint, null, null, null);
        var rcol = RarityBrush(it.Rarity);
        // 1. Exact unique name match — safest, icon is always correct for this specific item
        var u = _target?.Uniques.FirstOrDefault(x => DiffEngine.PhraseMatch(x.Name, it.Name));
        if (u != null) return (it.Name, rcol, it.Name, u.ItemId, u.Image);
        // 2. Target gear ItemId name match — the target specifies this exact item, so its art is correct
        var tg = _target?.Gear.FirstOrDefault(x =>
            !string.IsNullOrEmpty(x.ItemId) && DiffEngine.PhraseMatch(x.ItemId, it.Name) && x.Image.HasValue);
        if (tg != null) return (it.Name, rcol, it.Name, tg.ItemId, tg.Image);
        // 3. Name-only lookup (IconResolver will try game-data by name via any configured templates)
        return (it.Name, rcol, it.Name, null, null);
    }

    // the slot's display tuple for the current doll view (target = build wants, mine = equipped)
    (string name, Brush col, string? iconName, string? id, long? image) SlotDisplay(Section s) =>
        _dollView is "mine" or "all" ? EquippedFor(s) : WantedFor(s);

    // a portrait equipment frame styled like the D4 in-game character screen: dark slot with a diagonal
    // rarity-colored gradient overlay and a rarity-colored border ring. Ancestral items get a cyan glow.
    FrameworkElement IconBox(Section s, int num)
    {
        var (_, rcol, iconName, wid, wimg) = SlotDisplay(s);
        var rc = ((SolidColorBrush)rcol).Color;

        // ancestral treatment: if viewing "my gear" and the item is ancestral, use the cyan shimmer
        var liveIt = s.Gear?.LiveItems.Count > 0 ? s.Gear.LiveItems[0] : null;
        bool ancestral = _dollView is "mine" or "all" && IsAncestral(liveIt?.Rarity);
        var frameBrush = ancestral ? RAncestral : rcol;
        var frameRc = ((SolidColorBrush)frameBrush).Color;

        const double boxW = 54, boxH = 76;    // portrait — tall and skinny, like a D4 inventory slot
        const double artW = 34, artH = 50;   // inset: 10px margin each side so placeholder sits inside the frame
        var grid = new Grid { Width = boxW, Height = boxH };

        // dark base + diagonal rarity-colored overlay (top-right → bottom-left, matching D4's slot shading)
        grid.Children.Add(new Border { Background = B("#080809"), CornerRadius = new CornerRadius(4) });
        var overlay = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0.1, 1) };
        overlay.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, frameRc.R, frameRc.G, frameRc.B), 0));
        overlay.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, frameRc.R, frameRc.G, frameRc.B), 0.35));
        overlay.GradientStops.Add(new GradientStop(Color.FromArgb(0x05, frameRc.R, frameRc.G, frameRc.B), 1));
        grid.Children.Add(new Border { Background = overlay, CornerRadius = new CornerRadius(4) });

        // rarity border ring (primary color)
        grid.Children.Add(new Border { BorderBrush = frameBrush, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(3.5), Margin = new Thickness(1) });

        // ancestral inner shimmer ring
        if (ancestral)
            grid.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x66, 0xD0, 0xF8)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2.5), Margin = new Thickness(3),
            });

        var icon = SlotOrItemIcon(iconName, SlotKey(s.Label), rcol, artW, artH, wid, wimg);
        icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        if (s.Gear != null && s.Gear.UpgradeItems.Count > 0)
            grid.Children.Add(new Border
            {
                Background = Green, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-5, -5, 0, 0), Child = TB("↑", B("#0C0C0F"), 11, true),
            });
        return grid;
    }

    // floating compare card shown while hovering a slot
    void ShowHover(Section s, UIElement target)
    {
        if (s.Gear == null) return;
        var it = s.Gear.LiveItems.Count > 0 ? s.Gear.LiveItems[0] : null;
        // adaptive placement: slots near the right edge open their card to the LEFT so it doesn't clip off-screen
        if (target is FrameworkElement fe)
        {
            try
            {
                var pt = fe.TransformToAncestor(this).Transform(new Point(0, 0));
                bool nearRight = pt.X + fe.ActualWidth + 460 > ActualWidth;   // ~460 = compare-card width budget
                _hoverPopup.Placement = nearRight
                    ? System.Windows.Controls.Primitives.PlacementMode.Left
                    : System.Windows.Controls.Primitives.PlacementMode.Right;
            }
            catch { /* not yet in the visual tree — keep default Right */ }
        }
        _hoverPopup.PlacementTarget = target;
        var cc = (FrameworkElement)CompareCard(s.Gear, it, s.Label, s.Key);
        cc.MaxWidth = 780;
        _hoverPopup.Child = cc;   // no outer wrapper — each panel has its own opaque background
        _hoverPopup.IsOpen = true;
    }

    // a Mobalytics-style slot row: icon + (slot label / wanted item name); hover to compare, click to pin
    UIElement SlotCell(Section s, int num, bool alignRight)
    {
        var (name, ncol, _, _, _) = SlotDisplay(s);
        bool pinned = _pinned.Contains(s.Key);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var lbl = TB(s.Label, Faint, 10.5, false);
        var nm = TB(name, ncol, 12.5, true); nm.TextWrapping = TextWrapping.Wrap;
        if (alignRight)
        {
            lbl.HorizontalAlignment = nm.HorizontalAlignment = HorizontalAlignment.Right;
            lbl.TextAlignment = nm.TextAlignment = TextAlignment.Right;
        }
        text.Children.Add(lbl); text.Children.Add(nm);
        if (_debugMode && s.Gear != null)
        {
            var it = s.Gear.LiveItems.Count > 0 ? s.Gear.LiveItems[0] : null;
            // Find the raw Item from the live build for timing info
            var rawItem = EffectiveLive().Gear.FirstOrDefault(g => it != null && DiffEngine.PhraseMatch(g.Name, it.Name));
            string age = "";
            if (rawItem?.LastScannedTicks > 0)
            {
                var ago = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - rawItem.LastScannedTicks);
                age = ago.TotalSeconds < 120 ? $" · {(int)ago.TotalSeconds}s ago"
                    : ago.TotalMinutes < 60  ? $" · {(int)ago.TotalMinutes}m ago"
                    : $" · {ago.TotalHours:0.0}h ago";
            }
            string dbgTxt = it != null
                ? $"{it.Rarity ?? "?"} · IP {it.ItemPower}{age} · {s.Key}"
                : $"empty · {s.Key}";
            var dbg = TB(dbgTxt, Faint, 9.5, false); dbg.TextWrapping = TextWrapping.Wrap; text.Children.Add(dbg);
        }

        var icon = IconBox(s, num);
        var dp = new DockPanel { Width = 236 };   // width = boxW(54) + margin(12) + text
        if (alignRight) { DockPanel.SetDock(icon, Dock.Right); icon.Margin = new Thickness(12, 0, 0, 0); }
        else { DockPanel.SetDock(icon, Dock.Left); icon.Margin = new Thickness(0, 0, 12, 0); }
        dp.Children.Add(icon); dp.Children.Add(text);

        var b = new Border
        {
            Child = dp, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(4),
            Background = pinned ? TileSel : System.Windows.Media.Brushes.Transparent,
            BorderBrush = pinned ? Gold : System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(pinned ? 1.5 : 1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        // hover → floating compare; click → toggle pin (collects below for side-by-side comparison)
        b.MouseEnter += (_, _) => { if (!pinned) b.Background = Card; ShowHover(s, b); };
        b.MouseLeave += (_, _) => { if (!pinned) b.Background = System.Windows.Media.Brushes.Transparent; _hoverPopup.IsOpen = false; };
        b.MouseLeftButtonUp += (_, _) =>
        {
            bool wasPinned = _pinned.Remove(s.Key);
            if (!wasPinned) _pinned.Add(s.Key);
            _hoverPopup.IsOpen = false; Render();
            Toast(wasPinned ? $"Unpinned  {s.Label}" : $"Pinned  {s.Label}");
        };
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

        if (s.Gear != null)
        {
            GearDetail(sp, s.Gear, s.Label, s.Key);
            var sub = SubFor(s);
            if (sub != null) sp.Children.Add(SubstituteBlock(sub));
        }
        else if (s.Cat != null && s.Cat.Id != "skills" && s.Cat.Id != "paragon" && s.Cat.Id != "mercenary")
        {
            foreach (var g in s.Cat.Groups) GroupRows(sp, g);
        }

        return new Border
        {
            Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(22, 16, 22, 20),
            Margin = new Thickness(0, 4, 0, 8), Child = sp,
        };
    }

    void GearDetail(StackPanel sp, Group g, string label, string sectionKey = "")
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
            sp.Children.Add(CompareCard(g, it, label, sectionKey));
            if (g.UpgradeItems.Count > 0) sp.Children.Add(StashUpgrades(g.UpgradeItems));
        }
    }

    // per-slot substitute analysis for a gear section (matched by group index in the target)
    SlotSub? SubFor(Section s)
    {
        if (_target == null || s.Gear == null || !s.Key.StartsWith("gear:") || !int.TryParse(s.Key.AsSpan(5), out var gi)) return null;
        var plan = Substitutes.Plan(_target, EffectiveLive(), _target.MinRollPercent ?? _minRollPct);
        return gi >= 0 && gi < plan.Count ? plan[gi] : null;
    }

    // "Substitutes & flexibility": best item you own, core vs flexible affixes, and a Now→Better→Best ladder
    UIElement SubstituteBlock(SlotSub sub)
    {
        var sp = new StackPanel();
        sp.Children.Add(TBs("SUBSTITUTES & FLEXIBILITY", Steel, 11.5, true, new Thickness(0, 0, 0, 7)));

        if (sub.BestOwned != null)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (sub.BestIsUpgrade)
            {
                var upb = new Border { Background = Green, CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = TB("UPGRADE", B("#0C0C0F"), 9.5, true) };
                DockPanel.SetDock(upb, Dock.Right); line.Children.Add(upb);
            }
            line.Children.Add(TB($"Best you own: {sub.BestOwned}  ({sub.CoreMet}/{sub.CoreTotal} core affixes)", sub.BestIsUpgrade ? Green : Ink, 12.5, false));
            sp.Children.Add(line);
        }

        var coreA = sub.Affixes.Where(a => a.Core).Select(a => a.Name).ToList();
        var flexA = sub.Affixes.Where(a => !a.Core).Select(a => a.Name).ToList();
        if (coreA.Count > 0) sp.Children.Add(Wrapped("Core:  ", Ink, string.Join(", ", coreA), Soft, new Thickness(0, 5, 0, 0)));
        if (flexA.Count > 0) sp.Children.Add(Wrapped("Flexible:  ", Faint, string.Join(", ", flexA), Faint, new Thickness(0, 2, 0, 0)));

        foreach (var l in sub.Ladder)
        {
            int c = l.IndexOf(':');
            var row = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            if (c > 0) { row.Inlines.Add(new System.Windows.Documents.Run(l[..(c + 1)]) { Foreground = Steel, FontWeight = FontWeights.SemiBold }); row.Inlines.Add(new System.Windows.Documents.Run(l[(c + 1)..]) { Foreground = Soft }); }
            else row.Inlines.Add(new System.Windows.Documents.Run(l) { Foreground = Soft });
            row.FontSize = 11.5; sp.Children.Add(row);
        }
        return new Border { Child = sp, Background = B("#121316"), BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(14, 11, 14, 13), Margin = new Thickness(0, 14, 0, 0) };
    }

    // a wrapping two-tone line: bold label + soft value
    UIElement Wrapped(string label, Brush labelCol, string value, Brush valCol, Thickness margin)
    {
        var tb = new TextBlock { Margin = margin, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
        tb.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = labelCol, FontWeight = FontWeights.SemiBold });
        tb.Inlines.Add(new System.Windows.Documents.Run(value) { Foreground = valCol });
        return tb;
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
        string.Join("   ·   ", new[] { it.IsAncestral ? "Ancestral" : "", it.Rarity ?? "", it.ItemPower != null ? "Item Power " + it.ItemPower : "" }.Where(x => x.Length > 0));

    // ---- compare view: equipped item beside what the build wants (D4 hold-to-compare idiom) ----
    UIElement CompareCard(Group g, GearLiveItem? it, string label, string sectionKey = "")
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Resolve the target gear for this section to get real icon handles.
        // For gear:N sections, this is the N-th target gear item.
        // For uni:* sections, the unique itself carries its icon.
        TargetGear? tg = null;
        if (sectionKey.StartsWith("gear:") && int.TryParse(sectionKey.AsSpan(5), out var gi)
            && _target != null && gi >= 0 && gi < _target.Gear.Count)
            tg = _target.Gear[gi];

        // EQUIPPED (left): your item + your rolls, colored by how they compare to the target.
        // Use the target gear's icon handle so the compare card shows real game art.
        var eq = new StackPanel();
        foreach (var i in g.Items) eq.Children.Add(EquippedRow(i));
        if (!string.IsNullOrEmpty(it?.Aspect)) eq.Children.Add(AspectBox(it!.Aspect!));
        if (g.Extras.Count > 0)
        {
            eq.Children.Add(Divider(RarityColor(it?.Rarity), 0x44));
            eq.Children.Add(TB("also: " + string.Join("   ·   ", g.Extras), Soft, 11, false));
        }
        // For compare-card EQUIPPED panel: use name-only resolution so we show the player's actual
        // item art, not the build's template icon (which would be wrong for a different weapon type).
        // Unique items already matched above via EquippedFor, so just pass the live item name here.
        var eqIconId  = (string?)null;
        var eqIconImg = (long?)null;
        var left = TooltipPanel("EQUIPPED",
            it != null ? it.Name.ToUpperInvariant() : "— EMPTY SLOT —",
            it != null ? RarityBrush(it.Rarity) : Miss,
            it != null ? Sub(it) : "nothing scanned in this slot yet",
            RarityColor(it?.Rarity), eq, it?.Name, SlotKey(label), eqIconId, eqIconImg);

        // BUILD WANTS (right): the wanted item + wanted affixes/thresholds.
        // Only show a specific unique when this is a synthesised unique section (uni:*).
        // For gear affix sections (gear:N), never let a weapon unique "leak" into all weapon slots.
        bool isUniqueSection = sectionKey.StartsWith("uni:");
        var wantUnique = isUniqueSection
            ? _target?.Uniques.FirstOrDefault(u => DiffEngine.PhraseMatch(u.Name, label)
                                                   || SlotKey(u.Slot ?? "") == SlotKey(label))
            : null;
        bool myth = wantUnique?.Mythic == true;
        Color wrc = wantUnique != null ? (myth ? Col("#D1492E") : Col("#C9A45C")) : Col("#C8A24E");
        Brush wbr = wantUnique != null ? (myth ? RMythic : RUnique) : Gold;
        var wp = new StackPanel();
        foreach (var i in g.Items) wp.Children.Add(WantedRow(i));
        if (!string.IsNullOrEmpty(g.WantAspect)) wp.Children.Add(AspectBox(g.WantAspect!));
        if (g.WantSockets.Count > 0) wp.Children.Add(SocketsBox(g.WantSockets));
        // Use the unique's real icon when a specific unique is targeted; silhouette when it's "Any <slot>".
        var wantIconId  = wantUnique?.ItemId;
        var wantIconImg = wantUnique?.Image;
        var right = TooltipPanel("BUILD WANTS",
            wantUnique != null ? wantUnique.Name.ToUpperInvariant() : "ANY " + label.ToUpperInvariant(),
            wbr,
            wantUnique != null ? (myth ? "Mythic Unique" : "Unique") : "any item with these affixes",
            wrc, wp, wantUnique?.Name ?? label, SlotKey(label), wantIconId, wantIconImg);

        Grid.SetColumn(left, 0); grid.Children.Add(left);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        return grid;
    }

    UIElement TooltipPanel(string title, string header, Brush headerBrush, string sub, Color rarity, StackPanel rows,
                           string? iconName, string slotKey, string? iconId = null, long? iconImage = null)
    {
        // header band: real item art (or slot silhouette) beside the title/name/subtitle
        var head = new StackPanel();
        head.Children.Add(TB(title, Faint, 10.5, true, new Thickness(0, 0, 0, 4)));
        head.Children.Add(TBs(header, headerBrush, 15.5, true, new Thickness(0, 0, 0, 1)));
        head.Children.Add(TB(sub, Soft, 11, false));

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var icon = SlotOrItemIcon(iconName, slotKey, headerBrush, 50, iconId, iconImage);
        var iconBox = new Border { Width = 52, Height = 64, Child = icon, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(iconBox, Dock.Left); top.Children.Add(iconBox);
        head.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(head);

        var inner = new StackPanel();
        inner.Children.Add(top);
        inner.Children.Add(Divider(rarity, 0xAA));
        inner.Children.Add(rows);
        return new Border
        {
            Background = new LinearGradientBrush(BlendOntoBase(rarity, 0x22), Col("#16171B"), 90),
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
        var nd = TB(i.Need ?? "", Soft, 11.5, false); nd.VerticalAlignment = VerticalAlignment.Center; nd.Margin = new Thickness(8, 0, 0, 0);
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
            Background = B("#0A0A0D"), BorderBrush = Edge, BorderThickness = new Thickness(1),
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
        g.Children.Add(new Border { Background = B("#1A1B20"), BorderBrush = B("#0A0A0C"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(h / 2) });

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
        box.Children.Add(new Border { Background = B("#0C0C0F"), BorderBrush = col, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(6) });
        var art = RealIcon(name, 44, 44);
        if (art != null) { art.Margin = new Thickness(4); box.Children.Add(art); }
        else { var mono = TB(Monogram(name), col, 17, true); mono.HorizontalAlignment = HorizontalAlignment.Center; mono.VerticalAlignment = VerticalAlignment.Center; box.Children.Add(mono); }
        if (badge != null)
        {
            var bt = TB(badge, B("#0C0C0F"), 9.5, true);
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
        // skills/paragon intentionally omitted from the raw view for now

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
