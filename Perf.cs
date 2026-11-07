using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Older-PC performance helpers.
//
//  Design rule: everything here is gentle and reversible.
//    * startup programs are switched off the way Task Manager does it (StartupApproved); nothing is deleted
//    * memory trimming only asks Windows to page out idle background programs; nothing is closed
//    * slowing a hog down only lowers its priority / turns on efficiency mode; nothing is closed
//    * temp cleanup is manual only, limited to your own temp folder, files only, old files only, never follows links
// -------------------------------------------------------------------------------------------------

internal sealed class SystemSummary
{
    public ulong TotalRam, AvailRam;
    public int MemLoad;
    public long DiskFree, DiskTotal;
    public string DiskName = "";
    public TimeSpan Uptime;
    public int ProcessCount;
}

internal enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

internal sealed class StartupItem
{
    public string Name = "", Command = "", Where = "", Publisher = "";
    public StartupSource Source;
    public bool Enabled, Broken;
    public bool NeedsAdmin => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder;
}

internal sealed class TrimCandidate
{
    public int Pid;
    public string Name = "";
    public long WorkingSet;
}

internal sealed class TempReport
{
    public string Root = "";
    public int Files, Deleted, Skipped;
    public long Bytes, Freed;
    public string? Error;
}

internal static class Perf
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWER_THROTTLING { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr h, int cls, ref POWER_THROTTLING s, int size);

    private const uint PROCESS_SET_INFORMATION = 0x200, PROCESS_SET_QUOTA = 0x100, PROCESS_QUERY_INFORMATION = 0x400;

    // ---- overview ---------------------------------------------------------------------------------

    public static string Bytes(long b)
    {
        double v = b;
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i >= 3 ? v.ToString("0.0") : v.ToString("0")) + " " + u[i];
    }

    public static SystemSummary Summary()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        var s = new SystemSummary
        {
            TotalRam = m.ullTotalPhys, AvailRam = m.ullAvailPhys, MemLoad = (int)m.dwMemoryLoad,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
        };
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory)!;
            var di = new DriveInfo(root);
            s.DiskName = root.TrimEnd('\\'); s.DiskFree = di.AvailableFreeSpace; s.DiskTotal = di.TotalSize;
        }
        catch { /* no disk info */ }
        foreach (var p in Process.GetProcesses()) { s.ProcessCount++; p.Dispose(); }
        return s;
    }

    public static double AvailablePercent(SystemSummary s) => s.TotalRam == 0 ? 100 : s.AvailRam * 100.0 / s.TotalRam;

    /// <summary>Plain-language suggestions based on this PC's actual numbers.</summary>
    public static List<string> Tips(SystemSummary s, int enabledStartup)
    {
        var t = new List<string>();
        double gb = s.TotalRam / 1073741824.0;
        if (gb < 6)
            t.Add($"This PC has {gb:0.#} GB of RAM. On older machines RAM is usually the limit: keep browser tabs and background apps down and the startup list short.");
        if (s.MemLoad >= 80)
            t.Add($"Memory is {s.MemLoad}% full right now. Try \"Free up memory\", or close programs you aren't using.");
        double freePct = s.DiskTotal > 0 ? s.DiskFree * 100.0 / s.DiskTotal : 100;
        if (freePct < 15)
            t.Add($"Drive {s.DiskName} has only {freePct:0}% free ({Bytes(s.DiskFree)}). Windows slows down when the system drive is nearly full: clear temp files and uninstall what you don't use.");
        if (s.Uptime.TotalDays >= 7)
            t.Add($"This PC has been running {s.Uptime.Days} days without a restart. A restart clears memory leaks and finishes pending updates.");
        if (enabledStartup > 8)
            t.Add($"{enabledStartup} programs start with Windows. Untick the ones you don't need every day in the list below (you can tick them again any time).");
        if (t.Count == 0)
            t.Add("Nothing stands out right now. Argus keeps watching for background hogs and analytics programs.");
        return t;
    }

    // ---- startup programs -----------------------------------------------------------------------------

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupItem> StartupItems()
    {
        var list = new List<StartupItem>();
        try { AddRun(list, RegistryHive.CurrentUser, RegistryView.Default, StartupSource.UserRun, "Run"); } catch { }
        try { AddRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, StartupSource.MachineRun, "Run"); } catch { }
        try { AddRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, StartupSource.MachineRun32, "Run32"); } catch { }
        try { AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, StartupSource.UserFolder); } catch { }
        try { AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, StartupSource.CommonFolder); } catch { }
        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsEnabled(RegistryKey? approved, string name) =>
        !(approved?.GetValue(name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1);   // odd first byte (03) = disabled

    private static void AddRun(List<StartupItem> list, RegistryHive hive, RegistryView view, StartupSource src, string approvedSub)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var run = baseKey.OpenSubKey(RunKey);
        if (run == null) return;
        using var approvedBase = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var approved = approvedBase.OpenSubKey(ApprovedBase + approvedSub);
        foreach (var name in run.GetValueNames())
        {
            string cmd = run.GetValue(name)?.ToString() ?? "";
            list.Add(Make(name, cmd, src, IsEnabled(approved, name)));
        }
    }

    private static void AddFolder(List<StartupItem> list, string dir, RegistryHive hive, StartupSource src)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        using var approvedBase = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var approved = approvedBase.OpenSubKey(ApprovedBase + "StartupFolder");
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            string fn = Path.GetFileName(file);
            if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            string cmd = fn.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : file;
            list.Add(Make(fn, cmd, src, IsEnabled(approved, fn)));
        }
    }

    private static string ResolveShortcut(string path)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return path;
            dynamic sh = Activator.CreateInstance(t)!;
            dynamic lnk = sh.CreateShortcut(path);
            string target = lnk.TargetPath, args = lnk.Arguments;
            Marshal.FinalReleaseComObject(lnk); Marshal.FinalReleaseComObject(sh);
            return string.IsNullOrEmpty(target) ? path : (args.Length > 0 ? $"\"{target}\" {args}" : $"\"{target}\"");
        }
        catch { return path; }
    }

    internal static string? ExePath(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : null; }
        int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (i >= 0) return cmd[..(i + 4)];
        int sp = cmd.IndexOf(' ');
        return sp > 0 ? cmd[..sp] : cmd;
    }

    private static StartupItem Make(string name, string cmd, StartupSource src, bool enabled)
    {
        string? exe = ExePath(cmd);
        bool exists = exe != null && File.Exists(exe);
        string publisher = "";
        if (exists) { try { publisher = FileVersionInfo.GetVersionInfo(exe!).CompanyName ?? ""; } catch { } }
        return new StartupItem
        {
            Name = name, Command = cmd, Source = src, Enabled = enabled, Publisher = publisher,
            Broken = exe != null && !exists && !(exe.Length == 0),
            Where = src switch
            {
                StartupSource.UserRun => "Your sign-in",
                StartupSource.MachineRun => "All users",
                StartupSource.MachineRun32 => "All users (32-bit)",
                StartupSource.UserFolder => "Your Startup folder",
                _ => "All users' Startup folder",
            },
        };
    }

    /// <summary>
    /// Turns a startup entry on/off exactly like Task Manager's Startup tab (StartupApproved). Nothing is deleted,
    /// so ticking it again restores it.
    /// </summary>
    public static string SetStartupEnabled(StartupItem item, bool enabled)
    {
        try
        {
            var hive = item.Source is StartupSource.UserRun or StartupSource.UserFolder ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            string sub = item.Source switch
            {
                StartupSource.UserRun or StartupSource.MachineRun => "Run",
                StartupSource.MachineRun32 => "Run32",
                _ => "StartupFolder",
            };
            using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = bk.CreateSubKey(ApprovedBase + sub, writable: true);
            var data = new byte[12];
            if (enabled) data[0] = 2;
            else { data[0] = 3; BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4); }
            key.SetValue(item.Name, data, RegistryValueKind.Binary);
            item.Enabled = enabled;
            Log.Write($"[Startup] {item.Name}: {(enabled ? "enabled" : "disabled")} (reversible, nothing was deleted)");
            return enabled ? "enabled" : "disabled";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return "needs administrator rights: run Argus as administrator to change this entry";
        }
        catch (Exception ex) { return "failed: " + ex.Message; }
    }

    // ---- free up memory ------------------------------------------------------------------------------------

    /// <summary>
    /// Idle background programs (no visible window, not the program you're using, not part of the same program as the
    /// one in front, not core Windows, not excluded) that hold real memory.
    /// </summary>
    public static List<TrimCandidate> TrimCandidates(long minBytes = 30L << 20)
    {
        var res = new List<TrimCandidate>();
        var wins = WindowTools.Snapshot();
        IntPtr fg = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
        Native.GetWindowThreadProcessId(fg, out uint fgPidU);
        int fgPid = (int)fgPidU;
        string? fgPath = fgPid > 0 ? Native.GetProcessPath(fgPid) : null;
        var winsBy = wins.GroupBy(w => (int)w.Pid).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                int pid = p.Id;
                if (pid <= 4 || pid == fgPid) continue;
                long ws;
                try { ws = p.WorkingSet64; } catch { continue; }
                if (ws < minBytes) continue;

                string name = p.ProcessName;
                string? path = Native.GetProcessPath(pid);
                if (path != null && fgPath != null && path.Equals(fgPath, StringComparison.OrdinalIgnoreCase)) continue;  // helpers of the app in front (browser tabs...)
                winsBy.TryGetValue(pid, out var pw);
                pw ??= new List<WinInfo>();
                if (pw.Any(w => w.IsRealWindow)) continue;
                if (Safety.IsNeverAuto(name, pid, path)) continue;
                if (Config.Excl.Match(name, path, pw.Select(w => w.Title).Where(t => t.Length > 0), pw.Select(w => w.Class)) != null) continue;
                res.Add(new TrimCandidate { Pid = pid, Name = name, WorkingSet = ws });
            }
        }
        return res.OrderByDescending(c => c.WorkingSet).ToList();
    }

    private static long WorkingSetOf(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.WorkingSet64; } catch { return 0; }
    }

    /// <summary>Asks Windows to page out one process's idle memory. Returns bytes released, or -1 if it couldn't.</summary>
    public static long TrimOne(int pid)
    {
        IntPtr h = Native.OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return -1;
        try
        {
            long before = WorkingSetOf(pid);
            if (!EmptyWorkingSet(h)) return -1;
            return Math.Max(0, before - WorkingSetOf(pid));
        }
        finally { Native.CloseHandle(h); }
    }

    public static (int Trimmed, long Freed, int Denied) TrimMemory(IEnumerable<TrimCandidate>? targets = null)
    {
        int ok = 0, denied = 0; long freed = 0;
        foreach (var c in targets ?? TrimCandidates())
        {
            long f = TrimOne(c.Pid);
            if (f < 0) denied++; else { ok++; freed += f; }
        }
        return (ok, freed, denied);
    }

    // ---- slow a hog down (instead of closing it) -----------------------------------------------------------

    public static string Throttle(int pid, DateTime? expectedStart)
    {
        Process p;
        try { p = Process.GetProcessById(pid); } catch { return "already gone"; }
        using (p)
        {
            string name = p.ProcessName;
            string? path = Native.GetProcessPath(pid);
            if (Safety.IsNeverAuto(name, pid, path)) return $"refused: {name} is a core/shell process";
            try
            {
                if (expectedStart != null && p.StartTime != expectedStart.Value)
                    return "skipped: process id was reused by a different program";
            }
            catch { /* StartTime not readable */ }

            try
            {
                var cur = p.PriorityClass;
                string msg;
                if (cur is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal) msg = $"already at {cur} priority";
                else { p.PriorityClass = ProcessPriorityClass.BelowNormal; msg = $"priority lowered from {cur} to BelowNormal"; }
                bool eco = TryEfficiencyMode(pid);
                return "slowed down: " + msg + (eco ? " + efficiency mode" : "");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { return "access denied (run Argus as administrator to change it)"; }
            catch (InvalidOperationException) { return "already exited"; }
            catch (Exception ex) { return "failed: " + ex.Message; }
        }
    }

    /// <summary>Windows 11 "Efficiency mode" (EcoQoS). Best effort; silently ignored where unsupported.</summary>
    private static bool TryEfficiencyMode(int pid)
    {
        IntPtr h = Native.OpenProcess(PROCESS_SET_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var st = new POWER_THROTTLING { Version = 1, ControlMask = 1, StateMask = 1 };   // EXECUTION_SPEED
            return SetProcessInformation(h, 4, ref st, Marshal.SizeOf<POWER_THROTTLING>());
        }
        finally { Native.CloseHandle(h); }
    }

    // ---- temp files -----------------------------------------------------------------------------------------

    /// <summary>Null if the folder is safe to clean: inside the user's temp folder, existing, not a link.</summary>
    public static string? ValidateTempRoot(string root)
    {
        string full;
        try { full = Path.GetFullPath(root).TrimEnd('\\'); } catch { return "invalid path"; }
        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
        bool inside = full.Equals(temp, StringComparison.OrdinalIgnoreCase) || full.StartsWith(temp + "\\", StringComparison.OrdinalIgnoreCase);
        if (!inside) return "only folders inside your user temp folder can be cleaned";
        if (full.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 4) return "that path is too shallow to be a temp folder";
        if (!Directory.Exists(full)) return "folder does not exist";
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return "folder is a link";
        return null;
    }

    /// <summary>
    /// Counts (and, if <paramref name="delete"/>, removes) files older than N days. Never follows links, never touches
    /// anything outside the validated temp root, skips anything that is locked or read-only.
    /// </summary>
    public static TempReport TempFiles(string root, int olderThanDays, bool delete)
    {
        var r = new TempReport { Root = root };
        string? err = ValidateTempRoot(root);
        if (err != null) { r.Error = err; return r; }

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(0, olderThanDays));
        Walk(new DirectoryInfo(root), true);
        return r;

        void Walk(DirectoryInfo dir, bool isRoot)
        {
            List<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos().ToList(); } catch { r.Skipped++; return; }
            foreach (var e in entries)
            {
                try
                {
                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { r.Skipped++; continue; }   // never follow links
                    if (e is DirectoryInfo d) { Walk(d, false); continue; }
                    var f = (FileInfo)e;
                    var stamp = f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc;
                    if (stamp > cutoff) continue;
                    long len = f.Length;
                    r.Files++; r.Bytes += len;
                    if (!delete) continue;
                    try { f.Delete(); r.Deleted++; r.Freed += len; } catch { r.Skipped++; }
                }
                catch { r.Skipped++; }
            }
            if (delete && !isRoot)
            {
                try { if (!dir.EnumerateFileSystemInfos().Any() && dir.LastWriteTimeUtc <= cutoff) dir.Delete(false); } catch { }
            }
        }
    }
}
