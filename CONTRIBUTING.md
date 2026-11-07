# Contributing to Argus

Thanks for helping. Argus closes and changes things on people's PCs, so safety comes first.

## Ground rules
- **Never test on real programs.** Use throwaway processes (a renamed copy of `ping.exe`), your own test folders and
  fake registry entries, and clean them up. Use `--config DIR` so tests do not touch your real settings.
- Every action must be reversible or confirmed. New features that delete or kill need a safety check and a test that
  proves the protected cases are refused.
- Keep defaults conservative: new detectors start on *Ask*, not *Automatic*.
- No network access, no telemetry, no elevation prompts.

## Build
```bash
dotnet build -c Release
```
Requires the .NET 10 SDK. The code is organised by job: `Detector` (what to flag), `Safety` (what is protected),
`Closer`/`Actions` (what to do), `Watcher` (the loop), `Perf*` (older-PC helpers), `Picker` (click-to-close), `MainForm*` (UI).

## Versioning and releases
Argus uses [Semantic Versioning](https://semver.org/). The version lives in one place: `<Version>` in `Argus.csproj`.

1. Move the entries under `## [Unreleased]` in `CHANGELOG.md` into a new `## [x.y.z] - YYYY-MM-DD` section.
2. Bump `<Version>` in `Argus.csproj`.
3. Commit, then tag `vx.y.z`.

Every user-visible change needs a line in `CHANGELOG.md` under *Added*, *Changed*, *Deprecated*, *Removed*, *Fixed* or *Security*.

## Pull requests
Describe what changed and how you tested it, and include the test steps that proved protected programs are left alone.
## Logo and UI assets
The original artwork and the UI reference live in `assets/source/`. Never edit the generated files in `assets/logo/` by hand;
regenerate them (needs Python with Pillow and numpy):

```bash
python tools/make_logo_assets.py assets/source/argus-logo-original.webp assets/logo
```

The owl keeps its original colours everywhere. It is mostly black, so on dark screens it sits on a light tile.
Colours and fonts for the UI are defined once, in `Theme` in `Ui.cs`.

## Tests
```bash
dotnet test tests/Argus.Tests/Argus.Tests.csproj
```
The suite covers the safety rails, so run it before every pull request and add a test for anything that decides what may be closed or deleted.
Tests only touch throwaway programs (renamed copies of `ping.exe`), their own folders inside your temp folder, and one fake startup entry that
is removed again. If you change a safety rule, check the tests can fail: break the rule on purpose and confirm the relevant tests go red.

## Portable build
`powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` builds the self-contained zip in `dist/`. A version tag (`vX.Y.Z`) makes CI do the same
and attach it to a GitHub release.
