# Security Policy

## Supported versions
Only the latest release receives fixes (currently 0.1.x).

## Reporting a vulnerability
Please **do not** open a public issue. Use GitHub's "Report a vulnerability" (private security advisory) on the
repository's Security tab. Include the Argus version (`Argus.exe --version`), your Windows version, and steps to reproduce.

## What Argus does and does not do
- Runs as a normal user; it never asks for administrator rights. Some actions on other users' or system processes
  are refused by Windows, and Argus reports that instead of working around it.
- Makes no network connections unless you turn on update checks, press **Check now**, or run `--update`. Then it talks only to GitHub
  (`api.github.com`, `github.com`, `*.githubusercontent.com`) over https and sends nothing about your PC: just a normal request with
  `User-Agent: Argus/<version>`. It sends no telemetry and has no other network code.
- Writes only to its own settings folder (`%APPDATA%\Argus`), the per-user `StartupApproved` registry values when you
  toggle a startup program, and (when you ask) old files in your own temp folder.

## Updates: what the checksum does and does not protect
The updater downloads `Argus-<version>-win-x64.zip` and its `.sha256` from the same GitHub release and refuses to install anything whose
SHA-256 does not match, whose zip tries to write outside its folder, or whose `Argus.exe` does not report the announced version. That protects
against corrupted or swapped downloads and bad mirrors. It does **not** protect against someone who takes over the GitHub account or the
release, because they could publish a matching checksum for a bad zip. Closing that gap needs a code-signing certificate and a signature
check in the updater (see [TODO.md](TODO.md)). Until then, turn on **Check for updates** only if you trust the repository owner.

## Areas worth reviewing
Process-closing safety rails (`Safety.cs`), the temp cleaner's path validation and link handling (`Perf.cs`), and the
low-level mouse/keyboard hooks used only while the click-to-close tool is active (`Picker.cs`), and the updater (`Updater.cs`): the download
checks, zip unpacking, the file swap and the rollback.