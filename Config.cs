using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Argus;

internal sealed class Settings
{
    // What to do per category: Off = ignore, Ask = list it and let me decide, Auto = deal with it.
    public Handling Analytics { get; set; } = Handling.Auto;
    public Handling Hung { get; set; } = Handling.Ask;
    public Handling Hog { get; set; } = Handling.Auto;       // default action only slows the program down (see HogAction)
    public Handling Weird { get; set; } = Handling.Ask;
    public Handling Blocker { get; set; } = Handling.Auto;   // only hides/click-through-ifies a window, never kills

    public int ScanIntervalSec { get; set; } = 5;
    public int HungSeconds { get; set; } = 20;
    public double HogCpuPercent { get; set; } = 25;          // % of total CPU capacity
    public int HogMemoryMB { get; set; } = 2000;
    public int HogSeconds { get; set; } = 60;
    public int WeirdScoreThreshold { get; set; } = 4;
    public int StalePopupSeconds { get; set; } = 45;
    public int MinOverlayArea { get; set; } = 40000;         // px^2; smaller invisible windows are ignored
    public int GracefulWaitSec { get; set; } = 5;
    public bool KillTree { get; set; } = true;
    public bool SkipWindowsDir { get; set; } = true;         // hog/weird checks ignore programs under C:\Windows
    public bool Paused { get; set; }

    // Older-PC performance helpers
    public HogAction HogAction { get; set; } = HogAction.Throttle;   // slow hogs down instead of closing them
    public int AutoTrimBelowPercent { get; set; }                    // 0 = never; else page out idle background programs when available RAM drops below this %
    // Updates (off by default: Argus does not touch the network unless this is turned on)
    public bool UpdateCheck { get; set; }                            // look for a new release about once a day
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string? SkippedVersion { get; set; }                      // "skip this version" in the update dialog
    // First-run learning mode (see Learning.cs). Off for existing installs; a fresh install starts with it on.
    public bool Learning { get; set; }
    public DateTime? LearningStartedUtc { get; set; }
    public int LearningDays { get; set; } = 3;
    public int TempAgeDays { get; set; } = 7;                        // temp files newer than this are never touched

    public Handling For(Category c) => c switch
    {
        Category.Analytics => Analytics,
        Category.Hung => Hung,
        Category.Hog => Hog,
        Category.Weird => Weird,
        Category.Blocker => Blocker,
        Category.Respawn => Handling.Ask,                 // changing services or tasks is never automatic
        _ => Handling.Off,
    };
}

/// <summary>
/// A user-editable list of wildcard patterns, one per line.
///   name            process name, with or without .exe        (chrome, chrome.exe, *telemetry*)
///   path:PATTERN    full executable path                       (path:C:\Games\*)
///   title:PATTERN   any window title of the process            (title:*Premiere*)
///   class:PATTERN   any window class of the process            (class:CabinetWClass)
/// '#' starts a comment. * and ? are wildcards. Case-insensitive.
/// </summary>
internal sealed class PatternList
{
    private sealed record Rule(string Kind, Regex Rx, string Raw);

    private List<Rule> _rules = new();
    private readonly object _lock = new();
    public string FilePath { get; private set; } = "";

    public static Regex Wild(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public void Load(string path)
    {
        FilePath = path;
        Reload();
    }

    public void Reload()
    {
        var rules = new List<Rule>();
        try
        {
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string kind = "name", pat = line;
                foreach (var k in new[] { "path", "title", "class" })
                {
                    if (line.StartsWith(k + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = k; pat = line[(k.Length + 1)..].Trim(); break;
                    }
                }
                if (pat.Length == 0) continue;
                rules.Add(new Rule(kind, Wild(pat), line));
            }
        }
        catch { /* missing/locked file: keep it empty */ }
        lock (_lock) _rules = rules;
    }

    public void Add(string line)
    {
        try
        {
            string existing = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
            var sb = new StringBuilder(existing);
            if (existing.Length > 0 && !existing.EndsWith('\n')) sb.AppendLine();
            sb.AppendLine(line);
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch (Exception ex) { Log.Write("Could not update " + Path.GetFileName(FilePath) + ": " + ex.Message); }
        Reload();
    }

    public int Count { get { lock (_lock) return _rules.Count; } }

    /// <summary>Returns the matching raw line, or null.</summary>
    public string? Match(string name, string? path, IEnumerable<string>? titles = null, IEnumerable<string>? classes = null)
    {
        List<Rule> rules; lock (_lock) rules = _rules;
        if (rules.Count == 0) return null;

        string file = path != null ? Path.GetFileName(path) : name + ".exe";
        string bare = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        foreach (var r in rules)
        {
            switch (r.Kind)
            {
                case "name":
                    if (r.Rx.IsMatch(bare) || r.Rx.IsMatch(file)) return r.Raw;
                    break;
                case "path":
                    if (path != null && r.Rx.IsMatch(path)) return r.Raw;
                    break;
                case "title":
                    if (titles != null && titles.Any(t => r.Rx.IsMatch(t))) return r.Raw;
                    break;
                case "class":
                    if (classes != null && classes.Any(c => r.Rx.IsMatch(c))) return r.Raw;
                    break;
            }
        }
        return null;
    }
}

internal static class Config
{
    public static string Dir = "";
    public static Settings Settings = new();
    public static readonly PatternList Block = new();
    public static readonly PatternList Excl = new();

    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string BlockPath => Path.Combine(Dir, "blocklist.txt");
    public static string ExclPath => Path.Combine(Dir, "exclusions.txt");
    public static string LogPath => Path.Combine(Dir, "log.txt");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Init(string? dirOverride)
    {
        Dir = dirOverride
              ?? Environment.GetEnvironmentVariable("ARGUS_HOME")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Argus");
        Directory.CreateDirectory(Dir);

        if (!File.Exists(BlockPath)) File.WriteAllText(BlockPath, DefaultBlocklist);
        if (!File.Exists(ExclPath)) File.WriteAllText(ExclPath, DefaultExclusions);
        Block.Load(BlockPath);
        Excl.Load(ExclPath);

        try
        {
            if (File.Exists(SettingsPath))
                Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), Json) ?? new Settings();
            else { Settings = new Settings { Learning = true, LearningStartedUtc = DateTime.UtcNow }; SaveSettings(); }   // fresh install: learn first, act later
        }
        catch { Settings = new Settings(); }
    }

    public static void SaveSettings()
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, Json)); }
        catch (Exception ex) { Log.Write("Could not save settings: " + ex.Message); }
    }

    private const string DefaultBlocklist = @"# Argus analytics / telemetry blocklist
# One pattern per line. * and ? are wildcards. Case-insensitive.
#   name            process name, with or without .exe        e.g.  CompatTelRunner
#   path:PATTERN    full path of the executable               e.g.  path:C:\Vendor\Tracker\*
# Matching only looks at the program's own file name - never at folder names.
# To stop closing something, delete its line or add it to exclusions.txt.

*telemetry*
*analytics*
CompatTelRunner
DeviceCensus
wsqmcons
DiagTrackRunner
NvTelemetryContainer
NvTmMon
NvTmRep
AdobeGCClient
GoogleCrashHandler
GoogleCrashHandler64
software_reporter_tool
";

    private const string DefaultExclusions = @"# Argus exclusions - programs, paths and windows Argus must never touch.
# One pattern per line. * and ? are wildcards. Case-insensitive.
#   name            process name, with or without .exe        e.g.  obs64
#   path:PATTERN    full path of the executable               e.g.  path:C:\Games\*
#   title:PATTERN   any window title of the process           e.g.  title:*Premiere*
#   class:PATTERN   any window class of the process           e.g.  class:CabinetWClass

# development tools (long-running, no window, often busy)
claude
dotnet
node
python
git
bash
pwsh
powershell
WindowsTerminal
cmd
Code
";
}

internal static class Log
{
    private static readonly object L = new();
    public static event Action<string>? Entry;

    public static void Write(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
        lock (L)
        {
            try
            {
                if (Config.Dir.Length > 0)
                {
                    var fi = new FileInfo(Config.LogPath);
                    if (fi.Exists && fi.Length > 1_000_000) File.Move(Config.LogPath, Config.LogPath + ".old", true);
                    File.AppendAllText(Config.LogPath, line + Environment.NewLine);
                }
            }
            catch { /* logging must never throw */ }
        }
        Entry?.Invoke(line);
    }
}
