using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Digital signatures (Authenticode), checked with Windows' own WinVerifyTrust.
//
//  Used for two things:
//    * the "Suspicious" check: a program with a valid signature from a trusted publisher is not suspicious just for
//      living in a temp folder or having no window; a program whose signature no longer matches its file is very suspicious
//    * the updater: a signed copy of Argus only accepts an update signed by the same publisher
//
//  No network is used (revocation lookups are off and URL retrieval is cache-only).
//  Windows' own files are signed through catalogs, not inside each file, so they report as "unsigned" here; they are
//  skipped by location anyway.
// -------------------------------------------------------------------------------------------------

internal enum SigState
{
    Unsigned,     // no embedded signature
    Valid,        // signed, trusted by Windows, file not modified since signing
    Untrusted,    // signed, but the certificate is expired / self-signed / from an unknown authority / distrusted
    Invalid,      // signed, but the file was modified after signing
}

internal sealed record SignatureInfo(SigState State, string? Publisher, string? Subject, string? Issuer, string? Thumbprint, string Detail)
{
    public static SignatureInfo None(string detail = "not signed") => new(SigState.Unsigned, null, null, null, null, detail);
}

internal static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000, WTD_REVOCATION_CHECK_NONE = 0x10;

    private static readonly Dictionary<string, (DateTime Stamp, long Length, SignatureInfo Info)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    /// <summary>The signature state of one file. Cached per path until the file changes. Never throws.</summary>
    public static SignatureInfo Check(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return SignatureInfo.None("file not found");
        try
        {
            var fi = new FileInfo(path);
            lock (CacheLock)
                if (Cache.TryGetValue(path, out var c) && c.Stamp == fi.LastWriteTimeUtc && c.Length == fi.Length) return c.Info;
            var info = Compute(path);
            lock (CacheLock) Cache[path] = (fi.LastWriteTimeUtc, fi.Length, info);
            return info;
        }
        catch (Exception ex) { return SignatureInfo.None("could not check (" + ex.Message + ")"); }
    }

    private static int Verify(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE,
            };
            var action = GenericVerifyV2;
            int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.dwStateAction = WTD_STATEACTION_CLOSE;                 // always release Windows' verification state
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
    }

    private static (string? Publisher, string? Subject, string? Issuer, string? Thumbprint) Signer(string path)
    {
        try
        {
            // CreateFromSignedFile is the only built-in way to read a signer out of a signed exe; X509CertificateLoader cannot do that
            // (it loads plain certificate files), and the alternative would be a NuGet package. WinVerifyTrust above decides trust, not this.
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            string? pub = cert.GetNameInfo(X509NameType.SimpleName, false);
            return (string.IsNullOrWhiteSpace(pub) ? null : pub, cert.Subject, cert.Issuer, cert.Thumbprint);
        }
        catch (CryptographicException) { return default; }               // no embedded signature
        catch (Exception) { return default; }
    }

    private static SignatureInfo Compute(string path)
    {
        uint code;
        try { code = unchecked((uint)Verify(path)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { return SignatureInfo.None("signature check is not available on this PC"); }

        var (pub, subj, iss, thumb) = Signer(path);
        return Classify(code, pub, subj, iss, thumb);
    }

    /// <summary>Turns a WinVerifyTrust result code into our four states. Separate so it can be tested without real signed files.</summary>
    internal static SignatureInfo Classify(uint code, string? pub, string? subj, string? iss, string? thumb)
    {
        switch (code)
        {
            case 0:
                return new SignatureInfo(SigState.Valid, pub, subj, iss, thumb, "valid signature");
            case 0x800B0100:   // TRUST_E_NOSIGNATURE
            case 0x800B0001:   // TRUST_E_PROVIDER_UNKNOWN
            case 0x800B0003:   // TRUST_E_SUBJECT_FORM_UNKNOWN (not a file type that can be signed)
                return SignatureInfo.None();
            case 0x80096010:   // TRUST_E_BAD_DIGEST: the file changed after it was signed
            case 0x800B0004:   // TRUST_E_SUBJECT_NOT_TRUSTED
                return new SignatureInfo(SigState.Invalid, pub, subj, iss, thumb, "the file was changed after it was signed");
            case 0x800B0101:   // CERT_E_EXPIRED
                return new SignatureInfo(SigState.Untrusted, pub, subj, iss, thumb, "the certificate has expired");
            case 0x800B0109:   // CERT_E_UNTRUSTEDROOT (includes self-signed)
            case 0x800B0112:   // CERT_E_UNTRUSTEDCA
            case 0x800B010D:   // CERT_E_UNTRUSTEDTESTROOT
            case 0x800B010A:   // CERT_E_CHAINING
                return new SignatureInfo(SigState.Untrusted, pub, subj, iss, thumb, "the certificate is self-signed or from an authority Windows does not trust");
            case 0x800B010C:   // CERT_E_REVOKED
                return new SignatureInfo(SigState.Untrusted, pub, subj, iss, thumb, "the certificate was revoked");
            case 0x800B0111:   // TRUST_E_EXPLICIT_DISTRUST
                return new SignatureInfo(SigState.Untrusted, pub, subj, iss, thumb, "the publisher is explicitly distrusted");
            default:
                return subj != null
                    ? new SignatureInfo(SigState.Untrusted, pub, subj, iss, thumb, $"the signature could not be verified (0x{code:X8})")
                    : SignatureInfo.None($"could not check (0x{code:X8})");
        }
    }
}

/// <summary>When may a downloaded Argus.exe replace the running one, as far as signatures go?</summary>
internal static class SignaturePolicy
{
    /// <summary>
    /// A tampered or untrusted signature on the new file is always refused. A copy that is itself validly signed only accepts an update
    /// that is validly signed by the same publisher, so a replaced release cannot downgrade it to an unsigned or differently signed file.
    /// An unsigned copy (today's releases) accepts an unsigned or validly signed update.
    /// </summary>
    public static (bool Allowed, string Reason) AllowUpdate(SignatureInfo current, SignatureInfo candidate)
    {
        if (candidate.State == SigState.Invalid)
            return (false, "The new Argus.exe has a digital signature that no longer matches the file, so it was changed after it was signed. It was rejected.");
        if (candidate.State == SigState.Untrusted)
            return (false, $"The new Argus.exe is signed, but Windows does not trust that signature ({candidate.Detail}). It was rejected.");

        if (current.State == SigState.Valid)
        {
            if (candidate.State != SigState.Valid)
                return (false, $"This copy of Argus is signed by {current.Publisher ?? "its publisher"}, but the new Argus.exe is not signed. It was rejected.");
            if (!string.Equals(current.Subject, candidate.Subject, StringComparison.OrdinalIgnoreCase))
                return (false, $"The new Argus.exe is signed by {candidate.Publisher ?? "someone else"}, not by {current.Publisher ?? "the publisher of this copy"}. It was rejected.");
            return (true, $"signed by {candidate.Publisher}, same publisher as this copy");
        }
        return candidate.State == SigState.Valid
            ? (true, $"signed by {candidate.Publisher}")
            : (true, "not signed (the checksum is the only check)");
    }
}
