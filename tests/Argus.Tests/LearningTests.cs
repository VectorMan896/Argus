namespace Argus.Tests;

[Collection("Config")]
public sealed class LearningTests : IDisposable
{
    private readonly TempDir _dir = new();

    public LearningTests() { }
    public void Dispose() { Learning.Reload(); _dir.Dispose(); }

    private static Finding F(Category c, string program = "tracker", string reason = "matches blocklist entry") =>
        new() { Category = c, ProcName = program, Reason = reason, Key = c + ":" + program };

    // ---- the rule ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Only_automatic_categories_are_held_back_while_learning()
    {
        var learning = new Settings { Learning = true };
        Assert.True(Learning.ShouldObserve(learning, Handling.Auto));
        Assert.False(Learning.ShouldObserve(learning, Handling.Ask));       // Ask already waits for the user
        Assert.False(Learning.ShouldObserve(learning, Handling.Off));
        Assert.False(Learning.ShouldObserve(new Settings { Learning = false }, Handling.Auto));
    }

    [Fact]
    public void The_learning_period_is_counted_in_days_and_never_ends_by_itself()
    {
        var s = new Settings { Learning = true, LearningDays = 3, LearningStartedUtc = DateTime.UtcNow.AddHours(-25) };
        Assert.Equal(2, Learning.DayNumber(s));
        Assert.False(Learning.PeriodOver(s));

        s.LearningStartedUtc = DateTime.UtcNow.AddDays(-4);
        Assert.True(Learning.PeriodOver(s));
        Assert.True(s.Learning);                                            // still learning: it waits for the user's decision

        Assert.False(Learning.PeriodOver(new Settings { Learning = false, LearningStartedUtc = DateTime.UtcNow.AddDays(-9) }));
    }

    [Fact]
    public void Starting_and_finishing_change_the_settings_and_save_them()
    {
        Config.Init(_dir.Path);
        Learning.Start(Config.Settings);
        Assert.True(Config.Settings.Learning);
        Assert.NotNull(Config.Settings.LearningStartedUtc);

        Learning.Finish(Config.Settings);
        Config.Init(_dir.Path);                                             // reload from disk
        Assert.False(Config.Settings.Learning);
    }

    // ---- who starts in learning mode ---------------------------------------------------------------------------------------

    [Fact]
    public void A_fresh_install_starts_in_learning_mode()
    {
        Config.Init(_dir.Path);                                             // no settings file yet
        Assert.True(Config.Settings.Learning);
        Assert.NotNull(Config.Settings.LearningStartedUtc);
        Assert.InRange((DateTime.UtcNow - Config.Settings.LearningStartedUtc!.Value).TotalMinutes, -1, 2);
    }

    [Fact]
    public void An_existing_install_keeps_acting_exactly_as_before()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"), "{ \"Analytics\": \"Auto\" }");     // a settings file from before learning mode existed
        Config.Init(_dir.Path);
        Assert.False(Config.Settings.Learning);
        Assert.Equal(Handling.Auto, Config.Settings.Analytics);
    }

    // ---- what is written down -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Analytics", "close it")]
    [InlineData("Hung", "close it")]
    [InlineData("Weird", "close it")]
    public void What_it_would_have_done_is_described_in_plain_words(string category, string expected) =>
        Assert.Equal(expected, Learning.WouldDo(F(Enum.Parse<Category>(category)), new Settings()));

    [Fact]
    public void Hogs_and_blockers_are_described_by_their_gentle_actions()
    {
        Assert.Equal("slow it down", Learning.WouldDo(F(Category.Hog), new Settings { HogAction = HogAction.Throttle }));
        Assert.Equal("close it", Learning.WouldDo(F(Category.Hog), new Settings { HogAction = HogAction.Close }));
        var overlay = F(Category.Blocker); overlay.Kind = BlockerKind.TransparentOverlay;
        var popup = F(Category.Blocker); popup.Kind = BlockerKind.StalePopup;
        Assert.Equal("make the window click-through", Learning.WouldDo(overlay, new Settings()));
        Assert.Equal("hide the window", Learning.WouldDo(popup, new Settings()));
    }

    [Fact]
    public void The_same_program_seen_scan_after_scan_is_one_entry_and_a_later_return_is_another_sighting()
    {
        Config.Init(_dir.Path); Learning.Reload(); Learning.Clear();
        var s = new Settings();
        for (int i = 0; i < 20; i++) Learning.Record(F(Category.Analytics, "tracker"), s);       // 20 scans in a row
        var e = Assert.Single(Learning.Entries());
        Assert.Equal("tracker", e.Program);
        Assert.Equal("Analytics", e.Category);
        Assert.Equal("close it", e.WouldDo);
        Assert.Equal(1, e.Sightings);

        e.LastUtc = DateTime.UtcNow.AddMinutes(-10);                                             // it went away and came back later
        Learning.Record(F(Category.Analytics, "tracker"), s);
        Assert.Equal(2, Assert.Single(Learning.Entries()).Sightings);

        Learning.Record(F(Category.Analytics, "other"), s);
        Assert.Equal(2, Learning.Entries().Count);
    }

    [Fact]
    public void The_list_survives_a_restart()
    {
        Config.Init(_dir.Path); Learning.Reload(); Learning.Clear();
        Learning.Record(F(Category.Analytics, "tracker"), new Settings());

        Learning.Reload();                                                                       // forget memory, read the file again
        Assert.Equal("tracker", Assert.Single(Learning.Entries()).Program);
    }

    [Fact]
    public void The_learning_window_draws_its_list_and_its_empty_state()
    {
        Config.Init(_dir.Path); Learning.Reload(); Learning.Clear();
        var s = Config.Settings; s.LearningStartedUtc = DateTime.UtcNow.AddHours(-30); s.Learning = true;

        using (var empty = new LearningDialog(s)) { Assert.True(RenderHasInk(empty, "learning-window-empty.png")); }

        var rnd = new Random(2);
        foreach (var (cat, prog) in new[] { (Category.Analytics, "NvTelemetryContainer"), (Category.Analytics, "CompatTelRunner"), (Category.Hog, "backgroundTaskHost"), (Category.Blocker, "NVIDIA Overlay") })
        {
            var f = F(cat, prog, "matches blocklist entry \"*telemetry*\""); if (cat == Category.Blocker) { f.Kind = BlockerKind.TransparentOverlay; f.WindowClass = "CEF-OSC-WIDGET"; }
            Learning.Record(f, s);
        }
        using (var full = new LearningDialog(s)) { Assert.True(RenderHasInk(full, "learning-window.png")); }
    }

    private static bool RenderHasInk(Form f, string name)
    {
        f.CreateControl(); f.Show(); f.Hide();                               // make sure the list has laid out
        using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
        string? dir = Environment.GetEnvironmentVariable("ARGUS_TEST_SNAPSHOT_DIR");
        if (!string.IsNullOrEmpty(dir)) bmp.Save(Path.Combine(dir, name));
        for (int y = 0; y < bmp.Height; y += 5) for (int xx = 0; xx < bmp.Width; xx += 5) if (bmp.GetPixel(xx, y).ToArgb() != Theme.Card.ToArgb()) return true;
        return false;
    }

    // ---- the real Watcher -------------------------------------------------------------------------------------------------------

    private void PrepareConfig(bool learning)
    {
        string started = DateTime.UtcNow.ToString("o");
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"),
            "{ \"Analytics\":\"Auto\",\"Hung\":\"Off\",\"Hog\":\"Off\",\"Weird\":\"Off\",\"Blocker\":\"Off\"," +
            $"\"Learning\":{learning.ToString().ToLower()},\"LearningStartedUtc\":\"{started}\" }}");
        File.WriteAllText(Path.Combine(_dir.Path, "blocklist.txt"), "argus_learning_probe");
        File.WriteAllText(Path.Combine(_dir.Path, "exclusions.txt"), "# none");
        Config.Init(_dir.Path);
        Learning.Reload(); Learning.Clear();
    }

    private static bool WaitFor(Func<bool> condition, int seconds = 12)
    {
        for (int i = 0; i < seconds * 4; i++) { if (condition()) return true; Thread.Sleep(250); }
        return false;
    }

    [Fact]
    public void While_learning_the_watcher_writes_it_down_and_leaves_the_program_running()
    {
        PrepareConfig(learning: true);
        var p = Fake.Start(_dir.Path, "argus_learning_probe.exe");
        try
        {
            using var w = new Watcher();
            w.ScanNow();
            Assert.True(WaitFor(() => Learning.Entries().Any(e => e.Program == "argus_learning_probe")), "the watcher never recorded the program");
            Thread.Sleep(1500);
            Assert.False(p.HasExited, "learning mode closed a program");
            Assert.True(WaitFor(() => !w.IsBusy, 20), "the scan never finished");
        }
        finally { Fake.Kill(p); }
    }

    [Fact]
    public void Once_learning_is_off_the_same_setup_really_closes_the_program()
    {
        PrepareConfig(learning: false);
        var p = Fake.Start(_dir.Path, "argus_learning_probe.exe");
        try
        {
            using var w = new Watcher();
            w.ScanNow();
            Assert.True(WaitFor(() => p.HasExited), "the watcher did not close the program");
            Assert.True(WaitFor(() => !w.IsBusy, 20), "the scan never finished");
            Assert.Empty(Learning.Entries());
        }
        finally { Fake.Kill(p); }
    }
}
