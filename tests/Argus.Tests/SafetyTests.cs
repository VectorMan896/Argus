namespace Argus.Tests;

public sealed class SafetyTests
{
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string Temp => Path.Combine(Path.GetTempPath(), "svchost.exe");

    [Theory]
    [InlineData("csrss")]
    [InlineData("lsass")]
    [InlineData("winlogon")]
    [InlineData("wininit")]
    [InlineData("services")]
    [InlineData("dwm")]
    [InlineData("smss")]
    public void Core_windows_processes_are_refused_when_they_run_from_the_system_folder(string name) =>
        Assert.True(Safety.IsRefused(name, 0, Path.Combine(Win, "System32", name + ".exe")));

    [Fact]
    public void Core_names_are_also_protected_when_the_path_is_unknown() =>
        Assert.True(Safety.IsRefused("csrss", 0, null));

    [Fact]
    public void A_fake_system_name_in_a_temp_folder_is_an_impostor_not_protected()
    {
        Assert.False(Safety.IsNeverAuto("svchost", 0, Temp));
        Assert.False(Safety.IsRefused("lsass", 0, Path.Combine(Path.GetTempPath(), "lsass.exe")));
    }

    [Fact]
    public void Real_svchost_and_explorer_are_never_closed_automatically()
    {
        Assert.True(Safety.IsNeverAuto("svchost", 0, Path.Combine(Win, "System32", "svchost.exe")));
        Assert.True(Safety.IsNeverAuto("explorer", 0, Path.Combine(Win, "explorer.exe")));
    }

    [Fact]
    public void Explorer_can_still_be_closed_by_an_explicit_user_action()
    {
        // never automatic, but not in the absolute-refuse list (the user may deliberately restart it)
        Assert.False(Safety.IsRefused("explorer", 0, Path.Combine(Win, "explorer.exe")));
    }

    [Fact]
    public void Argus_never_closes_itself() =>
        Assert.True(Safety.IsRefused("whatever", Environment.ProcessId, null));

    [Fact]
    public void Windows_Defender_in_programdata_is_protected()
    {
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assert.True(Safety.IsRefused("MsMpEng", 0, Path.Combine(pd, "Microsoft", "Windows Defender", "Platform", "4.18", "MsMpEng.exe")));
    }

    [Fact]
    public void Ordinary_programs_are_not_protected()
    {
        Assert.False(Safety.IsNeverAuto("chrome", 0, @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.False(Safety.IsRefused("notepad", 0, @"C:\Users\x\AppData\Local\Programs\notepad.exe"));
    }

    [Fact]
    public void Folder_helpers()
    {
        Assert.True(Safety.IsInWindowsDir(Path.Combine(Win, "System32", "x.exe")));
        Assert.False(Safety.IsInWindowsDir(@"C:\Users\x\Downloads\x.exe"));
        Assert.False(Safety.IsInWindowsDir(null));
        Assert.True(Safety.IsInTrustedInstallDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "App", "a.exe")));
        Assert.False(Safety.IsInTrustedInstallDir(@"C:\Users\x\Downloads\a.exe"));
    }
}
