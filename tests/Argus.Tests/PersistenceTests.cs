using Microsoft.Win32;

namespace Argus.Tests;

/// <summary>
/// "Programs that keep coming back". Services are only ever changed through a fake, so no real service is touched.
/// The startup-entry and scheduled-task checks use a uniquely named entry for a throwaway copy of ping.exe and remove it again.
/// </summary>
[Collection("Config")]
public sealed class PersistenceTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly TempDir _dir = new();
    private readonly IServiceControl _realServices = Persistence.Services;
    private readonly List<string> _runNames = new(), _taskNames = new();

    public PersistenceTests()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"), "{ \"Learning\": false }");
        Config.Init(_dir.Path);
    }

    public void Dispose()
    {
        Persistence.Services = _realServices;
        foreach (var n in _runNames)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k?.DeleteValue(n, false);
            using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true)) k?.DeleteValue(n, false);
        }
        foreach (var t in _taskNames) { try { DeleteTask(t); } catch { } }
        _dir.Dispose();
    }

    // ---- fakes and helpers -----------------------------------------------------------------------------------------------------

    private sealed class FakeServices : IServiceControl
    {
        public readonly Dictionary<string, int> Start = new();
        public string Refuse = "";                                   // when set, every change is refused with this reason
        public int Changes;
        public int? GetStart(string name) => Start.TryGetValue(name, out var s) ? s : null;
        public string SetStart(string name, int start)
        {
            if (Refuse != "") return Refuse;
            if (!Start.ContainsKey(name)) return "that service no longer exists";
            Start[name] = start; Changes++; return "ok";
        }
    }

    private FakeServices UseFakeServices(string name, int start)
    {
        var f = new FakeServices(); f.Start[name] = start;
        Persistence.Services = f;
        return f;
    }

    private static PersistenceSource Svc(string name) => new() { Kind = PersistKind.Service, Name = name, Display = name, NeedsAdmin = true };

    /// <summary>A throwaway program plus a HKCU Run entry for it. The entry is removed again in Dispose.</summary>
    private string MakeProgramWithRunEntry(out string name)
    {
        string exe = Path.Combine(_dir.Path, "argus_persist_probe_" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        name = "ArgusTest-" + Guid.NewGuid().ToString("N");
        _runNames.Add(name);
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue(name, "\"" + exe + "\" -t 127.0.0.1");
        return exe;
    }

    private static dynamic TaskService()
    {
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        svc.Connect();
        return svc;
    }

    /// <summary>A task for the current user that would run the program once in 2099. It never runs; it is deleted in Dispose.</summary>
    private string MakeTask(string exe)
    {
        string name = "ArgusTest-" + Guid.NewGuid().ToString("N");
        _taskNames.Add(name);
        dynamic svc = TaskService();
        dynamic def = svc.NewTask(0);
        def.RegistrationInfo.Description = "Argus test task (safe to delete)";
        dynamic trigger = def.Triggers.Create(1);                   // 1 = at a given time
        trigger.StartBoundary = "2099-01-01T03:00:00";
        dynamic action = def.Actions.Create(0);                      // 0 = run a program
        action.Path = exe;
        svc.GetFolder("\\").RegisterTaskDefinition(name, def, 6, null, null, 3);       // create or update, run as the signed-in user
        return "\\" + name;
    }

    private static bool TaskEnabled(string fullPath)
    {
        dynamic task = TaskService().GetFolder("\\").GetTask(fullPath.TrimStart('\\'));
        return (bool)task.Enabled;
    }

    private static void DeleteTask(string name) => TaskService().GetFolder("\\").DeleteTask(name, 0);

    private static bool WaitFor(Func<bool> condition, int seconds = 12)
    {
        for (int i = 0; i < seconds * 4; i++) { if (condition()) return true; Thread.Sleep(250); }
        return condition();
    }

    // ---- finding what starts a program (pure matching) -------------------------------------------------------------------------

    [Fact]
    public void A_service_is_matched_by_its_program_whatever_the_quotes_arguments_or_variables()
    {
        string pf = Environment.GetEnvironmentVariable("ProgramFiles")!;
        string exe = Path.Combine(pf, "Vendor", "tracker.exe");
        var services = new (string Name, string? Display, string? ImagePath, int Start)[]
        {
            ("QuotedSvc",   "Quoted service", "\"" + exe + "\" -service",           2),
            ("PlainSvc",    "Plain service",  exe + " -k group",                      3),
            ("EnvSvc",      "@%SystemRoot%\\x.dll,-100", @"%ProgramFiles%\Vendor\tracker.exe", 2),
            ("OtherSvc",    "Other",          Path.Combine(pf, "Vendor", "other.exe"), 2),
            ("DriverSvc",   "A driver",       @"\SystemRoot\System32\drivers\x.sys",   1),
            ("NoPath",      "Nothing",        null,                                    2),
        };

        var found = Persistence.FindServices(exe, services);

        Assert.Equal(new[] { "EnvSvc", "PlainSvc", "QuotedSvc" }, found.Select(f => f.Name).OrderBy(n => n).ToArray());
        Assert.All(found, f => { Assert.Equal(PersistKind.Service, f.Kind); Assert.True(f.NeedsAdmin); });
        Assert.Equal("EnvSvc", found.Single(f => f.Name == "EnvSvc").Display);          // an unreadable "@dll,-100" name falls back to the key name
    }

    [Fact]
    public void A_service_that_is_already_disabled_is_not_offered_again()
    {
        string exe = Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles")!, "Vendor", "tracker.exe");
        var services = new (string Name, string? Display, string? ImagePath, int Start)[] { ("Off", "Off", "\"" + exe + "\"", 4) };
        Assert.Empty(Persistence.FindServices(exe, services));
    }

    [Theory]
    [InlineData("svchost")]
    [InlineData("services")]
    [InlineData("explorer")]
    [InlineData("lsass")]
    public void Core_windows_programs_are_never_offered_for_disabling(string name)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name == "explorer" ? "" : "System32", name + ".exe");
        Assert.Empty(Persistence.Find(path));
    }

    [Fact]
    public void Nothing_is_found_for_an_unknown_or_missing_program()
    {
        Assert.Empty(Persistence.Find(null));
        Assert.Empty(Persistence.Find(""));
        Assert.Empty(Persistence.Find(Path.Combine(_dir.Path, "never_existed_" + Guid.NewGuid().ToString("N") + ".exe")));
    }

    // ---- services: only ever through the fake ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]      // automatic
    [InlineData(3)]      // manual
    public void Disabling_a_service_remembers_how_it_started_and_restoring_puts_that_back(int before)
    {
        var fake = UseFakeServices("FakeTracker", before);

        Assert.Equal("disabled", Persistence.Disable(Svc("FakeTracker"), @"C:\x\tracker.exe"));
        Assert.Equal(4, fake.Start["FakeTracker"]);
        var rec = Assert.Single(Persistence.Records());
        Assert.Equal("FakeTracker", rec.Name);
        Assert.Equal(before, rec.PreviousStart);

        Assert.Equal("restored", Persistence.Restore(rec));
        Assert.Equal(before, fake.Start["FakeTracker"]);
        Assert.Empty(Persistence.Records());
    }

    [Fact]
    public void The_undo_list_survives_a_restart()
    {
        UseFakeServices("FakeTracker", 2);
        Persistence.Disable(Svc("FakeTracker"), @"C:\x\tracker.exe");

        Config.Init(_dir.Path);                                       // as if Argus had been closed and opened again

        Assert.Equal("FakeTracker", Assert.Single(Persistence.Records()).Name);
    }

    [Fact]
    public void A_refused_change_changes_nothing_and_leaves_no_undo_record()
    {
        var fake = UseFakeServices("FakeTracker", 2);
        fake.Refuse = "needs administrator rights (use Rules > Administrator mode)";

        string r = Persistence.Disable(Svc("FakeTracker"), @"C:\x\tracker.exe");

        Assert.StartsWith("needs administrator", r);
        Assert.Equal(2, fake.Start["FakeTracker"]);
        Assert.Equal(0, fake.Changes);
        Assert.Empty(Persistence.Records());
    }

    [Fact]
    public void Disabling_a_service_twice_does_not_overwrite_how_it_originally_started()
    {
        var fake = UseFakeServices("FakeTracker", 2);
        Persistence.Disable(Svc("FakeTracker"), "");

        Assert.Equal("already disabled", Persistence.Disable(Svc("FakeTracker"), ""));

        Assert.Equal(2, Assert.Single(Persistence.Records()).PreviousStart);          // still the original setting, not 4
        Assert.Equal(1, fake.Changes);
    }

    [Fact]
    public void A_service_that_vanished_cannot_be_restored_and_stays_on_the_list_so_the_user_can_see_why()
    {
        var fake = UseFakeServices("FakeTracker", 2);
        Persistence.Disable(Svc("FakeTracker"), "");
        fake.Start.Remove("FakeTracker");                              // uninstalled in the meantime

        var rec = Assert.Single(Persistence.Records());
        string r = Persistence.Restore(rec);

        Assert.Contains("no longer exists", r);
        Assert.Single(Persistence.Records());
    }

    [Fact]
    public void An_unknown_service_is_reported_not_guessed_at()
    {
        UseFakeServices("FakeTracker", 2);
        Assert.Equal("could not read the service", Persistence.Disable(Svc("NotThere"), ""));
        Assert.Empty(Persistence.Records());
    }

    // ---- startup entries (a unique HKCU entry for a throwaway program) -------------------------------------------------------------

    [Fact]
    public void A_startup_entry_is_found_switched_off_without_deleting_it_and_switched_back_on()
    {
        string exe = MakeProgramWithRunEntry(out string name);

        var found = Assert.Single(Persistence.Find(exe));
        Assert.Equal(PersistKind.StartupEntry, found.Kind);
        Assert.Equal(name, found.Name);
        Assert.False(found.NeedsAdmin);                                // HKCU: no administrator needed

        Assert.Equal("disabled", Persistence.Disable(found, exe));
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            Assert.NotNull(k!.GetValue(name));                         // the Run value itself is still there
        Assert.Empty(Persistence.Find(exe));                           // and a disabled entry is no longer a starter

        var rec = Assert.Single(Persistence.Records());
        Assert.Equal("restored", Persistence.Restore(rec));
        Assert.Single(Persistence.Find(exe));                          // back to how it was
        Assert.Empty(Persistence.Records());
    }

    // ---- scheduled tasks (a task for the current user that would run once in 2099) ----------------------------------------------

    [Fact]
    public void A_scheduled_task_is_found_switched_off_and_switched_back_on()
    {
        string exe = Path.Combine(_dir.Path, "argus_task_probe.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        string task = MakeTask(exe);

        var found = Assert.Single(Persistence.Find(exe));
        Assert.Equal(PersistKind.ScheduledTask, found.Kind);
        Assert.Equal(task, found.Name);
        Assert.False(found.NeedsAdmin);                                 // registered for the signed-in user at normal privilege
        Assert.True(TaskEnabled(task));

        Assert.Equal("disabled", Persistence.Disable(found, exe));
        Assert.False(TaskEnabled(task));
        Assert.Empty(Persistence.Find(exe));                            // switched off: not offered again

        Assert.Equal("restored", Persistence.Restore(Assert.Single(Persistence.Records())));
        Assert.True(TaskEnabled(task));
    }

    [Fact]
    public void A_task_that_was_deleted_in_the_meantime_is_reported_not_crashed_on()
    {
        string exe = Path.Combine(_dir.Path, "argus_task_probe.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        string task = MakeTask(exe);
        var found = Assert.Single(Persistence.Find(exe));
        DeleteTask(task.TrimStart('\\'));

        string r = Persistence.Disable(found, exe);

        Assert.Contains("no longer exists", r);
        Assert.Empty(Persistence.Records());
    }

    [Fact]
    public void Every_kind_of_starter_for_one_program_is_listed_together()
    {
        string exe = MakeProgramWithRunEntry(out _);
        string task = MakeTask(exe);

        var kinds = Persistence.Find(exe).Select(s => s.Kind).ToHashSet();

        Assert.Contains(PersistKind.StartupEntry, kinds);
        Assert.Contains(PersistKind.ScheduledTask, kinds);
    }

    // ---- the real Watcher, with a program that is closed again and again ----------------------------------------------------------

    [Fact]
    public void A_program_closed_three_times_in_ten_minutes_is_offered_for_disabling_what_restarts_it()
    {
        string started = DateTime.UtcNow.ToString("o");
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"),
            "{ \"Analytics\":\"Auto\",\"Hung\":\"Off\",\"Hog\":\"Off\",\"Weird\":\"Off\",\"Blocker\":\"Off\",\"Learning\":false }");
        File.WriteAllText(Path.Combine(_dir.Path, "blocklist.txt"), "argus_respawn_probe");
        File.WriteAllText(Path.Combine(_dir.Path, "exclusions.txt"), "# none");
        Config.Init(_dir.Path);

        string exe = Path.Combine(_dir.Path, "argus_respawn_probe.exe");
        string runName = "ArgusTest-" + Guid.NewGuid().ToString("N");
        _runNames.Add(runName);
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe);
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(runName, "\"" + exe + "\" -t 127.0.0.1");

        using var w = new Watcher();
        for (int round = 1; round <= 3; round++)
        {
            Assert.DoesNotContain(w.Current, f => f.Category == Category.Respawn);      // not before the third time
            var p = Fake.Start(_dir.Path, "argus_respawn_probe.exe");
            try
            {
                w.ScanNow();
                Assert.True(WaitFor(() => p.HasExited), $"round {round}: the watcher did not close the program");
                Assert.True(WaitFor(() => !w.IsBusy, 20), $"round {round}: the scan never finished");
            }
            finally { Fake.Kill(p); }
        }

        w.ScanNow();
        Assert.True(WaitFor(() => w.Current.Any(f => f.Category == Category.Respawn)), "no 'keeps coming back' finding was raised");
        var finding = w.Current.Single(f => f.Category == Category.Respawn);
        Assert.Equal("argus_respawn_probe", finding.ProcName);
        var src = Assert.Single(finding.Sources!);
        Assert.Equal(PersistKind.StartupEntry, src.Kind);
        Assert.Equal(runName, src.Name);
        Assert.Contains("startup entry", finding.Reason);

        // nothing has been changed by merely offering it
        using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
            Assert.True(k?.GetValue(runName) == null || ((byte[])k.GetValue(runName)!)[0] != 3);

        // "closing" this kind of finding is refused: only the dialog may change a starter
        Assert.StartsWith("refused", Actions.Execute(finding, Config.Settings));

        // dealing with it takes it off the list
        Assert.Equal("disabled", Persistence.Disable(src, exe));
        w.ResolveRespawn(finding, dontAskAgain: false);
        Assert.True(WaitFor(() => !w.IsBusy, 20));
        w.ScanNow();
        Assert.True(WaitFor(() => !w.IsBusy, 20));
        Assert.DoesNotContain(w.Current, f => f.Category == Category.Respawn);
    }

    [Fact]
    public void Closing_a_program_twice_is_not_enough_to_raise_anything()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"), "{ \"Learning\": false }");
        Config.Init(_dir.Path);
        string exe = MakeProgramWithRunEntry(out _);
        using var w = new Watcher();

        w.NoteKill("argus_persist_probe", exe);
        w.NoteKill("argus_persist_probe", exe);
        w.ScanNow();
        Assert.True(WaitFor(() => !w.IsBusy, 20));

        Assert.DoesNotContain(w.Current, f => f.Category == Category.Respawn);
    }

    [Fact]
    public void Leave_it_alone_stops_the_same_program_being_offered_again()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "settings.json"), "{ \"Learning\": false }");
        Config.Init(_dir.Path);
        string exe = MakeProgramWithRunEntry(out _);
        using var w = new Watcher();
        for (int i = 0; i < 3; i++) w.NoteKill("argus_persist_probe", exe);
        w.ScanNow();
        Assert.True(WaitFor(() => w.Current.Any(f => f.Category == Category.Respawn)));

        w.ResolveRespawn(w.Current.Single(f => f.Category == Category.Respawn), dontAskAgain: true);
        Assert.True(WaitFor(() => !w.IsBusy, 20));
        for (int i = 0; i < 3; i++) w.NoteKill("argus_persist_probe", exe);                 // closed again and again afterwards
        w.ScanNow();
        Assert.True(WaitFor(() => !w.IsBusy, 20));

        Assert.DoesNotContain(w.Current, f => f.Category == Category.Respawn);
    }

    // ---- the rails ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Respawn_findings_are_never_handled_automatically()
    {
        foreach (var h in new[] { Handling.Auto, Handling.Off })
        {
            var s = new Settings { Analytics = h, Hung = h, Hog = h, Weird = h, Blocker = h };
            Assert.Equal(Handling.Ask, s.For(Category.Respawn));                            // changing a service is always the user's decision
        }
    }

    [Fact]
    public void The_switch_off_window_lists_every_starter_ticked_and_changes_nothing_until_asked()
    {
        var f = new Finding
        {
            Category = Category.Respawn, ProcName = "tracker", Key = "respawn:tracker", Path = @"C:\x\tracker.exe",
            Sources = new List<PersistenceSource>
            {
                new() { Kind = PersistKind.Service, Name = "TrackerSvc", Display = "Tracker Service", Detail = "starts automatically", NeedsAdmin = true },
                new() { Kind = PersistKind.ScheduledTask, Name = "\\TrackerDaily", Display = "TrackerDaily", Detail = "runs on a schedule or event", NeedsAdmin = true },
                new() { Kind = PersistKind.StartupEntry, Name = "Tracker", Display = "Tracker", Detail = "starts when you sign in" },
            },
        };
        using var d = new PersistenceDialog(f);
        var list = d.Controls.OfType<ListView>().Single();
        Assert.Equal(3, list.Items.Count);
        Assert.All(list.Items.Cast<ListViewItem>(), i => Assert.True(i.Checked));
        Assert.Equal(PersistOutcome.None, d.Outcome);
        Assert.Equal(0, d.DisabledCount);
        Assert.True(RenderHasInk(d, "persistence-window.png"));
    }

    [Fact]
    public void The_undo_window_draws_its_list_and_its_empty_state()
    {
        using (var empty = new DisabledByArgusDialog()) { Assert.Empty(empty.Controls.OfType<ListView>().Single().Items); Assert.True(RenderHasInk(empty, "disabled-window-empty.png")); }

        UseFakeServices("TrackerSvc", 2);
        Persistence.Disable(Svc("TrackerSvc"), @"C:\x\tracker.exe");
        using var full = new DisabledByArgusDialog();
        Assert.Single(full.Controls.OfType<ListView>().Single().Items);
        Assert.True(RenderHasInk(full, "disabled-window.png"));
    }

    private static bool RenderHasInk(Form f, string name)
    {
        f.CreateControl(); f.Show(); f.Hide();                               // make sure the list has laid out
        using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
        string? dir = Environment.GetEnvironmentVariable("ARGUS_TEST_SNAPSHOT_DIR");
        if (!string.IsNullOrEmpty(dir)) bmp.Save(Path.Combine(dir, name));
        for (int y = 0; y < bmp.Height; y += 5) for (int xx = 0; xx < bmp.Width; xx += 5) if (bmp.GetPixel(xx, y).ToArgb() != Theme.Card.ToArgb()) return true;
        return false;
    }
}