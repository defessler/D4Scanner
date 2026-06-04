using D4Scanner.Core;
using System.Diagnostics;

namespace D4Scanner.App;

public partial class App : System.Windows.Application
{
    // "--render <out.png>": render the window to a PNG with no visible window, then exit.
    // Lets the UI be inspected headlessly (e.g. to verify the on-screen guidance) without a display.
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
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

        // Auto-update: if a newer staged exe is already downloaded from a previous session,
        // apply it now (before any window appears) — rename the running image, copy the new
        // one into its place, relaunch, and exit.  Falls back gracefully if the swap fails.
        var staged = Updater.FindStagedUpdate();
        if (staged.HasValue)
        {
            var exe = System.Environment.ProcessPath
                   ?? Process.GetCurrentProcess().MainModule!.FileName;
            if (Updater.TryApplyStaged(staged.Value.path, exe))
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
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
