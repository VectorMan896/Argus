using System.Text;

namespace Argus;

internal static class WindowTools
{
    public static WinInfo Describe(IntPtr h)
    {
        var w = new WinInfo { Hwnd = h };
        Native.GetWindowThreadProcessId(h, out w.Pid);
        var sb = new StringBuilder(256);
        Native.GetWindowText(h, sb, sb.Capacity); w.Title = sb.ToString();
        sb.Clear();
        Native.GetClassName(h, sb, sb.Capacity); w.Class = sb.ToString();
        Native.GetWindowRect(h, out w.Rect);
        w.Visible = Native.IsWindowVisible(h);
        w.Iconic = Native.IsIconic(h);
        Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int));
        w.Cloaked = cloaked != 0;

        long ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        w.Layered = (ex & Native.WS_EX_LAYERED) != 0;
        w.Transparent = (ex & Native.WS_EX_TRANSPARENT) != 0;
        w.NoActivate = (ex & Native.WS_EX_NOACTIVATE) != 0;
        w.ToolWindow = (ex & Native.WS_EX_TOOLWINDOW) != 0;
        w.Topmost = (ex & Native.WS_EX_TOPMOST) != 0;
        if (w.Layered && Native.GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags) && (flags & Native.LWA_ALPHA) != 0)
        {
            w.AlphaConst = true;
            w.Alpha = alpha;
        }
        return w;
    }

    /// <summary>All top-level windows in z-order (top first). Hung state is only evaluated for visible windows.</summary>
    public static List<WinInfo> Snapshot()
    {
        var list = new List<WinInfo>(300);
        int z = 0;
        Native.EnumWindows((h, _) =>
        {
            z++;
            if (!Native.IsWindowVisible(h)) return true;       // hidden windows can't block anything
            var w = Describe(h);
            w.Z = z;
            if (!w.Cloaked && !w.Iconic) w.Hung = Native.IsHungAppWindow(h);
            list.Add(w);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Every window that would receive a click at (x,y), topmost first. Click-through windows are skipped,
    /// invisible click-catchers are included (that's the point). Cheap checks run before the full Describe.
    /// </summary>
    public static List<WinInfo> StackAt(int x, int y, Func<WinInfo, bool>? skip = null)
    {
        var list = new List<WinInfo>();
        int z = 0;
        uint self = (uint)Environment.ProcessId;
        Native.EnumWindows((h, _) =>
        {
            z++;
            if (!Native.IsWindowVisible(h)) return true;
            if (!Native.GetWindowRect(h, out var r) || !r.Contains(x, y)) return true;
            var w = Describe(h);
            w.Z = z;
            if (w.Cloaked || w.Iconic || w.Transparent) return true;
            if (w.Pid == self && w.Class.Contains("WindowsForms", StringComparison.Ordinal) && w.NoActivate) return true; // our own highlight
            if (skip != null && skip(w)) return true;
            list.Add(w);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // ---- fixes ---------------------------------------------------------------------------

    /// <summary>Makes a (layered) window ignore mouse input so clicks fall through to what is underneath. Reversible.</summary>
    public static bool MakeClickThrough(IntPtr h)
    {
        long ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED));
        Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        return ((long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0;
    }

    public static bool RestoreClickable(IntPtr h)
    {
        long ex = (long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_TRANSPARENT));
        Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        return ((long)Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) == 0;
    }

    public static void Hide(IntPtr h) => Native.ShowWindowAsync(h, Native.SW_HIDE);
    public static void CloseWindow(IntPtr h) => Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
}
