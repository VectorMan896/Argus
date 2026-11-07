using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Self-updater.
//
//  What it does:   asks GitHub for the latest release, and - only when the user says so - downloads the
//                  portable zip, checks it, swaps Argus.exe, and restarts.
//  What it never does:
//    * run without the user turning it on (off by default; "Check now" and --update are explicit)
//    * talk to anything except GitHub (api.github.com / github.com / githubusercontent.com)
//    * install without a matching SHA-256 checksum, or when the program inside is not the announced version
//    * overwrite a copy that was built from source, or one in a read-only folder
//    * delete the old version until the new one has run healthily (and it rolls back after two failed starts)
// -------------------------------------------------------------------------------------------------

internal sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
}

internal sealed record ReleaseInfo(string Version, string Tag, string Notes, string PageUrl,
                                   string ZipName, string ZipUrl, long ZipSize, string? ShaUrl);

internal readonly record struct UpdateProgress(string Step, long Done, long Total);

internal static class Updater
{
    public const string Repo = "VectorMan896/Argus";
    private const string GitHubApi = "https://api.github.com";

    /// <summary>Tests and manual end-to-end runs only: a loopback http:// address standing in for GitHub.</summary>
    internal static string? TestApiBase;
    internal static long MaxZipBytes = 250L * 1024 * 1024;
    private const long MaxJsonBytes = 1024 * 1024;
    private const long MaxExtractedBytes = 400L * 1024 * 1024;

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Argus/" + AppInfo.Version);   // the only thing about this PC that is sent
        return c;
    }

    /// <summary>GitHub, unless a loopback test address was given (never honoured for anything but 127.0.0.1 / localhost over http).</summary>
    public static string ApiBase
    {
        get
        {
            string? o = TestApiBase ?? Environment.GetEnvironmentVariable("ARGUS_UPDATE_URL");
            if (!string.IsNullOrWhiteSpace(o) && Uri.TryCreate(o, UriKind.Absolute, out var u) && u.IsLoopback && u.Scheme == Uri.UriSchemeHttp)
                return o.TrimEnd('/');
            return GitHubApi;
        }
    }

    private static bool LocalMode => ApiBase != GitHubApi;

    public static string UpdatesDir => Path.Combine(Config.Dir, "updates");

    /// <summary>Only GitHub over https (or the local test server in test mode).</summary>
    public static bool IsTrustedUrl(Uri u)
    {
        if (LocalMode) return u.IsLoopback;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        string h = u.Host.ToLowerInvariant();
        return h == "github.com" || h == "api.github.com" || h.EndsWith(".github.com") || h.EndsWith(".githubusercontent.com");
    }

    // ---- versions ---------------------------------------------------------------------------------------

    private static readonly Regex SemVerRx = new(
        @"^v?(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled);

    public static bool TryParseVersion(string? s, out int major, out int minor, out int patch, out string pre)
    {
        major = minor = patch = 0; pre = "";
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = SemVerRx.Match(s.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["maj"].Value, out major) || !int.TryParse(m.Groups["min"].Value, out minor) || !int.TryParse(m.Groups["pat"].Value, out patch))
            return false;
        pre = m.Groups["pre"].Value;
        return true;
    }

    /// <summary>Semantic-versioning order: negative if a is older than b. A pre-release is older than its release.</summary>
    public static int Compare(string a, string b)
    {
        TryParseVersion(a, out var a1, out var a2, out var a3, out var ap);
        TryParseVersion(b, out var b1, out var b2, out var b3, out var bp);
        int c = a1.CompareTo(b1); if (c != 0) return c;
        c = a2.CompareTo(b2); if (c != 0) return c;
        c = a3.CompareTo(b3); if (c != 0) return c;
        if (ap == bp) return 0;
        if (ap.Length == 0) return 1;
        if (bp.Length == 0) return -1;
        var x = ap.Split('.'); var y = bp.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xn = int.TryParse(x[i], out int xi), yn = int.TryParse(y[i], out int yi);
            int r = xn && yn ? xi.CompareTo(yi) : xn ? -1 : yn ? 1 : string.CompareOrdinal(x[i], y[i]);
            if (r != 0) return r;
        }
        return x.Length.CompareTo(y.Length);
    }

    public static bool IsNewer(string current, string candidate) =>
        TryParseVersion(current, out _, out _, out _, out _) && TryParseVersion(candidate, out _, out _, out _, out _)
        && Compare(current, candidate) < 0;

    // ---- asking GitHub ---------------------------------------------------------------------------------------

    private static readonly Regex ZipNameRx = new(@"^Argus-(?<v>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)-win-x64\.zip$", RegexOptions.Compiled);

    /// <summary>Reads the JSON GitHub returns for "latest release". Null (with a reason) if it is not something we would install.</summary>
    public static ReleaseInfo? ParseRelease(string json, out string? problem)
    {
        problem = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) { problem = "the latest release is a draft"; return null; }
            if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) { problem = "the latest release is a pre-release"; return null; }

            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out _, out _, out _, out _)) { problem = $"the release tag \"{tag}\" is not a version number"; return null; }
            string version = tag.TrimStart('v', 'V');
            string notes = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            string page = r.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? "" : "";

            string? zipName = null, zipUrl = null, shaUrl = null; long size = 0;
            var assets = r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : new List<JsonElement>();
            foreach (var asset in assets)
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                var m = ZipNameRx.Match(name);
                if (m.Success && m.Groups["v"].Value == version)
                {
                    zipName = name; zipUrl = asset.GetProperty("browser_download_url").GetString();
                    size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long sz) ? sz : 0;
                }
            }
            if (zipName == null || zipUrl == null) { problem = "the release has no portable Argus zip attached"; return null; }
            foreach (var asset in assets)
                if ((asset.GetProperty("name").GetString() ?? "") == zipName + ".sha256") shaUrl = asset.GetProperty("browser_download_url").GetString();

            if (!Uri.TryCreate(zipUrl, UriKind.Absolute, out var zu) || !IsTrustedUrl(zu)) { problem = "the release files are not hosted by GitHub"; return null; }
            if (shaUrl != null && (!Uri.TryCreate(shaUrl, UriKind.Absolute, out var su) || !IsTrustedUrl(su))) shaUrl = null;
            return new ReleaseInfo(version, tag, notes, page, zipName, zipUrl, size, shaUrl);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            problem = "GitHub's answer could not be understood";
            return null;
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage resp, long limit, CancellationToken ct)
    {
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[8192]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > limit) throw new UpdateException("GitHub's answer was unexpectedly large.");
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>One request to GitHub's "latest release" address. Never throws: problems come back as text.</summary>
    public static async Task<(ReleaseInfo? Release, string? Problem)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/repos/{Repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return (null, "no release has been published yet");
            if (resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429) return (null, "GitHub is limiting requests right now, try again later");
            if (!resp.IsSuccessStatusCode) return (null, $"GitHub answered {(int)resp.StatusCode}");
            if (!IsTrustedUrl(resp.RequestMessage!.RequestUri!)) return (null, "the request was redirected somewhere untrusted");
            string json = await ReadLimitedAsync(resp, MaxJsonBytes, ct);
            var rel = ParseRelease(json, out var problem);
            return (rel, problem);
        }
        catch (UpdateException ex) { return (null, ex.Message); }
        catch (OperationCanceledException) { return (null, "the check was cancelled or timed out"); }
        catch (HttpRequestException ex) { return (null, "could not reach GitHub (" + ex.Message + ")"); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    // ---- can this copy replace itself? ------------------------------------------------------------------------------

    /// <summary>Only the portable single-file Argus.exe in a writable folder. Never a build made from source.</summary>
    public static bool CanSelfUpdate(out string reason)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !Path.GetFileName(exe).Equals("Argus.exe", StringComparison.OrdinalIgnoreCase))
        { reason = "this is not the portable Argus.exe"; return false; }
        if (!string.IsNullOrEmpty(typeof(Updater).Assembly.Location))
        { reason = "this copy was built from source (update it with git pull and a rebuild)"; return false; }
        try
        {
            string probe = Path.Combine(Path.GetDirectoryName(exe)!, ".argus-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, ""); File.Delete(probe);
        }
        catch { reason = "the folder Argus runs from is read-only (move it somewhere you can write to)"; return false; }
        reason = "";
        return true;
    }

    // ---- download and verify -------------------------------------------------------------------------------------------

    /// <summary>The expected SHA-256 from a "hash  filename" checksum file.</summary>
    public static string ParseSha(string text, string zipName)
    {
        string line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Regex.IsMatch(parts[0], "^[0-9a-fA-F]{64}$")) throw new UpdateException("The release's checksum file is not in the expected format.");
        if (parts.Length > 1 && !parts[1].TrimStart('*').Equals(zipName, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("The release's checksum file is for a different file.");
        return parts[0].ToLowerInvariant();
    }

    public static bool VerifySha256(string file, string expectedHex)
    {
        using var s = File.OpenRead(file);
        string actual = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        return actual.Equals(expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !IsTrustedUrl(u)) throw new UpdateException("An update address is not a trusted GitHub address.");
        using var resp = await Http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (!IsTrustedUrl(resp.RequestMessage!.RequestUri!)) throw new UpdateException("A download was redirected to an untrusted address.");
        return await ReadLimitedAsync(resp, 4096, ct);
    }

    /// <summary>Downloads the zip next to its checksum, hashing as it streams. A wrong checksum deletes the file and throws.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo r, IProgress<UpdateProgress>? progress = null, CancellationToken ct = default)
    {
        if (r.ShaUrl == null) throw new UpdateException("This release has no checksum file, so Argus will not install it.");
        if (!Uri.TryCreate(r.ZipUrl, UriKind.Absolute, out var zipUri) || !IsTrustedUrl(zipUri)) throw new UpdateException("The download address is not a trusted GitHub address.");

        string dir = Path.Combine(UpdatesDir, r.Version);
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, r.ZipName), part = zip + ".part";

        progress?.Report(new UpdateProgress("Checking the checksum", 0, 0));
        string expected = ParseSha(await GetTextAsync(r.ShaUrl, ct), r.ZipName);

        using var resp = await Http.GetAsync(zipUri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (!IsTrustedUrl(resp.RequestMessage!.RequestUri!)) throw new UpdateException("The download was redirected to an untrusted address.");
        long total = resp.Content.Headers.ContentLength ?? r.ZipSize;
        if (total > MaxZipBytes) throw new UpdateException("The download is larger than Argus allows, so it was not downloaded.");
        if (r.ZipSize > 0 && total > 0 && total != r.ZipSize) throw new UpdateException("The download's size does not match the release.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(part))
            {
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    done += n;
                    if (done > MaxZipBytes) throw new UpdateException("The download is larger than Argus allows, so it was stopped.");
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    hash.AppendData(buf, 0, n);
                    progress?.Report(new UpdateProgress("Downloading", done, total));
                }
            }
        }
        catch { TryDelete(part); throw; }

        string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(part);
            throw new UpdateException("The download's checksum does not match the one the release published, so it was discarded and nothing was installed.");
        }
        File.Move(part, zip, true);
        return zip;
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    // ---- unpack and check ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Unpacks only Argus.exe and plain documents from the zip's top level, rejects any path that tries to leave the folder,
    /// then requires the exe to say it is the announced version. Returns the path of the staged Argus.exe.
    /// </summary>
    public static string ExtractAndValidate(string zipPath, string expectedVersion, string destDir)
    {
        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        Directory.CreateDirectory(destDir);
        string root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        string? exe = null; long total = 0;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var e in zip.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(destDir, e.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UpdateException("The update contains a file path that tries to leave its folder, so it was rejected.");
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
                total += e.Length;
                if (total > MaxExtractedBytes) throw new UpdateException("The update unpacks to an unexpectedly large size, so it was rejected.");
                if (e.FullName.Contains('/') || e.FullName.Contains('\\')) continue;                        // top level only
                string name = e.Name;
                bool wanted = name.Equals("Argus.exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name == "LICENSE";
                if (!wanted) continue;
                e.ExtractToFile(target, true);
                if (name.Equals("Argus.exe", StringComparison.OrdinalIgnoreCase)) exe = target;
            }
        }
        if (exe == null) throw new UpdateException("The update does not contain Argus.exe.");

        string? v = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
        if (v != null && v.Contains('+')) v = v[..v.IndexOf('+')];
        if (v != expectedVersion) throw new UpdateException($"The program inside the update says it is version {v ?? "unknown"}, not {expectedVersion}, so it was rejected.");
        return exe;
    }

    // ---- swap, rollback, restart ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Windows lets a running program be renamed but not overwritten: move it aside as Argus.exe.old, put the new one in its place.
    /// If the second step fails the first is undone. Returns the path of the kept old version.
    /// </summary>
    public static string Swap(string currentExe, string newExe)
    {
        string old = currentExe + ".old";
        try { if (File.Exists(old)) File.Delete(old); } catch { /* leftover still in use: Move below reports it */ }
        File.Move(currentExe, old, overwrite: true);
        try { File.Move(newExe, currentExe); }
        catch
        {
            File.Move(old, currentExe, overwrite: true);
            throw new UpdateException("Could not put the new version in place. Nothing was changed.");
        }
        return old;
    }

    /// <summary>Puts the previous version back. The failing program may be the one running, so it is renamed aside, not overwritten.</summary>
    public static void Rollback(string currentExe)
    {
        string old = currentExe + ".old", failed = currentExe + ".failed";
        if (!File.Exists(old)) throw new UpdateException("There is no previous version to go back to.");
        try { if (File.Exists(failed)) File.Delete(failed); } catch { }
        if (File.Exists(currentExe)) File.Move(currentExe, failed, overwrite: true);
        File.Move(old, currentExe, overwrite: true);
    }

    /// <summary>Download, verify, unpack, validate and swap. Does not restart. Returns the kept old version's path.</summary>
    public static async Task<string> InstallAsync(ReleaseInfo r, string currentExe, IProgress<UpdateProgress>? progress = null, CancellationToken ct = default)
    {
        string zip = await DownloadAsync(r, progress, ct);
        progress?.Report(new UpdateProgress("Checking the update", 0, 0));
        string staged = ExtractAndValidate(zip, r.Version, Path.Combine(UpdatesDir, r.Version, "files"));

        string next = Path.Combine(Path.GetDirectoryName(currentExe)!, "Argus.exe.new");
        try { File.Copy(staged, next, true); }
        catch (Exception ex) { throw new UpdateException("Could not write next to the running program: " + ex.Message); }

        progress?.Report(new UpdateProgress("Installing", 0, 0));
        PrimeProcessStart();                                            // load what the restart needs while Argus.exe is still untouched
        UpdateRecovery.MarkPending(currentExe, AppInfo.Version, r.Version);   // the safety marker goes down before the swap, not after
        string backup;
        try { backup = Swap(currentExe, next); }
        catch { UpdateRecovery.Clear(); throw; }
        try { Directory.Delete(Path.Combine(UpdatesDir, r.Version), true); } catch { }      // the downloaded copy is no longer needed
        return backup;
    }

    /// <summary>
    /// A running single-file program loads parts of itself the first time they are used, and that fails once Argus.exe has been
    /// replaced underneath it (found by testing: "Could not load System.Diagnostics.Process" when restarting). So start something
    /// harmless once, before the swap, to have everything the restart needs already in memory.
    /// </summary>
    private static void PrimeProcessStart()
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { /* priming is best effort */ }
    }

    /// <summary>Starts the new version, telling it which process to wait for (the old one is about to exit).</summary>
    public static void Relaunch(string exe)
    {
        Process.Start(new ProcessStartInfo(exe, $"--updated-from {Environment.ProcessId}")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        });
    }
}

/// <summary>
/// Safety net after an update. The installer leaves a marker; the new version deletes it (and the old exe) after ten healthy
/// seconds or a clean exit. If the marker is still there on the second start, the new version never got going: go back.
/// </summary>
internal static class UpdateRecovery
{
    private sealed class Marker
    {
        public string Exe { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public int Starts { get; set; }
    }

    private static string MarkerPath => Path.Combine(Config.Dir, "update-pending.json");

    public static bool Pending => File.Exists(MarkerPath);

    private static Marker? Read()
    {
        try { return File.Exists(MarkerPath) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerPath)) : null; }
        catch { return null; }
    }

    private static void Write(Marker m)
    {
        Directory.CreateDirectory(Config.Dir);
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(m));
    }

    public static void MarkPending(string exe, string from, string to) => Write(new Marker { Exe = exe, From = from, To = to });

    /// <summary>Called when the window app starts. True means it rolled back and the caller should exit.</summary>
    public static bool OnStart(bool relaunch = true)
    {
        var m = Read();
        if (m == null) return false;
        m.Starts++;
        Write(m);
        if (m.Starts < 2) return false;

        try { Updater.Rollback(m.Exe); }
        catch (Exception ex) { Log.Write("Could not roll back the failed update: " + ex.Message); return false; }
        TryDelete(MarkerPath);
        Log.Write($"Version {m.To} did not start properly twice, so Argus went back to {m.From}.");
        if (relaunch)
        {
            try { Process.Start(new ProcessStartInfo(m.Exe, $"--updated-from {Environment.ProcessId}") { UseShellExecute = false }); } catch { }
        }
        return true;
    }

    /// <summary>The new version is working: stop watching and drop the old one.</summary>
    public static void Confirm()
    {
        var m = Read();
        if (m == null) return;
        TryDelete(MarkerPath);
        TryDelete(m.Exe + ".old");
        TryDelete(m.Exe + ".failed");
        Log.Write($"Updated from {m.From} to {m.To}.");
    }

    /// <summary>Forget the pending update without touching any files (used when the new version could not even be started).</summary>
    public static void Clear() => TryDelete(MarkerPath);

    public static void WaitForExit(int pid, int seconds)
    {
        try { using var p = Process.GetProcessById(pid); p.WaitForExit(seconds * 1000); } catch { /* already gone */ }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
}
