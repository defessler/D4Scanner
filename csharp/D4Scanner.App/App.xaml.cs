using D4Scanner.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace D4Scanner.App;

public partial class App : System.Windows.Application
{
    static System.Threading.Mutex? _singleInstance;

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // "--render <out.png>": render the window to a PNG with no visible window, then exit.
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Headless render runs BEFORE the single-instance guard: it's a short-lived, windowless export, so
        // it must not be blocked just because the real app is already running (the mutex would short-circuit it).
        int idx = System.Array.IndexOf(e.Args, "--render");
        if (idx >= 0 && idx + 1 < e.Args.Length)
        {
            // optional size: "--render <out.png> [width] [height]" — verify reflow / on-screen fit headlessly
            int w = 1300, h = 2100;
            if (idx + 2 < e.Args.Length && int.TryParse(e.Args[idx + 2], out var pw)) w = pw;
            if (idx + 3 < e.Args.Length && int.TryParse(e.Args[idx + 3], out var ph)) h = ph;
            try { new MainWindow().HeadlessRender(e.Args[idx + 1], w, h); }
            catch (System.Exception ex) { System.Console.Error.WriteLine("render failed: " + ex); }
            Shutdown(0);
            return;
        }

        // Single-instance guard: if another D4Scanner is already running, bring it to the front.
        _singleInstance = new System.Threading.Mutex(true, "D4Scanner.App.SingleInstance", out bool isNew);
        if (!isNew)
        {
            var existing = Process.GetProcessesByName("D4Scanner").FirstOrDefault(p => p.Id != Process.GetCurrentProcess().Id);
            if (existing != null && existing.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(existing.MainWindowHandle, 9 /* SW_RESTORE */);
                SetForegroundWindow(existing.MainWindowHandle);
            }
            Shutdown(0);
            return;
        }

        // Auto-update: if a newer staged exe is already downloaded from a previous session,
        // apply it now (before any window appears) — rename the running image, copy the new
        // one into its place, relaunch, and exit.  Falls back gracefully if the swap fails.
        var staged = Updater.FindStagedUpdate();
        if (staged.HasValue)
        {
            var exe = System.Environment.ProcessPath
                   ?? Process.GetCurrentProcess().MainModule!.FileName;
            // Place the new exe with its version in the filename (same directory as the current exe)
            var dir      = System.IO.Path.GetDirectoryName(exe) ?? ".";
            var newName  = $"D4Scanner-{staged.Value.tag}-win-x64.exe";
            var newPath  = System.IO.Path.Combine(dir, newName);
            if (Updater.TryApplyStaged(staged.Value.path, exe, newPath))
            {
                // Delete the previous versioned exe (renamed to .old by TryApplyStaged)
                try { System.IO.File.Delete(exe + ".old"); } catch { }
                // Also try to delete the original exe path if the name changed (old version filename)
                try { if (exe != newPath && System.IO.File.Exists(exe)) System.IO.File.Delete(exe); } catch { }
                Process.Start(new ProcessStartInfo(newPath) { UseShellExecute = true });
                Shutdown(0);
                return;
            }
        }

        // Clean up the .old sidecar left by a prior successful update (best-effort)
        try
        {
            Updater.CleanUpOld(System.Environment.ProcessPath
                             ?? Process.GetCurrentProcess().MainModule!.FileName);
        }
        catch { }

        base.OnStartup(e);
    }
}
