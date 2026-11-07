using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace Argus.Tests;

public sealed class PerfTests
{
    private static SystemSummary Healthy() => new()
    {
        TotalRam = 16UL << 30, AvailRam = 10UL << 30, MemLoad = 37,
        DiskFree = 400L << 30, DiskTotal = 900L << 30, DiskName = "C:", Uptime = TimeSpan.FromHours(5), ProcessCount = 200,
    };

    [Fact]
    public void Healthy_pc_gets_the_all_clear()
    {
        var tips = Perf.Tips(Healthy(), 3);
        Assert.Single(tips);
        Assert.Contains("Nothing stands out", tips[0]);
    }

    [Fact]
    public void Tips_react_to_each_problem()
    {
        var s = Healthy();
        s.TotalRam = 4UL << 30; s.AvailRam = 400UL << 20; s.MemLoad = 90;
        s.DiskFree = 50L << 30;                       // 5% of 900 GB
        s.Uptime = TimeSpan.FromDays(12);

        string all = string.Join("\n", Perf.Tips(s, 12));

        Assert.Contains("GB of RAM", all);
        Assert.Contains("90% full", all);
        Assert.Contains("only 6% free", all);
        Assert.Contains("12 days", all);
        Assert.Contains("12 programs start with Windows", all);
        Assert.DoesNotContain("Nothing stands out", all);
    }

    [Fact]
    public void Bytes_formats_sizes()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("0 B", Perf.Bytes(0));
            Assert.Equal("1 MB", Perf.Bytes(1L << 20));
            Assert.Equal("1.5 GB", Perf.Bytes((long)(1.5 * (1L << 30))));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Startup_toggle_is_reversible_and_deletes_nothing()
    {
        string name = "ArgusTest-" + Guid.NewGuid().ToString("N");
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        string cmd = "\"" + Path.Combine(Environment.SystemDirectory, "notepad.exe") + "\"";

        using (var k = Registry.CurrentUser.CreateSubKey(run)) k.SetValue(name, cmd);
        try
        {
            var item = Perf.StartupItems().Single(i => i.Name == name);
            Assert.True(item.Enabled);
            Assert.Equal(StartupSource.UserRun, item.Source);

            Assert.Equal("disabled", Perf.SetStartupEnabled(item, false));
            using (var k = Registry.CurrentUser.OpenSubKey(approved))
                Assert.Equal(3, ((byte[])k!.GetValue(name)!)[0]);                       // same flag Task Manager writes
            using (var k = Registry.CurrentUser.OpenSubKey(run))
                Assert.Equal(cmd, k!.GetValue(name));                                    // the Run entry itself is untouched
            Assert.False(Perf.StartupItems().Single(i => i.Name == name).Enabled);

            Assert.Equal("enabled", Perf.SetStartupEnabled(item, true));
            using (var k = Registry.CurrentUser.OpenSubKey(approved))
                Assert.Equal(2, ((byte[])k!.GetValue(name)!)[0]);
            Assert.True(Perf.StartupItems().Single(i => i.Name == name).Enabled);
        }
        finally
        {
            using (var k = Registry.CurrentUser.OpenSubKey(run, true)) k?.DeleteValue(name, false);
            using (var k = Registry.CurrentUser.OpenSubKey(approved, true)) k?.DeleteValue(name, false);
        }
    }

    [Fact]
    public void Throttle_lowers_priority_and_keeps_the_program_running()
    {
        using var dir = new TempDir();
        var p = Fake.Start(dir.Path, "argus_test_busy.exe");
        try
        {
            string result = Perf.Throttle(p.Id, null);

            Assert.StartsWith("slowed down", result);
            p.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, p.PriorityClass);
            Assert.False(p.HasExited);
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void Throttle_refuses_core_processes_and_itself()
    {
        Assert.StartsWith("refused", Perf.Throttle(Environment.ProcessId, null));
        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
        if (explorer != null)
        {
            using (explorer) Assert.StartsWith("refused", Perf.Throttle(explorer.Id, null));
        }
    }

    [Fact]
    public void Trimming_memory_never_closes_the_program()
    {
        using var dir = new TempDir();
        var p = Fake.Start(dir.Path, "argus_test_trim.exe");
        try
        {
            long freed = Perf.TrimOne(p.Id);

            Assert.True(freed >= 0);
            Assert.False(p.HasExited);
        }
        finally { Fake.Kill(p); }
    }
}
