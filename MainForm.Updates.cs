namespace Argus;

internal sealed partial class MainForm
{
    private readonly PillButton _btnUpdate = new("Update available", "", PillStyle.Accent) { Visible = false };
    private PillButton _btnCheckNow = null!;
    private RowCard? _updatesCard;
    private ReleaseInfo? _update;
    private string? _notifiedVersion;
    private bool _trayWantsUpdate, _checking, _exitingForUpdate;
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60 * 60 * 1000 };   // hourly: is the daily check due?
    private readonly System.Windows.Forms.Timer _firstCheck = new() { Interval = 8000 };               // a few seconds after start, never during it
    private readonly System.Windows.Forms.Timer _confirmTimer = new() { Interval = 10_000 };

    /// <summary>Starts the update timers. Nothing here touches the network unless the user turned update checks on.</summary>
    private void InitUpdates()
    {
        WarnIfRunningFromTemporaryFolder();
        _btnUpdate.BackColor = Theme.Bg;
        _btnUpdate.Click += (_, _) => ShowUpdateDialog();

        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync(manual: false);
        _updateTimer.Start();
        _firstCheck.Tick += async (_, _) => { _firstCheck.Stop(); await CheckForUpdateAsync(manual: false); };
        _firstCheck.Start();

        // right after an update: once the new version has run healthily for a while, it counts as good and the old one is dropped
        if (UpdateRecovery.Pending)
        {
            _confirmTimer.Tick += (_, _) => { _confirmTimer.Stop(); UpdateRecovery.Confirm(); };
            _confirmTimer.Start();
        }
    }

    /// <summary>Double-clicking Argus.exe inside the zip window runs it from a temporary folder that Windows cleans up later.</summary>
    private void WarnIfRunningFromTemporaryFolder()
    {
        if (!Updater.IsTemporaryLocation(Environment.ProcessPath)) return;
        const string msg = "Argus is running from a temporary folder, probably straight out of the zip. Extract the zip to a folder you keep, " +
                           "for example C:\\Users\\you\\Apps\\Argus, and run it from there. Your settings are kept either way.";
        Log.Write(msg);
        var t = new System.Windows.Forms.Timer { Interval = 4000 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); _tray.ShowBalloonTip(15000, "Argus is running from a temporary folder", msg, ToolTipIcon.Warning); };
        t.Start();
    }

    private RowCard BuildUpdatesCard()
    {
        var card = new RowCard { Title = "Updates", Subtitle = "Off by default. Argus only contacts GitHub if you turn this on or press Check now." };
        var tog = Tog(Config.Settings.UpdateCheck, v =>
        {
            Config.Settings.UpdateCheck = v;
            Log.Write(v ? "Update checks turned on (about once a day, github.com only)." : "Update checks turned off.");
            if (v) _ = CheckForUpdateAsync(manual: false, force: true);
        });
        card.AddRow("Check for updates", "About once a day, from github.com only", tog);

        _btnCheckNow = new PillButton("Check now", "", PillStyle.Secondary, 150);
        _btnCheckNow.Click += async (_, _) => await CheckForUpdateAsync(manual: true);
        card.AddRow($"Argus {AppInfo.Version}", "Press Check now to look for a newer version", _btnCheckNow);
        _updatesCard = card;
        return card;
    }

    private void SetUpdateStatus(string text) => _updatesCard?.SetHint(_btnCheckNow, text);

    private void ShowUpdateButton(ReleaseInfo rel)
    {
        string text = $"Update {rel.Version}";
        _btnUpdate.Text = text;
        _btnUpdate.Width = Theme.Measure(text, Theme.Ui(9.25f, FontStyle.Bold)) + 40 + 28;
        _btnUpdate.Visible = true;
        LayoutHeader();
    }

    private static string Short(string? s) => string.IsNullOrEmpty(s) ? "Could not check" : (s.Length > 52 ? s[..51] + "..." : char.ToUpperInvariant(s[0]) + s[1..]);

    /// <summary>
    /// Asks GitHub for the latest release. A background check only runs if the user turned update checks on and a day has passed;
    /// "Check now" always runs. This never downloads or installs anything.
    /// </summary>
    private async Task CheckForUpdateAsync(bool manual, bool force = false)
    {
        var s = Config.Settings;
        if (_checking) return;
        if (!manual && !force)
        {
            if (!s.UpdateCheck) return;
            if (s.LastUpdateCheckUtc is DateTime last && (DateTime.UtcNow - last).TotalHours < 24) return;
        }

        _checking = true;
        SetUpdateStatus("Checking...");
        try
        {
            var (rel, problem) = await Updater.CheckAsync();
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            Config.SaveSettings();

            if (rel == null)
            {
                SetUpdateStatus(Short(problem));
                Log.Write("Update check: " + (problem ?? "no answer") + ".");
                return;
            }
            bool newer = Updater.IsNewer(AppInfo.Version, rel.Version);
            if (newer && (manual || s.SkippedVersion != rel.Version))
            {
                _update = rel;
                ShowUpdateButton(rel);
                SetUpdateStatus($"Version {rel.Version} is available");
                if (_notifiedVersion != rel.Version)
                {
                    _notifiedVersion = rel.Version;
                    _trayWantsUpdate = true;
                    Log.Write($"Update {rel.Version} is available.");
                    _tray.ShowBalloonTip(8000, "Argus update available", $"Version {rel.Version} is available. Click to see what is new.", ToolTipIcon.Info);
                }
            }
            else
            {
                _update = null; _btnUpdate.Visible = false; LayoutHeader();
                SetUpdateStatus(newer ? $"Version {rel.Version} skipped" : $"Up to date (checked {DateTime.Now:HH:mm})");
            }
        }
        finally { _checking = false; }
    }

    private void ShowUpdateDialog()
    {
        if (_update == null) return;
        var rel = _update;
        ShowMain();
        using var dlg = new UpdateDialog(rel);
        dlg.ShowDialog(this);

        if (dlg.Outcome == UpdateOutcome.Skipped)
        {
            Config.Settings.SkippedVersion = rel.Version;
            Config.SaveSettings();
            _update = null; _btnUpdate.Visible = false; LayoutHeader();
            SetUpdateStatus($"Version {rel.Version} skipped");
        }
        else if (dlg.Outcome == UpdateOutcome.Installed)
        {
            string exe = Environment.ProcessPath!;
            try
            {
                Log.Write($"Installed {rel.Version}. Restarting.");
                Updater.Relaunch(exe);
                _exitingForUpdate = true;          // do not mark the update as confirmed: the new version has not run yet
                ExitApp();
            }
            catch (Exception ex)
            {
                // the new version could not be started: put the old one back, so nothing is left half-done
                try { Updater.Rollback(exe); UpdateRecovery.Clear(); } catch { }
                Log.Write("Could not start the new version, went back to the old one: " + ex.Message);
                MessageBox.Show(this, "The new version could not be started, so Argus went back to the version you had.\n\n" + ex.Message,
                    "Argus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
