using System.Diagnostics;

namespace Argus.Tests;

/// <summary>
/// End-to-end checks against throwaway programs (renamed copies of ping.exe). Nothing here touches a real program.
/// </summary>
public sealed class ProcessTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private PatternList List(string name, params string[] lines)
    {
        string file = _dir.Combine(name);
        File.WriteAllLines(file, lines);
        var l = new PatternList();
        l.Load(file);
        return l;
    }

    /// <summary>Scans until the program shows up as a finding (it may take a moment to be visible), for at most about 5 seconds.</summary>
    private static Finding? ScanUntil(Detector det, int pid)
    {
        for (int i = 0; i < 20; i++)
        {
            var hit = det.Scan().SingleOrDefault(f => f.Pid == pid);
            if (hit != null) return hit;
            Thread.Sleep(250);
        }
        return null;
    }

    [Fact]
    public void A_program_on_the_blocklist_is_flagged_as_analytics()
    {
        var p = Fake.Start(_dir.Path, "argus_block_probe.exe");
        try
        {
            var det = new Detector(new Settings(), List("block.txt", "argus_block_probe"), List("excl.txt", "# none"));

            var hit = ScanUntil(det, p.Id);

            Assert.NotNull(hit);
            Assert.Equal(Category.Analytics, hit!.Category);
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void An_excluded_program_is_never_flagged()
    {
        var p = Fake.Start(_dir.Path, "argus_block_probe.exe");
        try
        {
            var det = new Detector(new Settings(), List("block.txt", "argus_block_probe"), List("excl.txt", "argus_block_probe"));

            Assert.DoesNotContain(det.Scan(), f => f.Pid == p.Id);
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void A_fake_svchost_in_a_temp_folder_is_flagged_as_suspicious()
    {
        var p = Fake.Start(_dir.Path, "svchost.exe");
        try
        {
            var det = new Detector(new Settings(), List("block.txt", "# none"), List("excl.txt", "# none"));

            var hit = ScanUntil(det, p.Id);

            Assert.NotNull(hit);
            Assert.Equal(Category.Weird, hit!.Category);
            Assert.Contains("Windows component name", hit.Reason);
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void Closer_ends_a_throwaway_program()
    {
        var p = Fake.Start(_dir.Path, "argus_test_close.exe");
        try
        {
            string result = Closer.CloseProcess(p.Id, null, gracefulSec: 0, killTree: true, userInitiated: false);

            Assert.Equal("killed", result);
            Assert.True(p.WaitForExit(5000));
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void Closer_refuses_itself_and_core_processes()
    {
        Assert.StartsWith("refused", Closer.CloseProcess(Environment.ProcessId, null, 0, true, userInitiated: true));

        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
        if (explorer != null)
        {
            using (explorer) Assert.StartsWith("refused", Closer.CloseProcess(explorer.Id, null, 0, true, userInitiated: false));
        }
    }

    [Fact]
    public void Closer_skips_a_reused_process_id()
    {
        var p = Fake.Start(_dir.Path, "argus_test_reuse.exe");
        try
        {
            // pretend the finding was about a different (earlier) process that had this id
            string result = Closer.CloseProcess(p.Id, p.StartTime.AddHours(-3), 0, true, userInitiated: false);

            Assert.StartsWith("skipped", result);
            Assert.False(p.HasExited);
        }
        finally { Fake.Kill(p); }
    }
}
