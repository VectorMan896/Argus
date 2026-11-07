namespace Argus;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var o = Cli.Parse(args);
        Config.Init(o.ConfigDir);

        if (o.Mode is CliMode.Scan or CliMode.Stack or CliMode.Perf or CliMode.Version)
            return Cli.Run(o);

        ApplicationConfiguration.Initialize();

        if (o.Mode == CliMode.Pick)
        {
            Application.Run(new PickerHost());
            return 0;
        }

        using var mutex = new Mutex(true, "Local\\Argus.SingleInstance" + AppInfo.Instance, out bool first);
        if (!first)
        {
            // already running: ask the running copy to show its window
            try { EventWaitHandle.OpenExisting("Local\\Argus.Show" + AppInfo.Instance).Set(); } catch { }
            return 0;
        }

        Application.ThreadException += (_, e) => Log.Write("UI error: " + e.Exception.Message);
        Application.Run(new MainForm(startHidden: o.StartHidden, initialTab: o.Tab));
        return 0;
    }
}
