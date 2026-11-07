using System.Reflection;

namespace Argus;

internal static class AppInfo
{
    /// <summary>
    /// Optional suffix for the single-instance lock names. Set the ARGUS_INSTANCE environment variable to run a second,
    /// fully separate copy side by side (used for testing); normally empty.
    /// </summary>
    public static string Instance =>
        Environment.GetEnvironmentVariable("ARGUS_INSTANCE") is { Length: > 0 } s ? "." + s : "";

    /// <summary>Semantic version from Argus.csproj (the single place the version is set).</summary>
    public static string Version
    {
        get
        {
            var v = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            int plus = v.IndexOf('+');                     // strip any build metadata
            return plus > 0 ? v[..plus] : v;
        }
    }
}
