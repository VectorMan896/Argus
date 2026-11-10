# Changelog

All notable changes to Argus are documented here.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and the project follows
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html). Before 1.0.0, minor versions may change behaviour.

## [Unreleased]

### Added
- **Disk card** on the Performance page, the first part of disk monitoring. Older PCs with a spinning hard disk often sit at "100% disk" and crawl; Argus now shows it.
  - A live graph of how busy the disk is (the same "Active time" Task Manager shows) with read and write speed, and whether the system drive is a
    **spinning hard disk or an SSD** (the drive itself is asked; if it does not say, the card says "type not reported" instead of guessing).
  - **Using the disk right now:** the programs reading and writing the most, with all of a program's processes added together. Core Windows programs are shown but
    marked *Protected*, and the usual heavy users get a plain note (Windows Search indexing, Windows Security scans, Windows Update, OneDrive). The numbers are the
    file reads and writes a program asked for, including ones answered from Windows' cache, so they show who is busy with files rather than exact disk accesses.
  - **Slow down selected** lowers that program's disk priority (and its CPU priority) so everything else gets the disk first. Nothing is closed, and it resets when
    the program restarts. Core Windows programs, this program and Argus itself are refused, like every other action.
  - When the disk has been busy about 85% or more for ten seconds, a tip appears under *What would help most* (a hard disk gets the "it can only do one thing at a time"
    explanation).
  - Nothing is read for the disk unless the Performance page is on screen, so it costs an older PC nothing the rest of the time.
- `Argus.exe --disk [--sample N]` prints the same readings and the busiest programs without changing anything; `--slow-disk-pid PID` slows one down.
- 30 more tests (248 in total): busy-time maths, reading the drive-type answer, who-is-using-it from made-up numbers (grouping, restarted programs, counters that went
  backwards, core Windows marked), a real write showing up in this program's counters, the disk priority change and its refusals on throwaway programs, the tip, the
  sampler staying silent when off, and the card drawing. Each rule was checked to fail when deliberately broken.
- **What restarts it?** button on *Needs a decision*: select any program and Argus lists the service, scheduled task or startup entry behind it straight away,
  without waiting for it to come back three times, and opens the same switch-off window.

### Fixed
- "Keeps coming back" now counts every real close, by Argus or by you, and for suspicious, not-responding and close-set resource-hog programs as well as analytics.
  Before, closing a suspicious program by hand never triggered it, so a service that restarts itself (for example a fingerprint-reader plugin) was closed again and
  again with no offer to switch it off.
- The switch-off window no longer says the running copy is closed for you; it is not, you close it from the list.
- 16 more tests (218 in total), including closing by hand three times, and a check that each new rule fails when deliberately broken.

## [0.5.0] - 2026-11-07

### Added
- **First days: learning mode.** A fresh install starts in learning mode for three days: Argus watches and writes down what it **would** have done with your
  rules, and closes, slows down and changes nothing. A **Learning** button in the header ("Learning: day 2 of 3", then "Learning done: review") opens the
  list, and a tray notice tells you when the period is over. From there you can start acting on your rules, keep learning, or open the rules first. Things
  set to *Ask* are never held back. Upgrading from an older version does not turn it on; *Rules > Options > Learning mode* switches it on or off.
- **Programs that keep coming back.** When an analytics program has been closed three times in ten minutes, Argus looks for the service, scheduled task or
  startup entry that restarts it and lists it on *Needs a decision* as "Keeps coming back". **Close / fix selected** opens a window with each starter ticked;
  *Switch off ticked* sets a service to Disabled, switches a task off or un-ticks a startup entry. Nothing is deleted, it is never automatic, core Windows
  programs are never offered, and *Rules > Options > Disabled by Argus > Show list* restores each one exactly as it was.
- **Administrator mode, on request.** *Rules > Options > Administrator mode > Restart as administrator* restarts Argus through Windows' own approval prompt so it
  can change protected services. Every safety rule stays in force with or without it, and the title and status pill say when it is on. Switching off a
  service or system task without it is refused with a clear message instead of failing quietly.
- **Digital signatures.** Argus reads a program's signature (without using the network). A valid signature now clears a "Suspicious" finding and says who signed it;
  a damaged signature raises it. `Argus.exe --signature FILE` shows a file's signature, `Argus.exe --starters FILE` shows what starts a program (read-only).
- **Signed updates.** The updater now refuses an update whose signature is damaged or untrusted, and, if your copy is signed, one that is not signed by the same
  publisher. Releases are signed in CI when a code-signing certificate is added (`scripts/sign-file.ps1`, [docs/SIGNING.md](docs/SIGNING.md)); until then they stay unsigned.
- **Clear install instructions.** Argus is a portable, no-install program, and the docs now say so up front: a "Get Argus (there is no installer)" section
  in the README, `INSTALL.txt` inside the zip, and an install block on every GitHub release page. They cover unblocking the zip before extracting it,
  extracting to a safe folder you keep and can write to, what to do about "Windows protected your PC", checking the checksum, and removing Argus.
- Release pages are now filled in automatically: pushing a version tag writes the release notes from that version's `CHANGELOG.md` section followed by the
  install block (`scripts/release-notes.ps1`). Argus's update window shows the same text.
- Argus warns (activity log and a tray notice) when it is running from a temporary folder, which is what happens when `Argus.exe` is double-clicked
  inside the zip window.
- 77 more tests (202 in total): signature classification and the update policy, learning mode (including the real watcher leaving a program running while it
  learns and closing it afterwards), the administrator restart command and the refusals that do not loosen with it, and "keeps coming back" end to end:
  services only ever through a fake, startup entries and scheduled tasks as uniquely named throwaway entries that are removed again, undo records, and
  the watcher raising it on the third close and not the second. Each new rule was checked to fail when deliberately broken.

### Changed
- The updater refuses to replace Argus when it is running from a temporary folder, and says to extract it to a folder you keep.
- The README, SAFETY and SECURITY pages describe administrator mode, learning mode, signatures and what Argus can switch off.
## [0.4.0] - 2026-11-07

### Added
- **Self-updater**, so nobody has to re-download Argus from GitHub. Off by default: nothing touches the network until the user turns on
  *Rules > Updates > Check for updates* (once a day) or presses *Check now*.
  - An **Update** button in the header and a tray notice appear when a newer release exists; the update window shows the release notes with
    *Update and restart*, *Skip this version* and *Later*. Nothing is downloaded until Update is pressed.
  - Downloads come only from GitHub over https, and must match the release's published SHA-256 checksum; only `Argus.exe` and plain documents
    are unpacked (a zip path that tries to leave its folder is rejected), and the program inside must report the announced version.
  - The old version is kept as `Argus.exe.old` until the new one has run for ten seconds, and Argus rolls back by itself if the new version
    fails to start twice.
  - Only the portable `Argus.exe` in a writable folder updates itself; a build made from source never overwrites itself.
- `--update [--yes] [--no-restart]` on the command line (`--check-update` is the same without installing), and `--updated-from PID` used
  internally when restarting.
- 53 more tests (125 in total): version ordering, reading GitHub's answer, checksums, tampered or oversized downloads, unsafe zip paths, the file
  swap, rollback, a full install against a fake GitHub on a loopback address, and drawing of the new Updates card and Update button.
- Checked end to end by hand with two real portable builds and a local fake GitHub: check only, a tampered checksum (refused, nothing
  changed), a full install from the update window with the real mouse (restart, confirm, old version removed), and the automatic rollback.

### Fixed
- Page titles and subtitles in the header no longer run underneath the buttons on the right.

### Changed
- Argus no longer promises "no network, ever": it makes no network connections unless update checks are turned on (README, SECURITY, SAFETY updated).

## [0.3.0] - 2026-11-07

### Added
- **CPU and GPU history graphs** on the Overview, next to the memory dial: about two minutes of history, one reading per second, a big current
  value, the peak, and a soft filled line that turns amber and then red as the load climbs.
  - CPU comes from the system's idle/kernel/user times, so it needs no performance-counter setup and works on any PC.
  - GPU comes from Windows' vendor-neutral "GPU Engine" counters and shows the busiest engine, the way Task Manager does. On PCs without them
    (older Windows or driver) the card says so plainly instead of showing a made-up number.
  - The CPU and GPU cards show the processor count and the graphics adapter name.
  - Nothing is sampled while the window is hidden, minimised or on another page, so the graphs cost older PCs nothing when you are not looking.
- `--usage` command-line option: prints the CPU and GPU load once a second.
- 19 more tests (72 in total): CPU and GPU maths, the GPU instance-name parsing, the live readings, graph history and drawing, hardware names.

### Changed
- The Overview is now a four-column grid: status card, This PC, then Activity, CPU, GPU and Memory.
- The cramped "Recent activity" text box is replaced by a compact **Activity** card: the latest events as tidy rows, plus an
  **Open activity log** button.
- The Log page is now called **Activity** (sidebar, header and docs). `--tab 5` opens it.
- "Startup programs enabled" is shortened to "Startup programs" so it fits the narrower This PC card.

## [0.2.0] - 2026-11-07

### Added
- Dashboard-style window: slim icon sidebar, large rounded dark cards, pill buttons, segmented Off / Ask / Auto switches,
  steppers and toggles, a memory dial and live "This PC" meters. Inspired by the reference in `assets/source/ui-reference.webp`.
- Overview page with what Argus is doing, counters since it started (waiting, closed, slowed down, windows fixed),
  live system numbers and recent activity.
- Argus logo (owl with clipboard): sidebar mark, Overview badge, window / tray / exe icon (`assets/logo/argus.ico`).
- Logo variants and a repeatable generator: `tools/make_logo_assets.py` builds the full logo, a dark-background version
  (owl on a light tile with white text), icon-only, text-only wordmarks (black, white, accent blue) and the app icon.
- Dark title bar and dark scrollbars.
- `--tab N` command-line option to open a specific page (0 Overview, 1 Needs a decision, 2 Performance, 3 Rules, 4 Lists, 5 Log).

- Portable build: `scripts/publish.ps1` makes one self-contained `Argus.exe` (no .NET install needed) plus a zip and a SHA-256 checksum.
- Automated test suite (`tests/Argus.Tests`, 53 tests): safety rules, list matching, the temp cleaner (including never following links),
  the startup toggle, slowing down and closing throwaway programs, settings and the command line.
- GitHub Actions workflow (`.github/workflows/ci.yml`): build and test on every push, and attach the portable zip to a release for version tags.
- `NuGet.Config` so a fresh clone restores without any machine-wide setup.
- `ARGUS_INSTANCE` environment variable to run a second, separate copy beside the first (for testing).
- `LICENSE` (MIT) and `TODO.md`.

### Changed
- The tab control was replaced by the sidebar. The old Rules dropdowns and number boxes are now segmented switches and steppers.
- The startup-programs list no longer shows the command column; hover a row to see the full command.
- The logo always keeps its original colours; on dark screens it sits on a light tile.

## [0.1.0] - 2026-11-07

First version.

### Added
- Tray app with a main window: *Needs a decision*, *Performance*, *Rules*, *Lists* and *Log* tabs.
- Monitor with five categories (analytics/telemetry, not responding, background hogs, suspicious programs,
  invisible click-blocking windows), each set to Off / Ask / Automatic.
- Editable `blocklist.txt` and `exclusions.txt` (name, `path:`, `title:`, `class:` patterns with wildcards).
- Click-to-close tool (Ctrl+Alt+K) showing every window under the click, with close, kill, hide, click-through and exclude.
- Graceful close (WM_CLOSE) before a forced kill; protected-process rails that never close core Windows processes or
  Argus's own parent processes; protected names only count when the file is in a system location.
- Older-PC helpers: system tips, startup-program toggles (Task Manager's mechanism), free-up-memory, slow-down for hogs
  (lower priority + efficiency mode), manual temp cleanup restricted to the user temp folder and never following links.
- Command line: `--version`, `--scan`, `--apply`, `--stack`, `--pick`, `--perf`, `--hidden`, `--config`, `--tab`.
- Log file and a note when a closed program keeps being restarted by a service or scheduled task.

[Unreleased]: https://github.com/VectorMan896/Argus/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/VectorMan896/Argus/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/VectorMan896/Argus/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/VectorMan896/Argus/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/VectorMan896/Argus/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/VectorMan896/Argus/releases/tag/v0.1.0