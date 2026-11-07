using System.Diagnostics;

namespace Argus;

/// <summary>Command-line access to the performance helpers (reporting, and a few explicit one-shot actions).</summary>
internal static class PerfCli
{
    public static void Run(CliOptions o, Action<string> P)
    {
        // --- explicit one-shot actions -------------------------------------------------------------
        if (o.StartupName != null)
        {
            var item = Perf.StartupItems().FirstOrDefault(i => i.Name.Equals(o.StartupName, StringComparison.OrdinalIgnoreCase));
            if (item == null) { P($"No startup entry named \"{o.StartupName}\"."); return; }
            P($"{item.Name}: {Perf.SetStartupEnabled(item, o.StartupOn)}");
            return;
        }

        if (o.Usage)
        {
            var cpu = new CpuMeter(); using var gpu = new GpuMeter();
            cpu.Read(); gpu.Read();
            P($"CPU / GPU load, once a second for {o.SampleSec} s (GPU counters available: {gpu.Available})");
            for (int i = 0; i < o.SampleSec; i++)
            {
                Thread.Sleep(1000);
                double? c = cpu.Read(), g = gpu.Available ? gpu.Read() : null;
                P($"  cpu {(c.HasValue ? c.Value.ToString("0.0") : "-"),5} %   gpu {(g.HasValue ? g.Value.ToString("0.0") : "-"),5} %");
            }
            return;
        }

        if (o.ThrottlePid != null)
        {
            P($"pid {o.ThrottlePid}: {Perf.Throttle(o.ThrottlePid.Value, null)}");
            return;
        }

        if (o.TrimPid != null)
        {
            int pid = o.TrimPid.Value;
            string name; string? path;
            try { using var p = Process.GetProcessById(pid); name = p.ProcessName; path = Native.GetProcessPath(pid); }
            catch { P($"pid {pid}: no such process"); return; }
            if (Safety.IsNeverAuto(name, pid, path)) { P($"pid {pid} ({name}): refused, core/shell process"); return; }
            long f = Perf.TrimOne(pid);
            P(f < 0 ? $"pid {pid} ({name}): could not trim (access denied)" : $"pid {pid} ({name}): released {Perf.Bytes(f)}");
            return;
        }

        if (o.TrimAll)
        {
            var before = Perf.Summary();
            var (n, freed, denied) = Perf.TrimMemory();
            Thread.Sleep(500);
            var after = Perf.Summary();
            P($"Trimmed {n} idle background programs ({denied} not allowed). Available memory {Perf.Bytes((long)before.AvailRam)} -> {Perf.Bytes((long)after.AvailRam)}; released {Perf.Bytes(freed)} from those programs.");
            return;
        }

        if (o.TempClean)
        {
            string root = o.TempDir ?? Path.GetTempPath();
            int days = o.TempDays ?? Config.Settings.TempAgeDays;
            var r = Perf.TempFiles(root, days, delete: true);
            P(r.Error != null
                ? $"Temp cleanup refused: {r.Error}"
                : $"Temp cleanup in {r.Root}: deleted {r.Deleted} of {r.Files} files older than {days} days, freed {Perf.Bytes(r.Freed)}, skipped {r.Skipped}.");
            return;
        }

        // --- read-only report ---------------------------------------------------------------------
        var s = Perf.Summary();
        var items = Perf.StartupItems();
        P($"Argus {AppInfo.Version} - performance overview (read-only)");
        P($"Memory : {Perf.Bytes((long)(s.TotalRam - s.AvailRam))} in use of {Perf.Bytes((long)s.TotalRam)} ({s.MemLoad}%), {Perf.Bytes((long)s.AvailRam)} available");
        P($"Drive  : {s.DiskName} {Perf.Bytes(s.DiskFree)} free of {Perf.Bytes(s.DiskTotal)}");
        P($"Uptime : {(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h, {s.ProcessCount} processes");
        P("");
        P("Tips:");
        foreach (var t in Perf.Tips(s, items.Count(i => i.Enabled))) P("  - " + t);
        P("");
        P($"Startup programs ({items.Count(i => i.Enabled)} enabled of {items.Count}):");
        foreach (var i in items)
            P($"  [{(i.Enabled ? "on " : "off")}] {i.Name,-34} {i.Where,-34} {(i.Broken ? "(file missing) " : i.Publisher)}");
        P("");
        P("Idle background programs Argus could trim (top 8, nothing is changed by this report):");
        foreach (var c in Perf.TrimCandidates().Take(8)) P($"  {c.Name,-28} {Perf.Bytes(c.WorkingSet)}");
        P("");
        string tmp = o.TempDir ?? Path.GetTempPath();
        int d = o.TempDays ?? Config.Settings.TempAgeDays;
        var tr = Perf.TempFiles(tmp, d, delete: false);
        P(tr.Error != null
            ? $"Temp scan: {tr.Error}"
            : $"Temp files older than {d} days in {tr.Root}: {tr.Files} files, {Perf.Bytes(tr.Bytes)} (nothing deleted)");
    }
}
