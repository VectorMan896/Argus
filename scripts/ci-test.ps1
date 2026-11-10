<#
  Runs the test suite the way CI does and, when something fails, prints each failed test as a GitHub annotation and in the run summary.
  GitHub only shows raw logs to people who are signed in; annotations and the summary are on the public run page.

  Usage (locally or in CI):   scripts\ci-test.ps1
  Exit code is the test run's own, so a failure still fails the job.
#>
param([string]$Project = "tests/Argus.Tests/Argus.Tests.csproj", [string]$Configuration = "Release", [string]$Filter = "")

$out = Join-Path ([IO.Path]::GetTempPath()) ("argus-test-output-" + [guid]::NewGuid().ToString("N") + ".txt")
$args2 = @("test", $Project, "-c", $Configuration, "--logger", "console;verbosity=normal")
if ($Filter) { $args2 += @("--filter", $Filter) }
& dotnet @args2 2>&1 | Tee-Object -FilePath $out
$code = $LASTEXITCODE

# collect every failed test with the first lines of its message
$failed = New-Object System.Collections.Generic.List[object]
$cur = $null; $mode = ""
foreach ($line in Get-Content $out) {
    if ($line -match '^\s+Failed (.+?) \[') {
        if ($cur) { $failed.Add($cur) }
        $cur = @{ Name = $Matches[1]; Msg = New-Object System.Collections.Generic.List[string] }; $mode = "start"; continue
    }
    if (-not $cur) { continue }
    if ($line -match '^\s+Error Message:') { $mode = "msg"; continue }
    if ($line -match '^\s+Stack Trace:') { $mode = "stack"; continue }
    if ($line -match '^\s+(Passed|Skipped) ') { $failed.Add($cur); $cur = $null; $mode = ""; continue }
    if ($mode -eq "msg" -and $cur.Msg.Count -lt 8 -and $line.Trim().Length -gt 0) { $cur.Msg.Add($line.Trim()) }
    if ($mode -eq "stack" -and $cur.Msg.Count -lt 10 -and $line -match '^\s+at (Argus\.Tests\.[^\)]*\)).* in .*[\\/](tests[\\/].*:line \d+)') { $cur.Msg.Add("at " + $Matches[1] + " (" + $Matches[2] + ")") }
}
if ($cur) { $failed.Add($cur) }

function Escape-Annotation([string]$s) { $s.Replace('%', '%25').Replace("`r", '').Replace("`n", '%0A') }

if ($failed.Count -gt 0) {
    $shown = 0
    foreach ($f in $failed) {
        if ($shown++ -ge 10) { break }                                   # GitHub shows at most ten annotations per step
        Write-Host ("::error title=Test failed::" + (Escape-Annotation ($f.Name + "`n" + ($f.Msg -join "`n"))))
    }
    if ($env:GITHUB_STEP_SUMMARY) {
        $md = New-Object System.Collections.Generic.List[string]
        $md.Add("### $($failed.Count) test(s) failed"); $md.Add("")
        foreach ($f in $failed) { $md.Add("- **$($f.Name)**"); foreach ($m in $f.Msg) { $md.Add("  - ``$m``") } }
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value ($md -join "`n")
    }
}
Remove-Item $out -Force -ErrorAction SilentlyContinue
exit $code
