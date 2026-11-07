# Security Policy

## Supported versions
Only the latest release receives fixes (currently 0.1.x).

## Reporting a vulnerability
Please **do not** open a public issue. Use GitHub's "Report a vulnerability" (private security advisory) on the
repository's Security tab. Include the Argus version (`Argus.exe --version`), your Windows version, and steps to reproduce.

## What Argus does and does not do
- Runs as a normal user; it never asks for administrator rights. Some actions on other users' or system processes
  are refused by Windows, and Argus reports that instead of working around it.
- Makes no network connections and sends no data anywhere.
- Writes only to its own settings folder (`%APPDATA%\Argus`), the per-user `StartupApproved` registry values when you
  toggle a startup program, and (when you ask) old files in your own temp folder.

## Areas worth reviewing
Process-closing safety rails (`Safety.cs`), the temp cleaner's path validation and link handling (`Perf.cs`), and the
low-level mouse/keyboard hooks used only while the click-to-close tool is active (`Picker.cs`).