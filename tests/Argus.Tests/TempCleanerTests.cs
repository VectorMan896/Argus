using System.Diagnostics;

namespace Argus.Tests;

public sealed class TempCleanerTests : IDisposable
{
    private readonly TempDir _root = new();

    public void Dispose() => _root.Dispose();

    private string NewFile(string relative, int daysOld, bool readOnly = false)
    {
        string path = _root.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        Fake.MakeOld(path, daysOld);
        if (readOnly) File.SetAttributes(path, FileAttributes.ReadOnly);
        return path;
    }

    [Fact]
    public void Deletes_old_files_and_keeps_new_ones()
    {
        string old = NewFile("old.tmp", 30);
        string sub = NewFile(@"sub\old2.tmp", 30);
        string fresh = NewFile("fresh.tmp", 0);

        var r = Perf.TempFiles(_root.Path, 7, delete: true);

        Assert.Null(r.Error);
        Assert.Equal(2, r.Deleted);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(sub));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void A_scan_deletes_nothing()
    {
        string old = NewFile("old.tmp", 30);

        var r = Perf.TempFiles(_root.Path, 7, delete: false);

        Assert.Equal(1, r.Files);
        Assert.Equal(0, r.Deleted);
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void Read_only_files_are_left_alone()
    {
        string ro = NewFile("readonly.tmp", 30, readOnly: true);

        var r = Perf.TempFiles(_root.Path, 7, delete: true);

        Assert.True(File.Exists(ro));
        Assert.Equal(0, r.Deleted);
    }

    [Fact]
    public void Removes_empty_old_folders_but_never_the_root()
    {
        string empty = _root.Combine("emptyold");
        Directory.CreateDirectory(empty);
        Fake.MakeOld(empty, 30);

        Perf.TempFiles(_root.Path, 7, delete: true);

        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(_root.Path));
    }

    [Fact]
    public void Never_follows_a_link_out_of_the_folder()
    {
        using var outside = new TempDir();
        string decoy = Path.Combine(outside.Path, "old-decoy.tmp");
        File.WriteAllText(decoy, "x");
        Fake.MakeOld(decoy, 30);

        string link = _root.Combine("link");
        var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside.Path}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        mk.WaitForExit();
        Assert.True(Directory.Exists(link), "could not create the test junction");

        Perf.TempFiles(_root.Path, 7, delete: true);

        Assert.True(File.Exists(decoy), "a file outside the cleaned folder was deleted through a link");
        Assert.True(Directory.Exists(link), "the link itself should be left alone");
    }

    [Fact]
    public void Folder_validation_only_accepts_the_users_temp_folder()
    {
        Assert.Null(Perf.ValidateTempRoot(_root.Path));
        Assert.NotNull(Perf.ValidateTempRoot(@"C:\"));
        Assert.NotNull(Perf.ValidateTempRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.NotNull(Perf.ValidateTempRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")));
        Assert.NotNull(Perf.ValidateTempRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        Assert.NotNull(Perf.ValidateTempRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.NotNull(Perf.ValidateTempRoot(Path.GetDirectoryName(Path.GetTempPath().TrimEnd('\\'))!));   // the folder above temp
    }

    [Fact]
    public void A_refused_folder_is_reported_and_nothing_is_touched()
    {
        var r = Perf.TempFiles(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 0, delete: true);

        Assert.NotNull(r.Error);
        Assert.Equal(0, r.Deleted);
        Assert.Equal(0, r.Files);
    }
}
