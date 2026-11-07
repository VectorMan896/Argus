using System.Text.Json;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  First-run learning mode.
//
//  A brand-new install does not act on anything by itself at first. Categories set to Auto only RECORD what they would have
//  done ("would have closed X"), for a few days. When the period is over (or whenever the user wants) Argus shows the list and
//  the user decides: start acting on their rules, keep learning, or change the rules first.
//
//  Only fresh installs start in learning mode. An install that already has settings keeps behaving exactly as before.
//  Categories set to Ask are unaffected (they already wait for the user), and so is everything on the Overview.
// -------------------------------------------------------------------------------------------------

internal static class Learning
{
    public sealed class Entry
    {
        public string Category { get; set; } = "";
        public string Program { get; set; } = "";
        public string WouldDo { get; set; } = "";
        public string Reason { get; set; } = "";
        public int Sightings { get; set; }
        public DateTime FirstUtc { get; set; }
        public DateTime LastUtc { get; set; }
    }

    private sealed class Data { public List<Entry> Entries { get; set; } = new(); }

    private static readonly object Lock = new();
    private static Data? _data;
    private const int MaxEntries = 300;
    private static readonly TimeSpan NewSighting = TimeSpan.FromMinutes(2);   // the same program seen again later counts as another sighting

    private static string FilePath => Path.Combine(Config.Dir, "learning.json");

    // ---- the rule ---------------------------------------------------------------------------------------------------

    /// <summary>While learning, a category set to Auto records what it would do instead of doing it. Ask and Off are untouched.</summary>
    public static bool ShouldObserve(Settings s, Handling configured) => s.Learning && configured == Handling.Auto;

    public static int Days(Settings s) => Math.Max(1, s.LearningDays);

    /// <summary>Which day of learning it is (1-based).</summary>
    public static int DayNumber(Settings s) =>
        s.LearningStartedUtc is DateTime st ? Math.Max(1, (int)Math.Floor((DateTime.UtcNow - st).TotalDays) + 1) : 1;

    /// <summary>True once the planned days have passed. Learning does not end by itself: Argus keeps recording until the user decides.</summary>
    public static bool PeriodOver(Settings s) =>
        s.Learning && s.LearningStartedUtc is DateTime st && DateTime.UtcNow >= st.AddDays(Days(s));

    /// <summary>Begins (or restarts) the learning period.</summary>
    public static void Start(Settings s)
    {
        s.Learning = true;
        s.LearningStartedUtc = DateTime.UtcNow;
        Config.SaveSettings();
    }

    /// <summary>Ends learning: from now on categories act on their settings.</summary>
    public static void Finish(Settings s)
    {
        s.Learning = false;
        Config.SaveSettings();
    }

    // ---- what it would have done ---------------------------------------------------------------------------------------

    public static string WouldDo(Finding f, Settings s) => f.Category switch
    {
        Category.Hog => s.HogAction == HogAction.Throttle ? "slow it down" : "close it",
        Category.Blocker => f.Kind == BlockerKind.TransparentOverlay ? "make the window click-through" : "hide the window",
        _ => "close it",
    };

    public static void Record(Finding f, Settings s)
    {
        string program = f.Category == Category.Blocker && f.WindowClass.Length > 0 ? $"{f.ProcName} ({f.WindowClass})" : f.ProcName;
        string category = f.CategoryLabel;
        string would = WouldDo(f, s);
        var now = DateTime.UtcNow;
        lock (Lock)
        {
            var d = Load();
            var e = d.Entries.FirstOrDefault(x => x.Program == program && x.Category == category && x.WouldDo == would);
            bool changed = false;
            if (e == null)
            {
                d.Entries.Add(new Entry { Category = category, Program = program, WouldDo = would, Reason = f.Reason, Sightings = 1, FirstUtc = now, LastUtc = now });
                if (d.Entries.Count > MaxEntries) d.Entries.RemoveAt(0);
                changed = true;
            }
            else
            {
                if (now - e.LastUtc > NewSighting) { e.Sightings++; e.Reason = f.Reason; changed = true; }
                e.LastUtc = now;
            }
            if (changed) Save(d);
        }
    }

    public static List<Entry> Entries()
    {
        lock (Lock) return Load().Entries.OrderByDescending(e => e.LastUtc).ToList();
    }

    public static void Clear()
    {
        lock (Lock) { _data = new Data(); Save(_data); }
    }

    /// <summary>Forget what is in memory so the next call reads the file again (after the settings folder changed).</summary>
    public static void Reload() { lock (Lock) _data = null; }

    private static Data Load()
    {
        if (_data != null) return _data;
        try { _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data() : new Data(); }
        catch { _data = new Data(); }
        return _data;
    }

    private static void Save(Data d)
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(d));
        }
        catch { /* the list is a convenience; never let it stop monitoring */ }
    }
}
