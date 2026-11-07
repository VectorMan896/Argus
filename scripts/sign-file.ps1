<#
.SYNOPSIS
  Signs one file with a code-signing certificate (Authenticode). Used by publish.ps1 when a certificate is configured.

.EXAMPLE
  scripts\sign-file.ps1 -Path dist\Argus.exe -PfxBase64 $env:ARGUS_SIGN_PFX_BASE64 -Password $env:ARGUS_SIGN_PASSWORD

.NOTES
  The certificate arrives as a base64 string (a GitHub secret), is loaded in memory, and is never written to disk by this script.
  A timestamp (-TimestampServer) keeps the signature valid after the certificate itself expires; leave it empty only for tests.
  See docs/SIGNING.md.
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$PfxBase64,
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$TimestampServer = ""
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Path)) { throw "File to sign not found: $Path" }

$bytes = [Convert]::FromBase64String($PfxBase64)
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($bytes, $Password)
try {
    $args = @{ FilePath = $Path; Certificate = $cert; HashAlgorithm = "SHA256" }
    if ($TimestampServer) { $args.TimestampServer = $TimestampServer }
    $r = Set-AuthenticodeSignature @args
    # A self-signed test certificate reports "UnknownError" (root not trusted) but the signature is applied; what matters is that a signer was attached.
    if (-not $r.SignerCertificate) { throw "Signing failed: $($r.StatusMessage)" }
    Write-Host ("Signed {0} as '{1}' (status: {2})" -f (Split-Path $Path -Leaf), $r.SignerCertificate.Subject, $r.Status)
}
finally {
    # remove the temporary private key that loading the .pfx created for this user
    try {
        $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
        if ($rsa -is [System.Security.Cryptography.RSACng]) { $rsa.Key.Delete() }
    } catch { }
}