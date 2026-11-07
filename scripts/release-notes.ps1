<#
.SYNOPSIS
  Writes the text for a GitHub release: that version's section of CHANGELOG.md, then the install instructions.

.EXAMPLE
  scripts\release-notes.ps1 -Version 0.4.0                  # prints it
  scripts\release-notes.ps1 -Version 0.4.0 -Out notes.md    # saves it (CI does this when a version tag is pushed)

.NOTES
  Argus's update window shows this text, so the changelog comes first and the install block last.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$changelog = [IO.File]::ReadAllText((Join-Path $root "CHANGELOG.md"))

$esc = [regex]::Escape($Version)
$m = [regex]::Match($changelog, "(?ms)^## \[$esc\][^\r\n]*\r?\n(.*?)(?=^## \[|^\[Unreleased\]:)")
$notes = if ($m.Success) { $m.Groups[1].Value.Trim() } else { "See [CHANGELOG.md](https://github.com/VectorMan896/Argus/blob/main/CHANGELOG.md)." }

$install = [IO.File]::ReadAllText((Join-Path $root ".github\release-install.md")).Replace("<version>", $Version).Trim()
$text = $notes + "`n`n" + $install + "`n"

if ($Out) { [IO.File]::WriteAllText($Out, $text, (New-Object Text.UTF8Encoding($false))) } else { $text }