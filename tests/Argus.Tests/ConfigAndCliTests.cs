namespace Argus.Tests;

/// <summary>Config holds static state, so everything that touches it runs one at a time.</summary>
[Collection("Config")]
public sealed class ConfigTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Defaults_are_conservative()
    {
        var s = new Settings();
        Assert.Equal(Handling.Auto, s.Analytics);      // the one thing the user explicitly asked to be closed
        Assert.Equal(Handling.Ask, s.Hung);            // closing a hung program loses unsaved work
        Assert.Equal(Handling.Ask, s.Weird);
        Assert.Equal(Handling.Auto, s.Hog);
        Assert.Equal(HogAction.Throttle, s.HogAction); // hogs are slowed down, never closed, by default
        Assert.Equal(Handling.Auto, s.Blocker);        // only hides / click-throughs a window
        Assert.Equal(4, s.WeirdScoreThreshold);
        Assert.Equal(0, s.AutoTrimBelowPercent);       // auto memory trimming is opt-in
        Assert.Equal(7, s.TempAgeDays);
        Assert.False(s.Paused);
    }

    [Fact]
    public void First_run_seeds_the_lists_and_settings()
    {
        Config.Init(_dir.Path);

        Assert.True(File.Exists(Config.SettingsPath));
        Assert.NotNull(Config.Block.Match("NvTelemetryContainer", null));
        Assert.NotNull(Config.Block.Match("CompatTelRunner", null));
        Assert.NotNull(Config.Excl.Match("claude", null));          // the tool that is running this must be excluded by default
        Assert.Null(Config.Block.Match("notepad", null));
    }

    [Fact]
    public void Settings_survive_a_restart_and_use_readable_enum_names()
    {
        Config.Init(_dir.Path);
        Config.Settings.Hung = Handling.Auto;
        Config.Settings.HogAction = HogAction.Close;
        Config.Settings.ScanIntervalSec = 9;
        Config.SaveSettings();

        string json = File.ReadAllText(Config.SettingsPath);
        Assert.Contains("\"Hung\": \"Auto\"", json);
        Assert.Contains("\"HogAction\": \"Close\"", json);

        Config.Init(_dir.Path);
        Assert.Equal(Handling.Auto, Config.Settings.Hung);
        Assert.Equal(HogAction.Close, Config.Settings.HogAction);
        Assert.Equal(9, Config.Settings.ScanIntervalSec);
    }

    [Fact]
    public void A_partial_settings_file_keeps_defaults_for_the_rest()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"), "{ \"Blocker\": \"Ask\" }");

        Config.Init(_dir.Path);

        Assert.Equal(Handling.Ask, Config.Settings.Blocker);
        Assert.Equal(Handling.Auto, Config.Settings.Analytics);
    }

    [Fact]
    public void A_corrupt_settings_file_falls_back_to_defaults()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"), "{ not json");

        Config.Init(_dir.Path);

        Assert.Equal(Handling.Auto, Config.Settings.Analytics);
    }
}

public sealed class CliTests
{
    [Fact]
    public void No_arguments_means_the_window() => Assert.Equal(CliMode.Gui, Cli.Parse(Array.Empty<string>()).Mode);

    [Fact]
    public void Scan_options()
    {
        var o = Cli.Parse(new[] { "--scan", "--apply", "--sample", "5", "--hung-sec", "3", "--hog-cpu", "12.5", "--config", "X", "--out", "Y" });
        Assert.Equal(CliMode.Scan, o.Mode);
        Assert.True(o.Apply);
        Assert.Equal(5, o.SampleSec);
        Assert.Equal(3, o.HungSec);
        Assert.Equal(12.5, o.HogCpu);
        Assert.Equal("X", o.ConfigDir);
        Assert.Equal("Y", o.OutFile);
    }

    [Fact]
    public void Stack_and_pick_and_version_and_tab()
    {
        var s = Cli.Parse(new[] { "--stack", "10", "20" });
        Assert.Equal(CliMode.Stack, s.Mode); Assert.Equal(10, s.X); Assert.Equal(20, s.Y);
        Assert.Equal(CliMode.Pick, Cli.Parse(new[] { "--pick" }).Mode);
        Assert.Equal(CliMode.Version, Cli.Parse(new[] { "--version" }).Mode);
        Assert.Equal(2, Cli.Parse(new[] { "--tab", "2" }).Tab);
        Assert.True(Cli.Parse(new[] { "--hidden" }).StartHidden);
    }

    [Fact]
    public void Performance_options()
    {
        var o = Cli.Parse(new[] { "--temp-clean", "--temp-dir", "D", "--temp-days", "3" });
        Assert.Equal(CliMode.Perf, o.Mode); Assert.True(o.TempClean); Assert.Equal("D", o.TempDir); Assert.Equal(3, o.TempDays);

        var t = Cli.Parse(new[] { "--startup-set", "Name", "on" });
        Assert.Equal("Name", t.StartupName); Assert.True(t.StartupOn);
        Assert.False(Cli.Parse(new[] { "--startup-set", "Name", "off" }).StartupOn);
    }

    [Fact]
    public void Version_comes_from_the_project_file()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
        Assert.DoesNotContain("+", AppInfo.Version);
    }
}
