using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace D4Scanner.Core;

public sealed class VisionResult
{
    public List<string> Aspects { get; set; } = new();
    public List<LiveSkill> Skills { get; set; } = new();
    public List<LiveParagon> Paragon { get; set; } = new();
    public string? Mercenary { get; set; }              // hired mercenary + skills, from a screenshot
    public List<string> Talismans { get; set; } = new(); // talismans/charms
    public List<string> Gems { get; set; } = new();      // socketed gems
    public List<string> Runes { get; set; } = new();     // socketed runes / runewords
}

/// <summary>
/// Port of parser/d4_vision_capture.py — sends D4 screenshots (paragon boards, skill tree, glyph
/// tooltips) to a vision-capable Claude model and extracts skills / paragon / glyph levels / aspects
/// via a forced structured-output tool. The gear half comes from the TTS log; this fills the rest.
/// </summary>
public static class VisionCapture
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };
    const string ApiUrl = "https://api.anthropic.com/v1/messages";
    public const string DefaultModel = "claude-opus-4-8";

    const string SystemPrompt =
        "You read Diablo IV UI screenshots and extract build data precisely. Only report what is visible. " +
        "For each paragon board give its name, the socketed glyph, and the glyph's numeric LEVEL if shown. " +
        "For skills give the name, rank (the n in n/n), whether it is a key passive, and whether it is slotted " +
        "on the action bar. If a Mercenary screen is shown, give the mercenary name and its chosen skills as one string. " +
        "List any talismans/charms, socketed gems, and socketed runes by name. " +
        "Do not invent values. Call emit_build_parts exactly once.";

    static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/png",
    };

    public static async Task<VisionResult> CaptureAsync(IReadOnlyList<string> images, string apiKey,
        string? model = null, Action<string>? log = null, CancellationToken ct = default)
    {
        log ??= _ => { };
        if (images.Count == 0) throw new InvalidOperationException("No screenshots selected.");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
        model ??= DefaultModel;

        var content = new List<object>();
        foreach (var p in images)
        {
            log($"reading {Path.GetFileName(p)} …");
            var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(p, ct));
            content.Add(new { type = "image", source = new { type = "base64", media_type = MediaType(p), data = b64 } });
        }
        content.Add(new { type = "text", text = "Extract the paragon boards (+ glyph + glyph level), skills (+ ranks, key passives), and any aspect names from these screenshots. Call emit_build_parts once." });

        var tool = new
        {
            name = "emit_build_parts",
            description = "Return the paragon boards, glyphs, skills, key passives, and aspect names visible in the screenshots.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    aspects = new { type = "array", items = new { type = "string" } },
                    skills = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string" },
                                rank = new { type = "integer" },
                                isKeyPassive = new { type = "boolean" },
                                slotted = new { type = "boolean" },
                            },
                            required = new[] { "name" },
                        },
                    },
                    paragon = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                board = new { type = "string" },
                                glyph = new { type = new[] { "string", "null" } },
                                glyphLevel = new { type = new[] { "integer", "null" } },
                            },
                            required = new[] { "board" },
                        },
                    },
                    mercenary = new { type = new[] { "string", "null" } },
                    talismans = new { type = "array", items = new { type = "string" } },
                    gems = new { type = "array", items = new { type = "string" } },
                    runes = new { type = "array", items = new { type = "string" } },
                },
                required = new[] { "skills", "paragon" },
            },
        };

        var body = new
        {
            model,
            max_tokens = 4096,
            system = SystemPrompt,
            tools = new[] { tool },
            tool_choice = new { type = "tool", name = "emit_build_parts" },
            messages = new[] { new { role = "user", content } },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        log("calling Claude vision …");
        using var resp = await Http.SendAsync(req, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API error {(int)resp.StatusCode}: {Trim(respText)}");

        using var doc = JsonDocument.Parse(respText);
        JsonElement input = default;
        bool found = false;
        if (doc.RootElement.TryGetProperty("content", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var block in arr.EnumerateArray())
                if (block.TryGetProperty("type", out var ty) && ty.GetString() == "tool_use"
                    && block.TryGetProperty("name", out var nm) && nm.GetString() == "emit_build_parts"
                    && block.TryGetProperty("input", out input)) { found = true; break; }
        if (!found) throw new InvalidOperationException("The model did not return structured build parts.");

        var result = new VisionResult();
        if (input.TryGetProperty("aspects", out var asp) && asp.ValueKind == JsonValueKind.Array)
            result.Aspects = asp.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();
        if (input.TryGetProperty("skills", out var sk) && sk.ValueKind == JsonValueKind.Array)
            foreach (var s in sk.EnumerateArray())
            {
                var name = s.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                if (name.Length == 0) continue;
                result.Skills.Add(new LiveSkill
                {
                    Name = name,
                    Rank = s.TryGetProperty("rank", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : (int?)null,
                    IsKeyPassive = s.TryGetProperty("isKeyPassive", out var kp) && kp.ValueKind == JsonValueKind.True,
                    Slotted = s.TryGetProperty("slotted", out var sl) && sl.ValueKind == JsonValueKind.True,
                });
            }
        if (input.TryGetProperty("paragon", out var pa) && pa.ValueKind == JsonValueKind.Array)
            foreach (var p in pa.EnumerateArray())
            {
                var board = p.TryGetProperty("board", out var b) ? (b.GetString() ?? "") : "";
                if (board.Length == 0) continue;
                result.Paragon.Add(new LiveParagon
                {
                    Board = board,
                    Glyph = p.TryGetProperty("glyph", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null,
                    GlyphLevel = p.TryGetProperty("glyphLevel", out var gl) && gl.ValueKind == JsonValueKind.Number ? gl.GetInt32() : (int?)null,
                });
            }
        if (input.TryGetProperty("mercenary", out var mc) && mc.ValueKind == JsonValueKind.String)
            result.Mercenary = mc.GetString();
        static List<string> Strs(JsonElement e, string key) =>
            e.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList() : new();
        result.Talismans = Strs(input, "talismans");
        result.Gems = Strs(input, "gems");
        result.Runes = Strs(input, "runes");
        log($"vision: {result.Skills.Count} skills, {result.Paragon.Count} boards, {result.Aspects.Count} aspects, " +
            $"{result.Gems.Count} gems, {result.Runes.Count} runes, {result.Talismans.Count} talismans");
        return result;
    }

    static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
