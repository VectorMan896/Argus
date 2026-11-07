using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Argus;

/// <summary>
/// Looks at running processes and windows and returns what deserves attention.
/// Holds state between scans (CPU deltas, "since when" timers). Not thread-safe: call from one thread at a time.
/// </summary>
internal sealed class Detector
{
    private readonly Settings _s;
    private readonly PatternList _block, _excl;
    private readonly int _cores = Environment.ProcessorCount;

    private readonly Dictionary<int, (ulong Ticks, DateTime Wall)> _cpu = new();
    private readonly Dictionary<int, DateTime> _hungSince = new();
    private readonly Dictionary<int, DateTime> _hogSince = new();
    private readonly Dictionary<IntPtr, DateTime> _popupSeen = new();
    private readonly Dictionary<int, string?> _pathCache = new();
    private readonly Dictionary<string, string?> _companyCache = new(StringComparer.OrdinalIgnoreCase);

    // Names that real Windows components use. A program with one of these names running from somewhere else is a classic disguise.
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "lsass", "csrss", "winlogon", "services", "smss", "wininit", "explorer", "taskhostw",
        "dwm", "spoolsv", "conhost", "dllhost", "runtimebroker", "searchindexer", "audiodg", "sihost",
    };

    private static readonly string[] SuspiciousDirs =
    {
        "\\Temp\\", "\\Downloads\\", "\\$Recycle.Bin\\", "\\Users\\Public\\", "\\AppData\\Local\\Temp\\",
    };

    public Detector(Settings s, PatternList block, PatternList excl)
    {
        _s = s; _block = block; _excl = excl;
    }

    public List<Finding> Scan()
    {
        var now = DateTime.UtcNow;
        var results = new List<Finding>();

        var wins = WindowTools.Snapshot();
        IntPtr fgRoot = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
        Native.GetWindowThreadProcessId(fgRoot, out uint fgPidU);
        int fgPid = (int)fgPidU;

        var winsByPid = wins.GroupBy(w => (int)w.Pid).ToDictionary(g => g.Key, g => g.ToList());
        var procInfo = new Dictionary<int, (string Name, string? Path)>();
        var alive = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                int pid = p.Id;
                if (pid <= 4) continue;
                alive.Add(pid);

                string name = p.ProcessName;
                string? path = GetPath(pid);
                procInfo[pid] = (name, path);

                winsByPid.TryGetValue(pid, out var pw);
                pw ??= new List<WinInfo>();

                var titles = pw.Select(w => w.Title).Where(t => t.Length > 0);
                var classes = pw.Select(w => w.Class);
                if (_excl.Match(name, path, titles, classes) != null) { ClearState(pid); continue; }
                if (Safety.IsNeverAuto(name, pid, path)) { ClearState(pid); continue; }

                string fileName = path != null ? Path.GetFileName(path) : name + ".exe";
                bool inWinDir = Safety.IsInWindowsDir(path);
                bool isForeground = pid == fgPid;

                // 1. analytics / telemetry (explicit blocklist) ---------------------------------
                if (_s.Analytics != Handling.Off)
                {
                    string? hit = _block.Match(name, path);
                    if (hit != null)
                    {
                        results.Add(Make(Category.Analytics, p, name, path, $"matches blocklist entry \"{hit}\""));
                        ClearState(pid);
                        continue;
                    }
                }

                // 2. hung windows -----------------------------------------------------------------
                if (_s.Hung != Handling.Off)
                {
                    var hung = pw.FirstOrDefault(w => w.Hung && w.Visible && !w.Cloaked && !w.Iconic);
                    if (hung != null)
                    {
                        if (!_hungSince.TryGetValue(pid, out var since)) _hungSince[pid] = since = now;
                        double secs = (now - since).TotalSeconds;
                        if (secs >= _s.HungSeconds)
                        {
                            string title = hung.Title.Length > 0 ? $"\"{Trunc(hung.Title, 50)}\"" : hung.Class;
                            results.Add(Make(Category.Hung, p, name, path, $"window {title} has not responded for {secs:0}s", hung.Hwnd));
                            continue;
                        }
                    }
                    else _hungSince.Remove(pid);
                }

                // 3. weird programs -----------------------------------------------------------------
                bool hasRealWindow = pw.Any(w => w.IsRealWindow);
                if (_s.Weird != Handling.Off && !isForeground && !(inWinDir && _s.SkipWindowsDir && !SystemNames.Contains(name)))
                {
                    var (score, why) = Score(name, path, hasRealWindow);
                    if (score >= _s.WeirdScoreThreshold)
                    {
                        results.Add(Make(Category.Weird, p, name, path, $"score {score}: {string.Join("; ", why)}"));
                        continue;
                    }
                }

                // 4. background resource hogs ---------------------------------------------------------
                if (_s.Hog != Handling.Off && !isForeground && !hasRealWindow && !(inWinDir && _s.SkipWindowsDir))
                {
                    double? cpu = SampleCpu(pid, now);
                    double memMB = p.PrivateMemorySize64 / 1048576.0;
                    bool hotCpu = cpu is double c && c >= _s.HogCpuPercent;
                    bool hotMem = memMB >= _s.HogMemoryMB;
                    if (hotCpu || hotMem)
                    {
                        if (!_hogSince.TryGetValue(pid, out var since)) _hogSince[pid] = since = now;
                        double secs = (now - since).TotalSeconds;
                        if (secs >= _s.HogSeconds)
                        {
                            var parts = new List<string>();
                            if (hotCpu) parts.Add($"{cpu:0}% CPU");
                            if (hotMem) parts.Add($"{memMB:0} MB memory");
                            results.Add(Make(Category.Hog, p, name, path, $"no visible window, using {string.Join(" and ", parts)} for {secs:0}s"));
                        }
                    }
                    else _hogSince.Remove(pid);
                }
                else
                {
                    _hogSince.Remove(pid);
                    if (isForeground || hasRealWindow) _cpu.Remove(pid);
                }
            }
        }

        // 5. invisible windows that swallow clicks ------------------------------------------------------
        if (_s.Blocker != Handling.Off)
        {
            var stillThere = new HashSet<IntPtr>();
            foreach (var w in wins)
            {
                if (!w.Visible || w.Cloaked || w.Iconic || w.Pid == (uint)Environment.ProcessId) continue;
                procInfo.TryGetValue((int)w.Pid, out var pi);
                string pname = pi.Name ?? "?";

                if (w.IsInvisibleClickCatcher && (long)w.Rect.Width * w.Rect.Height >= _s.MinOverlayArea)
                {
                    if (_excl.Match(pname, pi.Path, new[] { w.Title }, new[] { w.Class }) != null) continue;
                    results.Add(new Finding
                    {
                        Key = "B:" + w.Hwnd,
                        Category = Category.Blocker, Kind = BlockerKind.TransparentOverlay,
                        Pid = (int)w.Pid, ProcName = pname, Path = pi.Path, Hwnd = w.Hwnd, WindowClass = w.Class,
                        Reason = $"fully transparent {w.Rect.Width}x{w.Rect.Height} window at {w.Rect.Left},{w.Rect.Top} ({w.Class}) that still catches clicks",
                    });
                }
                else if (IsPopupHost(w))
                {
                    stillThere.Add(w.Hwnd);
                    if (!_popupSeen.TryGetValue(w.Hwnd, out var first)) _popupSeen[w.Hwnd] = first = now;
                    double age = (now - first).TotalSeconds;

                    IntPtr owner = Native.GetWindow(w.Hwnd, Native.GW_OWNER);
                    bool ownerUnfocused = owner != IntPtr.Zero && fgRoot != owner && fgRoot != w.Hwnd;
                    bool stale = age >= _s.StalePopupSeconds || (ownerUnfocused && age >= 5);
                    if (stale && _excl.Match(pname, pi.Path, new[] { w.Title }, new[] { w.Class }) == null)
                    {
                        results.Add(new Finding
                        {
                            Key = "B:" + w.Hwnd,
                            Category = Category.Blocker, Kind = BlockerKind.StalePopup,
                            Pid = (int)w.Pid, ProcName = pname, Path = pi.Path, Hwnd = w.Hwnd, WindowClass = w.Class,
                            Reason = $"popup host window {w.Rect.Width}x{w.Rect.Height} at {w.Rect.Left},{w.Rect.Top} left open for {age:0}s" +
                                     (ownerUnfocused ? " while its owner window is not focused" : ""),
                        });
                    }
                }
            }
            foreach (var h in _popupSeen.Keys.Where(h => !stillThere.Contains(h)).ToList()) _popupSeen.Remove(h);
        }

        // forget state of processes that went away
        foreach (var pid in _cpu.Keys.Where(k => !alive.Contains(k)).ToList()) _cpu.Remove(pid);
        foreach (var pid in _hungSince.Keys.Where(k => !alive.Contains(k)).ToList()) _hungSince.Remove(pid);
        foreach (var pid in _hogSince.Keys.Where(k => !alive.Contains(k)).ToList()) _hogSince.Remove(pid);
        foreach (var pid in _pathCache.Keys.Where(k => !alive.Contains(k)).ToList()) _pathCache.Remove(pid);

        return results;
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static bool IsPopupHost(WinInfo w) =>
        w.Class.Contains("PopupWindowSiteBridge", StringComparison.OrdinalIgnoreCase)
        || (w.Title == "PopupHost" && w.NoActivate);

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private void ClearState(int pid)
    {
        _hungSince.Remove(pid); _hogSince.Remove(pid); _cpu.Remove(pid);
    }

    private string? GetPath(int pid)
    {
        if (_pathCache.TryGetValue(pid, out var cached)) return cached;
        return _pathCache[pid] = Native.GetProcessPath(pid);
    }

    private static Finding Make(Category cat, Process p, string name, string? path, string reason, IntPtr hwnd = default)
    {
        DateTime? start = null;
        try { start = p.StartTime; } catch { /* access denied */ }
        return new Finding
        {
            Key = $"{cat}:{p.Id}",
            Category = cat, Pid = p.Id, ProcName = name, Path = path, ProcStart = start, Reason = reason, Hwnd = hwnd,
        };
    }

    /// <summary>CPU use of a process as % of total machine capacity since the previous call; null on first sample.</summary>
    private double? SampleCpu(int pid, DateTime now)
    {
        ulong? ticks = Native.GetCpuTicks(pid);
        if (ticks == null) return null;
        double? result = null;
        if (_cpu.TryGetValue(pid, out var prev))
        {
            double wall = (now - prev.Wall).TotalSeconds;
            if (wall > 0.2)
                result = (ticks.Value - prev.Ticks) / 1e7 / wall / _cores * 100.0;
        }
        _cpu[pid] = (ticks.Value, now);
        return result;
    }

    private string? Company(string path)
    {
        if (_companyCache.TryGetValue(path, out var c)) return c;
        try { c = FileVersionInfo.GetVersionInfo(path).CompanyName; } catch { c = null; }
        return _companyCache[path] = string.IsNullOrWhiteSpace(c) ? null : c;
    }

    /// <summary>Heuristic "does this look off?" score with human-readable reasons.</summary>
    internal (int Score, List<string> Why) Score(string name, string? path, bool hasRealWindow)
    {
        var why = new List<string>();
        int score = 0;
        if (path == null) return (0, why);                      // can't judge what we can't see

        // pretending to be a Windows component
        if (SystemNames.Contains(name) && !Safety.IsInWindowsDir(path))
        {
            score += 3; why.Add($"uses the Windows component name \"{name}\" but runs from {Path.GetDirectoryName(path)}");
        }

        if (Safety.IsInTrustedInstallDir(path)) return (score, why);

        // digital signature, checked by Windows itself: a valid signature from a trusted publisher clears the usual oddities
        // (an installer in Temp, a tray program with no window), while a signature that no longer matches the file is a red flag
        var sig = Authenticode.Check(path);
        if (sig.State == SigState.Valid)
        {
            why.Add($"signed by {sig.Publisher ?? "a trusted publisher"}");
            return (score, why);                      // only the "pretending to be a Windows component" points can remain
        }
        if (sig.State == SigState.Invalid) { score += 3; why.Add("its digital signature no longer matches the file (it was changed after signing)"); }
        else if (sig.State == SigState.Untrusted) { score += 1; why.Add($"signed, but {sig.Detail}"); }

        string lower = path;
        if (SuspiciousDirs.Any(d => lower.Contains(d, StringComparison.OrdinalIgnoreCase)))
        {
            score += 2; why.Add("runs from a temp/downloads-type folder");
        }
        else if (lower.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\", StringComparison.OrdinalIgnoreCase))
        {
            score += 1; why.Add("runs from ProgramData");
        }

        if (!hasRealWindow) { score += 1; why.Add("no visible window"); }

        string bare = Path.GetFileNameWithoutExtension(path);
        if (LooksRandom(bare)) { score += 1; why.Add($"random-looking name \"{bare}\""); }

        if (Company(path) == null) { score += 1; why.Add("no publisher in file properties"); }

        return (score, why);
    }

    private static readonly Regex Hex = new("^[0-9a-f]{8,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksRandom(string s)
    {
        if (s.Length < 8 || !s.All(char.IsLetterOrDigit)) return false;
        if (Hex.IsMatch(s)) return true;
        int vowels = s.Count(ch => "aeiouAEIOU".IndexOf(ch) >= 0);
        return (double)vowels / s.Length < 0.15;
    }
}
