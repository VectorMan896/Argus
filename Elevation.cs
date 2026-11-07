using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Administrator mode, on request.
//
//  Argus normally runs as an ordinary user, which is the safe default. Some jobs (slowing down or closing a protected service,
//  disabling a service that keeps restarting a program) need administrator rights. Instead of always asking for them, Argus
//  offers one clearly labelled button that restarts it through Windows' own administrator prompt (UAC).
//
//  Nothing is bypassed: Windows still shows its prompt, the user still has to approve it, and every safety rule in Safety.cs
//  applies exactly as before (core Windows processes and Argus itself are never closed, with or without administrator rights).
// -------------------------------------------------------------------------------------------------

internal static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>The command that restarts Argus through Windows' administrator prompt. The new copy waits for this one to exit.</summary>
    public static ProcessStartInfo BuildRestartInfo(string exe, int pid) => new(exe, $"--handover {pid}")
    {
        UseShellExecute = true,                        // required for the "runas" verb
        Verb = "runas",
        WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
    };

    /// <summary>
    /// Shows Windows' administrator prompt and, if approved, starts a new elevated Argus. The caller should exit afterwards.
    /// Returns false (with a message to show) when the prompt was cancelled or Argus cannot restart itself.
    /// </summary>
    public static bool TryRestartElevated(out string message)
    {
        message = "";
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !Path.GetFileName(exe).Equals("Argus.exe", StringComparison.OrdinalIgnoreCase))
        {
            message = "Administrator mode needs to restart Argus.exe itself, and this is not Argus.exe.";
            return false;
        }
        try
        {
            Process.Start(BuildRestartInfo(exe, Environment.ProcessId));
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)          // ERROR_CANCELLED: the user said no to the prompt
        {
            message = "You cancelled the Windows prompt, so Argus stayed as it was.";
            return false;
        }
        catch (Exception ex)
        {
            message = "Could not restart as administrator: " + ex.Message;
            return false;
        }
    }
}
