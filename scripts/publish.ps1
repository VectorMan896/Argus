<#
.SYNOPSIS
  Builds the portable Argus: one self-contained Argus.exe (no .NET install needed) plus a zip.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1

.NOTES
  The first publish downloads the .NET runtime packs from NuGet (needs internet once; later builds use the cache).
  First launch of the portable exe is a few seconds slower because it unpacks its native files to %TEMP%.
#>
param([string]$Out = "dist")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$xml = [xml](Get-Content (Join-Path $root "Argus.csproj"))
$version = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Could not read <Version> from Argus.csproj" }

$dir = Join-Path $Out "Argus-$version-win-x64"
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }

dotnet publish Argus.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:OutDir=obj/portable-build/ -o $dir   # OutDir keeps the normal bin\ folder free (a running Argus may be locking it)
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Optional code signing (see docs/SIGNING.md). With no certificate configured the build is simply unsigned.
$exePath = Join-Path $dir "Argus.exe"
if ($env:ARGUS_SIGN_PFX_BASE64 -and $env:ARGUS_SIGN_PASSWORD) {
    $ts = if ($env:ARGUS_SIGN_TIMESTAMP) { $env:ARGUS_SIGN_TIMESTAMP } else { "http://timestamp.digicert.com" }
    & (Join-Path $PSScriptRoot "sign-file.ps1") -Path $exePath -PfxBase64 $env:ARGUS_SIGN_PFX_BASE64 -Password $env:ARGUS_SIGN_PASSWORD -TimestampServer $ts
} else {
    Write-Host "Argus.exe is not signed (no signing certificate is configured)."
}

foreach ($f in "INSTALL.txt", "README.md", "CHANGELOG.md", "SAFETY.md", "LICENSE") {
    if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) -Destination $dir }
}

$zip = "$dir.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dir "*") -DestinationPath $zip

$exe = Get-Item (Join-Path $dir "Argus.exe")
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Write-Host ("Portable build: {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))
Write-Host ("Zip:            {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
