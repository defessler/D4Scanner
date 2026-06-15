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

    static readonly string UpdateDir = Path.Combine(AppPaths.Root, "update");

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
        if (!System.Version.TryParse(Core(latest), out var l)) return false;
        if (!System.Version.TryParse(Core(running), out var r)) return false;
        return l > r;
        // strip the leading 'v' AND any pre-release suffix ("v1.0.0-rc1" → "1.0.0") so System.Version parses
        static string Core(string tag) => tag.TrimStart('v', 'V').Split('-')[0];
    }

    /// <summary>Extract the FULL release tag from a staged-asset filename ("D4Scanner-{tag}-win-x64.exe"),
    /// INCLUDING any hyphenated pre-release suffix — a naive Split('-')[1] truncates "v1.0.0-rc1" to "v1.0.0",
    /// which mis-labels the update and can rename/clean the wrong file. Null if the name isn't the asset shape.</summary>
    public static string? TagFromAssetFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        const string pre = "D4Scanner-", suf = "-win-x64";
        return name.StartsWith(pre, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suf, StringComparison.OrdinalIgnoreCase)
            && name.Length > pre.Length + suf.Length
            ? name.Substring(pre.Length, name.Length - pre.Length - suf.Length)
            : null;
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
            var tag = TagFromAssetFile(f);
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

    /// <summary>Apply a staged update right now: swap it in beside <paramref name="currentExe"/> under its
    /// versioned filename and return the new exe's path (null when nothing is staged or the swap failed).
    /// Safe to call from the running app — Windows allows renaming a running image, just not deleting it,
    /// so the leftover sidecar is swept by <see cref="CleanUpSuperseded"/> on a later launch.</summary>
    public static string? ApplyStagedNow(string currentExe)
    {
        var staged = FindStagedUpdate();
        if (staged == null) return null;
        var dir = Path.GetDirectoryName(currentExe) ?? ".";
        var newPath = Path.Combine(dir, $"D4Scanner-{staged.Value.tag}-win-x64.exe");
        if (!TryApplyStaged(staged.Value.path, currentExe, newPath)) return null;
        try { File.Delete(currentExe + ".old"); } catch { }   // fails while still running from it — swept next launch
        try
        {
            if (!string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase)
                && File.Exists(currentExe)) File.Delete(currentExe);
        }
        catch { }
        return newPath;
    }

    /// <summary>
    /// Sweep update leftovers from the exe's directory: every "*.exe.old" sidecar, and every versioned
    /// "D4Scanner-v*-win-x64.exe" STRICTLY OLDER than the running version. The .old of the exe we were
    /// just updated FROM can't be deleted while its process is exiting, so without this sweep old
    /// binaries accumulate forever (one per release). Never touches the running image or a newer exe
    /// (which could be a mid-flight update). Best-effort: locked files are skipped and caught next launch.
    /// </summary>
    public static void CleanUpSuperseded(string currentExe)
    {
        try
        {
            var dir = Path.GetDirectoryName(currentExe);
            if (dir == null || !Directory.Exists(dir)) return;
            var running = RunningVersion();
            var self = Path.GetFullPath(currentExe);

            foreach (var f in Directory.GetFiles(dir, "*.exe.old"))
                try { File.Delete(f); } catch { }

            foreach (var f in Directory.GetFiles(dir, "D4Scanner-v*-win-x64.exe"))
            {
                if (string.Equals(Path.GetFullPath(f), self, StringComparison.OrdinalIgnoreCase)) continue;
                var tag = TagFromAssetFile(f);
                if (tag != null && IsNewer(running, tag))   // running > tag → superseded binary
                    try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }
}
