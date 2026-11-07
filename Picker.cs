using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Argus;

/// <summary>
/// Click-to-close tool. While active, a border follows the window under the cursor (orange when it is an
/// invisible click-catcher). The next left click is swallowed and reports where you clicked;
/// right-click or Esc cancels.
/// </summary>
internal sealed class Picker
{
    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _kbProc;
    private IntPtr _mHook, _kHook;
    private HighlightForm? _hl;
    private System.Windows.Forms.Timer? _timer;
    private SynchronizationContext? _ctx;
    private Native.POINT _pt;
    private bool _moved, _downSeen;
    private Action<int, int, bool>? _done;
    private readonly Dictionary<uint, string> _names = new();

    public bool Active => _mHook != IntPtr.Zero;

    public Picker()
    {
        _mouseProc = MouseProc;
        _kbProc = KeyProc;
    }

    /// <summary>Starts picking. <paramref name="done"/> gets (x, y, cancelled) on the UI thread.</summary>
    public void Begin(Action<int, int, bool> done)
    {
        if (Active) return;
        _done = done;
        _ctx = SynchronizationContext.Current;
        _downSeen = false;

        _hl = new HighlightForm();
        IntPtr mod = Native.GetModuleHandle(null);
        _mHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);
        _kHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
        if (_mHook == IntPtr.Zero) { Finish(0, 0, true); return; }

        Native.GetCursorPos(out _pt);
        _moved = true;
        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => UpdateHighlight();
        _timer.Start();
    }

    private IntPtr MouseProc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            int msg = (int)w;
            var ms = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
            switch (msg)
            {
                case Native.WM_MOUSEMOVE:
                    _pt = ms.pt; _moved = true;
                    break;
                case Native.WM_LBUTTONDOWN:
                    _pt = ms.pt; _downSeen = true;
                    return (IntPtr)1;                                  // swallow: the target never sees this click
                case Native.WM_LBUTTONUP when _downSeen:
                    { var p = _pt; _ctx?.Post(_ => Finish(p.X, p.Y, false), null); }
                    return (IntPtr)1;
                case Native.WM_RBUTTONDOWN:
                    return (IntPtr)1;
                case Native.WM_RBUTTONUP:
                    _ctx?.Post(_ => Finish(0, 0, true), null);
                    return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(_mHook, code, w, l);
    }

    private IntPtr KeyProc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && ((int)w == Native.WM_KEYDOWN || (int)w == Native.WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(l);
            if (kb.vkCode == Native.VK_ESCAPE)
            {
                _ctx?.Post(_ => Finish(0, 0, true), null);
                return (IntPtr)1;
            }
        }
        return Native.CallNextHookEx(_kHook, code, w, l);
    }

    private void UpdateHighlight()
    {
        if (!_moved || _hl == null) return;
        _moved = false;
        var top = WindowTools.StackAt(_pt.X, _pt.Y).FirstOrDefault();
        if (top == null) { _hl.Hide(); return; }
        bool ghost = top.IsInvisibleClickCatcher;
        string label = (ghost ? "INVISIBLE window - " : "") + ProcName(top.Pid) + (top.Title.Length > 0 ? " - " + top.Title : "  (" + top.Class + ")");
        _hl.Place(top.Rect, label, ghost ? Color.OrangeRed : Color.FromArgb(220, 30, 30));
    }

    private string ProcName(uint pid)
    {
        if (_names.TryGetValue(pid, out var n)) return n;
        try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = "?"; }
        return _names[pid] = n;
    }

    private void Finish(int x, int y, bool cancelled)
    {
        if (_mHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mHook); _mHook = IntPtr.Zero; }
        if (_kHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kHook); _kHook = IntPtr.Zero; }
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _hl?.Close(); _hl?.Dispose(); _hl = null;
        var cb = _done; _done = null;
        cb?.Invoke(x, y, cancelled);
    }

    public void Cancel() { if (Active) Finish(0, 0, true); }
}

/// <summary>Click-through frame drawn around the window that would be picked.</summary>
internal sealed class HighlightForm : Form
{
    private string _label = "";
    private Color _color = Color.Red;

    public HighlightForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED);
            return cp;
        }
    }

    public void Place(Native.RECT r, string label, Color color)
    {
        _label = label; _color = color;
        if (!Visible) Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.Left, r.Top, Math.Max(r.Width, 8), Math.Max(r.Height, 8), Native.SWP_NOACTIVATE);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(_color, 5);
        e.Graphics.DrawRectangle(pen, 2, 2, Width - 6, Height - 6);
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        string text = _label + "   (click = select, right-click / Esc = cancel)";
        var size = e.Graphics.MeasureString(text, font);
        float w = Math.Min(size.Width + 12, Width - 10);
        using var bg = new SolidBrush(_color);
        e.Graphics.FillRectangle(bg, 5, 5, w, size.Height + 6);
        e.Graphics.DrawString(text, font, Brushes.White, 10, 8);
    }
}

/// <summary>Everything that would receive the click, topmost first, with the actions you can take on it.</summary>
internal sealed class PickDialog : Form
{
    private readonly int _x, _y;
    private readonly ListView _list = new();
    private readonly Label _info = new(), _status = new();
    private readonly Button _bCloseWin = new(), _bCloseProg = new(), _bKill = new(), _bHide = new(), _bThrough = new(), _bExclude = new();
    private List<WinInfo> _stack = new();

    public PickDialog(int x, int y)
    {
        _x = x; _y = y;
        Text = $"Argus - what is at {x},{y}";
        Width = 920; Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 420);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = true;
        TopMost = true;

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 8, 10, 0),
            Text = "Windows under your click, topmost first. The first one is what you clicked. Entries marked INVISIBLE are windows you cannot see " +
                   "but that still catch clicks - pick one and use \"Make click-through\" or \"Hide window\".",
        };

        _list.Dock = DockStyle.Fill; _list.View = View.Details; _list.FullRowSelect = true; _list.GridLines = true;
        _list.MultiSelect = false; _list.HideSelection = false;
        _list.Columns.Add("Window", 250); _list.Columns.Add("Program", 120); _list.Columns.Add("Class", 200);
        _list.Columns.Add("Size", 90); _list.Columns.Add("Notes", 200);
        _list.SelectedIndexChanged += (_, _) => UpdateSelection();

        _info.Dock = DockStyle.Bottom; _info.Height = 44; _info.Padding = new Padding(10, 4, 10, 0);
        _status.Dock = DockStyle.Bottom; _status.Height = 24; _status.Padding = new Padding(10, 3, 10, 0); _status.ForeColor = Color.DarkGreen;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 6, 8, 0), WrapContents = false };
        void Add(Button b, string text, int w, Action click)
        {
            b.Text = text; b.Width = w; b.Height = 32; b.Click += (_, _) => click();
            buttons.Controls.Add(b);
        }
        Add(_bCloseWin, "Close window", 105, () => Do(w => { WindowTools.CloseWindow(w.Hwnd); return "close request sent"; }));
        Add(_bCloseProg, "Close program", 110, () => ClosePrograms(false));
        Add(_bKill, "Kill program now", 120, () => ClosePrograms(true));
        Add(_bHide, "Hide window", 100, () => Do(w => { WindowTools.Hide(w.Hwnd); return "window hidden"; }));
        Add(_bThrough, "Make click-through", 140, () => Do(w => WindowTools.MakeClickThrough(w.Hwnd) ? "window is now click-through" : "could not change that window"));
        Add(_bExclude, "Exclude program", 120, ExcludeProgram);
        var close = new Button { Text = "Done", Width = 80, Height = 32, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(close);
        CancelButton = close;

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(_info);
        Controls.Add(_status);
        Controls.Add(buttons);
        _list.BringToFront();

        Reload();
    }

    private void Reload()
    {
        uint self = (uint)Environment.ProcessId;
        _stack = WindowTools.StackAt(_x, _y).Where(w => w.Pid != self).ToList();
        _list.Items.Clear();
        foreach (var w in _stack)
        {
            string proc = "?";
            try { proc = Process.GetProcessById((int)w.Pid).ProcessName; } catch { }
            var notes = new List<string>();
            if (w.IsInvisibleClickCatcher) notes.Add("INVISIBLE");
            if (w.Class.Contains("PopupWindowSiteBridge")) notes.Add("popup host");
            if (w.Topmost) notes.Add("topmost");
            var item = new ListViewItem(w.Title.Length > 0 ? w.Title : "(no title)");
            item.SubItems.Add(proc);
            item.SubItems.Add(w.Class);
            item.SubItems.Add($"{w.Rect.Width}x{w.Rect.Height}");
            item.SubItems.Add(string.Join(", ", notes));
            if (w.IsInvisibleClickCatcher) item.ForeColor = Color.OrangeRed;
            _list.Items.Add(item);
        }
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        else _info.Text = "Nothing at that point.";
        UpdateSelection();
    }

    private WinInfo? Selected => _list.SelectedIndices.Count > 0 ? _stack[_list.SelectedIndices[0]] : null;

    private static string NameOf(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
    }

    private void UpdateSelection()
    {
        var w = Selected;
        bool has = w != null;
        string? path = has ? Native.GetProcessPath((int)w!.Pid) : null;
        bool refused = has && Safety.IsRefused(NameOf(w!.Pid), (int)w.Pid, path);
        if (has) _info.Text = $"{NameOf(w!.Pid)}  (pid {w.Pid})   {path ?? "path not readable"}" +
                              (refused ? "\nThis is a protected system process - it can't be closed from here." : "");
        _bCloseWin.Enabled = _bHide.Enabled = _bThrough.Enabled = has;
        _bCloseProg.Enabled = _bKill.Enabled = _bExclude.Enabled = has && !refused;
    }

    private void Do(Func<WinInfo, string> action)
    {
        var w = Selected; if (w == null) return;
        _status.Text = action(w);
        BeginInvoke(async () => { await Task.Delay(400); if (!IsDisposed) Reload(); });
    }

    private void ClosePrograms(bool kill)
    {
        var w = Selected; if (w == null) return;
        string name = NameOf(w.Pid);
        bool core = Safety.IsNeverAuto(name, (int)w.Pid, Native.GetProcessPath((int)w.Pid));
        string msg = $"{(kill ? "Force-close" : "Close")} {name} (pid {w.Pid})?\n\nUnsaved work in that program will be lost." +
                     (core ? "\n\nThis is part of Windows' desktop shell; Windows will usually restart it, but you may see your taskbar or windows flicker." : "");
        if (MessageBox.Show(this, msg, "Argus", MessageBoxButtons.YesNo, core ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;

        int pid = (int)w.Pid;
        _status.Text = "working...";
        Task.Run(() => Closer.CloseProcess(pid, null, kill ? 0 : Config.Settings.GracefulWaitSec, Config.Settings.KillTree, userInitiated: true))
            .ContinueWith(t =>
            {
                Log.Write($"[Picker] {name} (pid {pid}) closed by you -> {t.Result}");
                BeginInvoke(() => { _status.Text = $"{name}: {t.Result}"; Reload(); });
            });
    }

    private void ExcludeProgram()
    {
        var w = Selected; if (w == null) return;
        string name = NameOf(w.Pid);
        Config.Excl.Add(name);
        _status.Text = $"{name} added to exclusions.txt - Argus will leave it alone.";
    }
}

/// <summary>Hosts the picker when started with --pick (no main window).</summary>
internal sealed class PickerHost : Form
{
    private readonly Picker _picker = new();

    public PickerHost()
    {
        ShowInTaskbar = false; Opacity = 0; FormBorderStyle = FormBorderStyle.None;
        Size = new Size(1, 1); StartPosition = FormStartPosition.Manual; Location = new Point(-2000, -2000);
        Shown += (_, _) => _picker.Begin((x, y, cancelled) =>
        {
            if (cancelled) { Close(); return; }
            using var d = new PickDialog(x, y);
            d.ShowDialog();
            Close();
        });
    }
}
