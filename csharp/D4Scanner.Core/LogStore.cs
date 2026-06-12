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

    /// <summary>Archived log files, oldest → newest (the rotation naming sorts lexically by date).</summary>
    public static List<string> Archives(string activeLog)
    {
        try
        {
            var dir = ArchiveDir(activeLog);
            if (!Directory.Exists(dir)) return new();
            var stem = Path.GetFileNameWithoutExtension(activeLog);
            return Directory.GetFiles(dir, stem + ".*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Move the active log into the archive folder under a dated name
    /// (<c>logs\d4_tts.2026-06-12_1.log</c>; the counter suffix de-collides multiple rotations per
    /// day). The shim recreates the active file on its next write. Returns the archive path, or null
    /// when there was nothing to rotate / the move failed (e.g. a reader mid-poll — retry later).</summary>
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
                var candidate = Path.Combine(dir, $"{stem}.{date}_{n}.log");
                if (File.Exists(candidate)) continue;
                File.Move(activeLog, candidate);
                return candidate;
            }
            return null;
        }
        catch { return null; }
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
            var remaining = Archives(activeLog);
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
            int read = fs.Read(buf, 0, buf.Length);
            return System.Text.Encoding.UTF8.GetString(buf, 0, read).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }
}
