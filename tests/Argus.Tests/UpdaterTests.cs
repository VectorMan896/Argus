using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Argus.Tests;

public sealed class VersionTests
{
    [Theory]
    [InlineData("0.3.0", "0.3.1", true)]
    [InlineData("0.3.0", "0.4.0", true)]
    [InlineData("0.3.0", "1.0.0", true)]
    [InlineData("0.9.0", "0.10.0", true)]                  // numbers, not text
    [InlineData("0.3.0", "0.3.0", false)]
    [InlineData("0.3.1", "0.3.0", false)]
    [InlineData("1.0.0-beta.1", "1.0.0", true)]            // a pre-release is older than its release
    [InlineData("1.0.0", "1.0.0-beta.2", false)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", true)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", true)]
    [InlineData("v0.3.0", "v0.4.0", true)]                 // tags carry a leading v
    [InlineData("0.3.0+abc123", "0.3.1", true)]            // build metadata is ignored
    public void Ordering(string current, string candidate, bool newer) => Assert.Equal(newer, Updater.IsNewer(current, candidate));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("v")]
    public void Garbage_is_never_newer_and_never_throws(string junk)
    {
        Assert.False(Updater.IsNewer("0.3.0", junk));
        Assert.False(Updater.IsNewer(junk, "0.3.0"));
    }
}

/// <summary>Everything that touches the updater's static test hook or the settings folder runs one at a time.</summary>
[Collection("Config")]
public sealed class UpdaterTests : IDisposable
{
    private readonly TempDir _dir = new();

    public UpdaterTests() { Updater.TestApiBase = null; Config.Init(_dir.Path); }
    public void Dispose() { Updater.TestApiBase = null; Updater.MaxZipBytes = 250L * 1024 * 1024; _dir.Dispose(); }

    private const string Good = "https://github.com/VectorMan896/Argus/releases/download/v0.4.0/";

    private static string Json(string tag = "v0.4.0", string zip = "Argus-0.4.0-win-x64.zip", string? zipUrl = null, bool sha = true,
                               bool prerelease = false, bool draft = false, string body = "First line\\nSecond line")
    {
        string url = zipUrl ?? Good + zip;
        string shaAsset = sha ? $",{{\"name\":\"{zip}.sha256\",\"browser_download_url\":\"{url}.sha256\",\"size\":90}}" : "";
        return $"{{\"tag_name\":\"{tag}\",\"draft\":{draft.ToString().ToLower()},\"prerelease\":{prerelease.ToString().ToLower()}," +
               $"\"body\":\"{body}\",\"html_url\":\"https://github.com/VectorMan896/Argus/releases/tag/{tag}\"," +
               $"\"assets\":[{{\"name\":\"{zip}\",\"browser_download_url\":\"{url}\",\"size\":1234}}{shaAsset}]}}";
    }

    // ---- reading GitHub's answer ----------------------------------------------------------------------

    [Fact]
    public void A_normal_release_is_understood()
    {
        var r = Updater.ParseRelease(Json(), out var problem);
        Assert.Null(problem);
        Assert.NotNull(r);
        Assert.Equal("0.4.0", r!.Version);
        Assert.Equal("v0.4.0", r.Tag);
        Assert.Equal("Argus-0.4.0-win-x64.zip", r.ZipName);
        Assert.EndsWith(".zip.sha256", r.ShaUrl);
        Assert.Contains("Second line", r.Notes);
        Assert.Equal(1234, r.ZipSize);
    }

    [Fact]
    public void Drafts_and_prereleases_are_not_offered()
    {
        Assert.Null(Updater.ParseRelease(Json(draft: true), out var p1)); Assert.Contains("draft", p1);
        Assert.Null(Updater.ParseRelease(Json(prerelease: true), out var p2)); Assert.Contains("pre-release", p2);
    }

    [Fact]
    public void Files_not_hosted_by_github_are_refused()
    {
        Assert.Null(Updater.ParseRelease(Json(zipUrl: "https://evil.example.com/Argus-0.4.0-win-x64.zip"), out var p1));
        Assert.Contains("not hosted by GitHub", p1);
        Assert.Null(Updater.ParseRelease(Json(zipUrl: "http://github.com/x/Argus-0.4.0-win-x64.zip"), out var p2));   // plain http
        Assert.Contains("not hosted by GitHub", p2);
    }

    [Fact]
    public void A_zip_whose_version_does_not_match_the_tag_is_ignored()
    {
        Assert.Null(Updater.ParseRelease(Json(tag: "v0.4.0", zip: "Argus-0.5.0-win-x64.zip"), out var p));
        Assert.Contains("no portable Argus zip", p);
    }

    [Fact]
    public void A_release_without_a_checksum_can_be_read_but_has_no_checksum_address()
    {
        var r = Updater.ParseRelease(Json(sha: false), out _);
        Assert.NotNull(r);
        Assert.Null(r!.ShaUrl);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"tag_name\": 5}")]
    public void Junk_never_throws(string junk)
    {
        Assert.Null(Updater.ParseRelease(junk, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Only_a_loopback_http_address_can_replace_github_for_testing()
    {
        Assert.Equal("https://api.github.com", Updater.ApiBase);
        Updater.TestApiBase = "https://evil.example.com"; Assert.Equal("https://api.github.com", Updater.ApiBase);
        Updater.TestApiBase = "http://example.com"; Assert.Equal("https://api.github.com", Updater.ApiBase);
        Updater.TestApiBase = "http://127.0.0.1:5555/"; Assert.Equal("http://127.0.0.1:5555", Updater.ApiBase);
    }

    [Fact]
    public void This_test_build_is_a_source_build_and_never_updates_itself()
    {
        Assert.False(Updater.CanSelfUpdate(out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    // ---- where Argus is allowed to replace itself --------------------------------------------------------

    [Fact]
    public void Temporary_folders_are_recognised()
    {
        Assert.True(Updater.IsTemporaryLocation(Path.Combine(Path.GetTempPath(), "Temp1_Argus-0.4.0-win-x64.zip", "Argus.exe")));   // straight out of the zip window
        Assert.True(Updater.IsTemporaryLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "Argus", "Argus.exe")));
        Assert.False(Updater.IsTemporaryLocation(@"C:\Users\someone\Apps\Argus\Argus.exe"));
        Assert.False(Updater.IsTemporaryLocation(@"C:\Program Files\Argus\Argus.exe"));
        Assert.False(Updater.IsTemporaryLocation(null));
        Assert.False(Updater.IsTemporaryLocation(""));
    }

    [Fact]
    public void A_copy_in_a_temporary_folder_is_told_to_move_and_is_never_updated()
    {
        string exe = Path.Combine(Path.GetTempPath(), "Temp1_Argus-0.4.0-win-x64.zip", "Argus.exe");
        Assert.False(Updater.CanSelfUpdate(exe, builtFromSource: false, out var reason));
        Assert.Contains("temporary folder", reason);
        Assert.Contains("Extract it to a folder you keep", reason);
    }

    [Fact]
    public void Other_copies_that_must_not_update_are_refused_with_a_reason()
    {
        Assert.False(Updater.CanSelfUpdate(@"C:\Tools\Argus\notepad.exe", false, out var r1)); Assert.Contains("not the portable", r1);
        Assert.False(Updater.CanSelfUpdate(null, false, out var r2)); Assert.Contains("not the portable", r2);
        Assert.False(Updater.CanSelfUpdate(Path.Combine(AppContext.BaseDirectory, "Argus.exe"), builtFromSource: true, out var r3)); Assert.Contains("built from source", r3);
    }

    [Fact]
    public void A_portable_copy_in_a_normal_writable_folder_may_update_itself()
    {
        // the test output folder is writable and is not a temporary folder
        Assert.True(Updater.CanSelfUpdate(Path.Combine(AppContext.BaseDirectory, "Argus.exe"), builtFromSource: false, out var reason), reason);
        Assert.Equal("", reason);
    }

    // ---- checksums ------------------------------------------------------------------------------------

    [Fact]
    public void Checksum_files_are_read_strictly()
    {
        string hash = new string('a', 64);
        Assert.Equal(hash, Updater.ParseSha(hash + "  Argus-0.4.0-win-x64.zip\n", "Argus-0.4.0-win-x64.zip"));
        Assert.Equal(hash, Updater.ParseSha(hash.ToUpper(), "Argus-0.4.0-win-x64.zip"));
        Assert.Throws<UpdateException>(() => Updater.ParseSha("short  x.zip", "x.zip"));
        Assert.Throws<UpdateException>(() => Updater.ParseSha("", "x.zip"));
        Assert.Throws<UpdateException>(() => Updater.ParseSha(hash + "  other-file.zip", "x.zip"));     // checksum for a different file
    }

    [Fact]
    public void A_file_is_checked_against_its_sha256()
    {
        string f = _dir.Combine("data.bin");
        File.WriteAllText(f, "hello");
        string good = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello"))).ToLowerInvariant();
        Assert.True(Updater.VerifySha256(f, good));
        Assert.True(Updater.VerifySha256(f, good.ToUpperInvariant()));
        Assert.False(Updater.VerifySha256(f, new string('0', 64)));
    }

    // ---- unpacking ------------------------------------------------------------------------------------

    /// <summary>A real file that carries this build's version resource, standing in for "Argus.exe".</summary>
    private static byte[] RealProgramBytes() => File.ReadAllBytes(typeof(Updater).Assembly.Location);

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, data) in entries)
            {
                var e = z.CreateEntry(name);
                using var s = e.Open(); s.Write(data);
            }
        return ms.ToArray();
    }

    private string WriteZip(params (string Name, byte[] Data)[] entries)
    {
        string path = _dir.Combine("update-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, Zip(entries));
        return path;
    }

    [Fact]
    public void A_good_zip_unpacks_and_its_version_is_checked()
    {
        string zip = WriteZip(("Argus.exe", RealProgramBytes()), ("README.md", Encoding.UTF8.GetBytes("hi")));
        string exe = Updater.ExtractAndValidate(zip, AppInfo.Version, _dir.Combine("staged"));
        Assert.True(File.Exists(exe));
        Assert.True(File.Exists(_dir.Combine("staged", "README.md")));
    }

    [Fact]
    public void A_program_that_claims_a_different_version_is_rejected()
    {
        string zip = WriteZip(("Argus.exe", RealProgramBytes()));
        var ex = Assert.Throws<UpdateException>(() => Updater.ExtractAndValidate(zip, "9.9.9", _dir.Combine("staged")));
        Assert.Contains("says it is version", ex.Message);
    }

    [Fact]
    public void A_zip_with_a_path_that_escapes_its_folder_is_rejected_and_writes_nothing()
    {
        string zip = WriteZip(("Argus.exe", RealProgramBytes()), ("..\\escaped.txt", Encoding.UTF8.GetBytes("x")));
        Assert.Throws<UpdateException>(() => Updater.ExtractAndValidate(zip, AppInfo.Version, _dir.Combine("staged")));
        Assert.False(File.Exists(_dir.Combine("escaped.txt")));
        Assert.False(File.Exists(Path.Combine(_dir.Path, "..", "escaped.txt")));
    }

    [Fact]
    public void A_zip_without_the_program_is_rejected()
    {
        string zip = WriteZip(("README.md", Encoding.UTF8.GetBytes("hi")));
        Assert.Throws<UpdateException>(() => Updater.ExtractAndValidate(zip, AppInfo.Version, _dir.Combine("staged")));
    }

    [Fact]
    public void Only_the_program_and_plain_documents_are_ever_unpacked()
    {
        string zip = WriteZip(("Argus.exe", RealProgramBytes()), ("payload.dll", new byte[] { 1, 2, 3 }), ("sub/other.exe", new byte[] { 4 }), ("LICENSE", new byte[] { 5 }));
        Updater.ExtractAndValidate(zip, AppInfo.Version, _dir.Combine("staged"));
        Assert.False(File.Exists(_dir.Combine("staged", "payload.dll")));
        Assert.False(Directory.Exists(_dir.Combine("staged", "sub")));
        Assert.True(File.Exists(_dir.Combine("staged", "LICENSE")));
    }

    // ---- swapping and rolling back -------------------------------------------------------------------------------

    [Fact]
    public void Swap_puts_the_new_program_in_place_and_keeps_the_old_one()
    {
        string cur = _dir.Combine("Argus.exe"), next = _dir.Combine("Argus.exe.new");
        File.WriteAllText(cur, "OLD"); File.WriteAllText(next, "NEW");

        string backup = Updater.Swap(cur, next);

        Assert.Equal("NEW", File.ReadAllText(cur));
        Assert.Equal("OLD", File.ReadAllText(backup));
        Assert.Equal(cur + ".old", backup);
        Assert.False(File.Exists(next));
    }

    [Fact]
    public void A_failed_swap_leaves_the_current_program_exactly_as_it_was()
    {
        string cur = _dir.Combine("Argus.exe");
        File.WriteAllText(cur, "OLD");

        Assert.Throws<UpdateException>(() => Updater.Swap(cur, _dir.Combine("does-not-exist.exe")));

        Assert.Equal("OLD", File.ReadAllText(cur));
        Assert.False(File.Exists(cur + ".old"));
    }

    [Fact]
    public void Rollback_restores_the_old_program_and_sets_the_failed_one_aside()
    {
        string cur = _dir.Combine("Argus.exe");
        File.WriteAllText(cur, "NEW"); File.WriteAllText(cur + ".old", "OLD");

        Updater.Rollback(cur);

        Assert.Equal("OLD", File.ReadAllText(cur));
        Assert.Equal("NEW", File.ReadAllText(cur + ".failed"));
        Assert.False(File.Exists(cur + ".old"));
    }

    [Fact]
    public void Rollback_without_a_previous_version_says_so()
    {
        string cur = _dir.Combine("Argus.exe");
        File.WriteAllText(cur, "NEW");
        Assert.Throws<UpdateException>(() => Updater.Rollback(cur));
        Assert.Equal("NEW", File.ReadAllText(cur));
    }

    // ---- the safety net after an update -------------------------------------------------------------------------------

    [Fact]
    public void A_new_version_that_never_confirms_is_rolled_back_on_its_second_start()
    {
        string cur = _dir.Combine("Argus.exe");
        File.WriteAllText(cur, "NEW"); File.WriteAllText(cur + ".old", "OLD");
        UpdateRecovery.MarkPending(cur, "0.3.0", "0.4.0");

        Assert.False(UpdateRecovery.OnStart(relaunch: false));       // first start: give it a chance
        Assert.Equal("NEW", File.ReadAllText(cur));
        Assert.True(UpdateRecovery.OnStart(relaunch: false));        // second start without confirming: go back
        Assert.Equal("OLD", File.ReadAllText(cur));
        Assert.False(UpdateRecovery.Pending);
    }

    [Fact]
    public void A_confirmed_update_drops_the_old_version_and_the_marker()
    {
        string cur = _dir.Combine("Argus.exe");
        File.WriteAllText(cur, "NEW"); File.WriteAllText(cur + ".old", "OLD");
        UpdateRecovery.MarkPending(cur, "0.3.0", "0.4.0");
        Assert.False(UpdateRecovery.OnStart(relaunch: false));

        UpdateRecovery.Confirm();

        Assert.False(UpdateRecovery.Pending);
        Assert.False(File.Exists(cur + ".old"));
        Assert.Equal("NEW", File.ReadAllText(cur));
        Assert.False(UpdateRecovery.OnStart(relaunch: false));       // nothing left to roll back
    }

    [Fact]
    public void Without_an_update_in_progress_nothing_happens()
    {
        Assert.False(UpdateRecovery.Pending);
        Assert.False(UpdateRecovery.OnStart(relaunch: false));
    }
}

/// <summary>A pretend GitHub on a loopback port, so the whole download path can be tested without the internet.</summary>
internal sealed class FakeGitHub : IDisposable
{
    private readonly HttpListener _l = new();
    public string Base { get; }
    public Dictionary<string, (string Type, byte[] Body)> Routes { get; } = new();

    public FakeGitHub()
    {
        var t = new TcpListener(IPAddress.Loopback, 0); t.Start();
        int port = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop();
        Base = $"http://127.0.0.1:{port}";
        _l.Prefixes.Add(Base + "/");
        _l.Start();
        Task.Run(Loop);
    }

    public void Add(string path, string type, byte[] body) => Routes[path] = (type, body);

    private async Task Loop()
    {
        while (_l.IsListening)
        {
            HttpListenerContext c;
            try { c = await _l.GetContextAsync(); } catch { return; }
            try
            {
                if (Routes.TryGetValue(c.Request.Url!.AbsolutePath, out var r))
                {
                    c.Response.ContentType = r.Type; c.Response.ContentLength64 = r.Body.Length;
                    await c.Response.OutputStream.WriteAsync(r.Body);
                }
                else c.Response.StatusCode = 404;
            }
            catch { }
            finally { try { c.Response.Close(); } catch { } }
        }
    }

    public void Dispose() { try { _l.Stop(); _l.Close(); } catch { } }
}

[Collection("Config")]
public sealed class UpdaterNetworkTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeGitHub _gh = new();

    public UpdaterNetworkTests() { Config.Init(_dir.Path); Updater.TestApiBase = _gh.Base; }

    public void Dispose()
    {
        Updater.TestApiBase = null; Updater.MaxZipBytes = 250L * 1024 * 1024;
        _gh.Dispose(); _dir.Dispose();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static byte[] ZipOf(byte[] program)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var s = z.CreateEntry("Argus.exe").Open()) s.Write(program);
            using (var s = z.CreateEntry("README.md").Open()) s.Write(Encoding.UTF8.GetBytes("readme"));
        }
        return ms.ToArray();
    }

    /// <summary>Serves a release for <paramref name="version"/> whose zip contains <paramref name="program"/>.</summary>
    private ReleaseInfo Publish(string version, byte[] program, string? shaOverride = null, bool includeSha = true)
    {
        string name = $"Argus-{version}-win-x64.zip";
        byte[] zip = ZipOf(program);
        _gh.Add($"/dl/{name}", "application/zip", zip);
        _gh.Add($"/dl/{name}.sha256", "text/plain", Encoding.ASCII.GetBytes($"{shaOverride ?? Sha(zip)}  {name}\n"));
        string shaAsset = includeSha ? $",{{\"name\":\"{name}.sha256\",\"browser_download_url\":\"{_gh.Base}/dl/{name}.sha256\",\"size\":90}}" : "";
        string json = $"{{\"tag_name\":\"v{version}\",\"draft\":false,\"prerelease\":false,\"body\":\"Fixes things\\nAdds things\",\"html_url\":\"{_gh.Base}/rel\"," +
                      $"\"assets\":[{{\"name\":\"{name}\",\"browser_download_url\":\"{_gh.Base}/dl/{name}\",\"size\":{zip.Length}}}{shaAsset}]}}";
        _gh.Add($"/repos/{Updater.Repo}/releases/latest", "application/json", Encoding.UTF8.GetBytes(json));
        var rel = Updater.ParseRelease(json, out var problem);
        Assert.NotNull(rel);
        Assert.Null(problem);
        return rel!;
    }

    private static byte[] RealProgram() => File.ReadAllBytes(typeof(Updater).Assembly.Location);

    [Fact]
    public async Task Check_finds_the_latest_release()
    {
        Publish("9.9.9", RealProgram());
        var (rel, problem) = await Updater.CheckAsync();
        Assert.Null(problem);
        Assert.Equal("9.9.9", rel!.Version);
        Assert.True(Updater.IsNewer(AppInfo.Version, rel.Version));
        Assert.Contains("Adds things", rel.Notes);
    }

    [Fact]
    public async Task Check_reports_a_missing_release_in_plain_words()
    {
        var (rel, problem) = await Updater.CheckAsync();         // nothing published on the fake server
        Assert.Null(rel);
        Assert.Contains("no release has been published", problem);
    }

    [Fact]
    public async Task Check_reports_an_unreachable_server_without_throwing()
    {
        Updater.TestApiBase = "http://127.0.0.1:1";              // nothing listens there
        var (rel, problem) = await Updater.CheckAsync();
        Assert.Null(rel);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public async Task A_good_download_is_saved_and_reports_progress()
    {
        var rel = Publish("9.9.9", RealProgram());
        var seen = new List<UpdateProgress>();
        string zip = await Updater.DownloadAsync(rel, new SyncProgress(seen.Add));
        Assert.True(File.Exists(zip));
        Assert.Contains(seen, p => p.Step == "Downloading" && p.Done > 0);
        Assert.False(File.Exists(zip + ".part"));
    }

    [Fact]
    public async Task A_download_with_the_wrong_checksum_is_discarded()
    {
        var rel = Publish("9.9.9", RealProgram(), shaOverride: new string('0', 64));
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Updater.DownloadAsync(rel));
        Assert.Contains("checksum does not match", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(Updater.UpdatesDir, "*", SearchOption.AllDirectories));      // nothing left behind
    }

    [Fact]
    public async Task A_release_without_a_checksum_is_never_downloaded()
    {
        var rel = Publish("9.9.9", RealProgram(), includeSha: false);
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Updater.DownloadAsync(rel));
        Assert.Contains("no checksum", ex.Message);
    }

    [Fact]
    public async Task A_download_over_the_size_limit_is_refused()
    {
        var rel = Publish("9.9.9", RealProgram());
        Updater.MaxZipBytes = 10;
        await Assert.ThrowsAsync<UpdateException>(() => Updater.DownloadAsync(rel));
    }

    [Fact]
    public async Task A_full_install_swaps_the_program_keeps_the_old_one_and_marks_the_update_pending()
    {
        var rel = Publish(AppInfo.Version, RealProgram());       // same number as this build, so the version check can pass
        string cur = _dir.Combine("app", "Argus.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(cur)!);
        File.WriteAllText(cur, "OLD PROGRAM");

        string backup = await Updater.InstallAsync(rel, cur);

        Assert.Equal(RealProgram(), File.ReadAllBytes(cur));
        Assert.Equal("OLD PROGRAM", File.ReadAllText(backup));
        Assert.False(File.Exists(cur.Replace("Argus.exe", "Argus.exe.new")));
        Assert.True(UpdateRecovery.Pending);
    }

    [Fact]
    public async Task A_program_with_the_wrong_version_inside_never_replaces_anything()
    {
        var rel = Publish("9.9.9", RealProgram());               // the zip claims 9.9.9 but the program inside is this build's version
        string cur = _dir.Combine("app", "Argus.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(cur)!);
        File.WriteAllText(cur, "OLD PROGRAM");

        await Assert.ThrowsAsync<UpdateException>(() => Updater.InstallAsync(rel, cur));

        Assert.Equal("OLD PROGRAM", File.ReadAllText(cur));
        Assert.False(File.Exists(cur + ".old"));
        Assert.False(UpdateRecovery.Pending);
    }

    private sealed class SyncProgress : IProgress<UpdateProgress>
    {
        private readonly Action<UpdateProgress> _a;
        public SyncProgress(Action<UpdateProgress> a) { _a = a; }
        public void Report(UpdateProgress value) => _a(value);
    }
}
