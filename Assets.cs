namespace Argus;

/// <summary>Logo pieces embedded in the exe (see Argus.csproj). Source artwork and the generator live in assets/ and tools/.</summary>
internal static class Assets
{
    private static Stream Open(string name) =>
        typeof(Assets).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException("Missing embedded asset " + name);

    public static Bitmap Load(string name)
    {
        using var s = Open(name);
        using var raw = new Bitmap(s);
        return new Bitmap(raw);                      // detach from the stream
    }

    public static Icon AppIcon()
    {
        using var s = Open("argus.ico");
        return new Icon(s);
    }
}

/// <summary>What Argus has done since it started (shown on the Overview page).</summary>
internal static class Stats
{
    private static int _closed, _slowed, _fixed;
    public static readonly DateTime Since = DateTime.Now;

    public static int Closed => _closed;
    public static int Slowed => _slowed;
    public static int Fixed => _fixed;

    public static void Record(Finding f, string result)
    {
        if (result.StartsWith("slowed down")) Interlocked.Increment(ref _slowed);
        else if (result is "closed gracefully" or "killed") Interlocked.Increment(ref _closed);
        else if (result is "made click-through" or "hidden") Interlocked.Increment(ref _fixed);
    }
}
