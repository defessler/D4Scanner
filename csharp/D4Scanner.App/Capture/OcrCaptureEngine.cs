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

    readonly int _intervalMs;
    readonly PanelOracle? _oracle;   // shared with the TTS LogWatcher: OCR Observes the panel it visually sees
    System.Threading.Timer? _timer;
    ulong _lastHash;
    readonly List<Item> _orderedGear = new();
    readonly List<Item> _orderedInv  = new();

    static readonly System.Text.RegularExpressions.Regex ReItemPower =
        new(@"\d[\d,]*\s+Item Power", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    public OcrCaptureEngine(PanelOracle? oracle = null, int intervalMs = 20_000)
    { _oracle = oracle; _intervalMs = intervalMs; }

    public void Start()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => _ = ScanCoreAsync(), null, 2000, _intervalMs);
    }

    public Task ScanNowAsync() => ScanCoreAsync();

    int _scanning;   // 0 = idle, 1 = a scan is running
    async Task ScanCoreAsync()
    {
        // Skip-if-busy: a 4K WGC grab + OCR can exceed the 20 s interval, so the next timer tick must not
        // start a second scan that races the shared _orderedGear/_orderedInv/_lastHash state (torn lists).
        if (System.Threading.Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            var proc = Process.GetProcessesByName("Diablo IV").FirstOrDefault();
            if (proc == null) return;
            if (GetForegroundWindow() != proc.MainWindowHandle) return;

            using var bmp = await WindowsGraphicsCapture.GrabAsync().ConfigureAwait(false);
            if (bmp == null) return;

            var hash = FrameHash(bmp);
            if (hash == _lastHash) return;
            _lastHash = hash;

            // `using`: the SoftwareBitmap is consumed by RecognizeAsync below and not referenced
            // afterward — dispose it deterministically rather than leaking a full-screen native
            // bitmap to the finalizer on every changed-frame scan (same class as the WGC grab fix).
            using var sb = await BitmapToSoftwareBitmapAsync(bmp).ConfigureAwait(false);
            if (sb == null) return;

            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return;

            var result = await engine.RecognizeAsync(sb).AsTask().ConfigureAwait(false);
            if (result == null) return;

            var lines = result.Lines.Select(l => l.Text).ToList();
            ProcessOcrLines(lines);
        }
        catch { /* best-effort */ }
        finally { System.Threading.Interlocked.Exchange(ref _scanning, 0); }
    }

    void ProcessOcrLines(List<string> lines)
    {
        // Detect which panel is open from on-screen chrome, then feed the shared oracle so the TTS classifier
        // can use it as worn/browsed ground truth (the OCR↔TTS sensor fusion). PanelOracle.Detect lives in
        // Core (pure string logic) so it is headlessly testable.
        string? panel = PanelOracle.Detect(lines);
        _oracle?.Observe(panel, DateTime.UtcNow.Ticks);

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

        if (!changed) return;

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

    public void Dispose() => _timer?.Dispose();
}
