namespace Argus;

/// <summary>Runs the detector on a timer, performs Auto actions, and publishes the list of things waiting for a decision.</summary>
internal sealed class Watcher : IDisposable
{
    private readonly Detector _det = new(Config.Settings, Config.Block, Config.Excl);
    private System.Threading.Timer? _timer;
    private int _busy;

    private readonly Dictionary<string, DateTime> _first = new();
    private readonly HashSet<string> _notified = new();
    private readonly HashSet<string> _gaveUp = new();                 // auto actions that failed once: don't retry/spam
    private readonly HashSet<string> _acted = new();                  // auto actions already applied (e.g. a hog already slowed down)
    private DateTime _lastTrim = DateTime.MinValue;
    private readonly Dictionary<string, List<DateTime>> _kills = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _respawnWarned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Finding> _respawn = new(StringComparer.OrdinalIgnoreCase);     // "keeps coming back" findings waiting for the user
    private readonly HashSet<string> _respawnDismissed = new(StringComparer.OrdinalIgnoreCase);    // all three guarded by lock (_respawn)
    private readonly HashSet<string> _respawnNotified = new(StringComparer.OrdinalIgnoreCase);

    public event Action<IReadOnlyList<Finding>>? Updated;
    public event Action<Finding>? NeedsAttention;
    public IReadOnlyList<Finding> Current { get; private set; } = Array.Empty<Finding>();

    /// <summary>True while a scan is running (tests wait for this to clear before moving on).</summary>
    internal bool IsBusy => Volatile.Read(ref _busy) == 1;

    public void Start() => _timer = new System.Threading.Timer(_ => Tick(), null, 1500, Timeout.Infinite);

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try { if (!Config.Settings.Paused) Run(); }
        catch (Exception ex) { Log.Write("scan error: " + ex.Message); }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            _timer?.Change(Math.Max(1, Config.Settings.ScanIntervalSec) * 1000, Timeout.Infinite);
        }
    }

    public void ScanNow() => ThreadPool.QueueUserWorkItem(_ =>
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try { Run(); }
        catch (Exception ex) { Log.Write("scan error: " + ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    });

    private void Run()
    {
        var s = Config.Settings;
        var findings = _det.Scan();
        var pending = new List<Finding>();

        foreach (var f in findings)
        {
            if (_first.TryGetValue(f.Key, out var t)) f.First = t; else _first[f.Key] = f.First;
            if (_acted.Contains(f.Key)) continue;

            switch (s.For(f.Category))
            {
                case Handling.Auto when s.Learning:
                    Learning.Record(f, s);              // first-run learning mode: write down what would have happened, do nothing
                    break;

                case Handling.Auto when !_gaveUp.Contains(f.Key):
                    string result = Actions.Execute(f, s);
                    Log.Write($"[{f.CategoryLabel}] {f.ProcName} (pid {f.Pid}): {f.Reason} -> {result}");
                    bool ok = !(result.StartsWith("refused") || result.StartsWith("access denied") || result.StartsWith("failed")
                                || result.StartsWith("could not") || result.StartsWith("skipped"));
                    if (ok)
                    {
                        Stats.Record(f, result);
                        if (f.Category == Category.Analytics) NoteKill(f.ProcName, f.Path);
                        if (f.Category == Category.Hog) _acted.Add(f.Key);
                        continue;
                    }
                    _gaveUp.Add(f.Key);
                    pending.Add(f);
                    break;

                case Handling.Auto:       // gave up earlier: show it so the user can decide
                case Handling.Ask:
                    pending.Add(f);
                    if (_notified.Add(f.Key)) NeedsAttention?.Invoke(f);
                    break;
            }
        }

        var keys = findings.Select(f => f.Key).ToHashSet();
        foreach (var k in _first.Keys.Where(k => !keys.Contains(k)).ToList()) _first.Remove(k);
        _notified.RemoveWhere(k => !keys.Contains(k));
        _gaveUp.RemoveWhere(k => !keys.Contains(k));
        _acted.RemoveWhere(k => !keys.Contains(k));

        AutoTrim();
        lock (_respawn)
            foreach (var r in _respawn.Values.OrderBy(r => r.First)) { pending.Add(r); if (_respawnNotified.Add(r.Key)) NeedsAttention?.Invoke(r); }
        Current = pending;
        Updated?.Invoke(pending);
    }

    /// <summary>When RAM runs low (and the user enabled it), ask Windows to page out idle background programs. Never closes anything.</summary>
    private void AutoTrim()
    {
        int threshold = Config.Settings.AutoTrimBelowPercent;
        if (threshold <= 0 || (DateTime.UtcNow - _lastTrim).TotalMinutes < 2) return;
        var before = Perf.Summary();
        if (Perf.AvailablePercent(before) >= threshold) return;
        _lastTrim = DateTime.UtcNow;
        var (n, _, _) = Perf.TrimMemory();
        Log.Write($"[Memory] only {Perf.AvailablePercent(before):0}% of RAM was free: asked Windows to page out {n} idle background programs (nothing closed).");
    }

    internal void NoteKill(string name, string? path = null)
    {
        var now = DateTime.UtcNow;
        if (!_kills.TryGetValue(name, out var list)) _kills[name] = list = new List<DateTime>();
        list.Add(now);
        list.RemoveAll(d => (now - d).TotalMinutes > 10);
        if (list.Count >= 3 && (!_respawnWarned.TryGetValue(name, out var w) || (now - w).TotalHours >= 1))
        {
            _respawnWarned[name] = now;
            Log.Write($"NOTE: {name} has been closed {list.Count} times in 10 minutes - something keeps restarting it " +
                      "(a service or scheduled task). Disabling that service/task is a cleaner fix than closing it each time.");
            RaiseRespawn(name, path, list.Count);
        }
    }

    /// <summary>Looks for what keeps restarting the program. If something is found it is offered to the user; nothing is changed here.</summary>
    private void RaiseRespawn(string name, string? path, int times)
    {
        lock (_respawn) if (_respawnDismissed.Contains(name)) return;
        List<PersistenceSource> sources;
        try { sources = Persistence.Find(path); }
        catch (Exception ex) { Log.Write("could not look for what restarts " + name + ": " + ex.Message); return; }
        if (sources.Count == 0) return;
        var f = new Finding
        {
            Key = "respawn:" + name, Category = Category.Respawn, ProcName = name, Path = path, Sources = sources,
            Reason = $"Closed {times} times in 10 minutes. Restarted by: " + string.Join("; ", sources.Select(s => $"{s.KindLabel} \"{s.Display}\"")),
        };
        lock (_respawn) _respawn[name] = f;
    }

    /// <summary>The user dealt with it (disabled the starters, or said to leave it): stop listing it.</summary>
    public void ResolveRespawn(Finding f, bool dontAskAgain)
    {
        lock (_respawn)
        {
            _respawn.Remove(f.ProcName);
            _respawnNotified.Remove(f.Key);
            if (dontAskAgain) _respawnDismissed.Add(f.ProcName);
        }
        ScanNow();
    }

    /// <summary>Run one finding's fix on a worker thread (user clicked a button).</summary>
    public void Apply(Finding f, bool force = false) => Task.Run(() =>
    {
        string result = Actions.Execute(f, Config.Settings, force);
        Log.Write($"[{f.CategoryLabel}] {f.ProcName} (pid {f.Pid}): {(force ? "force " : "")}closed by you -> {result}");
        Stats.Record(f, result);
        ScanNow();
    });

    public void Dispose() => _timer?.Dispose();
}
