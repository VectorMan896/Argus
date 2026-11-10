namespace Argus;

internal sealed partial class MainForm
{
    /// <summary>"Keeps coming back": show what restarts the program and let the user switch each starter off.</summary>
    private void ShowPersistenceDialog(Finding f)
    {
        ShowMain();
        using var d = new PersistenceDialog(f);
        d.ShowDialog(this);
        switch (d.Outcome)
        {
            case PersistOutcome.Changed:
                _watcher.ResolveRespawn(f, dontAskAgain: false);
                break;
            case PersistOutcome.LeaveAlone:
                _watcher.ResolveRespawn(f, dontAskAgain: true);
                Log.Write($"{f.ProcName}: left alone; Argus will not suggest disabling what restarts it again.");
                break;
            case PersistOutcome.RestartAsAdministrator:
                RestartAsAdministrator();
                break;
        }
    }

    /// <summary>"What restarts it?": look for the service, task or startup entry behind the selected program without waiting for it to come back three times.</summary>
    private void ShowStarters()
    {
        var f = Selected().FirstOrDefault(x => x.Category != Category.Blocker);
        if (f == null)
        {
            MessageBox.Show(this, "Select a program in the list first.", "Argus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Finding? r;
        var old = Cursor; Cursor = Cursors.WaitCursor;                     // reading Task Scheduler can take a second
        try { r = f.Category == Category.Respawn ? f : Persistence.FindingFor(f.ProcName, f.Path); }
        finally { Cursor = old; }
        if (r == null)
        {
            MessageBox.Show(this,
                $"No service, scheduled task or startup entry points at {f.ProcName}.\n\nIf it comes back, something else starts it (an installer, an updater, " +
                "or another program), or it is a core Windows program, which Argus never offers to switch off.",
                "Argus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ShowPersistenceDialog(r);
    }

    private void ShowDisabledByArgus()
    {
        using var d = new DisabledByArgusDialog();
        d.ShowDialog(this);
    }
}
