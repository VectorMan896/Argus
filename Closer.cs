using System.ComponentModel;
using System.Diagnostics;

namespace Argus;

internal static class Closer
{
    /// <summary>
    /// Politely asks the program's windows to close, waits, then kills it (and optionally its child processes).
    /// Returns a short human-readable result for the log.
    /// </summary>
    public static string CloseProcess(int pid, DateTime? expectedStart, int gracefulSec, bool killTree, bool userInitiated)
    {
        Process p;
        try { p = Process.GetProcessById(pid); }
        catch { return "already gone"; }

        using (p)
        {
            string name = p.ProcessName;
            string? path = Native.GetProcessPath(pid);
            if (Safety.IsRefused(name, pid, path)) return $"refused: {name} is protected";
            if (!userInitiated && Safety.IsNeverAuto(name, pid, path)) return $"refused: {name} is a core/shell process";

            try
            {
                if (expectedStart != null && p.StartTime != expectedStart.Value)
                    return "skipped: process id was reused by a different program";
            }
            catch { /* StartTime not readable: carry on */ }

            try
            {
                if (gracefulSec > 0)
                {
                    var mine = WindowTools.Snapshot().Where(w => w.Pid == (uint)pid).ToList();
                    if (mine.Count > 0)
                    {
                        foreach (var w in mine) WindowTools.CloseWindow(w.Hwnd);
                        if (p.WaitForExit(gracefulSec * 1000)) return "closed gracefully";
                    }
                }
                p.Kill(killTree);
                p.WaitForExit(3000);
                return "killed";
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                return "access denied (run Argus as administrator to close it)";
            }
            catch (InvalidOperationException) { return "already exited"; }
            catch (Exception ex) { return "failed: " + ex.Message; }
        }
    }
}

internal static class Actions
{
    /// <summary>Does the default fix for a finding: close/kill the program, or neutralise the blocking window.</summary>
    public static string Execute(Finding f, Settings s, bool force = false)
    {
        if (f.Category == Category.Blocker)
        {
            if (f.Kind == BlockerKind.TransparentOverlay)
                return WindowTools.MakeClickThrough(f.Hwnd) ? "made click-through" : "could not change the window (access denied?)";
            WindowTools.Hide(f.Hwnd);
            return "hidden";
        }

        if (f.Category == Category.Hog && s.HogAction == HogAction.Throttle && !force)
            return Perf.Throttle(f.Pid, f.ProcStart);

        int graceful = (f.Category == Category.Hung || force) ? 0 : s.GracefulWaitSec;
        return Closer.CloseProcess(f.Pid, f.ProcStart, graceful, s.KillTree, userInitiated: false);
    }
}
