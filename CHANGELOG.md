# Changelog

All notable changes to Argus are documented here.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and the project follows
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html). Before 1.0.0, minor versions may change behaviour.

## [Unreleased]

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

[Unreleased]: https://github.com/VectorMan896/Argus/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/VectorMan896/Argus/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/VectorMan896/Argus/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/VectorMan896/Argus/releases/tag/v0.1.0