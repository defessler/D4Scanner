using System.Text;
using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Watches d4_tts.log and emits each completed item tooltip as a JSON line to d4_tts.jsonl.
/// Enables direct JSON consumption without raw-log parsing overhead.
///
/// Architecture: the DLL stays unchanged (simple, signed, stable). This C# post-processor
/// does the semantic lifting: it tails the .log, feeds lines through GearParser, and on block
/// completion serializes the Item to the .jsonl side-car file.
///
/// The app can read from .jsonl for fast-startup deserialization (BuildFromJsonl), falling back
/// to the raw .log if the .jsonl is absent or stale.
/// </summary>
public sealed class LogToJsonlConverter : IDisposable
{
    GearParser _parser = new();
    readonly string _logPath;
    readonly string _jsonlPath;
    long _pos;
    string _buf = "";
    System.Threading.Timer? _timer;

    public LogToJsonlConverter(string logPath)
    {
        _logPath = logPath;
        _jsonlPath = Path.Combine(
            Path.GetDirectoryName(logPath) ?? "",
            Path.GetFileNameWithoutExtension(logPath) + ".jsonl");
    }

    /// <summary>Convert any buffered log content immediately, then poll every <paramref name="pollMs"/> ms.</summary>
    public void Start(int pollMs = 500)
    {
        Poll();
        _timer = new System.Threading.Timer(_ => Poll(), null, pollMs, pollMs);
    }

    void Poll()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            long size = new FileInfo(_logPath).Length;
            if (size < _pos) { _pos = 0; _buf = ""; _parser = new(); }   // log rotated/cleared
            if (size <= _pos) return;

            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(_pos, SeekOrigin.Begin);
            var bytes = new byte[size - _pos];
            int read = fs.Read(bytes, 0, bytes.Length);
            _pos = fs.Position;
            _buf += Encoding.UTF8.GetString(bytes, 0, read);

            var lines = _buf.Split('\n');
            _buf = lines[^1];   // keep partial last line
            for (int i = 0; i < lines.Length - 1; i++)
            {
                var item = _parser.Feed(lines[i]);
                if (item != null && GearParser.LooksLikeItem(item))
                    EmitJson(item);
            }
        }
        catch { /* file mid-write; retry next tick */ }
    }

    // Compact (non-indented) options so each item is exactly one line in the .jsonl file.
    static readonly JsonSerializerOptions CompactOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    void EmitJson(Item item)
    {
        try
        {
            var json = JsonSerializer.Serialize(item, CompactOpts);
            File.AppendAllText(_jsonlPath, json + "\n");
        }
        catch { }
    }

    /// <summary>
    /// Read back a LiveBuild from the .jsonl side-car (fast path — no parsing, just deserialise).
    /// Falls back to null if the file is absent or unreadable.
    /// </summary>
    public static LiveBuild? BuildFromJsonl(string logPath)
    {
        var jsonlPath = Path.Combine(
            Path.GetDirectoryName(logPath) ?? "",
            Path.GetFileNameWithoutExtension(logPath) + ".jsonl");
        if (!File.Exists(jsonlPath)) return null;
        try
        {
            var items = new List<Item>();
            foreach (var line in File.ReadLines(jsonlPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonSerializer.Deserialize<Item>(line, CompactOpts);
                if (item != null) items.Add(item);
            }
            return new LiveBuild { Gear = LogWatcher.LatestPerSlot(items) };
        }
        catch { return null; }
    }

    /// <summary>Delete the .jsonl side-car so it regenerates fresh on next Start().</summary>
    public void Invalidate()
    {
        try { if (File.Exists(_jsonlPath)) File.Delete(_jsonlPath); } catch { }
    }

    public void Dispose() => _timer?.Dispose();
}
