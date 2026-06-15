using System.Globalization;
using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Phase-0 OCR↔TTS capture diagnostics (UI-free so it is headlessly testable). The App's OCR engine,
/// when the user turns on the <c>captureDiag</c> setting, writes one small JSON <see cref="DiagRecord"/>
/// per scan into <c>capture-diag\</c> — the OCR words WITH their on-screen rects, the detected panel, the
/// scan time, and a snapshot of the concurrent TTS log tail — so we can later (offline) match OCR reads to
/// the TTS log by second and map tooltip rect → slot. A frame PNG is saved rarely (a separate App concern).
/// This class owns the pure, testable parts: the record schema + JSON, the adaptive-cadence decision, the
/// timestamp keys, the bounded log-tail read, and the folder retention sweep (modeled on <see cref="LogStore.Prune"/>).
/// </summary>
public static class CaptureDiag
{
    public const int SchemaVersion = 1;

    /// <summary>One OCR word and its rect in the captured frame's pixel space (rounded from the WinRT
    /// Rect of doubles). Position is interpretable against the record's FrameW/FrameH.</summary>
    public sealed record DiagWord(string Text, int X, int Y, int W, int H);

    /// <summary>One OCR scan, paired with the concurrent TTS context. Self-contained and grep-joinable to
    /// the TTS log by <see cref="ScanIso"/> (the shim emits whole-second timestamps, so 1s is the match
    /// granularity). A sibling <c>&lt;stem&gt;.png</c> exists iff <see cref="PngFile"/> is set.</summary>
    public sealed record DiagRecord(
        int SchemaVersion,
        long ScanTicks,
        string ScanIso,
        string? Panel,
        string CapturePath,   // "wgc" | "printwindow"
        int FrameW,
        int FrameH,
        bool FrameUnchanged,
        string? PngFile,
        List<DiagWord> Words,
        List<string> Lines,
        List<string> TtsTail);

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static string ToJson(DiagRecord r) => JsonSerializer.Serialize(r, JsonOpts);
    public static DiagRecord? FromJson(string json)
    { try { return JsonSerializer.Deserialize<DiagRecord>(json, JsonOpts); } catch { return null; } }

    /// <summary>Adaptive cadence: scan fast while GEAR is on screen (a panel OR a floating item tooltip) to
    /// catch a hover sequence for the fusion, idle during gameplay. Keyed on the LAST SUCCESSFUL scan's gear
    /// visibility — never a frame-hash-skipped or not-foreground tick (those carry no fresh signal and must not
    /// flip the cadence to idle). NB: a tooltip frame often has no panel chrome, so the caller must OR in an
    /// "Item Power" tooltip signal, not just a detected panel — else ring/weapon hovers fall between idle scans.</summary>
    public static int NextIntervalMs(bool gearVisible, int activeMs, int idleMs) => gearVisible ? activeMs : idleMs;

    /// <summary>The whole-second UTC string the shim writes (<c>YYYY-MM-DDTHH:MM:SSZ</c>) — the literal
    /// key for joining an OCR record to TTS log lines.</summary>
    public static string IsoSecond(long utcTicks) =>
        new DateTimeOffset(utcTicks, TimeSpan.Zero).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>A filesystem-safe per-scan stem with millisecond ordering (the JSON + PNG share it).</summary>
    public static string Stem(long utcTicks) =>
        new DateTimeOffset(utcTicks, TimeSpan.Zero).ToString("yyyy-MM-ddTHH-mm-ss-fffZ", CultureInfo.InvariantCulture);

    /// <summary>Read the last <paramref name="maxLines"/> non-empty lines of the TTS log AT SCAN TIME
    /// (rotation/move-proof, unlike an offline join). Bounded: seeks near EOF and reads at most
    /// <paramref name="maxBytes"/>, shared-read so it never collides with the shim's per-line appends.</summary>
    public static List<string> TailLines(string? logPath, int maxLines = 40, int maxBytes = 64 * 1024)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(logPath)) return result;
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long len = fs.Length;
            if (len == 0) return result;
            long from = Math.Max(0, len - maxBytes);
            // When the window starts mid-file, seek ONE byte earlier so buf[0] is a sentinel: '\n' there means
            // the window opens exactly on a line boundary (keep the first whole line); anything else means the
            // first line is a partial fragment (drop it). A blind RemoveAt(0) was wrong on the boundary case —
            // a fixed maxBytes can land exactly on a newline and then it ate a real line.
            bool atBof = from == 0;
            long readFrom = atBof ? 0 : from - 1;
            fs.Seek(readFrom, SeekOrigin.Begin);
            var buf = new byte[len - readFrom];
            fs.ReadExactly(buf);   // FileStream.Read may return a PARTIAL read; ReadExactly fills the buffer (or throws),
                                   // so a short read can't silently truncate the NEWEST tail lines (mirrors LogStore.ReadSession)
            bool headPartial = !atBof && buf[0] != (byte)'\n';
            var text = System.Text.Encoding.UTF8.GetString(buf, 0, buf.Length);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (headPartial && lines.Count > 0) lines.RemoveAt(0);
            return lines.Count > maxLines ? lines.GetRange(lines.Count - maxLines, maxLines) : lines;
        }
        catch { return result; }
    }

    /// <summary>Retention sweep for the diagnostic folder: delete OLDEST records first until the folder is
    /// under <paramref name="maxTotalMB"/>, and drop anything older than <paramref name="maxAgeDays"/>.
    /// Records are pruned as a UNIT by shared stem so a <c>.json</c> and its <c>.png</c> never orphan.
    /// Never throws (per-file try/catch). Returns the number of FILES deleted.</summary>
    public static int Prune(string dir, int maxTotalMB, int maxAgeDays, DateTime? nowUtc = null)
    {
        int deleted = 0;
        try
        {
            if (!Directory.Exists(dir)) return 0;
            // group files by stem (filename without extension); a "record" is all files sharing a stem.
            var byStem = Directory.EnumerateFiles(dir)
                .Select(f => new FileInfo(f))
                .GroupBy(fi => Path.GetFileNameWithoutExtension(fi.Name), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Stem: g.Key, Files: g.ToList(),
                              Bytes: g.Sum(fi => { try { return fi.Length; } catch { return 0L; } }),
                              Written: g.Max(fi => { try { return fi.LastWriteTimeUtc; } catch { return DateTime.MinValue; } })))
                .OrderBy(r => r.Stem, StringComparer.Ordinal)   // stem sorts chronologically (ISO timestamp)
                .ToList();

            // age sweep first
            var cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-Math.Max(1, maxAgeDays));
            foreach (var rec in byStem.Where(r => r.Written < cutoff).ToList())
            {
                foreach (var fi in rec.Files) { try { fi.Delete(); deleted++; } catch { } }
                byStem.Remove(rec);
            }

            // total-MB budget — delete oldest stems until under cap. Decrement `total` only by bytes ACTUALLY
            // freed: a swallowed Delete (a locked file held by AV/indexer) must not make `total` lie and exit
            // the loop with the file still on disk and the folder over cap. Advancing past a stuck stem is fine —
            // a later Prune (every WriteDiag re-runs it) re-enumerates from disk and retries.
            long budget = (long)Math.Max(1, maxTotalMB) * 1024 * 1024;
            long total = byStem.Sum(r => r.Bytes);
            int i = 0;
            while (total > budget && i < byStem.Count)
            {
                foreach (var fi in byStem[i].Files)
                { try { long b = fi.Length; fi.Delete(); deleted++; total -= b; } catch { } }
                i++;
            }
        }
        catch { }
        return deleted;
    }
}
