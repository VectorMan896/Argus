namespace Argus;

internal sealed partial class MainForm
{
    private readonly DiskSampler _diskSampler = new();
    private DiskCard _diskCard = null!;
    private readonly Queue<double> _diskRecent = new();           // the last ten readings, for the tip on the Performance page
    private SystemSummary? _lastSummary;
    private int _lastEnabledStartup;
    private bool _diskTipShown;

    private DiskCard BuildDiskCard()
    {
        _diskCard = new DiskCard();
        _diskCard.SlowDownRequested += SlowDownDisk;
        _diskSampler.Reading += r => { if (IsHandleCreated) BeginInvoke(() => OnDiskReading(r)); };
        _diskSampler.Users += u => { if (IsHandleCreated) BeginInvoke(() => _diskCard.ApplyUsers(u)); };
        return _diskCard;
    }

    private void OnDiskReading(DiskReading r)
    {
        _diskCard.SetKind(_diskSampler.Kind, Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "");
        _diskCard.ApplyReading(r);
        _diskRecent.Enqueue(r.Busy);
        while (_diskRecent.Count > 10) _diskRecent.Dequeue();

        bool high = RecentDiskTip() != null;
        if (high != _diskTipShown) { _diskTipShown = high; RenderTips(); }             // the tip appears and disappears with the disk
    }

    /// <summary>The disk has been busy for a while (average of the last ten seconds), or null.</summary>
    private DiskTip? RecentDiskTip()
    {
        if (_diskRecent.Count < 8) return null;
        double avg = _diskRecent.Average();
        return avg >= Perf.DiskBusyTipPercent ? new DiskTip(avg, _diskSampler.Kind) : null;
    }

    private void RenderTips()
    {
        if (_lastSummary == null) return;
        _perfTips.Text = "-  " + string.Join("\r\n\r\n-  ", Perf.Tips(_lastSummary, _lastEnabledStartup, RecentDiskTip()));
    }

    private void SlowDownDisk(DiskUser u)
    {
        _diskCard.ShowResult("Working...", true);
        Task.Run(() =>
        {
            var results = u.Members.Select(m => Perf.ThrottleDisk(m.Pid, m.Start)).ToList();
            int ok = results.Count(r => r.StartsWith("slowed down"));
            string first = results.FirstOrDefault(r => !r.StartsWith("slowed down")) ?? "";
            string text = ok > 0
                ? $"{u.Name}: slowed down ({ok} process{(ok == 1 ? "" : "es")}). Disk priority is lower until it restarts; nothing was closed."
                : $"{u.Name}: {first}";
            Log.Write($"[Disk] {u.Name}: {string.Join(" | ", results.Distinct())}");
            for (int i = 0; i < ok; i++) Stats.Record(new Finding(), "slowed down");
            if (IsHandleCreated) BeginInvoke(() => _diskCard.ShowResult(text, ok > 0));
        });
    }
}
