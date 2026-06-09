using System.Net.Http;
using System.Text.Json;

namespace D4Scanner.Core;

/// <summary>
/// Silent in-place auto-updater for D4Scanner. Checks github.com/defessler/D4Scanner/releases
/// for a newer version, downloads the new exe to a staging directory, and applies it on the next
/// launch by renaming the running image out of the way and moving the staged file into its place.
/// No helper process, no admin rights, no installer — just a file rename and relaunch.
/// </summary>
public static class Updater
{
    public const string Repo = "defessler/D4Scanner";

    static readonly string UpdateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "d4scanner", "update");

    // GitHub API requires a User-Agent; reuse the same string the rest of the app uses.
    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("d4scanner");
        return h;
    }

    // ---- version helpers ----

    /// <summary>Returns the running app's version as "vX.Y.Z" to match GitHub tag_name. Reads the ENTRY
    /// (App) assembly — Updater lives in Core, whose own assembly version is unset (defaults to 1.0.0),
    /// so GetExecutingAssembly() here would report a bogus v1.0.0 in non-CI builds.</summary>
    public static string RunningVersion() =>
        "v" + ((System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly())
               .GetName().Version?.ToString(3) ?? "0.0.0");

    public static bool IsNewer(string latest, string running)
    {
        if (!System.Version.TryParse(latest.TrimStart('v'), out var l)) return false;
        if (!System.Version.TryParse(running.TrimStart('v'), out var r)) return false;
        return l > r;
    }

    // ---- GitHub API ----

    /// <summary>Returns the latest release tag (e.g. "v0.6.3"), or null on network/parse error.</summary>
    public static async Task<string?> GetLatestTagAsync()
    {
        var info = await GetLatestReleaseInfoAsync();
        return info?.tag;
    }

    /// <summary>Returns tag and release-notes body for the latest release, or null on error.</summary>
    public static async Task<(string tag, string body)?> GetLatestReleaseInfoAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag  = root.GetProperty("tag_name").GetString() ?? "";
            var body = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
            return (tag, body);
        }
        catch { return null; }
    }

    // ---- staging ----

    static string StagedPath(string tag) =>
        Path.Combine(UpdateDir, $"D4Scanner-{tag}-win-x64.exe");

    /// <summary>Returns the staged update file and its tag if one is newer than the running version, else null.</summary>
    public static (string path, string tag)? FindStagedUpdate()
    {
        if (!Directory.Exists(UpdateDir)) return null;
        var running = RunningVersion();
        foreach (var f in Directory.GetFiles(UpdateDir, "D4Scanner-v*-win-x64.exe"))
        {
            // filename: "D4Scanner-v0.6.3-win-x64.exe" → parts[1] = "v0.6.3"
            var parts = Path.GetFileNameWithoutExtension(f).Split('-');
            var tag = parts.Length >= 2 ? parts[1] : null;
            if (tag != null && IsNewer(tag, running)) return (f, tag);
        }
        return null;
    }

    /// <summary>Download the release asset for <paramref name="tag"/> to the staging directory.
    /// Reports 0–100 progress via <paramref name="progress"/> if provided.</summary>
    public static async Task<bool> DownloadUpdateAsync(string tag, IProgress<double>? progress = null)
    {
        var url  = $"https://github.com/{Repo}/releases/download/{tag}/D4Scanner-{tag}-win-x64.exe";
        var dest = StagedPath(tag);
        var tmp  = dest + ".tmp";
        try
        {
            Directory.CreateDirectory(UpdateDir);
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;
            var buf = new byte[81920];
            using var stream = await resp.Content.ReadAsStreamAsync();
            using (var fs = File.Create(tmp))
            {
                int read;
                while ((read = await stream.ReadAsync(buf)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0) progress?.Report(downloaded * 100.0 / total);
                }
            }
            File.Move(tmp, dest, overwrite: true);
            progress?.Report(100);
            return true;
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            return false;
        }
    }

    // ---- apply on next launch ----

    /// <summary>
    /// Swap the staged update into the running exe's location and return true so the caller
    /// can immediately relaunch the new binary. If <paramref name="newExePath"/> is supplied,
    /// the update lands at that path (versioned filename) instead of overwriting the current name.
    /// </summary>
    public static bool TryApplyStaged(string stagedPath, string currentExe, string? newExePath = null)
    {
        var target = newExePath ?? currentExe;
        var old = currentExe + ".old";
        try
        {
            File.Move(currentExe, old, overwrite: true);
            try
            {
                bool sameVol = string.Equals(
                    Path.GetPathRoot(stagedPath),
                    Path.GetPathRoot(target),
                    StringComparison.OrdinalIgnoreCase);
                if (sameVol)
                    File.Move(stagedPath, target, overwrite: true);
                else
                {
                    File.Copy(stagedPath, target, overwrite: true);
                    try { File.Delete(stagedPath); } catch { }
                }
                return true;
            }
            catch
            {
                try { File.Move(old, currentExe, overwrite: true); } catch { }
                return false;
            }
        }
        catch { return false; }
    }

    /// <summary>Delete the .old sidecar left by a prior successful update (best-effort).</summary>
    public static void CleanUpOld(string currentExe)
    {
        try { var old = currentExe + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
    }
}
