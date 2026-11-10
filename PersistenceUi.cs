namespace Argus;

internal enum PersistOutcome { None, Changed, LeaveAlone, RestartAsAdministrator }

/// <summary>
/// "Keeps coming back": lists what restarts a program and lets the user switch each starter off. Nothing is changed until
/// the button is pressed, nothing is deleted, and every change can be undone from Rules > Disabled by Argus.
/// </summary>
internal sealed class PersistenceDialog : Form
{
    private readonly Finding _finding;
    private readonly ListView _list = new();
    private readonly Label _summary;
    private readonly PillButton _disable, _admin;

    public PersistOutcome Outcome { get; private set; }
    public int DisabledCount { get; private set; }

    public PersistenceDialog(Finding f)
    {
        _finding = f;
        Text = "Argus: " + f.ProcName + " keeps coming back";
        ClientSize = new Size(780, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Card; ForeColor = Theme.Text; Font = Theme.Ui(9.5f);
        Icon = Assets.AppIcon();

        var sources = f.Sources ?? new List<PersistenceSource>();
        bool needsAdmin = sources.Any(s => s.NeedsAdmin) && !Elevation.IsElevated;

        var title = new Label
        {
            Text = f.ProcName + " keeps coming back", Font = Theme.Ui(17f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card,
            AutoSize = false, Bounds = new Rectangle(28, 22, 724, 36),
        };
        var sub = new Label
        {
            Text = "Something starts this program again after it is closed. Closing it over and over does not help for long; switching off what starts it does. " +
                   "Tick what to switch off. Nothing is deleted, and you can switch each one back on from Rules > Disabled by Argus.",
            Font = Theme.Ui(10f), ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(30, 62, 722, 62),
        };

        _list.Bounds = new Rectangle(28, 134, 724, 270);
        _list.Columns.Add("What starts it", 190); _list.Columns.Add("Kind", 170); _list.Columns.Add("How it starts", 214); _list.Columns.Add("Result", 150);
        Theme.StyleList(_list, true);
        foreach (var s in sources)
        {
            var it = new ListViewItem(s.Display) { Tag = s, Checked = true, ToolTipText = s.Name };
            it.SubItems.Add(s.KindLabel + (s.NeedsAdmin ? " (admin)" : "")); it.SubItems.Add(s.Detail); it.SubItems.Add("");
            _list.Items.Add(it);
        }
        _list.ShowItemToolTips = true;

        var note = new Label
        {
            Text = needsAdmin
                ? "Services and system tasks can only be changed with administrator rights, and Argus is not running as administrator right now. " +
                  "Press \"Restart as administrator\" first, or switch off only the ones that do not need it."
                : "Core Windows programs are never offered here. If you are not sure about one of these, leave it unticked.",
            Font = Theme.Ui(9.5f), ForeColor = needsAdmin ? Theme.Warn : Theme.Faint, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(28, 412, 724, 52),
        };
        _summary = new Label
        {
            Text = "", Font = Theme.Ui(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(28, 468, 724, 40),
        };

        _disable = new PillButton("Switch off ticked", "", PillStyle.Primary, 180);
        _admin = new PillButton("Restart as administrator", "", PillStyle.Secondary, 230) { Visible = needsAdmin };
        var leave = new PillButton("Leave it alone", "", PillStyle.Ghost, 150);
        var close = new PillButton("Close", "", PillStyle.Secondary, 80);
        int x = 28, y = 540;
        foreach (var b in new Control[] { _disable, _admin, leave, close }) { if (!b.Visible) continue; b.Location = new Point(x, y); x += b.Width + 10; }
        _disable.Click += (_, _) => SwitchOff();
        _admin.Click += (_, _) => { Outcome = PersistOutcome.RestartAsAdministrator; Close(); };
        leave.Click += (_, _) => { Outcome = PersistOutcome.LeaveAlone; Close(); };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { title, sub, _list, note, _summary, _disable, _admin, leave, close });
    }

    private void SwitchOff()
    {
        int ok = 0, refused = 0;
        foreach (ListViewItem it in _list.Items)
        {
            if (!it.Checked || it.SubItems[3].Text == "switched off") continue;
            var s = (PersistenceSource)it.Tag!;
            string r = Persistence.Disable(s, _finding.Path ?? "");
            if (r == "disabled") { ok++; it.SubItems[3].Text = "switched off"; it.Checked = false; it.ForeColor = Theme.Muted; }
            else { refused++; it.SubItems[3].Text = r.StartsWith("needs administrator") ? "needs administrator" : r; it.ForeColor = Theme.Warn; it.ToolTipText = r; }
        }
        DisabledCount += ok;
        if (ok > 0) Outcome = PersistOutcome.Changed;
        _list.Invalidate();
        _summary.Text = ok == 0 && refused == 0 ? "Nothing was ticked."
            : $"{ok} switched off." + (refused > 0 ? $" {refused} could not be changed (hover over the result for the reason)." : "") +
              (ok > 0 ? " It will not be started this way again. A copy that is running now is not closed by this: close it from the list." : "");
        _summary.ForeColor = refused > 0 ? Theme.Warn : Theme.Accent;
        if (refused > 0 && !Elevation.IsElevated) _admin.Visible = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }
}

/// <summary>Everything Argus ever switched off, with a way to switch each one back on.</summary>
internal sealed class DisabledByArgusDialog : Form
{
    private readonly ListView _list = new();
    private readonly Label _empty, _summary;

    public DisabledByArgusDialog()
    {
        Text = "Disabled by Argus";
        ClientSize = new Size(780, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Card; ForeColor = Theme.Text; Font = Theme.Ui(9.5f);
        Icon = Assets.AppIcon();

        var title = new Label
        {
            Text = "Disabled by Argus", Font = Theme.Ui(17f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card,
            AutoSize = false, Bounds = new Rectangle(28, 22, 724, 36),
        };
        var sub = new Label
        {
            Text = "Services, scheduled tasks and startup entries that Argus switched off at your request. Tick any and press Restore to put them back exactly as they were.",
            Font = Theme.Ui(10f), ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(30, 62, 722, 44),
        };

        _list.Bounds = new Rectangle(28, 116, 724, 300);
        _list.Columns.Add("Name", 270); _list.Columns.Add("Kind", 130); _list.Columns.Add("Switched off", 150); _list.Columns.Add("Result", 150);
        Theme.StyleList(_list, true);
        _empty = new Label
        {
            Text = "Nothing so far.\r\nArgus has not switched anything off.", Font = Theme.Ui(11f), ForeColor = Theme.Muted, BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleCenter, Bounds = _list.Bounds,
        };
        _summary = new Label { Text = "", Font = Theme.Ui(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(28, 428, 724, 40) };

        var restore = new PillButton("Restore ticked", "", PillStyle.Primary, 170);
        var close = new PillButton("Close", "", PillStyle.Secondary, 80);
        restore.Location = new Point(28, 470); close.Location = new Point(28 + 180, 470);
        restore.Click += (_, _) => RestoreTicked();
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { title, sub, _list, _empty, _summary, restore, close });
        Fill();
    }

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var r in Persistence.Records().OrderByDescending(r => r.WhenUtc))
        {
            var it = new ListViewItem(r.Display) { Tag = r, ToolTipText = r.Exe };
            it.SubItems.Add(new PersistenceSource { Kind = r.Kind }.KindLabel);
            it.SubItems.Add(r.WhenUtc.ToLocalTime().ToString("MMM d, HH:mm")); it.SubItems.Add("");
            _list.Items.Add(it);
        }
        _list.ShowItemToolTips = true;
        _list.Visible = _list.Items.Count > 0;
        _empty.Visible = _list.Items.Count == 0;
    }

    private void RestoreTicked()
    {
        int ok = 0, bad = 0;
        var failed = new Dictionary<string, string>();                      // what could not be restored stays listed, with the reason
        foreach (var it in _list.CheckedItems.Cast<ListViewItem>().ToList())
        {
            var rec = (DisabledRecord)it.Tag!;
            string r = Persistence.Restore(rec);
            if (r == "restored") ok++; else { bad++; failed[rec.Kind + "|" + rec.Name] = r; }
        }
        Fill();
        foreach (ListViewItem it in _list.Items)
            if (it.Tag is DisabledRecord d && failed.TryGetValue(d.Kind + "|" + d.Name, out var why))
            { it.SubItems[3].Text = why.StartsWith("needs administrator") ? "needs administrator" : why; it.ToolTipText = why; it.ForeColor = Theme.Warn; }
        _summary.Text = ok == 0 && bad == 0 ? "Nothing was ticked." : $"{ok} restored." + (bad > 0 ? $" {bad} could not be restored (administrator rights may be needed)." : "");
        _summary.ForeColor = bad > 0 ? Theme.Warn : Theme.Accent;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }
}
