namespace Argus;

internal enum LearningOutcome { None, Start, KeepLearning, OpenRules }

/// <summary>
/// "What Argus would have done": the list recorded during learning mode, and the three honest choices.
/// Nothing in this list was closed, slowed down or changed.
/// </summary>
internal sealed class LearningDialog : Form
{
    public LearningOutcome Outcome { get; private set; }

    public LearningDialog(Settings s)
    {
        Text = "Argus learning mode";
        ClientSize = new Size(780, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Card; ForeColor = Theme.Text; Font = Theme.Ui(9.5f);
        Icon = Assets.AppIcon();

        bool over = Learning.PeriodOver(s);
        var title = new Label
        {
            Text = over ? "Learning is finished" : $"Learning mode: day {Learning.DayNumber(s)} of {Learning.Days(s)}",
            Font = Theme.Ui(17f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(28, 22, 724, 36),
        };
        var sub = new Label
        {
            Text = "For now Argus only watches. This is what it WOULD have done with your rules; nothing was closed, slowed down or changed. " +
                   "When the list looks right, let it start acting.",
            Font = Theme.Ui(10f), ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(30, 62, 722, 44),
        };

        var list = new ListView { Bounds = new Rectangle(28, 116, 724, 380) };
        list.Columns.Add("Program", 210); list.Columns.Add("Kind", 120); list.Columns.Add("Would have", 215); list.Columns.Add("Seen", 55); list.Columns.Add("Last seen", 124);
        Theme.StyleList(list, false);
        var entries = Learning.Entries();
        foreach (var e in entries)
        {
            var it = new ListViewItem(e.Program) { ToolTipText = e.Reason };
            it.SubItems.Add(e.Category); it.SubItems.Add(e.WouldDo); it.SubItems.Add(e.Sightings.ToString());
            it.SubItems.Add(e.LastUtc.ToLocalTime().ToString("MMM d, HH:mm"));
            list.Items.Add(it);
        }
        list.ShowItemToolTips = true;

        var empty = new Label
        {
            Text = "Nothing so far.\r\nArgus has not seen anything it would have acted on.",
            Font = Theme.Ui(11f), ForeColor = Theme.Muted, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter, Bounds = list.Bounds, Visible = entries.Count == 0,
        };
        list.Visible = entries.Count > 0;

        var note = new Label
        {
            Text = entries.Count == 0 ? "" : $"{entries.Count} thing(s) recorded. Anything set to Ask was never held back: it is on the Needs a decision page.",
            Font = Theme.Ui(9f), ForeColor = Theme.Faint, BackColor = Theme.Card, AutoSize = false, Bounds = new Rectangle(28, 502, 724, 22),
        };

        var start = new PillButton("Start acting on my rules", "", PillStyle.Primary, 230);
        var more = new PillButton($"Keep learning {Learning.Days(s)} more days", "", PillStyle.Ghost, 250);
        var rules = new PillButton("Open Rules", "", PillStyle.Secondary, 120);
        var close = new PillButton("Close", "", PillStyle.Secondary, 80);
        int x = 28, y = 540;
        foreach (var b in new Control[] { start, more, rules, close }) { b.Location = new Point(x, y); x += b.Width + 10; }
        start.Click += (_, _) => { Outcome = LearningOutcome.Start; Close(); };
        more.Click += (_, _) => { Outcome = LearningOutcome.KeepLearning; Close(); };
        rules.Click += (_, _) => { Outcome = LearningOutcome.OpenRules; Close(); };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { title, sub, list, empty, note, start, more, rules, close });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }
}
