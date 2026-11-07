namespace Argus.Tests;

public sealed class ElevationTests
{
    [Fact]
    public void The_restart_command_goes_through_windows_own_administrator_prompt()
    {
        var info = Elevation.BuildRestartInfo(@"C:\Users\someone\Apps\Argus\Argus.exe", 4242);
        Assert.Equal("runas", info.Verb);                       // Windows shows its own approval prompt; nothing is bypassed
        Assert.True(info.UseShellExecute);                      // required for the runas verb
        Assert.Equal("--handover 4242", info.Arguments);        // the new copy waits for this one to exit
        Assert.Equal(@"C:\Users\someone\Apps\Argus\Argus.exe", info.FileName);
        Assert.Equal(@"C:\Users\someone\Apps\Argus", info.WorkingDirectory);
    }

    [Fact]
    public void Asking_whether_we_are_elevated_never_throws() => _ = Elevation.IsElevated;

    [Fact]
    public void A_program_that_is_not_Argus_exe_never_even_shows_the_prompt()
    {
        // the test runner is dotnet.exe or testhost.exe, so this must refuse before asking Windows for anything
        Assert.False(Elevation.TryRestartElevated(out var message));
        Assert.Contains("not Argus.exe", message);
    }

    [Fact]
    public void Handover_is_the_same_wait_as_the_updater_uses()
    {
        Assert.Equal(123, Cli.Parse(new[] { "--handover", "123" }).UpdatedFrom);
        Assert.Equal(456, Cli.Parse(new[] { "--updated-from", "456" }).UpdatedFrom);
    }

    [Theory]
    [InlineData("csrss")]
    [InlineData("lsass")]
    [InlineData("winlogon")]
    public void The_safety_rules_do_not_loosen_with_administrator_rights(string name)
    {
        // Safety never asks whether we are elevated: these stay refused either way
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", name + ".exe");
        Assert.True(Safety.IsRefused(name, 0, path));
        Assert.True(Safety.IsNeverAuto(name, 0, path));
    }
}
