namespace Argus;

internal sealed partial class MainForm
{
    // overview
    private Stat _stWaiting = null!, _stClosed = null!, _stSlowed = null!, _stFixed = null!;
    private Label _heroTitle = null!, _heroSub = null!;
    private PillButton _btnHeroPause = null!;
    private Meter _mMem = null!, _mDisk = null!, _mStart = null!;
    private KeyValue _kvUp = null!, _kvProc = null!;
    private RingGauge _gauge = null!;
    private TextBox _recent = null!;
    private int _startupEnabled, _startupTotal = -1;
    private DateTime _lastOverview = DateTime.MinValue;
    private readonly Queue<string> _recentLines = new();

    // decisions
    private readonly ListView _findings = new();
    private Label _emptyNote = null!;

    // lists and log
    private TextBox _blockBox = null!, _exclBox = null!, _logBox = null!;

    // ---- shared helpers -----------------------------------------------------------------------------------

    private static TextBox DarkText(bool readOnly, bool wrap)
    {
        var tb = new TextBox
        {
            Multiline = true, ReadOnly = readOnly, BorderStyle = BorderStyle.None, AcceptsReturn = true, Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both, WordWrap = wrap,
        };
        if (tb.Font.Name != "Cascadia Mono") tb.Font = new Font("Consolas", 9.5f);
        tb.BackColor = Theme.Input; tb.ForeColor = Theme.Text;
        Theme.DarkScroll(tb);
        return tb;
    }

    private static Panel Holder(Control inner)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(16, 14, 8, 14), Margin = new Padding(0) };
        p.Controls.Add(inner);
        return p;
    }

    private static void FillWidth(FlowLayoutPanel f)
    {
        void Do() { foreach (Control c in f.Controls) c.Width = Math.Max(50, f.ClientSize.Width - c.Margin.Horizontal); }
        f.Resize += (_, _) => Do();
        f.ControlAdded += (_, _) => Do();
    }

    private static string HandlingText(Handling h) => h switch { Handling.Off => "off", Handling.Ask => "ask me", _ => "automatic" };

    private static void Shell(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    // ---- overview ------------------------------------------------------------------------------------------

    private Control BuildOverview()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Bg };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        // hero: the owl, what Argus is doing, and what it has done
        var hero = new Card { Dock = DockStyle.Fill, Padding = new Padding(30, 26, 30, 26) };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var logo = new ImageBox(Assets.Load("argus-icon-tile-light.png"), Theme.Card) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 22, 0) };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Card };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _heroTitle = new Label { Dock = DockStyle.Fill, Font = Theme.Ui(21f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleLeft };
        _heroSub = new Label { Dock = DockStyle.Fill, Font = Theme.Ui(10f), ForeColor = Theme.Muted, BackColor = Theme.Card };
        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, ColumnCount = 4, RowCount = 1 };
        for (int c = 0; c < 4; c++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _stWaiting = new Stat("Waiting"); _stClosed = new Stat("Closed"); _stSlowed = new Stat("Slowed down"); _stFixed = new Stat("Windows fixed");
        int col = 0;
        foreach (var s in new[] { _stWaiting, _stClosed, _stSlowed, _stFixed }) { s.Dock = DockStyle.Fill; s.Margin = new Padding(0, 6, 8, 0); stats.Controls.Add(s, col++, 0); }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, WrapContents = false };
        _btnHeroPause = new PillButton("Pause", "\uE769", PillStyle.Secondary, 140) { Margin = new Padding(0, 4, 10, 0) };
        _btnHeroPause.Click += (_, _) => TogglePause();
        var open = new PillButton("Open decisions", "\uEA8F", PillStyle.Primary, 180) { Margin = new Padding(0, 4, 0, 0) };
        open.Click += (_, _) => GoTo(1);
        actions.Controls.Add(_btnHeroPause); actions.Controls.Add(open);

        right.Controls.Add(_heroTitle, 0, 0); right.Controls.Add(_heroSub, 0, 1); right.Controls.Add(stats, 0, 2); right.Controls.Add(actions, 0, 4);
        inner.Controls.Add(logo, 0, 0); inner.Controls.Add(right, 1, 0);
        hero.Controls.Add(inner);

        // this PC
        var sys = new Card { Dock = DockStyle.Fill, Title = "This PC", Subtitle = "Live numbers, refreshed every few seconds" };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Card };
        FillWidth(flow);
        _mMem = new Meter("Memory in use"); _mDisk = new Meter("System drive used"); _mStart = new Meter("Startup programs enabled");
        _kvUp = new KeyValue("Up for"); _kvProc = new KeyValue("Running processes");
        foreach (Control c in new Control[] { _mMem, _mDisk, _mStart, _kvUp, _kvProc }) { c.Margin = new Padding(0, 0, 0, 8); flow.Controls.Add(c); }
        sys.Controls.Add(flow);

        // recent activity
        var act = new Card { Dock = DockStyle.Fill, Title = "Recent activity", Subtitle = "The last things Argus did or noticed" };
        _recent = DarkText(true, true);
        act.Controls.Add(Holder(_recent));

        // memory dial (the compass in the reference)
        var dial = new Card { Dock = DockStyle.Fill, Title = "Memory", Subtitle = "How full this PC's RAM is" };
        _gauge = new RingGauge { Dock = DockStyle.Fill, BackColor = Theme.Card };
        dial.Controls.Add(_gauge);

        grid.Controls.Add(hero, 0, 0); grid.Controls.Add(sys, 1, 0); grid.Controls.Add(act, 0, 1); grid.Controls.Add(dial, 1, 1);
        return grid;
    }

    private void RefreshHero()
    {
        if (_heroTitle == null) return;
        var s = Config.Settings;
        _stWaiting.Set(_waiting.ToString(), _waiting > 0 ? Theme.Bad : Theme.Good);
        _stClosed.Set(Stats.Closed.ToString()); _stSlowed.Set(Stats.Slowed.ToString()); _stFixed.Set(Stats.Fixed.ToString());
        _heroTitle.Text = s.Paused ? "Argus is paused" : (_waiting > 0 ? "Something needs you" : "Argus is watching");
        _heroSub.Text = s.Paused
            ? "Monitoring is paused. Nothing will be closed or changed until you resume."
            : $"Scanning every {s.ScanIntervalSec} s.  Analytics: {HandlingText(s.Analytics)}.  Hogs: {HandlingText(s.Hog)} ({(s.HogAction == HogAction.Throttle ? "slow down" : "close")}).  " +
              $"Blockers: {HandlingText(s.Blocker)}.  Counters since {Stats.Since:HH:mm}.";
        _btnHeroPause.Text = s.Paused ? "Resume" : "Pause";
        _btnHeroPause.Glyph = s.Paused ? "\uE768" : "\uE769";
        _btnHeroPause.Invalidate();
    }

    private void RefreshOverview()
    {
        if ((DateTime.UtcNow - _lastOverview).TotalSeconds < 2) return;
        _lastOverview = DateTime.UtcNow;
        if (_startupTotal < 0) RefreshStartupCounts();
        Task.Run(() =>
        {
            var s = Perf.Summary();
            if (IsHandleCreated) BeginInvoke(() => ApplyOverview(s));
        });
    }

    private void RefreshStartupCounts() => Task.Run(() =>
    {
        var items = Perf.StartupItems();
        _startupEnabled = items.Count(i => i.Enabled); _startupTotal = items.Count;
        if (IsHandleCreated) BeginInvoke(ApplyStartupMeter);
    });

    private void ApplyStartupMeter()
    {
        if (_mStart == null || _startupTotal < 0) return;
        _mStart.Set(_startupTotal == 0 ? 0 : (double)_startupEnabled / _startupTotal, $"{_startupEnabled} of {_startupTotal}",
            _startupEnabled > 8 ? Theme.Warn : Theme.Accent);
    }

    private void ApplyOverview(SystemSummary s)
    {
        double memUsed = s.MemLoad / 100.0;
        Color memColor = s.MemLoad >= 85 ? Theme.Bad : s.MemLoad >= 70 ? Theme.Warn : Theme.Accent;
        _mMem.Set(memUsed, $"{s.MemLoad}%", memColor);

        double freePct = s.DiskTotal > 0 ? s.DiskFree * 100.0 / s.DiskTotal : 100;
        _mDisk.Set(1 - freePct / 100.0, $"{Perf.Bytes(s.DiskFree)} free", freePct < 10 ? Theme.Bad : freePct < 15 ? Theme.Warn : Theme.Accent);
        ApplyStartupMeter();
        _kvUp.Set($"{(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h");
        _kvProc.Set(s.ProcessCount.ToString());

        _gauge.Set(s.MemLoad, "RAM", $"{s.MemLoad}%", $"{(s.TotalRam - s.AvailRam) / 1073741824.0:0.#} of {s.TotalRam / 1073741824.0:0.#} GB", memColor);
    }

    // ---- needs a decision --------------------------------------------------------------------------------------

    private Control BuildDecisions()
    {
        var card = new Card { Dock = DockStyle.Fill, Title = "Waiting for you", Subtitle = "Programs and windows Argus flagged but did not handle on its own" };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Card };
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        _findings.Dock = DockStyle.Fill; _findings.Margin = new Padding(0);
        _findings.Columns.Add("Type", 150); _findings.Columns.Add("Program", 190); _findings.Columns.Add("PID", 80);
        _findings.Columns.Add("Since", 100); _findings.Columns.Add("What was found", 600);
        Theme.StyleList(_findings, false);
        _findings.DoubleClick += (_, _) => ShowDetails();

        _emptyNote = new Label
        {
            Dock = DockStyle.Fill, Text = "Nothing needs your attention.\r\nArgus will list anything it is unsure about here.",
            Font = Theme.Ui(11f), ForeColor = Theme.Muted, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter,
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var fix = new PillButton("Close / fix selected", "\uE73E", PillStyle.Primary) { Margin = new Padding(0, 0, 10, 0) };
        var kill = new PillButton("Kill now", "\uE711", PillStyle.Danger) { Margin = new Padding(0, 0, 10, 0) };
        var excl = new PillButton("Never touch this again", "", PillStyle.Secondary) { Margin = new Padding(0, 0, 10, 0) };
        var det = new PillButton("Details", "", PillStyle.Ghost);
        fix.Click += (_, _) => { foreach (var f in Selected()) _watcher.Apply(f); };
        kill.Click += (_, _) =>
        {
            var sel = Selected().Where(f => f.Category != Category.Blocker).ToList();
            if (sel.Count == 0) return;
            if (MessageBox.Show(this, $"Force-close {sel.Count} program(s) immediately? Unsaved work will be lost.", "Argus",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                foreach (var f in sel) _watcher.Apply(f, force: true);
        };
        excl.Click += (_, _) =>
        {
            foreach (var f in Selected())
                Config.Excl.Add(f.Category == Category.Blocker && f.WindowClass.Length > 0 ? "class:" + f.WindowClass : f.ProcName);
            LoadLists();
            _watcher.ScanNow();
        };
        det.Click += (_, _) => ShowDetails();
        bar.Controls.AddRange(new Control[] { fix, kill, excl, det });

        var cell = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(0) };
        cell.Controls.Add(_findings); cell.Controls.Add(_emptyNote);
        _findings.Visible = false;
        t.Controls.Add(cell, 0, 0); t.Controls.Add(bar, 0, 1);
        card.Controls.Add(t);
        return card;
    }

    private IEnumerable<Finding> Selected() =>
        _findings.SelectedItems.Cast<ListViewItem>().Select(i => (Finding)i.Tag!).ToList();

    private void ShowDetails()
    {
        var f = Selected().FirstOrDefault();
        if (f == null) return;
        MessageBox.Show(this, $"{f.CategoryLabel}: {f.ProcName} (pid {f.Pid})\n\n{f.Reason}\n\n{f.Path ?? "(path not readable)"}\nSince {f.First:HH:mm:ss}",
            "Argus", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnFindings(IReadOnlyList<Finding> list)
    {
        _waiting = list.Count;
        var keep = _findings.SelectedItems.Cast<ListViewItem>().Select(i => ((Finding)i.Tag!).Key).ToHashSet();
        _findings.BeginUpdate();
        _findings.Items.Clear();
        foreach (var f in list.OrderBy(f => f.Category).ThenBy(f => f.First))
        {
            var it = new ListViewItem(f.CategoryLabel) { Tag = f, Selected = keep.Contains(f.Key) };
            it.SubItems.Add(f.ProcName); it.SubItems.Add(f.Pid.ToString()); it.SubItems.Add(f.First.ToString("HH:mm:ss")); it.SubItems.Add(f.Reason);
            _findings.Items.Add(it);
        }
        _findings.EndUpdate();
        _findings.Visible = list.Count > 0;
        _emptyNote.Visible = list.Count == 0;
        UpdateStatus();
    }

    // ---- rules ---------------------------------------------------------------------------------------------------

    private static FlowLayoutPanel Column()
    {
        var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
        FillWidth(f);
        Theme.DarkScroll(f);
        return f;
    }

    private Segmented Seg(Handling cur, Action<Handling> set)
    {
        var sg = new Segmented("Off", "Ask", "Auto");
        sg.SetSilently((int)cur);
        sg.Changed += (_, _) => { set((Handling)sg.SelectedIndex); Config.SaveSettings(); UpdateStatus(); };
        return sg;
    }

    private Stepper Step(decimal min, decimal max, decimal cur, Action<decimal> set, decimal step = 1, string suffix = "")
    {
        var st = new Stepper { Minimum = min, Maximum = max, Step = step, Suffix = suffix };
        st.SetSilently(cur);
        st.Changed += (_, _) => { set(st.Value); Config.SaveSettings(); UpdateStatus(); };
        return st;
    }

    private static Toggle Tog(bool cur, Action<bool> set)
    {
        var t = new Toggle();
        t.SetSilently(cur);
        t.Changed += (_, _) => { set(t.Checked); Config.SaveSettings(); };
        return t;
    }

    private Control BuildRules()
    {
        var s = Config.Settings;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        var left = Column(); var right = Column();
        grid.Controls.Add(left, 0, 0); grid.Controls.Add(right, 1, 0);

        var react = new RowCard { Title = "When Argus finds something", Subtitle = "Off = ignore it    Ask = list it for you    Auto = deal with it" };
        react.AddRow("Analytics and telemetry programs", "Programs on your blocklist (Lists page)", Seg(s.Analytics, v => s.Analytics = v));
        react.AddRow("Programs that stopped responding", "Closing one loses unsaved work", Seg(s.Hung, v => s.Hung = v));
        react.AddRow("Background resource hogs", "No visible window, busy for a while", Seg(s.Hog, v => s.Hog = v));
        var hogAct = new Segmented("Slow down", "Close");
        hogAct.SetSilently((int)s.HogAction);
        hogAct.Changed += (_, _) => { s.HogAction = (HogAction)hogAct.SelectedIndex; Config.SaveSettings(); UpdateStatus(); };
        react.AddRow("      ...what to do with a hog", "Slow down = lower priority; nothing closes", hogAct);
        react.AddRow("Suspicious programs", "Disguised names, temp-folder programs, odd files", Seg(s.Weird, v => s.Weird = v));
        react.AddRow("Invisible windows that block clicks", "Only made click-through or hidden, never killed", Seg(s.Blocker, v => s.Blocker = v));
        left.Controls.Add(react);

        var opts = new RowCard { Title = "Options" };
        opts.AddRow("Also close child processes", "When Argus closes a program", Tog(s.KillTree, v => s.KillTree = v));
        opts.AddRow("Skip Windows' own programs", "For the hog and suspicious checks", Tog(s.SkipWindowsDir, v => s.SkipWindowsDir = v));
        opts.AddRow("Start Argus when I sign in", "It runs quietly in the tray", Tog(IsStartupEnabled(), SetStartup));
        left.Controls.Add(opts);

        var timing = new RowCard { Title = "Timing" };
        timing.AddRow("Scan every", "", Step(2, 300, s.ScanIntervalSec, v => s.ScanIntervalSec = (int)v, 1, " s"));
        timing.AddRow("Not responding for at least", "", Step(5, 600, s.HungSeconds, v => s.HungSeconds = (int)v, 5, " s"));
        timing.AddRow("Wait for a polite close", "Before forcing a program to end", Step(0, 60, s.GracefulWaitSec, v => s.GracefulWaitSec = (int)v, 1, " s"));
        right.Controls.Add(timing);

        var hog = new RowCard { Title = "Resource hogs" };
        hog.AddRow("CPU at least", "% of the whole PC (100 = every core)", Step(1, 100, (decimal)s.HogCpuPercent, v => s.HogCpuPercent = (double)v, 5, " %"));
        hog.AddRow("Memory at least", "", Step(100, 200000, s.HogMemoryMB, v => s.HogMemoryMB = (int)v, 100, " MB"));
        hog.AddRow("...for at least", "", Step(5, 3600, s.HogSeconds, v => s.HogSeconds = (int)v, 10, " s"));
        right.Controls.Add(hog);

        var other = new RowCard { Title = "Other thresholds" };
        other.AddRow("Suspicious score needed", "Temp folder 2, fake name 3, odd = 1 each", Step(1, 10, s.WeirdScoreThreshold, v => s.WeirdScoreThreshold = (int)v));
        other.AddRow("Hide a stuck popup after", "File Explorer popups left over a folder", Step(10, 3600, s.StalePopupSeconds, v => s.StalePopupSeconds = (int)v, 5, " s"));
        other.AddRow("Free memory when RAM free is below", "0 = never; nothing is closed", Step(0, 50, s.AutoTrimBelowPercent, v => s.AutoTrimBelowPercent = (int)v, 5, " %"));
        other.AddRow("Temp cleanup: files older than", "Cleanup is always manual", Step(1, 365, s.TempAgeDays, v => s.TempAgeDays = (int)v, 1, " days"));
        right.Controls.Add(other);
        return grid;
    }

    // ---- lists ----------------------------------------------------------------------------------------------------

    private Card ListCard(string title, string help, out TextBox box, string saveText, Func<string> path, PatternList list)
    {
        var card = new Card { Dock = DockStyle.Fill, Title = title, Subtitle = "One pattern per line.  * and ? are wildcards." };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Card };
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var h = new Label { Dock = DockStyle.Fill, Text = help, Font = Theme.Ui(9f), ForeColor = Theme.Muted, BackColor = Theme.Card };
        box = DarkText(false, false);
        var msg = new Label { AutoSize = true, ForeColor = Theme.Good, BackColor = Theme.Card, Font = Theme.Ui(9f), Margin = new Padding(14, 12, 0, 0) };
        var save = new PillButton(saveText, "\uE74E", PillStyle.Primary) { Margin = new Padding(0, 10, 0, 0) };
        var tb = box;
        save.Click += (_, _) => SaveList(tb, path(), list, title, msg);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, WrapContents = false };
        bar.Controls.Add(save); bar.Controls.Add(msg);

        t.Controls.Add(h, 0, 0); t.Controls.Add(Holder(box), 0, 1); t.Controls.Add(bar, 0, 2);
        card.Controls.Add(t);
        return card;
    }

    private Control BuildLists()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var block = ListCard("Blocklist", "Analytics and telemetry programs Argus closes. A plain line is a program name (chrome or chrome.exe). path:C:\\Tools\\* matches a file path. It only looks at the file name, never folder names.",
            out _blockBox, "Save blocklist", () => Config.BlockPath, Config.Block);
        var excl = ListCard("Exclusions", "Argus never touches these. Prefixes: path:  title:  class:      e.g.  obs64      path:C:\\Games\\*      title:*Premiere*      class:CabinetWClass",
            out _exclBox, "Save exclusions", () => Config.ExclPath, Config.Excl);
        grid.Controls.Add(block, 0, 0); grid.Controls.Add(excl, 1, 0);
        LoadLists();
        return grid;
    }

    private void LoadLists()
    {
        _blockBox.Text = ReadText(Config.BlockPath);
        _exclBox.Text = ReadText(Config.ExclPath);
    }

    private static string ReadText(string path)
    {
        try { return File.ReadAllText(path).Replace("\r\n", "\n").Replace("\n", "\r\n"); } catch { return ""; }
    }

    private void SaveList(TextBox box, string path, PatternList list, string name, Label msg)
    {
        try
        {
            File.WriteAllText(path, box.Text);
            list.Reload();
            msg.ForeColor = Theme.Good; msg.Text = $"Saved ({list.Count} entries).";
            Log.Write($"{name} saved ({list.Count} entries).");
            _watcher.ScanNow();
        }
        catch (Exception ex) { msg.ForeColor = Theme.Bad; msg.Text = "Could not save: " + ex.Message; }
    }

    // ---- log -----------------------------------------------------------------------------------------------------------

    private Control BuildLog()
    {
        var card = new Card { Dock = DockStyle.Fill, Title = "Log", Subtitle = "Also saved to the settings folder, so it survives restarts" };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Card };
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        _logBox = DarkText(true, true);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var open = new PillButton("Open log file", "\uE8A5", PillStyle.Secondary) { Margin = new Padding(0, 0, 10, 0) };
        var folder = new PillButton("Open settings folder", "", PillStyle.Secondary) { Margin = new Padding(0, 0, 10, 0) };
        var clear = new PillButton("Clear view", "", PillStyle.Ghost);
        open.Click += (_, _) => Shell(Config.LogPath);
        folder.Click += (_, _) => Shell(Config.Dir);
        clear.Click += (_, _) => _logBox.Clear();
        bar.Controls.AddRange(new Control[] { open, folder, clear });
        t.Controls.Add(Holder(_logBox), 0, 0); t.Controls.Add(bar, 0, 1);
        card.Controls.Add(t);
        return card;
    }

    private void LoadLogTail()
    {
        try
        {
            if (!File.Exists(Config.LogPath)) return;
            var lines = File.ReadAllLines(Config.LogPath);
            _logBox.Text = string.Join("\r\n", lines.Skip(Math.Max(0, lines.Length - 200))) + "\r\n";
            foreach (var l in lines.Skip(Math.Max(0, lines.Length - 8))) _recentLines.Enqueue(l);
            _recent.Text = string.Join("\r\n", _recentLines);
            ScrollToEnd(_recent); ScrollToEnd(_logBox);
        }
        catch { }
    }

    private void AppendLog(string line)
    {
        if (_logBox.TextLength > 200_000) _logBox.Text = _logBox.Text[100_000..];
        _logBox.AppendText(line + "\r\n");
        _recentLines.Enqueue(line);
        while (_recentLines.Count > 8) _recentLines.Dequeue();
        _recent.Text = string.Join("\r\n", _recentLines);
        ScrollToEnd(_recent);
    }

    private static void ScrollToEnd(TextBox tb) { try { tb.SelectionStart = tb.TextLength; tb.ScrollToCaret(); } catch { } }
}
