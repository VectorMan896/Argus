namespace Argus;

internal sealed partial class MainForm
{
    private readonly PillButton _btnLearning = new("Learning", "", PillStyle.Ghost) { Visible = false };
    private bool _learningNotified, _trayWantsLearning;

    private void InitLearning()
    {
        _btnLearning.BackColor = Theme.Bg;
        _btnLearning.Click += (_, _) => ShowLearningDialog();
        UpdateLearningUi();
    }

    /// <summary>The header button: "Learning: day 2 of 3", then "Learning done: review". Hidden when learning mode is off.</summary>
    private void UpdateLearningUi()
    {
        var s = Config.Settings;
        _btnLearning.Visible = s.Learning;
        if (s.Learning)
        {
            bool over = Learning.PeriodOver(s);
            string text = over ? "Learning done: review" : $"Learning: day {Learning.DayNumber(s)} of {Learning.Days(s)}";
            if (_btnLearning.Text != text)
            {
                _btnLearning.Text = text;
                _btnLearning.Width = Theme.Measure(text, Theme.Ui(9.25f, FontStyle.Bold)) + 40 + 28;
            }
            if (over && !_learningNotified)
            {
                _learningNotified = true;
                _trayWantsLearning = true;
                _tray.ShowBalloonTip(10000, "Argus finished learning", "See what Argus would have done, then decide whether it should start acting.", ToolTipIcon.Info);
            }
        }
        LayoutHeader();
    }

    private void RestartAsAdministrator()
    {
        var ans = MessageBox.Show(this,
            "Argus will ask Windows for administrator rights and restart.\n\n" +
            "With them Argus can slow down, close or disable things a normal account cannot touch. Every safety rule stays in force " +
            "(Windows' core processes and Argus itself are never closed), but a mistake can now affect the whole PC, so only do this when you need it.\n\n" +
            "Windows will show its own approval prompt. Continue?",
            "Argus", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ans != DialogResult.Yes) return;

        if (Elevation.TryRestartElevated(out string message))
        {
            Log.Write("Restarting with administrator rights.");
            _exitingForUpdate = true;                  // a hand-over, not a normal exit: do not mark a pending update as confirmed
            ExitApp();
        }
        else MessageBox.Show(this, message, "Argus", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowLearningDialog()
    {
        ShowMain();
        using var d = new LearningDialog(Config.Settings);
        d.ShowDialog(this);
        switch (d.Outcome)
        {
            case LearningOutcome.Start:
                Learning.Finish(Config.Settings);
                Log.Write("Learning mode finished: Argus now acts on its rules.");
                break;
            case LearningOutcome.KeepLearning:
                Learning.Start(Config.Settings);
                _learningNotified = false;
                Log.Write($"Learning restarted for {Learning.Days(Config.Settings)} more days.");
                break;
            case LearningOutcome.OpenRules:
                GoTo(3);
                break;
        }
        UpdateStatus();
    }
}
