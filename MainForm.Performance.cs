using System.Text.RegularExpressions;

namespace Argus;

internal sealed partial class MainForm
{
    private Label _perfSummary = null!, _perfTips = null!, _perfResult = null!;
    private readonly ListView _startupList = new();
    private PillButton _bTrim = null!, _bScanTemp = null!, _bCleanTemp = null!;
    private bool _loadingStartup;

    private static readonly Regex Sensitive = new("security|defender|audio|realtek|touchpad|driver|nvidia|intel|amd|bluetooth",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private Control BuildPerformance()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Bg };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

        // what would help
        var tips = new Card { Dock = DockStyle.Fill, Title = "What would help most", Subtitle = "Based on this PC's real numbers" };
        var tt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Card };
        tt.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        tt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _perfSummary = new Label { Dock = DockStyle.Fill, Font = Theme.Ui(9.5f, FontStyle.Bold), ForeColor = Theme.Accent, BackColor = Theme.Card };
        _perfTips = new Label { Dock = DockStyle.Fill, Font = Theme.Ui(10f), ForeColor = Theme.Text, BackColor = Theme.Card };
        tt.Controls.Add(_perfSummary, 0, 0); tt.Controls.Add(_perfTips, 0, 1);
        tips.Controls.Add(tt);

        // tools
        var tools = new Card { Dock = DockStyle.Fill, Title = "Free up memory and space", Subtitle = "Gentle, and either undoable or confirmed first" };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Card };
        _bTrim = new PillButton("Free up memory now", "", PillStyle.Primary, 240) { Margin = new Padding(0, 0, 0, 10) };
        _bScanTemp = new PillButton("Scan old temp files", "", PillStyle.Secondary, 240) { Margin = new Padding(0, 0, 0, 10) };
        _bCleanTemp = new PillButton("Clean old temp files...", "", PillStyle.Secondary, 240) { Margin = new Padding(0, 0, 0, 12) };
        _perfResult = new Label { AutoSize = false, Size = new Size(420, 70), Font = Theme.Ui(9f), ForeColor = Theme.Good, BackColor = Theme.Card };
        _bTrim.Click += async (_, _) => await TrimNowAsync();
        _bScanTemp.Click += async (_, _) => await TempAsync(clean: false);
        _bCleanTemp.Click += async (_, _) => await TempAsync(clean: true);
        flow.Controls.AddRange(new Control[] { _bTrim, _bScanTemp, _bCleanTemp, _perfResult });
        flow.Resize += (_, _) => _perfResult.Width = Math.Max(120, flow.ClientSize.Width - 6);
        tools.Controls.Add(flow);

        // startup programs
        var st = new Card { Dock = DockStyle.Fill, Title = "Startup programs", Subtitle = "Untick one to stop it starting with Windows. Nothing is deleted: tick it again to undo." };
        _startupList.Dock = DockStyle.Fill;
        _startupList.Columns.Add("Program", 260); _startupList.Columns.Add("Where", 200); _startupList.Columns.Add("Publisher", 190); _startupList.ShowItemToolTips = true;
        Theme.StyleList(_startupList, true);
        _startupList.ItemChecked += (_, e) => OnStartupChecked(e.Item);
        st.Controls.Add(_startupList);

        grid.Controls.Add(tips, 0, 0);
        grid.Controls.Add(tools, 0, 1);
        grid.Controls.Add(st, 1, 0); grid.SetRowSpan(st, 2);
        return grid;
    }

    private void RefreshPerf()
    {
        Task.Run(() =>
        {
            var sum = Perf.Summary();
            var items = Perf.StartupItems();
            if (IsHandleCreated) BeginInvoke(() => ApplyPerf(sum, items));
        });
    }

    private void ApplyPerf(SystemSummary s, List<StartupItem> items)
    {
        int enabled = items.Count(i => i.Enabled);
        _startupEnabled = enabled; _startupTotal = items.Count;
        ApplyStartupMeter();
        ApplyOverview(s);

        _perfSummary.Text =
            $"Memory {Perf.Bytes((long)(s.TotalRam - s.AvailRam))} of {Perf.Bytes((long)s.TotalRam)} ({s.MemLoad}%)    " +
            $"Drive {s.DiskName} {Perf.Bytes(s.DiskFree)} free\r\n" +
            $"Up {(int)s.Uptime.TotalDays}d {s.Uptime.Hours}h    {s.ProcessCount} processes    {enabled} of {items.Count} startup programs on";
        _perfTips.Text = "-  " + string.Join("\r\n\r\n-  ", Perf.Tips(s, enabled));

        _loadingStartup = true;
        _startupList.BeginUpdate();
        _startupList.Items.Clear();
        foreach (var i in items)
        {
            var it = new ListViewItem(i.Name + (i.NeedsAdmin ? "   (admin)" : "")) { Tag = i, Checked = i.Enabled };
            it.SubItems.Add(i.Where);
            it.SubItems.Add(i.Broken ? "(file is missing)" : i.Publisher);
            it.ToolTipText = i.Command;
            if (i.Broken || !i.Enabled) it.ForeColor = Theme.Muted;
            _startupList.Items.Add(it);
        }
        _startupList.EndUpdate();
        _loadingStartup = false;
    }

    private void OnStartupChecked(ListViewItem it)
    {
        if (_loadingStartup || it.Tag is not StartupItem item) return;
        bool want = it.Checked;
        if (want == item.Enabled) return;

        if (!want && (Sensitive.IsMatch(item.Name) || Sensitive.IsMatch(item.Publisher) || item.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)))
        {
            var ans = MessageBox.Show(this,
                $"\"{item.Name}\" looks like part of Windows or a hardware driver.\nStopping it from starting is usually fine, but leaving it on is the safe choice.\n\nStop it from starting anyway?",
                "Argus", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (ans != DialogResult.Yes) { Revert(it, item); return; }
        }

        string res = Perf.SetStartupEnabled(item, want);
        if (res is "enabled" or "disabled")
        {
            _perfResult.ForeColor = Theme.Good;
            _perfResult.Text = $"{item.Name}: {res}. Takes effect the next time you sign in.";
            it.ForeColor = want ? Theme.Text : Theme.Muted;
            RefreshStartupCounts();
        }
        else
        {
            Revert(it, item);
            _perfResult.ForeColor = Theme.Warn;
            _perfResult.Text = $"{item.Name}: {res}";
        }
    }

    private void Revert(ListViewItem it, StartupItem item)
    {
        _loadingStartup = true; it.Checked = item.Enabled; _loadingStartup = false;
        _startupList.Invalidate();
    }

    private async Task TrimNowAsync()
    {
        _bTrim.Enabled = false; _perfResult.ForeColor = Theme.Muted; _perfResult.Text = "Working...";
        try
        {
            var r = await Task.Run(() =>
            {
                var before = Perf.Summary();
                var t = Perf.TrimMemory();
                Thread.Sleep(600);
                return (t, before, after: Perf.Summary());
            });
            string msg = $"Asked Windows to page out {r.t.Trimmed} idle background programs. Available memory: " +
                         $"{Perf.Bytes((long)r.before.AvailRam)} to {Perf.Bytes((long)r.after.AvailRam)}. Nothing was closed.";
            _perfResult.ForeColor = Theme.Good; _perfResult.Text = msg;
            Log.Write("[Memory] " + msg);
            RefreshPerf();
        }
        finally { _bTrim.Enabled = true; }
    }

    private async Task TempAsync(bool clean)
    {
        string root = Path.GetTempPath();
        int days = Config.Settings.TempAgeDays;
        _perfResult.ForeColor = Theme.Muted; _perfResult.Text = "Scanning...";
        var scan = await Task.Run(() => Perf.TempFiles(root, days, delete: false));
        if (scan.Error != null) { _perfResult.ForeColor = Theme.Bad; _perfResult.Text = "Temp cleanup unavailable: " + scan.Error; return; }

        if (!clean)
        {
            _perfResult.ForeColor = Theme.Good;
            _perfResult.Text = $"{scan.Files} files ({Perf.Bytes(scan.Bytes)}) older than {days} days in your temp folder. Nothing is deleted until you press Clean.";
            return;
        }
        if (scan.Files == 0) { _perfResult.ForeColor = Theme.Good; _perfResult.Text = $"No files older than {days} days in your temp folder."; return; }

        var ans = MessageBox.Show(this,
            $"Delete {scan.Files} files ({Perf.Bytes(scan.Bytes)}) older than {days} days from your temp folder?\n\n{root}\n\n" +
            "Only old files in that folder are removed; files in use are skipped. This cannot be undone.",
            "Argus", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ans != DialogResult.Yes) { _perfResult.ForeColor = Theme.Muted; _perfResult.Text = "Cancelled. Nothing was deleted."; return; }

        _perfResult.ForeColor = Theme.Muted; _perfResult.Text = "Cleaning...";
        var done = await Task.Run(() => Perf.TempFiles(root, days, delete: true));
        string msg = $"Deleted {done.Deleted} files, freed {Perf.Bytes(done.Freed)} ({done.Skipped} skipped because they were in use or protected).";
        _perfResult.ForeColor = Theme.Good; _perfResult.Text = msg;
        Log.Write("[Temp] " + msg);
        RefreshPerf();
    }
}
