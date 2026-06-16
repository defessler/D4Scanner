using System.Globalization;

namespace D4Scanner.Core;

/// <summary>
/// TTS-log file management: rotation of the active <c>d4_tts.log</c> into a <c>logs\</c> archive
/// folder, retention pruning (file count + age), and a session index across the archives + active
/// file for "load your build from a previous session". UI-free and path-driven so it is headlessly
/// testable. Safe alongside the shim: saapi64 opens/appends/closes PER LINE (never holds a handle),
/// so a moved-away active file is simply recreated on its next write; the live watcher's shrink-reset
/// handles the cut-over. Callers gate rotation on "Diablo IV not running" so a mid-session rotation
/// can't archive hovers the watcher hasn't replayed yet.
/// </summary>
public static class LogStore
{
    /// <summary>One shim session (attach marker → next attach / EOF) inside a specific log file.
    /// Offsets are raw BYTE offsets into that file.</summary>
    public sealed record Session(string File, long StartOffset, long EndOffset, DateTimeOffset? Start, int LineCount)
    {
        public string Label =>
            (Start != null ? Start.Value.ToLocalTime().ToString("MMM d  HH:mm", CultureInfo.InvariantCulture) : "undated session")
            + $"  ·  {LineCount:#,0} lines";
    }

    static readonly byte[] AttachMarker = System.Text.Encoding.UTF8.GetBytes("=== d4scanner tts shim attached");

    public static string ArchiveDir(string activeLog) =>
        Path.Combine(Path.GetDirectoryName(activeLog) ?? ".", "logs");

    /// <summary>Archived log files, oldest → newest. Sorted by parsed (date, counter) — NOT raw lexical
    /// order — so chronology holds even when the counter isn't zero-padded: a plain string sort places
    /// "_10".."_12" (the newest of a heavy day) BEFORE "_2".."_9", which would make Prune delete the
    /// newest archives and the session list show them out of order.</summary>
    public static List<string> Archives(string activeLog)
    {
        try
        {
            var dir = ArchiveDir(activeLog);
            if (!Directory.Exists(dir)) return new();
            var stem = Path.GetFileNameWithoutExtension(activeLog);
            return Directory.GetFiles(dir, stem + ".*.log")
                .OrderBy(f => ArchiveSortKey(Path.GetFileName(f)), StringComparer.Ordinal).ToList();
        }
        catch { return new(); }
    }

    /// <summary>A lexically-sortable key "{date}_{counter:D6}" for an archive filename, so ordinal order
    /// matches chronological order regardless of the counter's zero-padding (legacy "_1" and new "_001"
    /// both map to "..._000001"). Unparseable names sort last (treated as newest, so Prune won't delete
    /// them first) by their raw name.</summary>
    static string ArchiveSortKey(string fileName)
    {
        var noExt = Path.GetFileNameWithoutExtension(fileName);   // "{stem}.{yyyy-MM-dd}_{n}"
        int us = noExt.LastIndexOf('_');
        if (us > 0 && long.TryParse(noExt.AsSpan(us + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return string.Concat(noExt.AsSpan(0, us), "_", n.ToString("D6", CultureInfo.InvariantCulture));
        return "~" + fileName;
    }

    /// <summary>Move the active log into the archive folder under a dated name
    /// (<c>logs\d4_tts.2026-06-12_001.log</c>; the zero-padded counter de-collides multiple rotations
    /// per day AND keeps lexical order chronological). The shim recreates the active file on its next
    /// write. Returns the archive path, or null when there was nothing to rotate / the move failed
    /// (e.g. a reader mid-poll — retry later).</summary>
    public static string? Rotate(string activeLog, DateTime? nowUtc = null)
    {
        try
        {
            if (!File.Exists(activeLog) || new FileInfo(activeLog).Length == 0) return null;
            var dir = ArchiveDir(activeLog);
            Directory.CreateDirectory(dir);
            var stem = Path.GetFileNameWithoutExtension(activeLog);
            var date = (nowUtc ?? DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (int n = 1; n < 1000; n++)
            {
                var candidate = Path.Combine(dir, $"{stem}.{date}_{n:D3}.log");
                if (File.Exists(candidate)) continue;
                File.Move(activeLog, candidate);
                return candidate;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Relocate the archive folder beside <paramref name="oldActiveLog"/> to sit beside
    /// <paramref name="newActiveLog"/> as part of a log MOVE. Cross-volume-safe (a plain
    /// <see cref="Directory.Move"/> throws across drives) and MERGES into an existing destination
    /// <c>logs\</c> folder instead of silently stranding the archives (which left a user's rotated
    /// history orphaned and un-prunable). Existing same-named files at the destination are preserved.
    /// No-op when there are no archives or source and destination resolve to the same folder. Copies
    /// before deleting, so a mid-move failure leaves the SOURCE archives intact (caller can roll back).</summary>
    public static void MoveArchives(string oldActiveLog, string newActiveLog)
    {
        var src = ArchiveDir(oldActiveLog);
        var dst = ArchiveDir(newActiveLog);
        if (!Directory.Exists(src) || string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(dst))
        {
            try { Directory.Move(src, dst); return; }       // fast path: same volume, destination absent
            catch (IOException) { /* cross-volume, or dst appeared — fall through to copy + delete */ }
        }
        Directory.CreateDirectory(dst);
        var copied = new List<string>();
        foreach (var f in Directory.GetFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(f));
            if (File.Exists(target)) continue;              // never clobber an archive already at the destination
            File.Copy(f, target); copied.Add(f);
        }
        foreach (var f in copied) { try { File.Delete(f); } catch { } }   // only delete sources we successfully copied
        try { if (!Directory.EnumerateFileSystemEntries(src).Any()) Directory.Delete(src); } catch { }
    }

    /// <summary>Retention: delete the OLDEST archives beyond <paramref name="maxFiles"/> and anything
    /// older than <paramref name="maxAgeDays"/> (by last-write time). Never touches the active log.
    /// Returns how many files were deleted.</summary>
    public static int Prune(string activeLog, int maxFiles, int maxAgeDays, DateTime? nowUtc = null)
    {
        int deleted = 0;
        try
        {
            var archives = Archives(activeLog);
            var cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-Math.Max(1, maxAgeDays));
            var byAge = archives.Where(f => { try { return File.GetLastWriteTimeUtc(f) < cutoff; } catch { return false; } }).ToList();
            foreach (var f in byAge) { try { File.Delete(f); deleted++; } catch { } }
            var remaining = archives.Except(byAge).ToList();   // intended-keep set, oldest→newest (no 2nd disk scan)
            int excess = remaining.Count - Math.Max(1, maxFiles);
            for (int i = 0; i < excess; i++) { try { File.Delete(remaining[i]); deleted++; } catch { } }   // oldest first
        }
        catch { }
        return deleted;
    }

    /// <summary>Every shim session across the archives and the active file, oldest → newest. Each
    /// session spans from its attach marker to the next one (or EOF) WITHIN one file — a session split
    /// by rotation appears as two entries, which is honest about what each file can replay.</summary>
    public static List<Session> Sessions(string activeLog)
    {
        var result = new List<Session>();
        foreach (var file in Archives(activeLog).Concat(File.Exists(activeLog) ? new[] { activeLog } : Array.Empty<string>()))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); } catch { continue; }
            var starts = new List<long>();
            for (long at = 0; at >= 0 && at < bytes.Length;)
            {
                int hit = bytes.AsSpan((int)at).IndexOf(AttachMarker);
                if (hit < 0) break;
                long abs = at + hit;
                long lineStart = abs;
                while (lineStart > 0 && bytes[lineStart - 1] != (byte)'\n') lineStart--;   // include the [ISO] prefix
                starts.Add(lineStart);
                at = abs + AttachMarker.Length;
            }
            for (int i = 0; i < starts.Count; i++)
            {
                long s = starts[i], e = i + 1 < starts.Count ? starts[i + 1] : bytes.Length;
                int lines = 0;
                for (long b = s; b < e; b++) if (bytes[b] == (byte)'\n') lines++;
                // the marker line's own [ISO] prefix is the session's start time
                int lineEnd = (int)s;
                while (lineEnd < e && bytes[lineEnd] != (byte)'\n' && lineEnd - s < 256) lineEnd++;
                var markerLine = System.Text.Encoding.UTF8.GetString(bytes, (int)s, lineEnd - (int)s);
                GearParser.CleanWithTime(markerLine, out var t);
                result.Add(new Session(file, s, e, t, lines));
            }
        }
        return result;
    }

    /// <summary>The raw lines of one session, for replay through <see cref="LogWatcher.BuildFromLines"/>.</summary>
    public static string[] ReadSession(Session s)
    {
        try
        {
            using var fs = new FileStream(s.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(s.StartOffset, SeekOrigin.Begin);
            var buf = new byte[s.EndOffset - s.StartOffset];
            fs.ReadExactly(buf);   // FileStream.Read may return a PARTIAL read for a large session; ReadExactly fills the buffer (or throws) so a big session never replays truncated
            return System.Text.Encoding.UTF8.GetString(buf).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }
}
