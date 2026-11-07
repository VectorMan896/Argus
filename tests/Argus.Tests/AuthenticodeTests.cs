using System.Diagnostics;

namespace Argus.Tests;

public sealed class SignatureClassifyTests
{
    [Theory]
    [InlineData(0u, "Valid")]
    [InlineData(0x800B0100u, "Unsigned")]       // no signature
    [InlineData(0x800B0001u, "Unsigned")]       // provider unknown
    [InlineData(0x800B0003u, "Unsigned")]       // not a signable file
    [InlineData(0x80096010u, "Invalid")]        // changed after signing
    [InlineData(0x800B0004u, "Invalid")]
    [InlineData(0x800B0101u, "Untrusted")]      // expired
    [InlineData(0x800B0109u, "Untrusted")]      // untrusted root (self-signed)
    [InlineData(0x800B010Au, "Untrusted")]
    [InlineData(0x800B0112u, "Untrusted")]
    [InlineData(0x800B010Cu, "Untrusted")]      // revoked
    [InlineData(0x800B0111u, "Untrusted")]      // explicitly distrusted
    public void Windows_result_codes_map_to_our_states(uint code, string expected) =>
        Assert.Equal(expected, Authenticode.Classify(code, "Pub", "CN=Pub", "CN=CA", "ABCD").State.ToString());

    [Fact]
    public void An_unknown_failure_with_a_signer_is_untrusted_and_without_one_is_unsigned()
    {
        Assert.Equal(SigState.Untrusted, Authenticode.Classify(0x80091234u, "Pub", "CN=Pub", "CN=CA", "ABCD").State);
        Assert.Equal(SigState.Unsigned, Authenticode.Classify(0x80091234u, null, null, null, null).State);
    }
}

public sealed class SignaturePolicyTests
{
    private static SignatureInfo S(SigState st, string pub = "Argus Author", string subject = "CN=Argus Author, O=Argus Author") =>
        st == SigState.Unsigned ? SignatureInfo.None() : new SignatureInfo(st, pub, subject, "CN=CA", "AA", st.ToString());

    [Fact]
    public void Today_an_unsigned_copy_accepts_an_unsigned_or_properly_signed_update()
    {
        Assert.True(SignaturePolicy.AllowUpdate(S(SigState.Unsigned), S(SigState.Unsigned)).Allowed);
        Assert.True(SignaturePolicy.AllowUpdate(S(SigState.Unsigned), S(SigState.Valid)).Allowed);      // the future first signed release
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_tampered_update_is_always_refused(bool currentIsSigned)
    {
        var (ok, why) = SignaturePolicy.AllowUpdate(S(currentIsSigned ? SigState.Valid : SigState.Unsigned), S(SigState.Invalid));
        Assert.False(ok);
        Assert.Contains("changed after it was signed", why);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_update_signed_with_a_certificate_windows_does_not_trust_is_refused(bool currentIsSigned)
    {
        var (ok, why) = SignaturePolicy.AllowUpdate(S(currentIsSigned ? SigState.Valid : SigState.Unsigned), S(SigState.Untrusted));
        Assert.False(ok);
        Assert.Contains("does not trust", why);
    }

    [Fact]
    public void A_signed_copy_refuses_to_be_downgraded_to_an_unsigned_update()
    {
        var (ok, why) = SignaturePolicy.AllowUpdate(S(SigState.Valid), S(SigState.Unsigned));
        Assert.False(ok);
        Assert.Contains("is not signed", why);
    }

    [Fact]
    public void A_signed_copy_refuses_an_update_signed_by_someone_else()
    {
        var other = S(SigState.Valid, "Somebody Else", "CN=Somebody Else");
        var (ok, why) = SignaturePolicy.AllowUpdate(S(SigState.Valid), other);
        Assert.False(ok);
        Assert.Contains("Somebody Else", why);
        Assert.Contains("Argus Author", why);
    }

    [Fact]
    public void A_signed_copy_accepts_an_update_from_the_same_publisher_even_with_a_renewed_certificate()
    {
        var renewed = new SignatureInfo(SigState.Valid, "Argus Author", "CN=Argus Author, O=Argus Author", "CN=CA", "ZZ", "valid");   // new thumbprint, same subject
        Assert.True(SignaturePolicy.AllowUpdate(S(SigState.Valid), renewed).Allowed);
    }
}

/// <summary>Real files, real Windows verification. The test program itself is signed by Microsoft, so no certificate is needed.</summary>
public sealed class AuthenticodeRealFileTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static string SignedMicrosoftExe => Environment.ProcessPath!;      // dotnet.exe or testhost.exe

    [Fact]
    public void A_program_signed_by_microsoft_is_valid_and_names_its_publisher()
    {
        var info = Authenticode.Check(SignedMicrosoftExe);
        Assert.Equal(SigState.Valid, info.State);
        Assert.Contains("Microsoft", info.Publisher);
        Assert.False(string.IsNullOrEmpty(info.Thumbprint));
    }

    [Fact]
    public void A_signed_program_that_was_changed_afterwards_is_invalid()
    {
        string copy = _dir.Combine("changed.exe");
        File.Copy(SignedMicrosoftExe, copy);
        using (var fs = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Position = 0x800;                       // inside the program, not inside the signature
            int b = fs.ReadByte();
            fs.Position = 0x800;
            fs.WriteByte((byte)(b ^ 0xFF));
        }
        var info = Authenticode.Check(copy);
        Assert.Equal(SigState.Invalid, info.State);
        Assert.Contains("Microsoft", info.Publisher);   // it still says who signed it, and that is exactly the problem
    }

    [Fact]
    public void An_unsigned_file_and_a_missing_file_are_unsigned()
    {
        Assert.Equal(SigState.Unsigned, Authenticode.Check(typeof(Updater).Assembly.Location).State);       // Argus's own build output
        Assert.Equal(SigState.Unsigned, Authenticode.Check(_dir.Combine("nope.exe")).State);
        Assert.Equal(SigState.Unsigned, Authenticode.Check(null).State);
    }

    [Fact]
    public void Results_are_cached_until_the_file_changes()
    {
        string copy = _dir.Combine("cached.exe");
        File.Copy(SignedMicrosoftExe, copy);
        var first = Authenticode.Check(copy);
        Assert.Same(first, Authenticode.Check(copy));
        File.AppendAllText(copy, "x");
        Assert.NotSame(first, Authenticode.Check(copy));
    }

    // ---- the "Suspicious" score now uses the signature ----------------------------------------------------------

    private static Detector NewDetector() => new(new Settings(), new PatternList(), new PatternList());

    [Fact]
    public void A_validly_signed_program_in_a_temp_folder_with_no_window_is_not_suspicious()
    {
        string path = _dir.Combine("a3f9c1d27b44e8.exe");                      // temp folder + random name + no window would normally score 4
        File.Copy(SignedMicrosoftExe, path);
        var (score, why) = NewDetector().Score("a3f9c1d27b44e8", path, hasRealWindow: false);
        Assert.Equal(0, score);
        Assert.Contains(why, w => w.StartsWith("signed by"));
    }

    [Fact]
    public void The_same_program_changed_after_signing_is_very_suspicious()
    {
        string path = _dir.Combine("a3f9c1d27b44e8.exe");
        File.Copy(SignedMicrosoftExe, path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)) { fs.Position = 0x800; int b = fs.ReadByte(); fs.Position = 0x800; fs.WriteByte((byte)(b ^ 0xFF)); }
        var (score, why) = NewDetector().Score("a3f9c1d27b44e8", path, hasRealWindow: false);
        Assert.True(score >= 4, $"score was {score}");
        Assert.Contains(why, w => w.Contains("signature no longer matches"));
    }

    [Fact]
    public void An_unsigned_program_scores_as_before()
    {
        string path = _dir.Combine("a3f9c1d27b44e8.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), path);   // unsigned here (Windows signs it by catalog), has a publisher in its properties
        var (score, _) = NewDetector().Score("a3f9c1d27b44e8", path, hasRealWindow: false);
        Assert.Equal(4, score);                                                    // temp folder 2 + no window 1 + random name 1
    }
}
