using System.Diagnostics;

namespace Argus.Tests;

/// <summary>A scratch folder inside the user's temp folder that is removed (links first) when disposed.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "argus-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories))
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) Directory.Delete(d);   // remove the link only, never its target
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch { /* best effort */ }
    }
}

internal static class Fake
{
    /// <summary>Starts a harmless throwaway program: a renamed copy of ping.exe pinging localhost forever, with no window.</summary>
    public static Process Start(string dir, string exeName)
    {
        Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, exeName);
        File.Copy(System.IO.Path.Combine(Environment.SystemDirectory, "PING.EXE"), path, true);
        var p = Process.Start(new ProcessStartInfo(path, "-t 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        for (int i = 0; i < 50 && Native.GetProcessPath(p.Id) == null; i++) Thread.Sleep(100);    // up to 5 s for it to be visible
        Thread.Sleep(300);
        return p;
    }

    public static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(true); } catch { }
        p.Dispose();
    }

    public static void MakeOld(string path, int daysAgo)
    {
        var when = DateTime.UtcNow.AddDays(-daysAgo);
        if (Directory.Exists(path)) { Directory.SetLastWriteTimeUtc(path, when); Directory.SetCreationTimeUtc(path, when); }
        else { File.SetLastWriteTimeUtc(path, when); File.SetCreationTimeUtc(path, when); }
    }
}
