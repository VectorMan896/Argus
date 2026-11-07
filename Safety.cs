namespace Argus;

/// <summary>
/// Hard safety rails. These apply no matter what the blocklist or settings say.
/// </summary>
internal static class Safety
{
    /// <summary>Never touched by anything, not even by an explicit user click (the OS would break or refuse).</summary>
    private static readonly HashSet<string> Refuse = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm",
        "dwm", "fontdrvhost", "msmpeng", "nissrv", "argus",
    };

    /// <summary>Core desktop/shell pieces: never closed automatically, only on an explicit, confirmed user action.</summary>
    private static readonly HashSet<string> NeverAuto = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "explorer", "sihost", "ctfmon", "taskhostw", "runtimebroker", "startmenuexperiencehost",
        "shellexperiencehost", "searchhost", "textinputhost", "widgets", "applicationframehost", "audiodg",
        "spoolsv", "wmiprvse", "dllhost", "conhost", "securityhealthservice", "securityhealthsystray",
        "searchindexer", "sgrmbroker", "lockapp", "logonui", "userinit",
    };

    // Processes that launched us (shell, terminal, Claude...) must survive.
    private static readonly HashSet<int> Ancestors = ComputeAncestors();

    private static HashSet<int> ComputeAncestors()
    {
        var set = new HashSet<int> { Environment.ProcessId };
        int pid = Environment.ProcessId;
        for (int i = 0; i < 12; i++)
        {
            int parent = Native.ParentPid(pid);
            if (parent <= 4 || !set.Add(parent)) break;
            pid = parent;
        }
        return set;
    }

    private static string Bare(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>
    /// A protected NAME only counts when the file really lives in a system/install location (or its path is unknown).
    /// "svchost.exe" running from a Temp or Downloads folder is an impostor, not a core process.
    /// </summary>
    private static bool NameIsGenuine(string? path)
    {
        if (string.IsNullOrEmpty(path) || IsInTrustedInstallDir(path)) return true;
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);   // Defender lives in ProgramData\Microsoft
        return pd.Length > 0 && path.StartsWith(pd + "\\Microsoft\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRefused(string name, int pid = 0, string? path = null) =>
        pid == Environment.ProcessId || (Refuse.Contains(Bare(name)) && NameIsGenuine(path));

    public static bool IsNeverAuto(string name, int pid = 0, string? path = null) =>
        IsRefused(name, pid, path)
        || (NeverAuto.Contains(Bare(name)) && NameIsGenuine(path))
        || (pid != 0 && Ancestors.Contains(pid));

    public static bool IsInWindowsDir(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return path.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInTrustedInstallDir(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var f in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        })
        {
            if (f.Length > 0 && path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
    }
}
