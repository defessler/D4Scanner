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
            try { new MainWindow().HeadlessRender(e.Args[idx + 1]); }
            catch (System.Exception ex) { System.Console.Error.WriteLine("render failed: " + ex); }
            Shutdown(0);
            return;
        }
        base.OnStartup(e);
    }
}
