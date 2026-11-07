namespace Argus;

internal enum Category { Analytics, Hung, Hog, Weird, Blocker }

/// <summary>What to do when a category fires.</summary>
internal enum Handling { Off, Ask, Auto }

internal enum BlockerKind { None, TransparentOverlay, StalePopup }

/// <summary>What to do to a background resource hog: slow it down (gentle, default) or close it.</summary>
internal enum HogAction { Throttle, Close }

internal sealed class Finding
{
    public string Key = "";
    public Category Category;
    public int Pid;
    public string ProcName = "";
    public string? Path;
    public DateTime? ProcStart;
    public string Reason = "";
    public IntPtr Hwnd;
    public BlockerKind Kind;
    public string WindowClass = "";
    public DateTime First = DateTime.Now;

    public string CategoryLabel => Category switch
    {
        Category.Analytics => "Analytics",
        Category.Hung => "Not responding",
        Category.Hog => "Resource hog",
        Category.Weird => "Suspicious",
        Category.Blocker => "Invisible blocker",
        _ => Category.ToString(),
    };
}

/// <summary>Snapshot of one top-level window.</summary>
internal sealed class WinInfo
{
    public IntPtr Hwnd;
    public uint Pid;
    public string Title = "", Class = "";
    public Native.RECT Rect;
    public bool Visible, Cloaked, Iconic, Hung;
    public bool Layered, Transparent, NoActivate, ToolWindow, Topmost;
    public bool AlphaConst;   // layered window using a constant alpha (SetLayeredWindowAttributes)
    public byte Alpha = 255;
    public int Z;

    /// <summary>A window the user can actually see and interact with (not an invisible helper/overlay).</summary>
    public bool IsRealWindow =>
        Visible && !Cloaked && !Iconic && !Transparent
        && !(AlphaConst && Alpha == 0)
        && Rect.Width > 50 && Rect.Height > 50;

    public bool IsInvisibleClickCatcher =>
        Visible && !Cloaked && !Iconic && Layered && AlphaConst && Alpha == 0 && !Transparent;
}
