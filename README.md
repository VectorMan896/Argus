# Argus

<p align="center"><img src="assets/logo/argus-logo.png" alt="Argus" width="220"></p>

*Named after Argus Panoptes, the hundred-eyed watchman of Greek myth.*

Argus is a small Windows tray app that keeps an eye on your PC. It closes analytics/telemetry programs, deals with hung
programs and background resource hogs, neutralises invisible windows that swallow your clicks, and helps older PCs run
better. It has exclusion lists, and a "click a window to close it" tool.

**Version:** 0.3.0 (early; see [CHANGELOG.md](CHANGELOG.md)). **Platform:** Windows 10/11, x64. **Network:** none, ever.

## What it does

| Category | What Argus looks for | Default |
|---|---|---|
| Analytics / telemetry | programs matching `blocklist.txt` (e.g. `*telemetry*`, `CompatTelRunner`) | close automatically |
| Not responding | a window that has not responded for 20 s | ask you |
| Background resource hogs | no visible window and busy CPU/memory for 60 s | slow it down (lower priority + efficiency mode), never close |
| Suspicious programs | disguised system names, temp-folder programs, random names, no publisher | ask you |
| Invisible blockers | fully transparent windows or stuck popup windows that catch clicks | make click-through / hide |

Each category can be set to **Off**, **Ask** or **Auto** on the Rules page.

### The window
A slim icon sidebar (white circle = current page) and rounded dark cards:

| Page | What is there |
|---|---|
| Overview | status, counters since Argus started, live CPU and GPU graphs, memory dial, system meters, latest activity |
| Needs a decision | everything Argus flagged but did not handle on its own, with Close / Kill / Never touch / Details |
| Performance | tips from your real numbers, startup programs, free up memory, old temp files |
| Rules | Off / Ask / Auto per category, thresholds, options |
| Lists | the blocklist and the exclusions, editable |
| Activity | everything Argus did, and why (also on the Overview as a short peek with a button) |

### Click a window to close it
Press **Ctrl+Alt+K** (or use the button / tray menu). A border follows the window under your cursor (orange if it is an
invisible click-catcher). Click to select; right-click or Esc cancels. You get the full stack of windows under the click with
*Close window*, *Close program*, *Kill program now*, *Hide window*, *Make click-through* and *Exclude program*.

### Older-PC performance
- Plain-language tips based on your real RAM, free disk space, uptime and startup list.
- **Startup programs:** untick to stop one starting with Windows (same mechanism as Task Manager; nothing is deleted).
- **Free up memory:** asks Windows to page out idle background programs. Nothing is closed.
- **Temp cleanup:** manual only, your own temp folder only, old files only (default 7 days), asks first.
- Optional auto-free of memory when available RAM drops below a percentage you choose (off by default).

### Exclusions
`exclusions.txt` lists what Argus must never touch. One pattern per line, `*` and `?` wildcards:

```
obs64                     # program name
path:C:\Games\*           # file path
title:*Premiere*          # window title
class:CabinetWClass       # window class
```

## Safety
Argus is built so it cannot easily hurt your PC. See [SAFETY.md](SAFETY.md).

## Build and run
Requires the .NET 10 SDK.

```bash
dotnet build -c Release
dotnet run -c Release
```

The executable is `bin\Release\net10.0-windows\win-x64\Argus.exe`. Settings, lists and the log live in `%APPDATA%\Argus`.

## Command line
```
Argus.exe --version
Argus.exe --scan [--apply] [--sample N] [--out FILE]   # report what would be flagged; --apply performs Automatic actions
Argus.exe --stack X Y                                  # windows that would receive a click at X,Y
Argus.exe --pick                                       # click-to-close tool without the main window
Argus.exe --perf                                       # read-only performance overview
Argus.exe --usage [--sample N]                         # print CPU and GPU load once a second
Argus.exe --hidden                                     # start in the tray
Argus.exe --tab N                                      # open a page: 0 Overview ... 5 Activity
Argus.exe --config DIR                                 # use another settings folder
```
Output goes to `--out FILE` (the exe is a GUI-subsystem program).

## Portable build and tests
Make one self-contained `Argus.exe` (about 47 MB, no .NET install needed, so it runs on a PC that has nothing installed):

```bash
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

This writes `dist/Argus-<version>-win-x64.zip` and its `.sha256` checksum. The first publish downloads the .NET runtime packs from NuGet.
The first launch of the portable exe is a few seconds slower because it unpacks its native files to `%TEMP%`.

Run the automated tests (72 so far; they only use throwaway programs, folders and registry entries):

```bash
dotnet test tests/Argus.Tests/Argus.Tests.csproj
```

To run a second copy of Argus beside your own (for testing), set the `ARGUS_INSTANCE` environment variable to any name first.

## Look and feel
The window follows a dark dashboard style (see `assets/source/ui-reference.webp`). Colours, fonts and every custom control
(cards, pill buttons, segmented switches, steppers, meters, dial) live in `Ui.cs`.

The owl always keeps its original colours. It is mostly black, so on dark screens it sits on a light rounded tile.

| File in `assets/logo/` | Use |
|---|---|
| `argus-logo.png` | full logo, light backgrounds |
| `argus-logo-on-dark.png` | full logo, dark backgrounds (owl on a light tile, white text) |
| `argus-icon.png` | owl only, transparent background |
| `argus-icon-tile-light.png` | owl on a light tile: sidebar, Overview badge |
| `argus-wordmark.png`, `-light.png`, `-accent.png` | the ARGUS text alone in black, white or accent blue |
| `argus.ico` | window, tray and exe icon |

They are generated from `assets/source/argus-logo-original.webp` by `tools/make_logo_assets.py`.

## Status
Detection, exclusions, the safe actions and the performance helpers were tested against throwaway test processes,
folders and registry entries, and are now covered by an automated test suite. The new window was checked page by page.
The ideas and known gaps are in [TODO.md](TODO.md).

## Project
[CHANGELOG](CHANGELOG.md) | [TODO](TODO.md) | [CONTRIBUTING](CONTRIBUTING.md) | [SECURITY](SECURITY.md) | [SAFETY](SAFETY.md) | [CODE OF CONDUCT](CODE_OF_CONDUCT.md)

License: [MIT](LICENSE).
