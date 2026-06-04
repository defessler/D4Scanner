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
/// <see cref="LiveBuild"/>. Also saves a character portrait when the character panel is open.
/// </summary>
public sealed class OcrCaptureEngine : IDisposable
{
    public event Action<LiveBuild>? Updated;

    readonly int _intervalMs;
    System.Threading.Timer? _timer;
    ulong _lastHash;
    readonly List<Item> _orderedGear = new();
    readonly List<Item> _orderedInv  = new();

    static readonly System.Text.RegularExpressions.Regex ReItemPower =
        new(@"\d[\d,]*\s+Item Power", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    public OcrCaptureEngine(int intervalMs = 20_000) => _intervalMs = intervalMs;

    public void Start()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => _ = ScanCoreAsync(), null, 2000, _intervalMs);
    }

    public Task ScanNowAsync() => ScanCoreAsync();

    async Task ScanCoreAsync()
    {
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

            var sb = await BitmapToSoftwareBitmapAsync(bmp).ConfigureAwait(false);
            if (sb == null) return;

            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return;

            var result = await engine.RecognizeAsync(sb).AsTask().ConfigureAwait(false);
            if (result == null) return;

            var lines = result.Lines.Select(l => l.Text).ToList();
            ProcessOcrLines(lines);
        }
        catch { /* best-effort */ }
    }

    void ProcessOcrLines(List<string> lines)
    {
        // Detect panel context from well-known navigation lines
        string? panel = null;
        foreach (var ln in lines)
        {
            var c = GearParser.Clean(ln);
            if (c.Equals("Equipment", StringComparison.OrdinalIgnoreCase) ||
                c.Equals("Head",      StringComparison.OrdinalIgnoreCase) ||
                c.Equals("Torso",     StringComparison.OrdinalIgnoreCase))
            { panel = "Character"; break; }
            if (c.Equals("Inventory", StringComparison.OrdinalIgnoreCase)) { panel = "Inventory"; break; }
            if (c.Equals("Stash",     StringComparison.OrdinalIgnoreCase)) { panel = "Stash";     break; }
        }

        var blocks = ExtractTooltipBlocks(lines);
        bool changed = false;
        foreach (var block in blocks)
        {
            var item = GearParser.ParseTooltipLines(block);
            if (item == null) continue;
            item.Source = ItemSource.Ocr;
            item.UiPanel = panel;
            item.LastScannedTicks = DateTime.UtcNow.Ticks;
            item.Equipped = panel != "Inventory" && panel != "Stash";
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
            Inventory = LogWatcher.LatestPerSlot(_orderedInv, 15),
        });

        if (panel == "Character") _ = TrySavePortraitAsync();
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
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
        ms.Seek(0, SeekOrigin.Begin);
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream()).AsTask().ConfigureAwait(false);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().ConfigureAwait(false);
    }

    static async Task TrySavePortraitAsync()
    {
        try
        {
            using var bmp = await WindowsGraphicsCapture.GrabAsync().ConfigureAwait(false);
            if (bmp == null) return;
            int w = bmp.Width, h = bmp.Height;
            int cropW = Math.Min(400, w / 3), cropH = Math.Min(520, h * 2 / 3);
            int cropX = Math.Max(0, (w - cropW) / 2);
            int cropY = Math.Max(0, (h - cropH) / 2 - h / 10);
            cropW = Math.Min(cropW, w - cropX); cropH = Math.Min(cropH, h - cropY);
            using var crop = bmp.Clone(new System.Drawing.Rectangle(cropX, cropY, cropW, cropH),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var dest = Path.Combine(Path.GetDirectoryName(TargetLoader.DefaultLogPath())!, "character.png");
            crop.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch { }
    }

    public void Dispose() => _timer?.Dispose();
}
