namespace Argus;

/// <summary>
///   Argus.exe --update               look for a newer release and say what it found
///   Argus.exe --update --yes         ... and install it (portable Argus.exe only), then restart
///   add --no-restart to install without starting the new version
/// The environment variable ARGUS_UPDATE_URL may point at a loopback test server instead of GitHub.
/// </summary>
internal static class UpdateCli
{
    private sealed class StepPrinter : IProgress<UpdateProgress>
    {
        private readonly Action<string> _p; private string _last = "";
        public StepPrinter(Action<string> p) { _p = p; }
        public void Report(UpdateProgress u)
        {
            if (u.Step == _last) return;
            _last = u.Step;
            _p("  " + u.Step + (u.Total > 0 ? $" ({Perf.Bytes(u.Total)})" : "") + "...");
        }
    }

    public static async Task Run(CliOptions o, Action<string> P)
    {
        P($"Argus {AppInfo.Version}");
        bool can = Updater.CanSelfUpdate(out string why);
        P(can ? "This copy can update itself." : "This copy cannot update itself: " + why + ".");

        var (rel, problem) = await Updater.CheckAsync();
        if (rel == null) { P("Could not find a release to install: " + (problem ?? "unknown reason") + "."); return; }
        P($"Latest release: {rel.Version} ({rel.Tag})");
        if (!Updater.IsNewer(AppInfo.Version, rel.Version)) { P("You are up to date."); return; }

        P("An update is available.");
        foreach (var line in rel.Notes.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(8)) P("  | " + line);
        if (!o.UpdateYes) { P("Run again with --update --yes to install it."); return; }
        if (!can) { P("Not installing: " + why + "."); return; }

        string exe = Environment.ProcessPath!;
        try
        {
            string backup = await Updater.InstallAsync(rel, exe, new StepPrinter(P));
            P($"Installed {rel.Version}. The previous version is kept as {backup} until the new one has run for a few seconds.");
            if (!o.NoRestart) { Updater.Relaunch(exe); P("Started the new version."); }
        }
        catch (UpdateException ex) { P("Update failed: " + ex.Message); }
        catch (Exception ex) { P($"Update failed: {ex.GetType().Name}: {ex.Message}"); }
    }
}
