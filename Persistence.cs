using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  "Programs that keep coming back".
//
//  Some analytics programs are restarted by a Windows service, a scheduled task or a startup entry. Closing them again and
//  again is pointless; the clean fix is to disable what restarts them. This file finds those starters and can disable and
//  restore them.
//
//  Safety:
//    * never offered for core Windows programs (the same rails as everywhere else)
//    * nothing is deleted: a service is set to Disabled, a task is switched off, a startup entry is un-ticked
//    * every change is written to a list (disabled-by-argus.json) so it can be restored from the Rules page
//    * services and system tasks need administrator rights; without them the change is refused and nothing is touched
// -------------------------------------------------------------------------------------------------

internal enum PersistKind { Service, ScheduledTask, StartupEntry }

internal sealed class PersistenceSource
{
    public PersistKind Kind { get; set; }
    public string Name { get; set; } = "";            // service key name / full task path / startup entry name
    public string Display { get; set; } = "";
    public string Detail { get; set; } = "";          // how it starts, in plain words
    public bool NeedsAdmin { get; set; }
    public StartupSource StartupSrc { get; set; }     // only for startup entries

    public string KindLabel => Kind switch { PersistKind.Service => "Windows service", PersistKind.ScheduledTask => "scheduled task", _ => "startup entry" };
}

internal sealed class DisabledRecord
{
    public PersistKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Display { get; set; } = "";
    public int PreviousStart { get; set; }            // services: the start type before it was disabled (2 automatic, 3 manual, ...)
    public StartupSource StartupSrc { get; set; }
    public string Exe { get; set; } = "";
    public DateTime WhenUtc { get; set; }
}

/// <summary>Reads and changes a service's start type. A seam so tests never touch a real service.</summary>
internal interface IServiceControl
{
    int? GetStart(string name);
    /// <summary>"ok", or a short human-readable reason it was not changed.</summary>
    string SetStart(string name, int start);
}

internal sealed class ScmServiceControl : IServiceControl
{
    private const uint SC_MANAGER_CONNECT = 1, SERVICE_CHANGE_CONFIG = 2, SERVICE_NO_CHANGE = 0xFFFFFFFF;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig(IntPtr svc, uint serviceType, uint startType, uint errorControl, string? binaryPath, string? loadOrderGroup,
                                                   IntPtr tagId, string? dependencies, string? account, string? password, string? displayName);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);

    public int? GetStart(string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            return k?.GetValue("Start") is int s ? s : null;
        }
        catch { return null; }
    }

    public string SetStart(string name, int start)
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return Describe(Marshal.GetLastWin32Error());
        try
        {
            IntPtr svc = OpenService(scm, name, SERVICE_CHANGE_CONFIG);
            if (svc == IntPtr.Zero) return Describe(Marshal.GetLastWin32Error());
            try
            {
                return ChangeServiceConfig(svc, SERVICE_NO_CHANGE, (uint)start, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null)
                    ? "ok" : Describe(Marshal.GetLastWin32Error());
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    private static string Describe(int error) => error switch
    {
        5 => "needs administrator rights (use Rules > Administrator mode)",
        1060 => "that service no longer exists",
        _ => $"Windows refused (error {error})",
    };
}

internal static class Persistence
{
    /// <summary>Replaced by tests so they never change a real service.</summary>
    internal static IServiceControl Services = new ScmServiceControl();

    private static string RecordsPath => Path.Combine(Config.Dir, "disabled-by-argus.json");
    private static readonly object Lock = new();

    // ---- finding the starters ------------------------------------------------------------------------------------------

    /// <summary>Every service, scheduled task and startup entry that starts this program. Empty for core Windows programs.</summary>
    public static List<PersistenceSource> Find(string? exePath)
    {
        var list = new List<PersistenceSource>();
        if (string.IsNullOrWhiteSpace(exePath)) return list;
        string name = Path.GetFileNameWithoutExtension(exePath);
        if (Safety.IsNeverAuto(name, 0, exePath)) return list;               // never the plumbing of core Windows

        try { list.AddRange(FindServices(exePath, ReadServices())); } catch { }
        try { list.AddRange(FindTasks(exePath)); } catch { }
        try { list.AddRange(FindStartup(exePath)); } catch { }
        return list;
    }

    /// <summary>
    /// A "keeps coming back" finding for one program, listing everything that starts it. Null when nothing does (or it is core Windows).
    /// Used by the watcher after repeated closes and by the "What restarts it?" button. Nothing is changed.
    /// </summary>
    public static Finding? FindingFor(string name, string? path, string reasonPrefix = "")
    {
        var sources = Find(path);
        if (sources.Count == 0) return null;
        return new Finding
        {
            Key = "respawn:" + name, Category = Category.Respawn, ProcName = name, Path = path, Sources = sources,
            Reason = reasonPrefix + "Restarted by: " + string.Join("; ", sources.Select(s => $"{s.KindLabel} \"{s.Display}\"")),
        };
    }

    internal static bool SameExe(string? commandOrPath, string target)
    {
        if (string.IsNullOrWhiteSpace(commandOrPath)) return false;
        string? exe = Perf.ExePath(commandOrPath.Trim());
        if (exe == null) return false;
        try { return Path.GetFullPath(exe.Trim('"')).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static IEnumerable<(string Name, string? Display, string? ImagePath, int Start)> ReadServices()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (root == null) yield break;
        foreach (var name in root.GetSubKeyNames())
        {
            string? image = null, display = null; int start = 3;
            try
            {
                using var k = root.OpenSubKey(name);
                if (k == null) continue;
                image = k.GetValue("ImagePath") as string;
                display = k.GetValue("DisplayName") as string;
                if (k.GetValue("Start") is int s) start = s;
            }
            catch { continue; }
            yield return (name, display, image, start);
        }
    }

    /// <summary>Pure matching, so it can be tested with made-up services.</summary>
    internal static List<PersistenceSource> FindServices(string exe, IEnumerable<(string Name, string? Display, string? ImagePath, int Start)> services)
    {
        var found = new List<PersistenceSource>();
        foreach (var (name, display, image, start) in services)
        {
            if (start == 4 || string.IsNullOrWhiteSpace(image)) continue;            // already disabled, or no program
            if (!SameExe(Environment.ExpandEnvironmentVariables(image), exe)) continue;
            string how = start switch { 0 or 1 => "starts when Windows boots", 2 => "starts automatically", _ => "starts on demand" };
            found.Add(new PersistenceSource { Kind = PersistKind.Service, Name = name, Display = string.IsNullOrWhiteSpace(display) || display.StartsWith('@') ? name : display, Detail = how, NeedsAdmin = true });
        }
        return found;
    }

    private static List<PersistenceSource> FindTasks(string exe)
    {
        var found = new List<PersistenceSource>();
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type == null) return found;
        dynamic svc = Activator.CreateInstance(type)!;
        svc.Connect();

        void Walk(dynamic folder)
        {
            try
            {
                foreach (dynamic task in folder.GetTasks(1))                           // 1 = include hidden tasks
                {
                    try
                    {
                        if (!(bool)task.Enabled) continue;
                        foreach (dynamic action in task.Definition.Actions)
                        {
                            if ((int)action.Type != 0) continue;                         // 0 = "run a program"
                            string? p = action.Path as string;
                            if (p != null && SameExe(Environment.ExpandEnvironmentVariables(p), exe))
                            {
                                found.Add(new PersistenceSource { Kind = PersistKind.ScheduledTask, Name = (string)task.Path, Display = (string)task.Name, Detail = "runs on a schedule or event", NeedsAdmin = TaskNeedsAdmin(task.Definition.Principal) });
                                break;
                            }
                        }
                    }
                    catch { /* a task we cannot read */ }
                }
            }
            catch { }
            try { foreach (dynamic sub in folder.GetFolders(0)) Walk(sub); } catch { }
        }
        Walk(svc.GetFolder("\\"));
        return found;
    }

    /// <summary>A task owned by another account (SYSTEM, a service account, a group) or set to run with highest privileges cannot be changed without administrator rights.</summary>
    private static bool TaskNeedsAdmin(dynamic principal)
    {
        try
        {
            if ((int)principal.RunLevel == 1) return true;                                    // TASK_RUNLEVEL_HIGHEST
            if (!string.IsNullOrEmpty(principal.GroupId as string)) return true;
            string user = (principal.UserId as string) ?? "";
            if (user.Length == 0) return false;
            return !(user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)
                     || user.EndsWith("\\" + Environment.UserName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return true; }                                                                  // unsure: assume it needs it, the result tells the truth
    }

    private static List<PersistenceSource> FindStartup(string exe) =>
        Perf.StartupItems().Where(i => i.Enabled && SameExe(i.Command, exe))
            .Select(i => new PersistenceSource { Kind = PersistKind.StartupEntry, Name = i.Name, Display = i.Name, Detail = "starts when you sign in", NeedsAdmin = i.NeedsAdmin, StartupSrc = i.Source })
            .ToList();

    // ---- disabling and restoring ---------------------------------------------------------------------------------------

    /// <summary>Disables one starter. Returns "disabled", or the reason nothing was changed. Everything disabled is written to the undo list.</summary>
    public static string Disable(PersistenceSource s, string exe = "")
    {
        try
        {
            switch (s.Kind)
            {
                case PersistKind.Service:
                {
                    int? current = Services.GetStart(s.Name);
                    if (current == null) return "could not read the service";
                    if (current == 4) return "already disabled";
                    string r = Services.SetStart(s.Name, 4);
                    if (r != "ok") return r;
                    AddRecord(new DisabledRecord { Kind = s.Kind, Name = s.Name, Display = s.Display, PreviousStart = current.Value, Exe = exe });
                    return "disabled";
                }
                case PersistKind.ScheduledTask:
                {
                    string r = SetTaskEnabled(s.Name, false);
                    if (r != "ok") return r;
                    AddRecord(new DisabledRecord { Kind = s.Kind, Name = s.Name, Display = s.Display, Exe = exe });
                    return "disabled";
                }
                default:
                {
                    var item = Perf.StartupItems().FirstOrDefault(i => i.Name == s.Name && i.Source == s.StartupSrc);
                    if (item == null) return "no longer there";
                    string r = Perf.SetStartupEnabled(item, false);
                    if (r != "disabled") return r;
                    AddRecord(new DisabledRecord { Kind = s.Kind, Name = s.Name, Display = s.Display, StartupSrc = s.StartupSrc, Exe = exe });
                    return "disabled";
                }
            }
        }
        catch (Exception ex) { return "failed: " + ex.Message; }
    }

    /// <summary>Puts one disabled starter back the way it was. Returns "restored" or the reason it was not.</summary>
    public static string Restore(DisabledRecord r)
    {
        try
        {
            string result;
            switch (r.Kind)
            {
                case PersistKind.Service:
                    result = Services.SetStart(r.Name, r.PreviousStart is >= 0 and <= 3 ? r.PreviousStart : 3);
                    if (result != "ok") return result;
                    break;
                case PersistKind.ScheduledTask:
                    result = SetTaskEnabled(r.Name, true);
                    if (result != "ok") return result;
                    break;
                default:
                    var item = Perf.StartupItems().FirstOrDefault(i => i.Name == r.Name && i.Source == r.StartupSrc);
                    if (item != null)
                    {
                        string s = Perf.SetStartupEnabled(item, true);
                        if (s != "enabled") return s;
                    }
                    break;
            }
            RemoveRecord(r);
            return "restored";
        }
        catch (Exception ex) { return "failed: " + ex.Message; }
    }

    private static string SetTaskEnabled(string fullPath, bool enabled)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) return "Task Scheduler is not available";
            dynamic svc = Activator.CreateInstance(type)!;
            svc.Connect();
            int cut = fullPath.LastIndexOf('\\');
            string folder = cut <= 0 ? "\\" : fullPath[..cut];
            string name = fullPath[(cut + 1)..];
            dynamic task = svc.GetFolder(folder).GetTask(name);
            task.Enabled = enabled;
            return "ok";
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005) { return "needs administrator rights (use Rules > Administrator mode)"; }
        catch (UnauthorizedAccessException) { return "needs administrator rights (use Rules > Administrator mode)"; }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { return "that task no longer exists"; }
        catch (FileNotFoundException) { return "that task no longer exists"; }              // .NET turns ERROR_FILE_NOT_FOUND into this
        catch (Exception ex) { return "failed: " + ex.Message; }
    }

    // ---- the undo list ----------------------------------------------------------------------------------------------------

    public static List<DisabledRecord> Records()
    {
        lock (Lock)
        {
            try { return File.Exists(RecordsPath) ? JsonSerializer.Deserialize<List<DisabledRecord>>(File.ReadAllText(RecordsPath)) ?? new() : new(); }
            catch { return new(); }
        }
    }

    private static void Save(List<DisabledRecord> list)
    {
        Directory.CreateDirectory(Config.Dir);
        File.WriteAllText(RecordsPath, JsonSerializer.Serialize(list));
    }

    private static void AddRecord(DisabledRecord r)
    {
        r.WhenUtc = DateTime.UtcNow;
        lock (Lock)
        {
            var list = Records();
            list.RemoveAll(x => x.Kind == r.Kind && x.Name == r.Name);
            list.Add(r);
            Save(list);
        }
        Log.Write($"Disabled {r.Display} ({new PersistenceSource { Kind = r.Kind }.KindLabel}). It can be restored from Rules > Disabled by Argus.");
    }

    private static void RemoveRecord(DisabledRecord r)
    {
        lock (Lock)
        {
            var list = Records();
            list.RemoveAll(x => x.Kind == r.Kind && x.Name == r.Name);
            Save(list);
        }
        Log.Write($"Restored {r.Display}.");
    }
}
