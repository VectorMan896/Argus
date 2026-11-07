using Microsoft.Win32;

namespace Argus;

/// <summary>
/// The Argus window: a slim icon sidebar (owl mark on top, round nav buttons, white = active) and a stage of
/// rounded dark cards. Pages live in MainForm.Pages.cs and MainForm.Performance.cs.
/// </summary>
internal sealed partial class MainForm : Form
{
    private const int HotkeyId = 0x5047;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;

    private readonly Watcher _watcher = new();
    private readonly Picker _picker = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _miPause = new("Pause monitoring");
    private readonly EventWaitHandle _showSignal = new(false, EventResetMode.AutoReset, "Local\\Argus.Show" + AppInfo.Instance);
    private readonly ToolTip _tt = new();
    private readonly UsageSampler _sampler = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 4000 };

    private readonly Panel _stage = new(), _header = new(), _host = new();
    private readonly Label _title = new(), _subtitle = new();
    private readonly StatusPill _pill = new();
    private readonly List<(NavButton Nav, Control Page, string Title, string Sub)> _pages = new();
    private Panel _side = null!;
    private NavButton _navPause = null!, _navExit = null!, _navDecisions = null!;
    private PillButton _btnScan = null!, _btnPick = null!;
    private int _current = -1, _waiting;
    private bool _allowShow, _reallyExit, _trayTipShown;

    public MainForm(bool startHidden, int initialTab = 0)
    {
        Text = "Argus " + AppInfo.Version;
        ClientSize = new Size(1260, 800);
        MinimumSize = new Size(1060, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui(9.5f);
        Icon = Assets.AppIcon();
        _allowShow = !startHidden;

        BuildTray();
        BuildStage();
        BuildSidebar();
        Controls.Add(_stage);
        Controls.Add(_side);

        AddPage("", "Overview", "What Argus is doing right now", BuildOverview());
        _navDecisions = AddPage("", "Needs a decision", "Things Argus flagged but left for you to decide", BuildDecisions());
        AddPage("", "Performance", "Help an older PC run better, gently", BuildPerformance());
        AddPage("", "Rules", "What Argus does when it finds something, and how patient it is", BuildRules());
        AddPage("", "Lists", "What to close, and what to never touch", BuildLists());
        AddPage("", "Activity", "Everything Argus did, and why", BuildLog());

        Log.Entry += line => { if (IsHandleCreated) BeginInvoke(() => AppendLog(line)); };
        _watcher.Updated += list => { if (IsHandleCreated) BeginInvoke(() => OnFindings(list)); };
        _watcher.NeedsAttention += f => { if (IsHandleCreated) BeginInvoke(() => Notify(f)); };

        var t = new Thread(() => { while (_showSignal.WaitOne()) { try { BeginInvoke(ShowMain); } catch { return; } } }) { IsBackground = true };
        t.Start();

        _tick.Tick += (_, _) => { if (Visible) { RefreshHero(); if (_current == 0) RefreshOverview(); } };
        _tick.Start();

        GoTo(initialTab);
        UpdateStatus();
        Shown += (_, _) => { ScrollToEnd(_logBox); RefreshOverview(); if (_current == 2) RefreshPerf(); else RefreshStartupCounts(); };
        Log.Write($"Argus {AppInfo.Version} started ({Config.Block.Count} blocklist, {Config.Excl.Count} exclusions).");
        LoadLogTail();
        InitUpdates();
        _watcher.Start();
    }

    // ---- window plumbing -----------------------------------------------------------------------------

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow) { value = false; if (!IsHandleCreated) CreateHandle(); }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
        if (!Native.RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.K))
            Log.Write("Could not register Ctrl+Alt+K (another program uses it). Use the tray menu or the button instead.");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) StartPick();
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayTipShown)
            {
                _trayTipShown = true;
                _tray.ShowBalloonTip(3000, "Argus is still watching", "It keeps running from the tray. Right-click the icon to exit.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    private void ShowMain()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _picker.Cancel();
        _tray.Visible = false;
        _watcher.Dispose();
        _sampler.Dispose();
        if (!_exitingForUpdate) UpdateRecovery.Confirm();     // a clean exit counts as "the new version worked"
        Application.Exit();
    }

    // ---- frame ---------------------------------------------------------------------------------------

    private void BuildStage()
    {
        _stage.Dock = DockStyle.Fill; _stage.BackColor = Theme.Bg; _stage.Padding = new Padding(12, 14, 16, 12);

        _header.Dock = DockStyle.Top; _header.Height = 96; _header.BackColor = Theme.Bg;
        _title.Font = Theme.Ui(21f, FontStyle.Bold); _title.ForeColor = Theme.Text; _title.BackColor = Theme.Bg;
        _title.AutoSize = false; _title.AutoEllipsis = true; _title.SetBounds(10, 12, 520, 40);
        _subtitle.Font = Theme.Ui(10f); _subtitle.ForeColor = Theme.Muted; _subtitle.BackColor = Theme.Bg;
        _subtitle.AutoSize = false; _subtitle.AutoEllipsis = true; _subtitle.SetBounds(12, 54, 560, 24);

        _btnScan = new PillButton("Scan now", "", PillStyle.Secondary);
        _btnPick = new PillButton("Click a window to close", "", PillStyle.Primary);
        _btnScan.BackColor = _btnPick.BackColor = Theme.Bg;
        _btnScan.Click += (_, _) => _watcher.ScanNow();
        _btnPick.Click += (_, _) => StartPick();
        _tt.SetToolTip(_btnPick, "Ctrl+Alt+K");
        _header.Controls.AddRange(new Control[] { _title, _subtitle, _pill, _btnScan, _btnPick, _btnUpdate });
        _header.Resize += (_, _) => LayoutHeader();

        _host.Dock = DockStyle.Fill; _host.BackColor = Theme.Bg;
        _stage.Controls.Add(_host);
        _stage.Controls.Add(_header);
        LayoutHeader();
    }

    private void LayoutHeader()
    {
        int right = _header.Width - 6, top = 16;
        _btnPick.Location = new Point(right - _btnPick.Width, top);
        _btnScan.Location = new Point(_btnPick.Left - 10 - _btnScan.Width, top);
        _pill.Location = new Point(_btnScan.Left - 14 - _pill.Width, top + 2);
        _btnUpdate.Location = new Point(_pill.Left - 12 - _btnUpdate.Width, top);
        int limit = (_btnUpdate.Visible ? _btnUpdate.Left : _pill.Left) - 16;
        _title.Width = Math.Max(120, Math.Min(520, limit - _title.Left));
        _subtitle.Width = Math.Max(120, Math.Min(580, limit - _subtitle.Left));
    }

    private void BuildSidebar()
    {
        _side = new Panel { Dock = DockStyle.Left, Width = 90, BackColor = Theme.Bg };
        _side.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.Line });
        var logo = new ImageBox(Assets.Load("argus-icon-tile-light.png"), Theme.Bg) { Location = new Point(16, 22), Size = new Size(58, 58) };
        _tt.SetToolTip(logo, "Argus " + AppInfo.Version);
        _side.Controls.Add(logo);

        _navPause = new NavButton("") { Left = 18 };
        _navPause.Click += (_, _) => TogglePause();
        _navExit = new NavButton("") { Left = 18 };
        _navExit.Click += (_, _) => ExitApp();
        _tt.SetToolTip(_navExit, "Exit Argus (stops all monitoring)");
        _side.Controls.Add(_navPause);
        _side.Controls.Add(_navExit);
        _side.Resize += (_, _) => { _navExit.Top = _side.Height - 74; _navPause.Top = _side.Height - 134; };
        _navExit.Top = 700; _navPause.Top = 640;
    }

    private NavButton AddPage(string glyph, string title, string sub, Control page)
    {
        var nav = new NavButton(glyph) { Left = 18, Top = 112 + _pages.Count * 60 };
        int idx = _pages.Count;
        nav.Click += (_, _) => GoTo(idx);
        _tt.SetToolTip(nav, title);
        _side.Controls.Add(nav);

        page.Dock = DockStyle.Fill; page.Visible = false;
        _host.Controls.Add(page);
        _pages.Add((nav, page, title, sub));
        return nav;
    }

    private void GoTo(int i)
    {
        i = Math.Clamp(i, 0, _pages.Count - 1);
        for (int k = 0; k < _pages.Count; k++)
        {
            _pages[k].Nav.Active = k == i; _pages[k].Nav.Invalidate();
            _pages[k].Page.Visible = k == i;
        }
        _current = i;
        _title.Text = _pages[i].Title; _subtitle.Text = _pages[i].Sub;
        if (i == 0) RefreshOverview();
        else if (i == 2) RefreshPerf();
        UpdateSampling();
    }

    // ---- tray -------------------------------------------------------------------------------------------

    private void BuildTray()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Card, ForeColor = Theme.Text, ShowImageMargin = false, Font = Theme.Ui(9.5f) };
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
        menu.Items.Add("Open Argus", null, (_, _) => ShowMain());
        _miPause.Click += (_, _) => TogglePause();
        menu.Items.Add(_miPause);
        menu.Items.Add("Scan now", null, (_, _) => _watcher.ScanNow());
        menu.Items.Add("Click a window to close...   Ctrl+Alt+K", null, (_, _) => StartPick());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.Icon = Icon;
        _tray.Text = "Argus";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.BalloonTipClicked += (_, _) =>
        {
            ShowMain();
            if (_trayWantsUpdate) { _trayWantsUpdate = false; ShowUpdateDialog(); }
        };
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.Chip;
        public override Color MenuItemBorder => Theme.Chip;
        public override Color MenuBorder => Theme.Line;
        public override Color ToolStripDropDownBackground => Theme.Card;
        public override Color ImageMarginGradientBegin => Theme.Card;
        public override Color ImageMarginGradientMiddle => Theme.Card;
        public override Color ImageMarginGradientEnd => Theme.Card;
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.Line;
    }

    private void Notify(Finding f)
    {
        _trayWantsUpdate = false;                       // this notice is about a finding, not an update
        string text = $"{f.ProcName}: {f.Reason}";
        _tray.ShowBalloonTip(6000, $"Argus - {f.CategoryLabel}", text.Length > 220 ? text[..220] + "..." : text, ToolTipIcon.Warning);
    }

    // ---- state -------------------------------------------------------------------------------------------

    private void TogglePause()
    {
        Config.Settings.Paused = !Config.Settings.Paused;
        Config.SaveSettings();
        Log.Write(Config.Settings.Paused ? "Monitoring paused." : "Monitoring resumed.");
        UpdateStatus();
        if (!Config.Settings.Paused) _watcher.ScanNow();
    }

    private void UpdateStatus()
    {
        bool paused = Config.Settings.Paused;
        if (paused) _pill.Set("Paused", Theme.Warn);
        else if (_waiting > 0) _pill.Set($"{_waiting} need a decision", Theme.Bad);
        else _pill.Set($"Watching  -  every {Config.Settings.ScanIntervalSec}s", Theme.Good);

        _navPause.Glyph = paused ? "" : ""; _navPause.Invalidate();
        _tt.SetToolTip(_navPause, paused ? "Resume monitoring" : "Pause monitoring");
        _navDecisions.Badge = _waiting; _navDecisions.Invalidate();
        _miPause.Text = paused ? "Resume monitoring" : "Pause monitoring";
        _tray.Text = paused ? "Argus (paused)" : (_waiting > 0 ? $"Argus - {_waiting} waiting" : "Argus");
        LayoutHeader();
        RefreshHero();
    }

    private void StartPick()
    {
        if (_picker.Active) { _picker.Cancel(); return; }
        bool wasVisible = Visible;
        if (wasVisible) Hide();
        _picker.Begin((x, y, cancelled) =>
        {
            if (!cancelled)
            {
                using var d = new PickDialog(x, y);
                d.ShowDialog();
            }
            if (wasVisible) ShowMain();
        });
    }

    // ---- startup registration (used by the Rules page) ----------------------------------------------------

    private static bool IsStartupEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return k?.GetValue("Argus") != null; }
        catch { return false; }
    }

    private static void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            if (on) k.SetValue("Argus", $"\"{Environment.ProcessPath}\" --hidden");
            else k.DeleteValue("Argus", false);
            Log.Write(on ? "Argus will start with Windows." : "Argus will no longer start with Windows.");
        }
        catch (Exception ex) { Log.Write("Could not change startup entry: " + ex.Message); }
    }
}
