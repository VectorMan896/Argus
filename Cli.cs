using System.Text;

namespace Argus;

internal enum CliMode { Gui, Scan, Stack, Pick, Perf, Version }

internal sealed class CliOptions
{
    public CliMode Mode = CliMode.Gui;
    public string? ConfigDir, OutFile;
    public bool Apply, StartHidden;
    public int SampleSec = 3, X, Y, Tab;
    public int? HungSec, HogSec;
    public double? HogCpu;
    public int? TrimPid, TempDays, ThrottlePid;
    public bool TrimAll, TempClean, StartupOn;
    public string? StartupName, TempDir;
}

/// <summary>
/// Command line mode (handy for testing and scripting). The exe is a GUI-subsystem program, so
/// results go to --out FILE (and to the console when one is attached).
///   Argus.exe --scan [--apply] [--sample N] [--hung-sec N] [--hog-sec N] [--hog-cpu P] [--out FILE]
///   Argus.exe --stack X Y [--out FILE]      windows that would receive a click at X,Y
///   Argus.exe --pick                         click-to-close tool without the main window
///   Argus.exe --hidden                       start in the tray
///   --config DIR                                 use a different settings folder
/// </summary>
internal static class Cli
{
    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--scan": o.Mode = CliMode.Scan; break;
                case "--apply": o.Apply = true; break;
                case "--pick": o.Mode = CliMode.Pick; break;
                case "--version": o.Mode = CliMode.Version; break;
                case "--perf": o.Mode = CliMode.Perf; break;
                case "--trim": o.Mode = CliMode.Perf; o.TrimAll = true; break;
                case "--throttle-pid": o.Mode = CliMode.Perf; if (int.TryParse(Next(), out int thp)) o.ThrottlePid = thp; break;
                case "--trim-pid": o.Mode = CliMode.Perf; if (int.TryParse(Next(), out int tp)) o.TrimPid = tp; break;
                case "--startup-set": o.Mode = CliMode.Perf; o.StartupName = Next(); o.StartupOn = string.Equals(Next(), "on", StringComparison.OrdinalIgnoreCase); break;
                case "--temp-clean": o.Mode = CliMode.Perf; o.TempClean = true; break;
                case "--temp-dir": o.TempDir = Next(); break;
                case "--temp-days": if (int.TryParse(Next(), out int td)) o.TempDays = td; break;
                case "--hidden": o.StartHidden = true; break;
                case "--tab": int.TryParse(Next(), out o.Tab); break;
                case "--stack":
                    o.Mode = CliMode.Stack;
                    int.TryParse(Next(), out o.X); int.TryParse(Next(), out o.Y); break;
                case "--config": o.ConfigDir = Next(); break;
                case "--out": o.OutFile = Next(); break;
                case "--sample": if (int.TryParse(Next(), out int sm)) o.SampleSec = Math.Max(1, sm); break;
                case "--hung-sec": if (int.TryParse(Next(), out int hs)) o.HungSec = hs; break;
                case "--hog-sec": if (int.TryParse(Next(), out int gs)) o.HogSec = gs; break;
                case "--hog-cpu": if (double.TryParse(Next(), out double gc)) o.HogCpu = gc; break;
            }
        }
        return o;
    }

    public static int Run(CliOptions o)
    {
        var sb = new StringBuilder();
        void P(string line) { sb.AppendLine(line); try { Console.WriteLine(line); } catch { } }

        if (o.Mode == CliMode.Stack)
        {
            P($"Windows that receive a click at {o.X},{o.Y} (topmost first):");
            foreach (var w in WindowTools.StackAt(o.X, o.Y))
                P($"  z={w.Z,-4} {Describe(w)}");
        }
        else if (o.Mode == CliMode.Version) { P("Argus " + AppInfo.Version); }
        else if (o.Mode == CliMode.Perf) { PerfCli.Run(o, P); }
        else if (o.Mode == CliMode.Scan)
        {
            var s = Config.Settings;
            if (o.HungSec != null) s.HungSeconds = o.HungSec.Value;
            if (o.HogSec != null) s.HogSeconds = o.HogSec.Value;
            if (o.HogCpu != null) s.HogCpuPercent = o.HogCpu.Value;

            var det = new Detector(s, Config.Block, Config.Excl);
            List<Finding> found;
            var end = DateTime.UtcNow.AddSeconds(o.SampleSec);
            while (true)
            {
                found = det.Scan();
                if (DateTime.UtcNow >= end) break;
                Thread.Sleep(1000);
            }

            P($"Argus scan: {found.Count} finding(s). Settings folder: {Config.Dir}");
            foreach (var f in found.OrderBy(f => f.Category))
            {
                var h = s.For(f.Category);
                P($"[{f.CategoryLabel}] {f.ProcName} (pid {f.Pid}) handling={h}");
                P($"    {f.Reason}");
                if (f.Path != null) P($"    {f.Path}");
                if (o.Apply && h == Handling.Auto)
                    P($"    -> {Actions.Execute(f, s)}");
            }
        }

        if (o.OutFile != null)
        {
            try { File.WriteAllText(o.OutFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
        }
        return 0;
    }

    public static string Describe(WinInfo w)
    {
        string proc = "?";
        try { proc = System.Diagnostics.Process.GetProcessById((int)w.Pid).ProcessName; } catch { }
        var notes = new List<string>();
        if (w.IsInvisibleClickCatcher) notes.Add("INVISIBLE, catches clicks");
        if (w.Class.Contains("PopupWindowSiteBridge")) notes.Add("popup host");
        if (w.Topmost) notes.Add("topmost");
        string title = w.Title.Length > 0 ? $"\"{w.Title}\"" : "(no title)";
        return $"{proc} pid={w.Pid} class={w.Class} {title} {w.Rect.Width}x{w.Rect.Height}@{w.Rect.Left},{w.Rect.Top}" +
               (notes.Count > 0 ? "  [" + string.Join(", ", notes) + "]" : "");
    }
}
