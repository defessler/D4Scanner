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

        // Single-instance guard. An update restart hands off to a NEW process while the old one is still
        // shutting down — "--from-update" lets the successor WAIT for the mutex instead of losing the race
        // and silently exiting (the old "app never came back after updating" failure). A plain duplicate
        // launch still bounces instantly. An abandoned mutex (prior instance crashed) counts as acquired.
        bool fromUpdate = e.Args.Contains("--from-update");
        _singleInstance = new System.Threading.Mutex(false, "D4Scanner.App.SingleInstance");
        bool isNew;
        try { isNew = _singleInstance.WaitOne(fromUpdate ? System.TimeSpan.FromSeconds(10) : System.TimeSpan.Zero); }
        catch (System.Threading.AbandonedMutexException) { isNew = true; }   // previous instance died holding it — it's ours
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

        var exePath = System.Environment.ProcessPath
                   ?? Process.GetCurrentProcess().MainModule!.FileName;

        // Auto-update: if a newer staged exe is already downloaded from a previous session, swap it in
        // (versioned filename beside this one) and hand off to it. Falls back gracefully on failure.
        var newExe = Updater.ApplyStagedNow(exePath);
        if (newExe != null)
        {
            Process.Start(new ProcessStartInfo(newExe) { UseShellExecute = true, Arguments = "--from-update" });
            Shutdown(0);
            return;
        }

        // Sweep update leftovers: .old sidecars AND superseded versioned exes (they used to accumulate —
        // the .old of the exe an update replaced can never be deleted by the exiting process itself).
        Updater.CleanUpSuperseded(exePath);

        base.OnStartup(e);
    }
}
