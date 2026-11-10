using System.Diagnostics;

namespace Argus.Tests;

/// <summary>
/// Disk monitoring. Everything that changes anything (the disk priority) is only ever done to throwaway copies of ping.exe;
/// everything else is read-only, or made-up numbers.
/// </summary>
public sealed class DiskTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static bool WaitFor(Func<bool> condition, int seconds = 12)
    {
        for (int i = 0; i < seconds * 4; i++) { if (condition()) return true; Thread.Sleep(250); }
        return condition();
    }

    // ---- busy time ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 0)]        // always idle: not busy at all
    [InlineData(0, 100)]        // never idle: fully busy
    [InlineData(37.5, 62.5)]
    [InlineData(130, 0)]        // counters can overshoot
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 0)]
    public void Busy_time_is_one_hundred_minus_idle_time_and_stays_between_0_and_100(double idle, double busy) =>
        Assert.Equal(busy, DiskMeter.BusyFromIdle(idle), 3);

    [Fact]
    public void The_live_disk_counters_give_sensible_numbers_or_say_they_are_unavailable()
    {
        using var m = new DiskMeter();
        if (!m.Available) { Assert.Null(m.Read()); return; }                 // counters switched off: it must say so, not invent numbers
        Thread.Sleep(1100);
        var r = m.Read();
        Assert.NotNull(r);
        Assert.InRange(r!.Value.Busy, 0, 100);
        Assert.True(r.Value.ReadBps >= 0 && r.Value.WriteBps >= 0 && r.Value.Queue >= 0);
    }

    // ---- spinning disk or SSD ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, "Hdd")]
    [InlineData(false, "Ssd")]
    public void A_seek_penalty_means_a_spinning_disk(bool penalty, string expected) => Assert.Equal(Enum.Parse<DiskKind>(expected), DiskType.FromSeekPenalty(penalty));

    [Fact]
    public void No_answer_from_the_drive_means_type_not_reported_never_a_guess() => Assert.Equal(DiskKind.Unknown, DiskType.FromSeekPenalty(null));

    [Fact]
    public void The_seek_penalty_answer_is_read_from_the_right_byte_and_short_answers_are_refused()
    {
        var yes = new byte[] { 1, 0, 0, 0, 9, 0, 0, 0, 1 };                   // version 1, size 9, penalty
        var no = new byte[] { 1, 0, 0, 0, 9, 0, 0, 0, 0 };
        Assert.True(DiskType.ParseSeekPenalty(yes, 9));
        Assert.False(DiskType.ParseSeekPenalty(no, 9));
        Assert.Null(DiskType.ParseSeekPenalty(yes, 8));                       // the drive returned too little
        Assert.Null(DiskType.ParseSeekPenalty(new byte[] { 1, 0, 0, 0, 4, 0, 0, 0, 1 }, 9));   // it says its own answer is only 4 bytes
        Assert.Null(DiskType.ParseSeekPenalty(Array.Empty<byte>(), 0));
    }

    [Fact]
    public void Asking_about_the_system_drive_or_a_path_that_is_not_a_local_drive_never_throws()
    {
        Assert.True(Enum.IsDefined(DiskType.OfSystemDrive()));
        Assert.Equal(DiskKind.Unknown, DiskType.OfPath(@"\\no-such-server\share\file"));      // network paths are not asked
        Assert.Equal(DiskKind.Unknown, DiskType.OfPath("bad\0path"));                         // an invalid path is "not reported", not a crash
    }

    [Fact]
    public void Every_kind_of_drive_has_a_plain_description()
    {
        Assert.Contains("spinning", DiskType.Describe(DiskKind.Hdd));
        Assert.Contains("SSD", DiskType.Describe(DiskKind.Ssd));
        Assert.Contains("not reported", DiskType.Describe(DiskKind.Unknown));
    }

    // ---- who is using the disk (made-up numbers) -------------------------------------------------------------------------------

    private static Dictionary<int, IoSample> Snap(params (int Pid, string Name, ulong Read, ulong Write)[] items) =>
        items.ToDictionary(i => i.Pid, i => new IoSample(i.Name, new DateTime(2026, 1, 1), i.Read, i.Write));

    private const ulong MB = 1024 * 1024;

    private static string? NoPath(int pid) => null;

    [Fact]
    public void The_busiest_programs_come_first_with_their_rates_per_second()
    {
        var before = Snap((10, "alpha", 0, 0), (11, "beta", 0, 0), (12, "gamma", 0, 0));
        var after = Snap((10, "alpha", 10 * MB, 0), (11, "beta", 0, 60 * MB), (12, "gamma", 1 * MB, 1 * MB));

        var top = ProcessDisk.Top(before, after, 2, NoPath);

        Assert.Equal(new[] { "beta", "alpha", "gamma" }, top.Select(u => u.Name).ToArray());
        Assert.Equal(30 * MB, top[0].WriteBps);                                   // 60 MB in 2 s
        Assert.Equal(0, top[0].ReadBps);
        Assert.Equal(5 * MB, top[1].ReadBps);
    }

    [Fact]
    public void All_the_processes_of_one_program_are_added_together()
    {
        var before = Snap((20, "browser", 0, 0), (21, "browser", 0, 0), (22, "browser", 0, 0));
        var after = Snap((20, "browser", 4 * MB, 0), (21, "browser", 4 * MB, 0), (22, "browser", 4 * MB, 0));

        var u = Assert.Single(ProcessDisk.Top(before, after, 1, NoPath));

        Assert.Equal("browser", u.Name);
        Assert.Equal(3, u.Members.Count);
        Assert.Equal(12 * MB, u.ReadBps);
    }

    [Fact]
    public void A_restarted_program_that_reuses_a_process_id_is_not_counted_against_the_old_one()
    {
        var before = new Dictionary<int, IoSample> { [30] = new IoSample("old", new DateTime(2026, 1, 1), 900 * MB, 0) };
        var after = new Dictionary<int, IoSample> { [30] = new IoSample("old", new DateTime(2026, 1, 2), 950 * MB, 0) };      // a different start time

        Assert.Empty(ProcessDisk.Top(before, after, 1, NoPath));
    }

    [Fact]
    public void A_counter_that_went_backwards_and_a_program_that_just_appeared_add_nothing()
    {
        var before = Snap((40, "reset", 500 * MB, 500 * MB));
        var after = Snap((40, "reset", 1 * MB, 1 * MB), (41, "newcomer", 900 * MB, 0));        // 41 was not there before: no baseline

        Assert.Empty(ProcessDisk.Top(before, after, 1, NoPath));
    }

    [Fact]
    public void Quiet_programs_are_left_out_and_only_the_requested_number_are_listed()
    {
        var before = Snap((50, "quiet", 0, 0), (51, "a", 0, 0), (52, "b", 0, 0), (53, "c", 0, 0));
        var after = Snap((50, "quiet", 2 * 1024, 0), (51, "a", 3 * MB, 0), (52, "b", 2 * MB, 0), (53, "c", 1 * MB, 0));       // "quiet": 2 KB in 1 s

        var top = ProcessDisk.Top(before, after, 1, NoPath, count: 2);

        Assert.Equal(new[] { "a", "b" }, top.Select(u => u.Name).ToArray());
        Assert.Empty(ProcessDisk.Top(before, after, 0, NoPath));                              // no time passed: no rates
    }

    [Fact]
    public void Core_windows_programs_are_listed_but_marked_untouchable_and_known_heavy_users_are_explained()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var before = Snap((60, "svchost", 0, 0), (61, "SearchIndexer", 0, 0), (62, "argus_disk_probe", 0, 0));
        var after = Snap((60, "svchost", 8 * MB, 0), (61, "SearchIndexer", 6 * MB, 0), (62, "argus_disk_probe", 4 * MB, 0));
        string? PathOf(int pid) => pid switch
        {
            60 => Path.Combine(win, "System32", "svchost.exe"),
            61 => Path.Combine(win, "System32", "SearchIndexer.exe"),
            _ => Path.Combine(Path.GetTempPath(), "argus_disk_probe.exe"),
        };

        var top = ProcessDisk.Top(before, after, 1, PathOf).ToDictionary(u => u.Name);

        Assert.True(top["svchost"].Protected);
        Assert.True(top["SearchIndexer"].Protected);
        Assert.Contains("indexing", top["SearchIndexer"].Hint!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Indexing", top["SearchIndexer"].Label);
        Assert.False(top["argus_disk_probe"].Protected);
        Assert.Null(top["argus_disk_probe"].Hint);
    }

    [Fact]
    public void A_program_called_svchost_running_from_a_temp_folder_is_not_treated_as_core_windows()
    {
        var before = Snap((70, "svchost", 0, 0)); var after = Snap((70, "svchost", 8 * MB, 0));
        var top = ProcessDisk.Top(before, after, 1, _ => Path.Combine(Path.GetTempPath(), "svchost.exe"));
        Assert.False(Assert.Single(top).Protected);                              // same rule as everywhere else: the name only counts if the file is in a system folder
    }

    // ---- who is using the disk (live) ---------------------------------------------------------------------------------------------

    [Fact]
    public void Writing_a_file_shows_up_in_this_programs_own_counters_and_Argus_will_not_touch_itself()
    {
        var before = ProcessDisk.Snapshot();
        DateTime t0 = DateTime.UtcNow;
        File.WriteAllBytes(_dir.Combine("blob.bin"), new byte[8 * 1024 * 1024]);
        var after = ProcessDisk.Snapshot();
        double secs = Math.Max(0.5, (DateTime.UtcNow - t0).TotalSeconds);

        Assert.True(after[Environment.ProcessId].WriteBytes - before[Environment.ProcessId].WriteBytes >= 8 * MB, "the 8 MB write was not counted");
        var mine = ProcessDisk.Top(before, after, secs, Native.GetProcessPath, count: 50).Single(u => u.Members.Any(m => m.Pid == Environment.ProcessId));
        Assert.True(mine.Protected);                                              // this very program: never offered for slowing down
    }

    // ---- lowering the disk priority (throwaway programs only) --------------------------------------------------------------------

    /// <summary>
    /// A program starts with the disk priority of whatever started it, and GitHub's build machines start programs at Low. So a throwaway program is
    /// put at Normal explicitly before a test relies on it being there (raising to Normal needs no special rights).
    /// </summary>
    private static void StartAtNormal(Process p)
    {
        if (Perf.GetIoPriority(p.Id) != Perf.IoPriorityNormal) Assert.Equal("ok", Perf.SetIoPriority(p.Id, Perf.IoPriorityNormal));
        Assert.Equal(Perf.IoPriorityNormal, Perf.GetIoPriority(p.Id));
    }

    [Fact]
    public void Lowering_the_disk_priority_works_and_can_be_read_back()
    {
        var p = Fake.Start(_dir.Path, "argus_disk_probe.exe");
        try
        {
            StartAtNormal(p);
            Assert.Equal("ok", Perf.SetIoPriority(p.Id, Perf.IoPriorityLow));
            Assert.Equal(Perf.IoPriorityLow, Perf.GetIoPriority(p.Id));
            Assert.False(p.HasExited);
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void Slowing_down_a_disk_hog_lowers_disk_and_cpu_priority_closes_nothing_and_says_so_when_repeated()
    {
        var p = Fake.Start(_dir.Path, "argus_disk_probe.exe");
        try
        {
            StartAtNormal(p);
            string first = Perf.ThrottleDisk(p.Id, p.StartTime);
            Assert.StartsWith("slowed down", first);
            Assert.Contains("disk priority lowered to Low", first);
            Assert.Equal(Perf.IoPriorityLow, Perf.GetIoPriority(p.Id));
            p.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, p.PriorityClass);
            Assert.False(p.HasExited);

            string again = Perf.ThrottleDisk(p.Id, p.StartTime);
            Assert.Contains("already", again);                                    // not "lowered" a second time
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void A_program_that_already_has_low_disk_priority_is_reported_as_such_and_still_gets_its_cpu_priority_lowered()
    {
        var p = Fake.Start(_dir.Path, "argus_disk_probe.exe");
        try
        {
            Assert.Equal("ok", Perf.SetIoPriority(p.Id, Perf.IoPriorityLow));                  // as if it had inherited Low from the program that started it

            string r = Perf.ThrottleDisk(p.Id, p.StartTime);

            Assert.StartsWith("slowed down", r);
            Assert.Contains("disk priority was already Low", r);
            Assert.DoesNotContain("lowered to Low", r);
            Assert.Equal(Perf.IoPriorityLow, Perf.GetIoPriority(p.Id));
            p.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, p.PriorityClass);
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void Slowing_down_refuses_this_program_a_vanished_one_and_a_reused_process_id_without_changing_anything()
    {
        Assert.StartsWith("refused", Perf.ThrottleDisk(Environment.ProcessId, null));          // Argus itself

        var p = Fake.Start(_dir.Path, "argus_disk_probe.exe");
        int pid = p.Id;
        try
        {
            StartAtNormal(p);
            string reused = Perf.ThrottleDisk(pid, p.StartTime.AddHours(-1));
            Assert.StartsWith("skipped", reused);
            Assert.Equal(Perf.IoPriorityNormal, Perf.GetIoPriority(pid));                       // nothing changed
        }
        finally { Fake.Kill(p); }
        Assert.Equal("already gone", Perf.ThrottleDisk(pid, null));
    }

    // There is deliberately no test that points the slow-down at a real core Windows program such as explorer.exe: if the refusal were ever broken,
    // that test would change the real shell. The refusal is the same Safety.IsNeverAuto check the other actions use (see SafetyTests), and the test above
    // proves it refuses this very program, which is the one thing a broken refusal could only hurt in the test run itself.

    // ---- tips -----------------------------------------------------------------------------------------------------------------------

    private static SystemSummary Summary() => new()
    {
        TotalRam = 16UL << 30, AvailRam = 8UL << 30, MemLoad = 50, DiskFree = 400L << 30, DiskTotal = 900L << 30, DiskName = "C:",
        Uptime = TimeSpan.FromHours(5), ProcessCount = 200,
    };

    [Fact]
    public void A_hard_disk_that_is_busy_gets_the_one_thing_at_a_time_explanation_and_an_ssd_does_not()
    {
        string hdd = string.Join("\n", Perf.Tips(Summary(), 3, new DiskTip(97, DiskKind.Hdd)));
        Assert.Contains("97%", hdd);
        Assert.Contains("one thing at a time", hdd);

        string ssd = string.Join("\n", Perf.Tips(Summary(), 3, new DiskTip(97, DiskKind.Ssd)));
        Assert.Contains("97%", ssd);
        Assert.DoesNotContain("one thing at a time", ssd);
    }

    [Fact]
    public void Without_a_busy_disk_there_is_no_disk_tip()
    {
        Assert.DoesNotContain("disk has been busy", string.Join("\n", Perf.Tips(Summary(), 3)));
        Assert.Equal(85, Perf.DiskBusyTipPercent);
    }

    // ---- command line ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_command_line_knows_the_disk_options()
    {
        var o = Cli.Parse(new[] { "--disk", "--sample", "4" });
        Assert.Equal(CliMode.Perf, o.Mode);
        Assert.True(o.Disk);
        Assert.Equal(4, o.SampleSec);
        Assert.Equal(123, Cli.Parse(new[] { "--slow-disk-pid", "123" }).SlowDiskPid);
    }

    // ---- the sampler: nothing is read while it is off --------------------------------------------------------------------------------

    [Fact]
    public void The_sampler_reads_the_disk_while_on_and_nothing_at_all_once_it_is_turned_off()
    {
        using var s = new DiskSampler();
        int readings = 0, lists = 0;
        s.Reading += _ => Interlocked.Increment(ref readings);
        s.Users += _ => Interlocked.Increment(ref lists);

        s.WantUsers = true;
        s.Enabled = true;
        bool counters = WaitFor(() => s.Started && s.Available || readings > 0, 6);
        if (!counters) { s.Enabled = false; return; }                         // no disk counters on this machine: it stays silent, which is the point
        Assert.True(WaitFor(() => readings >= 2, 8), "no disk readings arrived");
        Assert.True(WaitFor(() => lists >= 1, 12), "the per-program list never arrived");

        s.Enabled = false;
        Thread.Sleep(300);
        int r = readings, l = lists;
        Thread.Sleep(2600);
        Assert.Equal(r, readings);                                            // turned off: no more reads
        Assert.Equal(l, lists);
    }

    // ---- drawing ----------------------------------------------------------------------------------------------------------------------

    private static bool RenderHasInk(Control c, string name)
    {
        var form = new Form { ClientSize = new Size(c.Width, c.Height), BackColor = Theme.Bg };
        form.Controls.Add(c);
        form.CreateControl(); form.Show(); form.Hide();
        using var bmp = new Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
        string? dir = Environment.GetEnvironmentVariable("ARGUS_TEST_SNAPSHOT_DIR");
        if (!string.IsNullOrEmpty(dir)) bmp.Save(Path.Combine(dir, name));
        bool ink = false;
        for (int y = 0; y < bmp.Height && !ink; y += 5) for (int x = 0; x < bmp.Width; x += 5) if (bmp.GetPixel(x, y).ToArgb() != Theme.Card.ToArgb()) { ink = true; break; }
        form.Dispose();
        return ink;
    }

    [Fact]
    public void The_disk_card_draws_its_graph_its_list_and_its_unavailable_state()
    {
        var card = new DiskCard { Size = new Size(760, 330) };
        card.SetKind(DiskKind.Hdd, "C:");
        Assert.Contains("Hard disk", card.Subtitle);
        for (int i = 0; i < 30; i++) card.ApplyReading(new DiskReading(20 + i * 2.5, 3 * MB, 1 * MB, i / 4.0));
        Assert.Equal(30, card.Graph.Count);
        card.ApplyUsers(new List<DiskUser>
        {
            new() { Name = "SearchIndexer", Members = { (1, null) }, ReadBps = 5 * MB, Protected = true, Hint = DiskCulprits.Hint("SearchIndexer"), Label = "Indexing" },
            new() { Name = "backup", Members = { (2, null), (3, null) }, WriteBps = 2 * MB },
        });
        Assert.Equal(2, card.List.Items.Count);
        Assert.True(RenderHasInk(card, "disk-card.png"));

        var broken = new DiskCard { Size = new Size(760, 330) };
        broken.SetAvailable(false);
        Assert.NotNull(broken.Graph.Unavailable);
        Assert.True(RenderHasInk(broken, "disk-card-unavailable.png"));
    }

    [Fact]
    public void Selecting_a_protected_program_turns_the_button_off_and_says_why_while_a_normal_one_can_be_slowed_down()
    {
        var card = new DiskCard { Size = new Size(760, 330) };
        card.ApplyUsers(new List<DiskUser>
        {
            new() { Name = "SearchIndexer", Members = { (1, null) }, ReadBps = 5 * MB, Protected = true, Hint = DiskCulprits.Hint("SearchIndexer") },
            new() { Name = "backup", Members = { (2, null) }, WriteBps = 2 * MB },
        });
        DiskUser? asked = null;
        card.SlowDownRequested += u => asked = u;
        var button = AllControls(card).OfType<PillButton>().Single(b => b.Text == "Slow down selected");
        Assert.False(button.Enabled);                                                          // nothing selected yet

        card.List.Items[0].Selected = true;
        Assert.False(button.Enabled);
        Assert.Contains("will not touch", card.StatusText);
        Assert.Contains("indexing", card.StatusText, StringComparison.OrdinalIgnoreCase);

        card.List.Items[1].Selected = true;
        Assert.True(button.Enabled);
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(button, new object[] { EventArgs.Empty });
        Assert.Equal("backup", asked?.Name);
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var d in AllControls(c)) yield return d; }
    }
}
