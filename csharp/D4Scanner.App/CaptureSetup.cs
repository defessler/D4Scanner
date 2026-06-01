using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

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
        @"D:\Games\Blizzard\Diablo IV", @"C:\Program Files (x86)\Diablo IV",
        @"C:\Program Files\Diablo IV", @"C:\Games\Diablo IV", @"C:\Program Files (x86)\Battle.net\Diablo IV",
    };

    public static string? GameDir() => GameCandidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "Diablo IV.exe")));

    /// <summary>True if saapi64.dll is somewhere Diablo IV will find it (game folder, System32, or our PATH bin).</summary>
    public static bool Installed()
    {
        var places = new List<string> { Path.Combine(BinDir, "saapi64.dll"), Path.Combine(System32, "saapi64.dll") };
        var g = GameDir(); if (g != null) places.Add(Path.Combine(g, "saapi64.dll"));
        return places.Any(File.Exists);
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
