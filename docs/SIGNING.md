# Signing Argus

A signed `Argus.exe` does three things that an unsigned one cannot:

1. **Windows shows who published it** instead of "Unknown publisher", and the SmartScreen warning ("Windows protected your PC") fades as the
   signed file builds a reputation. A new certificate may still trigger the warning for a while.
2. **The updater can tell a real update from a replaced one.** A checksum published next to a download cannot: someone who takes over the GitHub
   release can publish a matching checksum for a bad file. A signature cannot be forged without the certificate's private key.
3. **"Suspicious" gets sharper.** A program with a valid signature is not suspicious just for living in a temp folder or having no window.

Signing needs a code-signing certificate. Argus's code and build are ready for one; you only have to get a certificate and add two secrets.
Nothing here changes anything until you do.

## What Argus does with signatures

- **Updater rules** (`SignaturePolicy` in `Authenticode.cs`):
  - an update whose signature no longer matches the file (changed after signing) is always refused;
  - an update signed with a certificate Windows does not trust (self-signed, expired, revoked, unknown authority) is always refused;
  - a copy of Argus that is itself validly signed only accepts an update that is validly signed by the **same publisher** (the certificate's subject,
    so renewing the certificate is fine), so a signed Argus cannot be downgraded to an unsigned or differently signed file;
  - an unsigned copy (today's releases) accepts an unsigned or validly signed update. That is how the first signed release reaches people.
- **Checking a file yourself:** `Argus.exe --signature path\to\file.exe` prints the state, publisher, subject, issuer and thumbprint.
  You can also right-click a file, **Properties**, **Digital Signatures**.
- Checks use Windows' own `WinVerifyTrust`. No network is used (revocation lookups are off).

## Getting a certificate

Options change, so check each one's current terms and whether it is available to you. In rough order of effort:

- **A code-signing service for open-source projects.** Some organisations sign open-source projects for free if the project meets their criteria and
  builds in public CI (SignPath Foundation is one). You apply, and signing happens in their system from your GitHub workflow. This needs a different
  workflow step than the one provided here (their GitHub Action instead of a `.pfx`).
- **A cloud signing service** (for example Microsoft's Trusted Signing / Artifact Signing). Typically a monthly fee; identity validation required;
  eligibility depends on your country and whether you are an individual or an organisation.
- **A traditional code-signing certificate** from a certificate authority (OV or EV). A yearly fee and identity verification. Recent rules mean the
  private key must live on a hardware token or in a cloud HSM, so you may not get a plain `.pfx` file; if the issuer only offers a token, use their
  cloud-signing option or a self-hosted runner with the token.

Whatever you choose, the certificate's **subject** (the name it shows as publisher) is what a signed Argus will pin future updates to. Pick the name
you want to keep, and keep it the same when you renew.

## Using a `.pfx` certificate with the provided workflow

1. Export the certificate and its private key as a password-protected `.pfx` file. **Never commit it** (`*.pfx` is in `.gitignore`).
2. Turn it into text: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx")) | Set-Clipboard`
3. In the GitHub repository: *Settings, Secrets and variables, Actions, New repository secret*:
   - `ARGUS_SIGN_PFX_BASE64`: paste the text
   - `ARGUS_SIGN_PASSWORD`: the `.pfx` password
4. Push a version tag. `scripts/publish.ps1` sees the secrets and runs `scripts/sign-file.ps1`, which signs `Argus.exe` with SHA-256 and a
   timestamp (`http://timestamp.digicert.com` unless `ARGUS_SIGN_TIMESTAMP` says otherwise) before the zip is made. With no secrets it prints
   "Argus.exe is not signed" and carries on.
5. Check the result: download the release zip, extract it, and run `Argus.exe --signature Argus.exe`. It should say `Valid` and show your name.

To sign a local build: `scripts\sign-file.ps1 -Path dist\Argus-<version>-win-x64\Argus.exe -PfxBase64 <text> -Password <password> -TimestampServer http://timestamp.digicert.com`
(before zipping it).

## Things to know

- Sign **after** the single-file exe is built (the script does), not before.
- The timestamp keeps a signature valid after the certificate expires. Do not skip it for releases.
- If you ever lose control of the certificate, revoke it with the issuer and publish a release signed with a new certificate under the **same
  subject**. Copies pinned to the old subject will accept it; copies that only knew the old thumbprint would not, which is why the pin is the subject.
- A signature proves who published a file and that it was not changed. It does not prove the program is safe; that is what the source and the tests are for.
