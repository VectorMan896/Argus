namespace Argus;

internal enum UpdateOutcome { None, Installed, Skipped }

/// <summary>
/// "Argus X is available": release notes, and three clear choices. Nothing is downloaded until the user presses Update;
/// a copy that cannot safely update itself (built from source, read-only folder) is only offered the release page.
/// </summary>
internal sealed class UpdateDialog : Form
{
    private readonly ReleaseInfo _rel;
    private readonly Label _status = new();
    private readonly Meter _meter = new("Downloading");
    private readonly PillButton _install, _skip, _page, _later;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public UpdateOutcome Outcome { get; private set; }

    public UpdateDialog(ReleaseInfo rel)
    {
        _rel = rel;
        Text = "Argus update";
        ClientSize = new Size(644, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Card; ForeColor = Theme.Text; Font = Theme.Ui(9.5f);
        Icon = Assets.AppIcon();

        var title = new Label
        {
            Text = $"Argus {rel.Version} is available", Font = Theme.Ui(17f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Card,
            AutoSize = false, Bounds = new Rectangle(28, 22, 588, 36),
        };
        var sub = new Label
        {
            Text = $"You have {AppInfo.Version}. Here is what is new.", Font = Theme.Ui(10f), ForeColor = Theme.Muted, BackColor = Theme.Card,
            AutoSize = false, Bounds = new Rectangle(30, 60, 586, 24),
        };

        string notes = string.IsNullOrWhiteSpace(rel.Notes) ? "No release notes were published." : rel.Notes.Trim();
        if (notes.Length > 6000) notes = notes[..6000] + "\n...";
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Font = Theme.Ui(10f), Text = notes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };
        box.BackColor = Theme.Input; box.ForeColor = Theme.Text;
        box.TabStop = false;
        Shown += (_, _) => box.Select(0, 0);                             // a read-only box otherwise opens with everything highlighted
        Theme.DarkScroll(box);
        var holder = new Panel { BackColor = Theme.Input, Padding = new Padding(16, 14, 8, 14), Bounds = new Rectangle(28, 98, 588, 262) };
        holder.Controls.Add(box);

        _status.AutoSize = false; _status.Bounds = new Rectangle(28, 370, 588, 52);
        _status.Font = Theme.Ui(9.5f); _status.ForeColor = Theme.Muted; _status.BackColor = Theme.Card;
        _meter.Bounds = new Rectangle(28, 428, 588, 54); _meter.Visible = false;

        _install = new PillButton("Update and restart", "", PillStyle.Primary, 190);
        _skip = new PillButton("Skip this version", "", PillStyle.Ghost, 150);
        _page = new PillButton("View on GitHub", "", PillStyle.Secondary, 140);
        _later = new PillButton("Later", "", PillStyle.Secondary, 80);
        int x = 28, y = 500;
        foreach (var b in new Control[] { _install, _skip, _page, _later }) { b.Location = new Point(x, y); x += b.Width + 10; }

        _install.Click += async (_, _) => await InstallAsync();
        _skip.Click += (_, _) => { Outcome = UpdateOutcome.Skipped; Close(); };
        _page.Click += (_, _) => OpenPage();
        _later.Click += (_, _) => { if (_busy) _cts?.Cancel(); else Close(); };

        if (!Updater.CanSelfUpdate(out string why))
        {
            _install.Visible = false;
            _page.Style = PillStyle.Primary; _page.Location = _install.Location; _skip.Left = _page.Right + 10; _later.Left = _skip.Right + 10;
            _status.Text = $"This copy can't update itself: {why}.\nDownload the new version from GitHub instead.";
        }
        else _status.Text = "Argus downloads the update from GitHub, checks it against its published checksum, and keeps your current version until the new one is running.";

        Controls.AddRange(new Control[] { title, sub, holder, _status, _meter, _install, _skip, _page, _later });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy) { _cts?.Cancel(); e.Cancel = true; return; }          // let the running install stop first
        base.OnFormClosing(e);
    }

    private void OpenPage()
    {
        if (Uri.TryCreate(_rel.PageUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch { }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _install.Enabled = _skip.Enabled = _page.Enabled = !busy;
        _later.Text = busy ? "Cancel" : "Later";
        _later.Invalidate();
    }

    private async Task InstallAsync()
    {
        SetBusy(true);
        _meter.Visible = true; _meter.Set(0, "", Theme.Accent);
        _status.ForeColor = Theme.Muted; _status.Text = "Starting...";
        _cts = new CancellationTokenSource();
        var progress = new Progress<UpdateProgress>(p =>
        {
            _status.Text = p.Step + (p.Total > 0 ? $"  ({Perf.Bytes(p.Done)} of {Perf.Bytes(p.Total)})" : "...");
            _meter.Label = p.Step;
            _meter.Set(p.Total > 0 ? (double)p.Done / p.Total : 0, p.Total > 0 ? $"{Perf.Bytes(p.Done)} / {Perf.Bytes(p.Total)}" : "", Theme.Accent);
        });
        try
        {
            await Updater.InstallAsync(_rel, Environment.ProcessPath!, progress, _cts.Token);
            Outcome = UpdateOutcome.Installed;
            _busy = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) { _status.ForeColor = Theme.Muted; _status.Text = "Cancelled. Nothing was changed."; _meter.Visible = false; SetBusy(false); }
        catch (UpdateException ex) { Fail(ex.Message); }
        catch (Exception ex) { Fail("Something went wrong: " + ex.Message); }
    }

    private void Fail(string message)
    {
        _status.ForeColor = Theme.Bad;
        _status.Text = message + "\nNothing was changed.";
        _meter.Visible = false;
        SetBusy(false);
    }
}
