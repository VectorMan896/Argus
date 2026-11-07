using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Live CPU and GPU load for the Overview graphs.
//
//  Cheap on purpose, because older PCs are the point:
//    * CPU comes from GetSystemTimes (two numbers per second, no performance-counter setup needed)
//    * GPU comes from the vendor-neutral "GPU Engine" counters (Windows 10 1709+ with a WDDM 2.x driver);
//      where they do not exist the graph says so instead of guessing
//    * nothing is sampled while the window is hidden, minimised, or on another page
// -------------------------------------------------------------------------------------------------

/// <summary>Total CPU load from the system's idle / kernel / user times.</summary>
internal sealed class CpuMeter
{
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out Native.FILETIME idle, out Native.FILETIME kernel, out Native.FILETIME user);

    private ulong _idle, _kernel, _user;
    private bool _has;

    public void Reset() => _has = false;

    /// <summary>Percent busy since the previous call; null on the first call.</summary>
    public double? Read()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return null;
        double? result = null;
        if (_has) result = Percent(i.Value - _idle, k.Value - _kernel, u.Value - _user);
        _idle = i.Value; _kernel = k.Value; _user = u.Value; _has = true;
        return result;
    }

    /// <summary>Kernel time already includes idle time, so busy = (kernel + user) - idle.</summary>
    public static double Percent(ulong idleDelta, ulong kernelDelta, ulong userDelta)
    {
        ulong total = kernelDelta + userDelta;
        if (total == 0) return 0;
        double busy = total - Math.Min(idleDelta, total);
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }
}

internal static class Gpu
{
    /// <summary>
    /// Instance names look like  pid_1234_luid_0x00000000_0x0000F5A8_phys_0_eng_0_engtype_3D .
    /// Returns the adapter (luid) and the engine type (3D, Copy, VideoDecode ...).
    /// </summary>
    public static bool TryParse(string instance, out string luid, out string engine)
    {
        luid = engine = "";
        int l = instance.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
        int p = instance.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
        int e = instance.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
        if (l < 0 || p < l || e < 0) return false;
        luid = instance.Substring(l + 6, p - (l + 6));
        engine = instance[(e + 9)..];
        return engine.Length > 0;
    }

    /// <summary>
    /// GPU load the way Task Manager reports it: add up every program's use of an engine on an adapter, then take the busiest
    /// engine. Programs that are not using the GPU contribute nothing.
    /// </summary>
    public static double Aggregate(IEnumerable<(string Instance, double Value)> items)
    {
        var sums = new Dictionary<(string, string), double>();
        foreach (var (name, v) in items)
        {
            if (!TryParse(name, out var luid, out var engine)) continue;
            sums[(luid, engine)] = sums.GetValueOrDefault((luid, engine)) + Math.Max(0, v);
        }
        return sums.Count == 0 ? 0 : Math.Min(100, sums.Values.Max());
    }
}

/// <summary>Reads the GPU Engine performance counters through PDH (no extra libraries).</summary>
internal sealed class GpuMeter : IDisposable
{
    private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Item { public IntPtr Name; public uint Status; public double Value; }   // PDH_FMT_COUNTERVALUE_ITEM_W

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query, _counter;
    private int _reads;

    /// <summary>False on PCs without the GPU counters (older Windows or driver).</summary>
    public bool Available { get; private set; }

    public GpuMeter() { Available = Open(); }

    private bool Open()
    {
        Close();
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return false; }
            if (PdhAddEnglishCounterW(_query, CounterPath, IntPtr.Zero, out _counter) != 0) { Close(); return false; }
            PdhCollectQueryData(_query);                         // baseline sample; rates need two
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    private void Close()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
        _counter = IntPtr.Zero;
    }

    /// <summary>GPU load 0-100 since the previous call, or null if it could not be read.</summary>
    public double? Read()
    {
        if (!Available || _query == IntPtr.Zero) return null;
        if (PdhCollectQueryData(_query) != 0) return null;

        uint size = 0, count = 0;
        uint r = PdhGetFormattedCounterArrayW(_counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
        double? result = null;
        if (r == PDH_MORE_DATA && size > 0)
        {
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(_counter, PDH_FMT_DOUBLE, ref size, ref count, buf) == 0)
                {
                    var items = new List<(string, double)>((int)count);
                    int stride = Marshal.SizeOf<Item>();
                    for (int i = 0; i < count; i++)
                    {
                        var it = Marshal.PtrToStructure<Item>(buf + i * stride);
                        if (it.Status > 1) continue;                    // 0 = valid, 1 = new data; anything else is unusable
                        items.Add((Marshal.PtrToStringUni(it.Name) ?? "", it.Value));
                    }
                    result = Gpu.Aggregate(items);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        // programs start and stop all the time: rebuild the instance list every few seconds so new ones are seen
        if (++_reads % 8 == 0) Available = Open();
        return result;
    }

    public void Dispose() => Close();
}

/// <summary>Calls CPU and GPU meters once a second while enabled, and reports the readings.</summary>
internal sealed class UsageSampler : IDisposable
{
    private readonly CpuMeter _cpu = new();
    private GpuMeter? _gpu;
    private System.Threading.Timer? _timer;
    private int _busy;
    private bool _enabled;

    /// <summary>(cpu %, gpu %). Either is null when it could not be read.</summary>
    public event Action<double?, double?>? Sampled;

    public bool GpuAvailable => _gpu?.Available == true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;
            _enabled = value;
            if (value)
            {
                _cpu.Reset();
                _gpu?.Dispose(); _gpu = null;                          // fresh baselines after a pause
                _timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);
            }
            else { _timer?.Dispose(); _timer = null; }
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            _gpu ??= new GpuMeter();
            double? c = _cpu.Read();
            double? g = _gpu.Available ? _gpu.Read() : null;
            if (c != null || g != null) Sampled?.Invoke(c, g);
        }
        catch { /* a failed sample just leaves a gap */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    public void Dispose() { Enabled = false; _gpu?.Dispose(); }
}

internal static class Hardware
{
    private static string Tidy(string s) => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");

    private static void AddGpu(List<string> names, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string d = Tidy(raw);
        if (d.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || d.Contains("Remote", StringComparison.OrdinalIgnoreCase)
            || d.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) return;
        if (!names.Contains(d)) names.Add(d);
    }

    private static string? AsText(object? v) => v switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.Unicode.GetString(b).Trim('\0'),
        _ => null,
    };

    /// <summary>Graphics adapter names (real adapters only, no duplicates).</summary>
    public static List<string> GpuNames()
    {
        var names = new List<string>();
        try
        {
            // HARDWARE\DEVICEMAP\VIDEO lists \Device\Video0.. -> the registry key of each adapter output
            using var map = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\VIDEO");
            const string prefix = @"\Registry\Machine\";
            if (map != null)
                foreach (var valueName in map.GetValueNames().Where(n => n.StartsWith(@"\Device\Video", StringComparison.OrdinalIgnoreCase)))
                {
                    if (map.GetValue(valueName) is not string target || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    using var k = Registry.LocalMachine.OpenSubKey(target[prefix.Length..]);
                    AddGpu(names, AsText(k?.GetValue("DriverDesc")) ?? AsText(k?.GetValue("HardwareInformation.AdapterString")));
                }
        }
        catch { }
        if (names.Count == 0) names.AddRange(GpuNamesFromClassKey());
        return names;
    }

    public static string CpuName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? n = k?.GetValue("ProcessorNameString") as string;
            return string.IsNullOrWhiteSpace(n) ? "" : Tidy(n);
        }
        catch { return ""; }
    }

    /// <summary>Graphics adapter names from the display-class registry key (real adapters only).</summary>
    private static List<string> GpuNamesFromClassKey()
    {
        var names = new List<string>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-b6c3-0050fc9525c3}");
            if (cls == null) return names;
            foreach (var sub in cls.GetSubKeyNames().Where(s => s.Length == 4 && s.All(char.IsDigit)))
            {
                using var k = cls.OpenSubKey(sub);
                string? d = k?.GetValue("DriverDesc") as string;
                if (string.IsNullOrWhiteSpace(d)) continue;
                d = Tidy(d);
                if (d.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || d.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                    || d.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                if (!names.Contains(d)) names.Add(d);
            }
        }
        catch { }
        return names;
    }
}
