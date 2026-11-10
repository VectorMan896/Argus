using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Disk activity: how busy the disk is, whether it is a spinning disk or an SSD, and which programs are using it.
//
//  Older PCs with a hard disk often sit at "100% disk" and everything crawls. Everything here is read-only:
//    * busy time, throughput and queue length come from the "PhysicalDisk" performance counters (the same PDH route as the GPU graph)
//    * the drive type comes from asking the drive whether it pays a seek penalty (no administrator rights needed)
//    * per-program use comes from each process's I/O counters, compared between two moments
//  Changing anything (lowering a program's disk priority) lives in Perf.ThrottleDisk and always goes through the same safety rails.
// -------------------------------------------------------------------------------------------------

internal enum DiskKind { Unknown, Hdd, Ssd }

/// <summary>"The disk has been busy about N% of the time", for the tips on the Performance page.</summary>
internal readonly record struct DiskTip(double Busy, DiskKind Kind);

/// <summary>One reading of the whole machine's disk activity.</summary>
internal readonly record struct DiskReading(double Busy, double ReadBps, double WriteBps, double Queue);

/// <summary>Reads the PhysicalDisk performance counters through PDH. Cheap enough to run once a second.</summary>
internal sealed class DiskMeter : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query, _idle, _read, _write, _queue;

    /// <summary>False on PCs where the disk counters are switched off or missing; the card then says so instead of guessing.</summary>
    public bool Available { get; private set; }

    public DiskMeter() { Available = Open(); }

    private bool Open()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return false; }
            const string p = @"\PhysicalDisk(_Total)\";
            if (PdhAddEnglishCounterW(_query, p + "% Idle Time", IntPtr.Zero, out _idle) != 0
                || PdhAddEnglishCounterW(_query, p + "Disk Read Bytes/sec", IntPtr.Zero, out _read) != 0
                || PdhAddEnglishCounterW(_query, p + "Disk Write Bytes/sec", IntPtr.Zero, out _write) != 0) { Close(); return false; }
            PdhAddEnglishCounterW(_query, p + "Avg. Disk Queue Length", IntPtr.Zero, out _queue);       // nice to have, not essential
            PdhCollectQueryData(_query);                                                                  // baseline; rates need two samples
            return true;
        }
        catch (DllNotFoundException) { Close(); return false; }
        catch (EntryPointNotFoundException) { Close(); return false; }
    }

    private void Close()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
        _idle = _read = _write = _queue = IntPtr.Zero;
    }

    private static double? Get(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var v) != 0) return null;
        if (v.Status > 1) return null;                                // 0 = valid, 1 = new data; anything else is unusable
        return double.IsNaN(v.Value) || double.IsInfinity(v.Value) ? null : v.Value;
    }

    /// <summary>The disk activity since the previous call, or null if it could not be read.</summary>
    public DiskReading? Read()
    {
        if (!Available || _query == IntPtr.Zero) return null;
        if (PdhCollectQueryData(_query) != 0) return null;
        double? idle = Get(_idle), rd = Get(_read), wr = Get(_write);
        if (idle == null || rd == null || wr == null) return null;
        return new DiskReading(BusyFromIdle(idle.Value), Math.Max(0, rd.Value), Math.Max(0, wr.Value), Math.Max(0, Get(_queue) ?? 0));
    }

    /// <summary>"Active time" the way Task Manager shows it: 100 minus the share of time the disk sat idle.</summary>
    public static double BusyFromIdle(double idlePercent) =>
        double.IsNaN(idlePercent) ? 0 : Math.Clamp(100 - idlePercent, 0, 100);

    public void Dispose() => Close();
}

/// <summary>Spinning disk or SSD, from the drive's own answer to "do you pay a penalty for seeking?".</summary>
internal static class DiskType
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7, PropertyStandardQuery = 0;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

    public static DiskKind FromSeekPenalty(bool? incursSeekPenalty) =>
        incursSeekPenalty == null ? DiskKind.Unknown : incursSeekPenalty.Value ? DiskKind.Hdd : DiskKind.Ssd;

    /// <summary>DEVICE_SEEK_PENALTY_DESCRIPTOR: Version (4 bytes), Size (4 bytes), IncursSeekPenalty (1 byte).</summary>
    public static bool? ParseSeekPenalty(byte[] buffer, int length)
    {
        if (length < 9 || buffer.Length < 9) return null;
        uint size = BitConverter.ToUInt32(buffer, 4);
        if (size < 9) return null;
        return buffer[8] != 0;
    }

    /// <summary>The kind of the drive that holds the given folder or file (for example the Windows folder). Unknown when the drive does not say.</summary>
    public static DiskKind OfPath(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            if (root.Length < 2 || root[1] != ':') return DiskKind.Unknown;                  // network paths and the like
            IntPtr h = CreateFileW(@"\\.\" + root[..2], 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);   // access 0: asking a question, not reading the disk
            if (h == new IntPtr(-1)) return DiskKind.Unknown;
            try
            {
                var query = new byte[12];
                BitConverter.GetBytes(StorageDeviceSeekPenaltyProperty).CopyTo(query, 0);
                BitConverter.GetBytes(PropertyStandardQuery).CopyTo(query, 4);
                var answer = new byte[16];
                if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, query, query.Length, answer, answer.Length, out int got, IntPtr.Zero)) return DiskKind.Unknown;
                return FromSeekPenalty(ParseSeekPenalty(answer, got));
            }
            finally { Native.CloseHandle(h); }
        }
        catch { return DiskKind.Unknown; }
    }

    public static DiskKind OfSystemDrive() => OfPath(Environment.SystemDirectory);

    public static string Describe(DiskKind k) => k switch
    {
        DiskKind.Hdd => "Hard disk (spinning)",
        DiskKind.Ssd => "Solid-state drive (SSD)",
        _ => "Type not reported",
    };
}

/// <summary>One program (all its processes together) and how much it has been reading and writing.</summary>
internal sealed class DiskUser
{
    public string Name = "";
    public string? Path;
    public List<(int Pid, DateTime? Start)> Members = new();
    public double ReadBps, WriteBps;
    public bool Protected;            // core Windows: shown, but Argus will not touch it
    public string? Hint;              // plain-language "this is probably ..."
    public string? Label;             // the same in a word or two

    public double TotalBps => ReadBps + WriteBps;
}

internal readonly record struct IoSample(string Name, DateTime? Start, ulong ReadBytes, ulong WriteBytes);

internal static class ProcessDisk
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS counters);

    /// <summary>
    /// Every readable process's bytes read and written so far. These are the file reads and writes a program has asked for,
    /// including ones answered from Windows' cache, so they show who is busy with files rather than an exact count of disk accesses.
    /// </summary>
    public static Dictionary<int, IoSample> Snapshot()
    {
        var map = new Dictionary<int, IoSample>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                int pid = p.Id;
                if (pid <= 4) continue;
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (!GetProcessIoCounters(h, out var c)) continue;
                    DateTime? start = null;
                    try { start = p.StartTime; } catch { }
                    map[pid] = new IoSample(p.ProcessName, start, c.ReadTransferCount, c.WriteTransferCount);
                }
                finally { Native.CloseHandle(h); }
            }
        }
        return map;
    }

    /// <summary>
    /// The programs that used the disk most between two snapshots, busiest first. Processes of one program are added together.
    /// A process that restarted (same id, different start time) is ignored, and so is one that used almost nothing.
    /// </summary>
    public static List<DiskUser> Top(IReadOnlyDictionary<int, IoSample> before, IReadOnlyDictionary<int, IoSample> after, double seconds,
                                     Func<int, string?> pathOf, int count = 5, double minBytesPerSec = 8 * 1024)
    {
        var result = new List<DiskUser>();
        if (seconds <= 0) return result;
        var byName = new Dictionary<string, DiskUser>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pid, now) in after)
        {
            if (!before.TryGetValue(pid, out var was)) continue;
            if (was.Start != null && now.Start != null && was.Start != now.Start) continue;            // a different program with a reused id
            double read = now.ReadBytes >= was.ReadBytes ? (now.ReadBytes - was.ReadBytes) / seconds : 0;
            double write = now.WriteBytes >= was.WriteBytes ? (now.WriteBytes - was.WriteBytes) / seconds : 0;
            if (read + write <= 0) continue;
            if (!byName.TryGetValue(now.Name, out var u)) byName[now.Name] = u = new DiskUser { Name = now.Name };
            u.ReadBps += read; u.WriteBps += write; u.Members.Add((pid, now.Start));
        }

        foreach (var u in byName.Values.Where(u => u.TotalBps >= minBytesPerSec).OrderByDescending(u => u.TotalBps).Take(count))
        {
            u.Path = pathOf(u.Members[0].Pid);
            u.Protected = u.Members.Any(m => Safety.IsNeverAuto(u.Name, m.Pid, u.Path));
            u.Hint = DiskCulprits.Hint(u.Name);
            u.Label = DiskCulprits.Label(u.Name);
            result.Add(u);
        }
        return result;
    }
}

/// <summary>Plain-language notes on the usual heavy users of the disk on an older PC. Only said about the exact program names.</summary>
internal static class DiskCulprits
{
    public static string? Hint(string name) => name.ToLowerInvariant() switch
    {
        "searchindexer" or "searchprotocolhost" or "searchfilterhost" =>
            "Windows Search is indexing files. It usually settles by itself; Indexing Options in Windows can pause it.",
        "msmpeng" => "Windows Security is scanning files.",
        "tiworker" or "trustedinstaller" => "Windows is installing an update. Let it finish.",
        "onedrive" => "OneDrive is syncing files.",
        _ => null,
    };

    /// <summary>A word or two for the narrow Note column; the full sentence is the hint.</summary>
    public static string? Label(string name) => name.ToLowerInvariant() switch
    {
        "searchindexer" or "searchprotocolhost" or "searchfilterhost" => "Indexing",
        "msmpeng" => "Scanning",
        "tiworker" or "trustedinstaller" => "Updating",
        "onedrive" => "Syncing",
        _ => null,
    };
}

/// <summary>Reads the disk counters once a second and, when asked, the per-program use every few seconds. Samples nothing while off.</summary>
internal sealed class DiskSampler : IDisposable
{
    private DiskMeter? _meter;
    private System.Threading.Timer? _timer;
    private int _busy, _ticks;
    private bool _enabled, _wantUsers;
    private Dictionary<int, IoSample>? _before;
    private DateTime _beforeAt;

    public event Action<DiskReading>? Reading;
    public event Action<List<DiskUser>>? Users;

    public DiskKind Kind { get; private set; } = DiskKind.Unknown;
    public bool Available => _meter?.Available == true;
    public bool Started { get; private set; }

    /// <summary>Whether to sample at all (the Overview and Performance pages are on screen).</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;
            _enabled = value;
            if (value)
            {
                _meter?.Dispose(); _meter = null; _before = null;         // fresh baselines after a pause
                _timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);
            }
            else { _timer?.Dispose(); _timer = null; }
        }
    }

    /// <summary>Whether to also work out which programs use the disk (only the Performance page shows it).</summary>
    public bool WantUsers
    {
        get => _wantUsers;
        set { if (value == _wantUsers) return; _wantUsers = value; _before = null; }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (_meter == null)
            {
                _meter = new DiskMeter();
                Started = true;
                if (Kind == DiskKind.Unknown) Kind = DiskType.OfSystemDrive();
            }
            var r = _meter.Available ? _meter.Read() : null;
            if (r != null) Reading?.Invoke(r.Value);

            if (_wantUsers && ++_ticks % 3 == 0)                           // every third second is plenty for a list of names
            {
                var now = ProcessDisk.Snapshot(); var at = DateTime.UtcNow;
                if (_before != null)
                    Users?.Invoke(ProcessDisk.Top(_before, now, (at - _beforeAt).TotalSeconds, Native.GetProcessPath));
                _before = now; _beforeAt = at;
            }
        }
        catch { /* a failed sample just leaves a gap */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    public void Dispose() { Enabled = false; _meter?.Dispose(); }
}
