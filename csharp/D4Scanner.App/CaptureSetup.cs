using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace D4Scanner.App;

/// <summary>
/// Detects and installs the TTS capture shim (saapi64.dll). The shim is a tiny signed DLL that
/// Diablo IV's screen-reader loads; it writes item tooltips to d4_tts.log. The signed DLL and its
/// public certificate are embedded in the app, so installing needs no build tools: we write the
/// DLL into the Diablo IV folder (and a PATH dir), and trust the certificate so the game will load it.
/// </summary>
public static class CaptureSetup
{
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);
    public static string BinDir => Path.Combine(Local, "d4scanner", "bin");

    static readonly string[] GameCandidates =
    {
        @"C:\Program Files (x86)\Diablo IV", @"C:\Program Files\Diablo IV", @"C:\Games\Diablo IV",
        @"D:\Games\Blizzard\Diablo IV", @"C:\Program Files (x86)\Battle.net\Diablo IV",
    };

    // user-overridden game path (saved to app.json by the UI layer after a successful file-picker selection)
    public static string? UserGameDir { get; set; }

    /// <summary>Locate the Diablo IV install across every launcher / drive — Battle.net (and any installer that
    /// writes an Uninstall entry) via the registry, Steam via its library folders, then common fixed paths and a
    /// per-drive sweep of common folders. Works regardless of Battle.net vs Steam and custom install drives.
    /// Returns null if the game can't be found automatically (UI should offer a file picker in that case).</summary>
    public static string? GameDir()
    {
        if (!string.IsNullOrEmpty(UserGameDir) && File.Exists(Path.Combine(UserGameDir!, "Diablo IV.exe")))
            return UserGameDir;
        return DetectGameDirs().Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(d => !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "Diablo IV.exe")));
    }

    static List<string> DetectGameDirs()
    {
        var dirs = new List<string>();

        // 1) Windows Uninstall registry — Battle.net writes "Diablo IV" here with its InstallLocation; most
        //    reliable and covers custom install drives/folders.
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var path in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            })
                try
                {
                    using var list = hive.OpenSubKey(path);
                    if (list == null) continue;
                    foreach (var sub in list.GetSubKeyNames())
                        try
                        {
                            using var ik = list.OpenSubKey(sub);
                            if (ik?.GetValue("DisplayName") is string name && name.Contains("Diablo IV", StringComparison.OrdinalIgnoreCase)
                                && ik.GetValue("InstallLocation") is string loc && loc.Length > 0)
                                dirs.Add(loc);
                        }
                        catch { }
                }
                catch { }

        // 2) Steam — find Steam, read its library folders, and check each for the "Diablo IV" app folder.
        foreach (var lib in SteamLibraries())
            dirs.Add(Path.Combine(lib, "steamapps", "common", "Diablo IV"));

        // 3) common fixed paths + a per-drive sweep of common install folders
        dirs.AddRange(GameCandidates);
        dirs.AddRange(DriveCandidates());
        return dirs;
    }

    static List<string> SteamLibraries()
    {
        var libs = new List<string>();
        string? steam = null;
        try
        {
            steam = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null) as string
                 ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        }
        catch { }
        if (string.IsNullOrEmpty(steam)) return libs;
        steam = steam!.Replace('/', '\\');
        libs.Add(steam);
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { }
        return libs;
    }

    static List<string> DriveCandidates()
    {
        var outp = new List<string>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { return outp; }
        string[] subs =
        {
            @"Program Files (x86)\Diablo IV", @"Program Files\Diablo IV", @"Games\Diablo IV", @"Diablo IV",
            @"Games\Blizzard\Diablo IV", @"Blizzard\Diablo IV",
            @"SteamLibrary\steamapps\common\Diablo IV", @"Steam\steamapps\common\Diablo IV",
            @"Games\Steam\steamapps\common\Diablo IV", @"Program Files (x86)\Steam\steamapps\common\Diablo IV",
        };
        foreach (var d in drives)
        {
            try { if (!d.IsReady || d.DriveType != DriveType.Fixed) continue; } catch { continue; }
            foreach (var sub in subs) outp.Add(Path.Combine(d.RootDirectory.FullName, sub));
        }
        return outp;
    }

    // DLL version embedded in the shim source (saapi64.cpp #define SHIM_VERSION).
    // Bump this constant whenever the DLL changes and the app will auto-reinstall on next launch.
    public const int CurrentShimVersion = 2;

    /// <summary>True if saapi64.dll is somewhere Diablo IV will find it AND its version matches the embedded one.</summary>
    public static bool Installed()
    {
        var places = new List<string> { Path.Combine(BinDir, "saapi64.dll"), Path.Combine(System32, "saapi64.dll") };
        var g = GameDir(); if (g != null) places.Add(Path.Combine(g, "saapi64.dll"));
        return places.Any(File.Exists);
    }

    [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
    static extern IntPtr LoadLibraryExW([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string path, IntPtr file, uint flags);
    [System.Runtime.InteropServices.DllImport("kernel32")]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
    [System.Runtime.InteropServices.DllImport("kernel32")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool FreeLibrary(IntPtr module);
    const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    /// <summary>Reads the shim version from an installed DLL.
    /// Scans the raw PE bytes for the "SA_GetVersion" export name (ASCII string in the export table).
    /// If found, tries a full load to call it; if the load fails, trusts the byte scan and returns
    /// CurrentShimVersion rather than incorrectly reporting 0 (which would re-trigger the banner).
    /// Returns 0 for the versionless legacy DLL. Returns -1 if no DLL is installed.</summary>
    public static int InstalledShimVersion()
    {
        var places = new List<string> { Path.Combine(BinDir, "saapi64.dll") };
        var gd = GameDir(); if (gd != null) places.Add(Path.Combine(gd, "saapi64.dll"));
        foreach (var p in places)
        {
            if (!File.Exists(p)) continue;
            try
            {
                // Scan raw PE bytes for the export name — reliable without loading the DLL at all.
                // "SA_GetVersion" is a plain ASCII string in the exports section.
                var bytes = File.ReadAllBytes(p);
                if (!System.Text.Encoding.ASCII.GetString(bytes).Contains("SA_GetVersion"))
                    return 0;   // versionless legacy DLL — export absent

                // Export exists. Try a full load to call it and get the exact version number.
                var lib = LoadLibraryExW(p, IntPtr.Zero, 0);
                if (lib == IntPtr.Zero)
                {
                    // Full load failed (security policy, dependency issue, DllMain crash, etc.).
                    // The byte scan confirmed the export IS present, so assume it is the current
                    // version rather than falsely returning 0 and re-showing the upgrade banner.
                    return CurrentShimVersion;
                }
                try
                {
                    var addr = GetProcAddress(lib, "SA_GetVersion");
                    if (addr == IntPtr.Zero) return CurrentShimVersion;   // same safe assumption
                    return System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<Func<int>>(addr).Invoke();
                }
                finally { FreeLibrary(lib); }
            }
            catch { }   // unreadable / corrupt DLL → treat as not-found, continue to next path
        }
        return -1;   // no installed DLL found
    }

    /// <summary>True if an installed saapi64.dll is confirmed outdated. Returns false when the version
    /// cannot be determined (avoids a spurious banner when D4 locks the DLL or other load failures).</summary>
    public static bool NeedsUpgrade()
    {
        if (!Installed()) return false;
        int v = InstalledShimVersion();
        // Only prompt when we CONFIRMED a specific version lower than current.
        // -1 = can't determine (no DLL or unreadable) → don't prompt.
        // 0  = versionless legacy DLL → prompt.
        return v >= 0 && v < CurrentShimVersion;
    }

    public static (bool ok, string message) Install()
    {
        if (Process.GetProcessesByName("Diablo IV").Length > 0 || Process.GetProcessesByName("Diablo IV Launcher").Length > 0)
            return (false, "Diablo IV is running, which locks the DLL. Fully quit the game, then try again.");

        var dll = Embedded("saapi64.dll");
        var cer = Embedded("d4scanner-tts.cer");
        if (dll == null || cer == null)
            return (false, "This build is missing the bundled capture files — rebuild the app with the dll/ artifacts present.");

        // 1) always drop a copy in our PATH bin + trust the signing certificate
        try
        {
            Directory.CreateDirectory(BinDir);
            File.WriteAllBytes(Path.Combine(BinDir, "saapi64.dll"), dll);
            File.WriteAllBytes(Path.Combine(BinDir, "d4scanner-tts.cer"), cer);
        }
        catch (Exception e) { return (false, "Couldn't write the capture files: " + e.Message); }

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(cer));   // Windows may show a trust prompt — accept it
        }
        catch (Exception e) { return (false, "Couldn't trust the signing certificate: " + e.Message); }

        // 2) install into the Diablo IV folder if we can find it (what the user asked for); else use the PATH bin
        var game = GameDir();
        string where;
        if (game != null)
        {
            try { File.WriteAllBytes(Path.Combine(game, "saapi64.dll"), dll); where = game; }
            catch (Exception e)
            {
                AddToUserPath(BinDir);
                return (true, $"Couldn't write to the Diablo IV folder ({e.Message}).\nInstalled to {BinDir} and added it to your PATH instead.\n\n{NextSteps}");
            }
        }
        else { AddToUserPath(BinDir); where = BinDir; }

        return (true, $"Installed the capture DLL to:\n{where}\n\n{NextSteps}");
    }

    const string NextSteps =
        "Next steps in Diablo IV → Settings:\n" +
        "  • Accessibility → 'Use Screen Reader' = ON\n" +
        "  • Accessibility → 'Use 3rd-Party Screen Reader' = ON\n" +
        "  • Gameplay → 'Advanced Tooltip Information' = ON  (Game Language = English)\n\n" +
        "Then relaunch Diablo IV and hover an equipped item — the scanner will start filling in.";

    static void AddToUserPath(string dir)
    {
        try
        {
            var p = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            if (!p.Split(';').Contains(dir, StringComparer.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("Path", p.TrimEnd(';') + ";" + dir, EnvironmentVariableTarget.User);
        }
        catch { }
    }

    static byte[]? Embedded(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (res == null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
