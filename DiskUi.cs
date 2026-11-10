namespace Argus;

/// <summary>
/// The Disk card on the Performance page: a live "how busy is the disk" graph, what kind of drive it is, and the programs using it right now,
/// with one gentle button (lower that program's disk priority). Nothing is closed.
/// </summary>
internal sealed class DiskCard : Card
{
    private readonly LineGraph _graph = new() { Dock = DockStyle.Fill };
    private readonly ListView _list = new();
    private readonly Label _caption, _empty, _status;
    private readonly PillButton _slow;
    private readonly ToolTip _tt = new();
    private string _drive = "";

    public LineGraph Graph => _graph;
    public ListView List => _list;
    public string StatusText => _status.Text;

    /// <summary>The user pressed "Slow down selected": this program (all its processes) should get a lower disk priority.</summary>
    public event Action<DiskUser>? SlowDownRequested;

    public DiskCard()
    {
        Title = "Disk";
        Subtitle = "How busy the disk is, and who is using it";
        Dock = DockStyle.Fill;

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));              // without this the row is as tall as its contents and spills out of the card
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));

        // left: the graph and one line of numbers under it
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Card, Margin = new Padding(0, 0, 14, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        _caption = new Label { Dock = DockStyle.Fill, Font = Theme.Ui(8.5f), ForeColor = Theme.Muted, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleLeft };
        left.Controls.Add(_graph, 0, 0); left.Controls.Add(_caption, 0, 1);
        _tt.SetToolTip(_graph, "Busy time is the share of time the disk was working, like \"Active time\" in Task Manager.\n" +
                               "On a hard disk, near 100% means everything else has to wait its turn.");

        // right: who is using it, and the button
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Card, Margin = new Padding(0) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var heading = new Label
        {
            Dock = DockStyle.Fill, Text = "USING THE DISK RIGHT NOW", Font = Theme.Ui(7.5f, FontStyle.Bold), ForeColor = Theme.Faint,
            BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleLeft,
        };

        _list.Dock = DockStyle.Fill; _list.Margin = new Padding(0);
        _list.Columns.Add("Program", 150); _list.Columns.Add("Reads + writes", 104); _list.Columns.Add("Note", 120);
        Theme.StyleList(_list, false);
        _list.MultiSelect = false; _list.ShowItemToolTips = true;
        _list.SelectedIndexChanged += (_, _) => UpdateButton();
        _empty = new Label
        {
            Dock = DockStyle.Fill, Text = "Collecting...", Font = Theme.Ui(10f), ForeColor = Theme.Muted, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter,
        };
        var cell = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(0) };
        cell.Controls.Add(_list); cell.Controls.Add(_empty);
        _list.Visible = false;

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 0) };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _slow = new PillButton("Slow down selected", "", PillStyle.Secondary, 186) { Enabled = false };
        _slow.Click += (_, _) => { if (Selected() is { Protected: false } u) SlowDownRequested?.Invoke(u); };
        _status = new Label { Dock = DockStyle.Fill, Font = Theme.Ui(8.5f), ForeColor = Theme.Muted, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleLeft };
        bar.Controls.Add(_slow, 0, 0); bar.Controls.Add(_status, 1, 0);

        right.Controls.Add(heading, 0, 0); right.Controls.Add(cell, 0, 1); right.Controls.Add(bar, 0, 2);
        body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0);
        Controls.Add(body);
    }

    private DiskUser? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as DiskUser : null;

    private void UpdateButton()
    {
        var u = Selected();
        _slow.Enabled = u is { Protected: false };
        if (u == null) return;
        // what the selected program is probably doing, and why the button may be off
        string text = u.Hint ?? "";
        if (u.Protected) text = (text.Length > 0 ? text + " " : "") + "Argus will not touch this one.";
        if (text.Length > 0) ShowResult(text, true);
    }

    /// <summary>"Hard disk (spinning)  -  C:" under the title.</summary>
    public void SetKind(DiskKind kind, string drive)
    {
        _drive = drive;
        Subtitle = DiskType.Describe(kind) + (drive.Length > 0 ? "  -  " + drive : "");
    }

    public void SetAvailable(bool available)
    {
        _graph.SetUnavailable(available ? null : "Disk activity is not available on this PC.\nWindows' disk performance counters are switched off or missing.");
        if (!available) { _caption.Text = ""; _empty.Text = "No disk readings, so Argus cannot tell who is using it."; _empty.Visible = true; _list.Visible = false; }
    }

    public void ApplyReading(DiskReading r)
    {
        SetAvailable(true);
        _graph.Push(r.Busy);
        // the queue only matters once requests are piling up (two or more waiting), so it is only shown then
        _caption.Text = $"Read {Perf.Bytes((long)r.ReadBps)}/s   Write {Perf.Bytes((long)r.WriteBps)}/s" + (r.Queue >= 2 ? $"   Queue {r.Queue:0}" : "");
    }

    public void ApplyUsers(List<DiskUser> users)
    {
        string? keep = Selected()?.Name;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var u in users)
        {
            var it = new ListViewItem(u.Name + (u.Members.Count > 1 ? $"  ({u.Members.Count})" : "")) { Tag = u };
            it.SubItems.Add(Perf.Bytes((long)u.TotalBps) + "/s");
            it.SubItems.Add(u.Label ?? (u.Protected ? "Protected" : ""));
            it.ToolTipText = $"{u.Path ?? u.Name}\nRead {Perf.Bytes((long)u.ReadBps)}/s, write {Perf.Bytes((long)u.WriteBps)}/s" +
                             (u.Hint != null ? "\n" + u.Hint : "") + (u.Protected ? "\nProtected: Argus will not touch this program." : "");
            if (u.Protected) it.ForeColor = Theme.Muted;
            it.Selected = keep != null && u.Name == keep;
            _list.Items.Add(it);
        }
        _list.EndUpdate();
        _empty.Text = "Nothing is using the disk much right now.";
        _empty.Visible = users.Count == 0;
        _list.Visible = users.Count > 0;
        UpdateButton();
    }

    public void ShowResult(string text, bool good)
    {
        _status.ForeColor = good ? Theme.Good : Theme.Warn;
        _status.Text = text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tt.Dispose();
        base.Dispose(disposing);
    }
}
