using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>An item's TOTP reads from the vault as KeePassXC and KeePass 2 keep it, survives keypaste's saves, and its seed stays a protected field (9.2a).</summary>
public sealed class VaultTotpTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _rfcSha256Base32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA";
    private const string _rfcSha512 = "1234567890123456789012345678901234567890123456789012345678901234";

    private static readonly EntryName _checking = new("Banking", "Checking");
    private static readonly EntryName _github = new("Work", "GitHub");
    private static readonly DateTimeOffset _at = DateTimeOffset.FromUnixTimeSeconds(59);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-totp-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_otp_KeePassXC_wrote_reads_and_reads_the_same_after_keypaste_saves_the_entry()
    {
        var path = NewVaultPath();
        KdbxImportTests.WriteForeign(path, _master);
        string before;

        using (var vault = Vault.Open(path, _master))
        {
            using var spec = vault.ReadTotp(_checking, out var refusal);

            Assert.Null(refusal);
            Assert.Equal(TotpAlgorithm.Sha1, spec!.Algorithm);
            Assert.Equal(6, spec.Digits);
            Assert.Equal(TimeSpan.FromSeconds(30), spec.Period);
            before = spec.Code(_at).Value;

            vault.SetFields(_checking, [new FieldWrite("PIN", "9876")]);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var after = reopened.ReadTotp(_checking, out var again);

        Assert.Null(again);
        Assert.Equal(before, after!.Code(_at).Value);
        Assert.Contains(new EntryField(TotpSpec.OtpField, true, true), reopened.Fields(_checking)!);
    }

    [Fact]
    public void An_otp_field_on_an_entry_keypaste_wrote_reads_back_the_same_spec_after_a_save()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            vault.SetFieldsUnchecked(_github, [new FieldWrite(TotpSpec.OtpField, $"otpauth://totp/GitHub:alice?secret={_rfcSha256Base32}&issuer=GitHub&algorithm=SHA256&digits=8&period=30")]);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var spec = reopened.ReadTotp(_github, out var refusal);

        Assert.Null(refusal);
        Assert.Equal(TotpAlgorithm.Sha256, spec!.Algorithm);
        Assert.Equal(8, spec.Digits);
        Assert.Equal(TimeSpan.FromSeconds(30), spec.Period);
        Assert.False(spec.IsSteam);
        Assert.Equal("46119246", spec.Code(_at).Value);
        Assert.Contains(new EntryField(TotpSpec.OtpField, true, true), reopened.Fields(_github)!);
    }

    [Fact]
    public void KeePass_2_fields_keypaste_writes_are_protected_and_read_back()
    {
        var path = NewVaultPath();

        List<FieldWrite> writes =
        [
            new FieldWrite("TimeOtp-Secret", _rfcSha512),
            new FieldWrite("TimeOtp-Algorithm", "HMAC-SHA-512", Protect: false),
            new FieldWrite("TimeOtp-Length", "8", Protect: false),
        ];

        using (var vault = Seeded(path))
        {
            vault.SetFields(_github, writes);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var spec = reopened.ReadTotp(_github, out var refusal);

        Assert.Null(refusal);
        Assert.Equal(TotpAlgorithm.Sha512, spec!.Algorithm);
        Assert.Equal("90693936", spec.Code(_at).Value);
        Assert.Contains(new EntryField("TimeOtp-Secret", true, false), reopened.Fields(_github)!);
    }

    [Fact]
    public void A_missing_entry_or_one_with_no_TOTP_reads_as_none_and_reading_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        using var missing = vault.ReadTotp(new EntryName("Work", "Nobody"), out var missingRefusal);
        using var none = vault.ReadTotp(_github, out var noneRefusal);

        Assert.Null(missing);
        Assert.Null(missingRefusal);
        Assert.Null(none);
        Assert.Null(noneRefusal);
        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void A_malformed_otp_is_refused_naming_the_field_and_never_its_seed()
    {
        using var vault = Seeded(NewVaultPath());
        vault.SetFieldsUnchecked(_github, [new FieldWrite(TotpSpec.OtpField, $"otpauth://totp/GitHub?secret={_rfcSha256Base32}&digits=10")]);

        using var spec = vault.ReadTotp(_github, out var refusal);

        Assert.Null(spec);
        Assert.Equal("The otp field's digits must be 6, 7 or 8.", refusal);
        Assert.DoesNotContain("GEZDGNBV", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Neither_the_spec_nor_a_code_shows_the_seed()
    {
        using var vault = Seeded(NewVaultPath());
        vault.SetFieldsUnchecked(_github, [new FieldWrite(TotpSpec.OtpField, $"otpauth://totp/GitHub?secret={_rfcSha256Base32}&algorithm=SHA256&digits=8")]);

        using var spec = vault.ReadTotp(_github, out _);
        var code = spec!.Code(_at);

        Assert.DoesNotContain("GEZDGNBV", spec.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1234567890", spec.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(code.Value, code.ToString(), StringComparison.Ordinal);
    }

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = _github.Title, GroupPath = _github.GroupPath, Username = "alice", Password = "login-password" });
        vault.Save();
        return vault;
    }

    private string NewVaultPath() => Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
}
