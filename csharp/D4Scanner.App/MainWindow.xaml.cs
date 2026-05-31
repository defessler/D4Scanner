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
    static readonly Brush Ink = B("#F2E8D8"), Soft = B("#B9A88F"), Green = B("#5CB85C"),
        Amber = B("#E0A85A"), Miss = B("#D79A8C"), Card = B("#2E251C"), Line = B("#4A3A2A"), Crimson = B("#D2693E");

    LogWatcher? _watcher;
    System.Threading.Timer? _targetPoll;
    TargetBuild? _target;
    LiveBuild _live = new();
    string _log = TargetLoader.DefaultLogPath();
    string? _targetPath;
    DateTime _targetMtime;
    double _minRollPct = 50;

    string SettingsPath => Path.Combine(
        Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "app.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        TargetBtn.Click += (_, _) => PickTarget();
        LogBtn.Click += (_, _) => PickLog();
        TopmostBtn.Click += (_, _) => { Topmost = !Topmost; TopmostBtn.Content = Topmost ? "Unpin" : "Pin"; };
        ThreshSlider.ValueChanged += (_, _) =>
        {
            _minRollPct = ThreshSlider.Value;
            ThreshLbl.Text = ((int)_minRollPct) + "%";
            Render();
        };
        Loaded += (_, _) => StartWatching();
        Closed += (_, _) => { _watcher?.Dispose(); _targetPoll?.Dispose(); };
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
                new Dictionary<string, string?> { ["target"] = _targetPath, ["log"] = _log }));
        }
        catch { }
    }

    void StartWatching()
    {
        ReloadTarget();
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
            try { _target = TargetLoader.Load(_targetPath); _targetMtime = File.GetLastWriteTimeUtc(_targetPath); }
            catch { _target = null; }
    }

    void PickTarget()
    {
        var d = new OpenFileDialog { Filter = "Target build JSON|*.json|All files|*.*", Title = "Pick your target.json" };
        if (d.ShowDialog() == true) { _targetPath = d.FileName; SaveSettings(); ReloadTarget(); Render(); }
    }

    void PickLog()
    {
        var d = new OpenFileDialog { Filter = "TTS log|*.log;*.txt|All files|*.*", Title = "Pick the d4_tts.log" };
        if (d.ShowDialog() == true) { _log = d.FileName; SaveSettings(); StartWatching(); }
    }

    // ---- rendering ----
    static TextBlock TB(string text, Brush brush, double size, bool bold, Thickness? m = null) => new()
    {
        Text = text, Foreground = brush, FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Margin = m ?? new Thickness(0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };

    void Render()
    {
        if (_target == null)
        {
            BuildName.Text = "D4Scanner — Live Build Tracker";
            OverallPct.Text = "—";
            OverallCount.Text = "No target loaded — click ‘Target…’ and pick your target.json";
            OverallBar.Value = 0; Body.Children.Clear();
            Status.Text = $"log: {_log}";
            return;
        }
        var r = DiffEngine.Diff(_target, _live, _minRollPct);
        BuildName.Text = r.TargetName + (r.TargetClass != null ? "  ·  " + r.TargetClass : "");
        OverallPct.Text = r.Pct + "%";
        OverallCount.Text = $"{r.Matched} / {r.Total} met  ·  {_live.Gear.Count} equipped items"
            + (r.Under > 0 ? $"  ·  ⚠ {r.Under} under-rolled" : "");
        OverallBar.Value = r.Pct;
        Body.Children.Clear();
        foreach (var c in r.Categories) Body.Children.Add(CategoryCard(c));
        Status.Text = $"● live  ·  log: {_log}  ·  target: {Path.GetFileName(_targetPath)}";
    }

    UIElement CategoryCard(Category c)
    {
        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var pctText = $"{c.Matched} / {c.Total}  ({c.Pct}%)" + (c.Under > 0 ? $"   ⚠ {c.Under}" : "");
        head.Children.Add(Right(TB(pctText, c.Under > 0 ? Amber : Soft, 12, false, new Thickness(8, 3, 0, 0))));
        head.Children.Add(TB(c.Name, Ink, 14.5, true));
        sp.Children.Add(head);
        foreach (var g in c.Groups) sp.Children.Add(GroupBlock(g));
        return new Border
        {
            Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 10, 13, 12),
            Margin = new Thickness(0, 0, 0, 12), Child = sp,
        };
    }

    static UIElement Right(FrameworkElement e) { DockPanel.SetDock(e, Dock.Right); return e; }

    UIElement GroupBlock(Group g)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        var gh = new DockPanel();
        gh.Children.Add(Right(TB($"{g.Matched}/{g.Total}", Soft, 11, false)));
        gh.Children.Add(TB(g.Name.ToUpperInvariant(), Crimson, 11, true));
        sp.Children.Add(gh);

        if (g.Kind == "gear")
        {
            var it = g.LiveItems.Count > 0 ? g.LiveItems[0] : null;
            sp.Children.Add(it != null
                ? TB($"{it.Name}  ·  {it.Rarity}{(it.ItemPower != null ? "  ·  " + it.ItemPower : "")}", Ink, 12.5, true, new Thickness(0, 2, 0, 4))
                : TB("— no item captured for this slot —", Miss, 12, true, new Thickness(0, 2, 0, 4)));
        }

        foreach (var i in g.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // marker + colours keyed on status: met (green ✓), under-rolled (amber ⚠), missing (✗)
            Brush stcol = i.Status == "met" ? Green : i.Status == "under" ? Amber : Miss;
            var mark = TB(i.Status == "met" ? "✓" : i.Status == "under" ? "⚠" : "✗", stcol, 13, true);
            Grid.SetColumn(mark, 0); row.Children.Add(mark);

            var lbl = TB(i.Label, i.Status == "met" ? Soft : Ink, 13, false);
            Grid.SetColumn(lbl, 1); row.Children.Add(lbl);

            string right; Brush rb;
            if (g.Kind == "gear")
            {
                if (i.Status == "missing") { right = "—"; rb = Soft; }
                else
                {
                    string roll = i.RollPct != null ? $"  {Math.Round(i.RollPct.Value)}%" : "";
                    string need = i.Status == "under" && i.Need != null ? "  (" + i.Need + ")" : "";
                    right = (i.Val ?? "ok") + roll + need;
                    rb = i.Status == "under" ? Amber : Green;
                }
            }
            else { right = i.Have != null ? "have: " + i.Have : ""; rb = i.Have != null ? Miss : (i.Done ? Green : Soft); }
            var val = TB(right, rb, 12.5, i.Status != "missing", new Thickness(8, 0, 0, 0));
            val.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(val, 2); row.Children.Add(val);

            sp.Children.Add(row);
        }

        if (g.Kind == "gear" && g.Extras.Count > 0)
            sp.Children.Add(TB("also on your item:  " + string.Join("   ·   ", g.Extras), Soft, 11, false, new Thickness(0, 4, 0, 0)));

        return sp;
    }
}
