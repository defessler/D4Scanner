using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using D4Scanner.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace D4Scanner.App.Capture;

/// <summary>
/// Periodically captures the Diablo IV window, OCRs it for gear tooltips, and emits a
/// <see cref="LiveBuild"/>. Also feeds the shared <see cref="PanelOracle"/> the panel it visually
/// detects each scan, so the TTS classifier can use it as worn/browsed ground truth.
/// </summary>
public sealed class OcrCaptureEngine : IDisposable
{
    public event Action<LiveBuild>? Updated;

    readonly int _idleMs;
    const int ActiveMs = 1500;   // adaptive cadence: scan fast while GEAR is on screen (a panel OR a floating item
                                 // tooltip) so a hover sequence (ring1→ring2→ring1) is caught, not lost between scans
    readonly PanelOracle? _oracle;   // shared with the TTS LogWatcher: OCR Observes the panel it visually sees
    readonly Func<bool>? _diagEnabled;   // captureDiag setting, live-readable (like _debugMode — no engine rebuild on toggle)
    readonly string? _diagDir;           // %LOCALAPPDATA%\d4scanner\capture-diag
    readonly Func<string?>? _logPath;    // live TTS-log path (reflects a mid-session Move) for the tail snapshot
    System.Threading.Timer? _timer;
    volatile bool _disposed;             // cooperative shutdown: fast cadence widens the dispose-mid-scan window
    ulong _lastHash;
    string? _lastPanel;                  // panel from the last OCR'd frame — re-Observed on a hash-skipped frame so a
    bool _lastGearVisible;               // gear on screen last scan (a panel OR an item tooltip) → drives the fast
                                         // cadence; a static character sheet also keeps the oracle warm (rescue freshness)
    string? _lastPngPanel; long _lastPngTicks;   // PNG throttle: only on a panel transition + wall-clock spacing
    readonly List<Item> _orderedGear = new();
    readonly List<Item> _orderedInv  = new();

    static readonly System.Text.RegularExpressions.Regex ReItemPower =
        new(@"\d[\d,]*\s+Item Power", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    public OcrCaptureEngine(PanelOracle? oracle = null, int intervalMs = 6_000,
        Func<bool>? diagEnabled = null, string? diagDir = null, Func<string?>? logPath = null)
    { _oracle = oracle; _idleMs = intervalMs; _diagEnabled = diagEnabled; _diagDir = diagDir; _logPath = logPath; }

    const int DiagMaxMB = 400, DiagMaxAgeDays = 30, PngThrottleMs = 4000, PngMaxEdge = 1400;

    public void Start()
    {
        _timer?.Dispose();
        // One-shot self-rescheduling timer: the period is data-driven (adaptive cadence), re-armed ONLY from
        // the timer callback (TickAsync) so a manual ScanNow never perturbs the cadence; the re-arm is
        // dispose-guarded. Sweep stale diagnostics once at start, like the log prune.
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, 2000, System.Threading.Timeout.Infinite);
        if (_diagEnabled?.Invoke() == true && _diagDir != null)
            try { CaptureDiag.Prune(_diagDir, DiagMaxMB, DiagMaxAgeDays); } catch { }
    }

    async Task TickAsync()
    {
        try { await ScanCoreAsync().ConfigureAwait(false); }
        finally
        {
            if (!_disposed)
                try { _timer?.Change(CaptureDiag.NextIntervalMs(_lastGearVisible, ActiveMs, _idleMs), System.Threading.Timeout.Infinite); }
                catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Manual "Scan now" — one scan, no cadence reschedule (the periodic timer keeps its own clock).</summary>
    public Task ScanNowAsync() => ScanCoreAsync();

    int _scanning;   // 0 = idle, 1 = a scan is running
    async Task ScanCoreAsync()
    {
        // Skip-if-busy: a 4K WGC grab + OCR can exceed the interval, so the next timer tick must not start a
        // second scan that races the shared _orderedGear/_orderedInv/_lastHash state (torn lists).
        if (System.Threading.Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            var proc = Process.GetProcessesByName("Diablo IV").FirstOrDefault();
            if (proc == null) return;
            if (GetForegroundWindow() != proc.MainWindowHandle) return;

            var grab = await WindowsGraphicsCapture.GrabAsync().ConfigureAwait(false);
            using var bmp = grab.Bitmap;
            if (bmp == null) return;

            var hash = FrameHash(bmp);
            if (hash == _lastHash)
            {
                // Unchanged frame: skip OCR, but KEEP THE ORACLE WARM — re-Observe the last-known panel so a
                // perfectly static character-sheet hover doesn't let the worn-gear rescue's 25 s window go
                // stale (a real v0.79 hole: Observe used to fire only on a CHANGED frame).
                _oracle?.Observe(_lastPanel, DateTime.UtcNow.Ticks);
                return;
            }
            _lastHash = hash;

            // `using`: the SoftwareBitmap is consumed by RecognizeAsync and not referenced afterward.
            using var sb = await BitmapToSoftwareBitmapAsync(bmp).ConfigureAwait(false);
            if (sb == null) return;
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return;
            var result = await engine.RecognizeAsync(sb).AsTask().ConfigureAwait(false);
            if (result == null) return;

            var lines = result.Lines.Select(l => l.Text).ToList();
            var panel = PanelOracle.Detect(lines);   // computed ONCE — fed to the oracle, the items, AND the diag
            _oracle?.Observe(panel, DateTime.UtcNow.Ticks);
            // Gear-visible drives the fast cadence. A floating item tooltip often has NO panel chrome (Detect
            // returns null while the tooltip occludes the sheet), so key off the tooltip's "Item Power" line too —
            // else a ring hover sits on the idle cadence and falls between scans (the measured reason ring captures
            // were sparse: tooltips kept landing 20 s apart because their frame had panel==null).
            bool hasTooltip = lines.Any(l => ReItemPower.IsMatch(l));
            _lastPanel = panel; _lastGearVisible = panel != null || hasTooltip;

            ProcessOcrLines(lines, panel);

            if (_diagEnabled?.Invoke() == true && _diagDir != null)
                try { WriteDiag(result, panel, bmp, grab.Path); } catch { /* diagnostics never break the scan */ }
        }
        catch { /* best-effort */ }
        finally { System.Threading.Interlocked.Exchange(ref _scanning, 0); }
    }

    // panel is computed once in ScanCoreAsync (fed to the oracle + diag too) and passed in here.
    void ProcessOcrLines(List<string> lines, string? panel)
    {
        var blocks = ExtractTooltipBlocks(lines);
        bool changed = false;
        foreach (var block in blocks)
        {
            var item = GearParser.ParseTooltipLines(block);
            if (item == null) continue;
            item.Source = ItemSource.Ocr;
            item.UiPanel = panel;
            item.LastScannedTicks = DateTime.UtcNow.Ticks;
            // Worn ONLY on an explicit character-panel signal. Un-paneled tooltips (ground loot, vendor,
            // trade, or a bag hover whose header word OCR missed this frame) must never masquerade as
            // equipped — they poisoned the upgrade bar and hid the player's own bag items from All Items.
            item.Equipped = panel == "Character";
            item.Context  = item.Equipped ? UiContext.WornGear : UiContext.BagItem;

            if (item.Equipped) _orderedGear.Add(item);
            else               _orderedInv.Add(item);
            changed = true;
        }

        if (!changed || _disposed) return;   // a disposed/replaced engine must not push stale OCR gear

        if (_orderedGear.Count > 2000) _orderedGear.RemoveRange(0, _orderedGear.Count - 1000);
        if (_orderedInv.Count  > 2000) _orderedInv.RemoveRange(0, _orderedInv.Count - 1000);
        Updated?.Invoke(new LiveBuild
        {
            Gear      = LogWatcher.LatestPerSlot(_orderedGear),
            // NO contentIdentity here (unlike the TTS watcher): OCR mis-reads fork values between
            // re-scans of the SAME item, so content-fingerprint dedup would multiply phantom copies.
            Inventory = LogWatcher.LatestPerSlot(_orderedInv, 15),
        });
    }

    static List<List<string>> ExtractTooltipBlocks(List<string> lines)
    {
        var blocks = new List<List<string>>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!ReItemPower.IsMatch(lines[i])) continue;
            // Walk backward to find the ALL-CAPS item name
            int nameIdx = -1;
            for (int j = i - 1; j >= Math.Max(0, i - 8); j--)
            {
                if (IsAllCapsName(GearParser.Clean(lines[j]))) { nameIdx = j; break; }
            }
            if (nameIdx < 0) continue;
            var block = new List<string>();
            for (int j = nameIdx; j <= Math.Min(lines.Count - 1, i + 12); j++)
                block.Add(lines[j]);
            blocks.Add(block);
            i += 3;  // skip past this tooltip's Item Power anchor
        }
        return blocks;
    }

    static bool IsAllCapsName(string s)
    {
        if (s.Length < 2 || s.Length > 64) return false;
        var letters = s.Where(char.IsLetter).ToList();
        return letters.Count >= 2 && letters.All(char.IsUpper);
    }

    static unsafe ulong FrameHash(System.Drawing.Bitmap bmp)
    {
        const int S = 64;
        int sh = Math.Max(1, S * bmp.Height / bmp.Width);
        using var small = new System.Drawing.Bitmap(bmp, S, sh);
        var bits = small.LockBits(new System.Drawing.Rectangle(0, 0, S, sh),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        ulong h = 0;
        var ptr = (byte*)bits.Scan0;
        for (int y = 0; y < sh; y += 4)
            for (int x = 0; x < S; x += 4)
            {
                byte* px = ptr + y * bits.Stride + x * 4;
                h = h * 1000003 ^ (ulong)((px[2] >> 2) | ((px[1] >> 2) << 8) | ((px[0] >> 2) << 16));
            }
        small.UnlockBits(bits);
        return h;
    }

    static async Task<SoftwareBitmap?> BitmapToSoftwareBitmapAsync(System.Drawing.Bitmap bmp)
    {
        using var iras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        var writer = new Windows.Storage.Streams.DataWriter(iras);
        writer.WriteBytes(ms.ToArray());
        await writer.StoreAsync().AsTask().ConfigureAwait(false);
        writer.DetachStream();   // detach before disposing: the writer's Dispose() closes its stream, but the decoder below still needs iras
        writer.Dispose();
        iras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(iras).AsTask().ConfigureAwait(false);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().ConfigureAwait(false);
    }

    // ---- capture diagnostics (Phase 0): one paired record per OCR scan, JSON always + PNG rarely ----
    void WriteDiag(OcrResult result, string? panel, System.Drawing.Bitmap bmp, string capturePath)
    {
        long now = DateTime.UtcNow.Ticks;
        var words = new List<CaptureDiag.DiagWord>();
        foreach (var ln in result.Lines)
            foreach (var w in ln.Words)
            {
                var r = w.BoundingRect;
                words.Add(new CaptureDiag.DiagWord(w.Text,
                    (int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height)));
            }
        var lines = result.Lines.Select(l => l.Text).ToList();
        var tail = CaptureDiag.TailLines(_logPath?.Invoke());
        var stem = CaptureDiag.Stem(now);

        // PNG is the expensive artifact: only on a panel TRANSITION, wall-clock spaced, and with disk headroom
        // — downscaled. The JSON (words + rects + TTS tail) carries the analysis; the PNG is for eyeballing.
        string? pngFile = null;
        bool panelChanged = !string.Equals(panel, _lastPngPanel, StringComparison.Ordinal);
        bool spaced = now - _lastPngTicks > TimeSpan.FromMilliseconds(PngThrottleMs).Ticks;
        if (panel != null && panelChanged && spaced && DiskHasHeadroom(_diagDir!))
            try
            {
                Directory.CreateDirectory(_diagDir!);
                SaveDownscaledPng(bmp, Path.Combine(_diagDir!, stem + ".png"), PngMaxEdge);
                pngFile = stem + ".png"; _lastPngPanel = panel; _lastPngTicks = now;
            }
            catch { pngFile = null; }

        var rec = new CaptureDiag.DiagRecord(CaptureDiag.SchemaVersion, now, CaptureDiag.IsoSecond(now), panel,
            capturePath, bmp.Width, bmp.Height, false, pngFile, words, lines, tail);
        Directory.CreateDirectory(_diagDir!);
        File.WriteAllText(Path.Combine(_diagDir!, stem + ".json"), CaptureDiag.ToJson(rec));
        CaptureDiag.Prune(_diagDir!, DiagMaxMB, DiagMaxAgeDays);   // self-limit even if Start's sweep was skipped
    }

    static bool DiskHasHeadroom(string dir)
    { try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace > 1L * 1024 * 1024 * 1024; } catch { return true; } }

    static void SaveDownscaledPng(System.Drawing.Bitmap src, string destPath, int maxEdge)
    {
        double scale = Math.Min(1.0, (double)maxEdge / Math.Max(src.Width, src.Height));
        int dw = Math.Max(1, (int)(src.Width * scale)), dh = Math.Max(1, (int)(src.Height * scale));
        using var small = new System.Drawing.Bitmap(dw, dh);
        using (var g = System.Drawing.Graphics.FromImage(small))
        { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, dw, dh); }
        small.Save(destPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void Dispose() { _disposed = true; _timer?.Dispose(); }   // cooperative: flag first, so an in-flight scan bails
}
