# Security Policy

## Supported versions
Only the latest release receives fixes (currently 0.1.x).

## Reporting a vulnerability
Please **do not** open a public issue. Use GitHub's "Report a vulnerability" (private security advisory) on the
repository's Security tab. Include the Argus version (`Argus.exe --version`), your Windows version, and steps to reproduce.

## What Argus does and does not do
- Runs as a normal user and never asks for administrator rights by itself. Some actions on other users' or system processes
  are refused by Windows, and Argus reports that instead of working around it. *Rules > Options > Administrator mode* restarts it through
  Windows' own approval prompt (the `runas` verb, so UAC decides); it only restarts `Argus.exe` itself, and no safety rule changes when elevated.
- Makes no network connections unless you turn on update checks, press **Check now**, or run `--update`. Then it talks only to GitHub
  (`api.github.com`, `github.com`, `*.githubusercontent.com`) over https and sends nothing about your PC: just a normal request with
  `User-Agent: Argus/<version>`. It sends no telemetry and has no other network code.
- Writes only to its own settings folder (`%APPDATA%\Argus`, including the list of what it switched off), the `StartupApproved` registry values
  when you toggle a startup program, and (when you ask) old files in your own temp folder. When you choose to switch off what keeps restarting a program
  it also sets one service's start type to Disabled or one scheduled task's enabled flag to off (needs administrator rights for system ones), and
  can set them back. It never deletes a service, task or registry entry.
- Reads, but never trusts blindly, digital signatures: `WinVerifyTrust` with revocation checks switched off and cache-only URL retrieval, so it makes no
  network request. A valid signature only lowers suspicion (and decides whether an update may replace your copy); it never makes Argus close or change anything.

## Updates: what the checksum does and does not protect
The updater downloads `Argus-<version>-win-x64.zip` and its `.sha256` from the same GitHub release and refuses to install anything whose
SHA-256 does not match, whose zip tries to write outside its folder, or whose `Argus.exe` does not report the announced version. That protects
against corrupted or swapped downloads and bad mirrors. It does **not** protect against someone who takes over the GitHub account or the
release, because they could publish a matching checksum for a bad zip.

The signature check closes that gap once releases are signed (see [docs/SIGNING.md](docs/SIGNING.md)). The updater checks the downloaded `Argus.exe`
with `WinVerifyTrust`: a damaged or untrusted signature is **always** refused; if your copy is signed, the new one must be validly signed by the same
publisher; if your copy is unsigned, an unsigned or validly signed update is accepted (there is nothing to compare against). Until a signing certificate
is in use, turn on **Check for updates** only if you trust the repository owner.

## Areas worth reviewing
Process-closing safety rails (`Safety.cs`), the temp cleaner's path validation and link handling (`Perf.cs`), and the
low-level mouse/keyboard hooks used only while the click-to-close tool is active (`Picker.cs`), and the updater (`Updater.cs`): the download
checks, zip unpacking, the signature policy, the file swap and the rollback; the signature reading (`Authenticode.cs`); and the code that disables services,
tasks and startup entries (`Persistence.cs`) and restarts Argus elevated (`Elevation.cs`).