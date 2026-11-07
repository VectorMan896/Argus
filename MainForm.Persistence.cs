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

    private void ShowDisabledByArgus()
    {
        using var d = new DisabledByArgusDialog();
        d.ShowDialog(this);
    }
}
