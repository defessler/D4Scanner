namespace D4Scanner.Core;

/// <summary>Single source of truth for the app's on-disk locations under <c>%LOCALAPPDATA%\d4scanner</c>.
/// Centralizing these retires the ~9 hand-rolled copies of the same path scattered across Core — and closes
/// the latent filename drift between the two readers of the Maxroll item DB (the importer and the icon index),
/// which each spelled <c>maxroll_data.min.json</c> independently.</summary>
public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\d4scanner — the app's data root (TTS log, profiles, settings, season override).</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d4scanner");

    /// <summary>%LOCALAPPDATA%\d4scanner\cache — downloaded/derived data (Maxroll DB, build index, icon art).</summary>
    public static string CacheDir => Path.Combine(Root, "cache");

    /// <summary>The cached Maxroll item DB — read by BOTH the importer and BaseIconIndex, so the filename is
    /// spelled here exactly once.</summary>
    public static string GameDataCache => Path.Combine(CacheDir, "maxroll_data.min.json");
}
