namespace Argus.Tests;

public sealed class PatternListTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private PatternList Make(params string[] lines)
    {
        string file = _dir.Combine("list.txt");
        File.WriteAllLines(file, lines);
        var list = new PatternList();
        list.Load(file);
        return list;
    }

    [Fact]
    public void Name_matches_with_and_without_exe_and_ignores_case()
    {
        var l = Make("Chrome");
        Assert.NotNull(l.Match("chrome", null));
        Assert.NotNull(l.Match("CHROME.EXE", null));
        Assert.NotNull(l.Match("chrome", @"C:\Program Files\Google\Chrome\chrome.exe"));
        Assert.Null(l.Match("chromium", null));
    }

    [Fact]
    public void Wildcards_work()
    {
        var l = Make("*telemetry*", "Nv?mMon");
        Assert.NotNull(l.Match("NvTelemetryContainer", null));
        Assert.NotNull(l.Match("NvTmMon", null));
        Assert.Null(l.Match("notepad", null));
        Assert.Null(l.Match("NvTmmmMon", null));
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var l = Make("# a comment", "", "   ", "notepad", "  # indented comment");
        Assert.Equal(1, l.Count);
    }

    [Fact]
    public void Path_pattern_matches_the_full_path()
    {
        var l = Make(@"path:C:\Games\*");
        Assert.NotNull(l.Match("game", @"C:\Games\Cool\game.exe"));
        Assert.Null(l.Match("game", @"C:\Other\game.exe"));
        Assert.Null(l.Match("game", null));
    }

    [Fact]
    public void Title_and_class_patterns_match_windows_of_the_process()
    {
        var l = Make("title:*Premiere*", "class:CabinetWClass");
        Assert.NotNull(l.Match("x", null, new[] { "Adobe Premiere Pro 2026" }, null));
        Assert.Null(l.Match("x", null, new[] { "Notepad" }, null));
        Assert.NotNull(l.Match("x", null, null, new[] { "CabinetWClass" }));
        Assert.Null(l.Match("x", null, null, new[] { "Notepad" }));
    }

    [Fact]
    public void Name_patterns_never_match_folder_names()
    {
        // a dev server living in a folder called "analytics-dashboard" must not be treated as an analytics program
        var l = Make("*analytics*");
        Assert.Null(l.Match("node", @"C:\work\analytics-dashboard\node.exe"));
        Assert.NotNull(l.Match("GoogleAnalyticsHelper", null));
    }

    [Fact]
    public void Add_appends_a_line_and_reloads()
    {
        var l = Make("one");
        Assert.Null(l.Match("two", null));
        l.Add("two");
        Assert.NotNull(l.Match("two", null));
        Assert.Contains("two", File.ReadAllLines(_dir.Combine("list.txt")));
    }

    [Fact]
    public void Missing_file_gives_an_empty_list()
    {
        var l = new PatternList();
        l.Load(_dir.Combine("does-not-exist.txt"));
        Assert.Equal(0, l.Count);
        Assert.Null(l.Match("anything", null));
    }
}
