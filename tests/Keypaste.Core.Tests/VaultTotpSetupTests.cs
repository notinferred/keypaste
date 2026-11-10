using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An item's TOTP is set, replaced and removed through its own write, kept as KeePassXC 2.8 keeps one, with the
/// earlier setup in the item's history, a setup <see cref="TotpSpec"/> refuses refused with its words, and the seed in
/// no refusal or edit.
/// </summary>
public sealed class VaultTotpSetupTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    // RFC 6238 Appendix B's SHA-1 seed in base32, whose 6-digit code at 59 s is 287082; the SHA-256 seed's 8-digit code there is 46119246.
    private const string _sha1Base32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string _sha256Base32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA";
    private const string _sha256Uri = $"otpauth://totp/GitHub:alice?secret={_sha256Base32}&issuer=GitHub&algorithm=SHA256&digits=8&period=30";

    private static readonly EntryName _github = new("Work", "GitHub");
    private static readonly DateTimeOffset _at = DateTimeOffset.FromUnixTimeSeconds(59);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-totp-setup-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_setup_key_is_kept_as_the_protected_otp_URI_KeePassXC_writes_with_its_defaults()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            Assert.True(vault.SetTotp(_github, "  gezd gnbv gy3t qojq gezd gnbv gy3t qojq  "));
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var spec = reopened.ReadTotp(_github, out var refusal);

        Assert.Equal($"otpauth://totp/GitHub?secret={_sha1Base32}&period=30&digits=6&issuer=GitHub", reopened.ReadField(_github, TotpSpec.OtpField));
        Assert.Contains(new EntryField(TotpSpec.OtpField, true, true), reopened.Fields(_github)!);
        Assert.Null(refusal);
        Assert.Equal((TotpAlgorithm.Sha1, 6, TimeSpan.FromSeconds(30)), (spec!.Algorithm, spec.Digits, spec.Period));
        Assert.Equal("287082", spec.Code(_at).Value);
    }

    [Fact]
    public void A_pasted_otpauth_URI_is_kept_as_given()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.True(vault.SetTotp(_github, $"\n{_sha256Uri} "));

        using var spec = vault.ReadTotp(_github, out _);

        Assert.Equal(_sha256Uri, vault.ReadField(_github, TotpSpec.OtpField));
        Assert.Equal("46119246", spec!.Code(_at).Value);
    }

    [Fact]
    public void Replacing_keeps_the_earlier_setup_in_the_history_and_drops_KeePassXCs_older_pair()
    {
        using var vault = Seeded(NewVaultPath());
        vault.SetFieldsUnchecked(_github, [new FieldWrite("TOTP Seed", _sha1Base32), new FieldWrite("TOTP Settings", "30;6", Protect: false)]);
        var before = vault.ReadHistory(_github)!.Count;

        Assert.True(vault.SetTotp(_github, _sha256Uri));

        Assert.Equal(before + 1, vault.ReadHistory(_github)!.Count);
        Assert.Equal([TotpSpec.OtpField], vault.Fields(_github)!.Select(field => field.Name));
        using (var spec = vault.ReadTotp(_github, out _))
        {
            Assert.Equal("46119246", spec!.Code(_at).Value);
        }

        Assert.True(vault.SetTotp(_github, _sha1Base32));
        Assert.Equal(before + 2, vault.ReadHistory(_github)!.Count);

        Assert.True(vault.RestoreRevision(_github, 0));
        Assert.Equal(_sha256Uri, vault.ReadField(_github, TotpSpec.OtpField));
    }

    [Fact]
    public void Removing_takes_otp_and_the_older_pair_keeps_them_in_the_history_and_leaves_KeePass_2s_fields()
    {
        using var vault = Seeded(NewVaultPath());
        Assert.True(vault.SetTotp(_github, _sha256Uri));
        vault.SetFieldsUnchecked(_github, [new FieldWrite("TOTP Seed", _sha1Base32), new FieldWrite("TimeOtp-Length", "8", Protect: false)]);
        var before = vault.ReadHistory(_github)!.Count;

        Assert.True(vault.RemoveTotp(_github));

        Assert.Equal(["TimeOtp-Length"], vault.Fields(_github)!.Select(field => field.Name));
        Assert.Equal(before + 1, vault.ReadHistory(_github)!.Count);
        Assert.False(vault.RemoveTotp(_github));
        Assert.Equal(before + 1, vault.ReadHistory(_github)!.Count);

        Assert.True(vault.RestoreRevision(_github, 0));
        Assert.Equal(_sha256Uri, vault.ReadField(_github, TotpSpec.OtpField));
    }

    [Fact]
    public void An_entry_that_is_not_there_is_neither_set_nor_removed()
    {
        using var vault = Seeded(NewVaultPath());
        var missing = new EntryName("Work", "Nobody");

        Assert.False(vault.SetTotp(missing, _sha1Base32));
        Assert.False(vault.RemoveTotp(missing));
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Theory]
    [InlineData("", "Type or paste the setup key or otpauth://totp/ link the site gave you.")]
    [InlineData("GEZDGNBV!Y3TQOJQ", "The otp field's secret is not base32.")]
    [InlineData("otpauth://hotp/GitHub?secret=GEZDGNBVGY3TQOJQ&counter=1", "The otp field holds a counter-based HOTP, which keypaste does not compute.")]
    [InlineData("otpauth://totp/GitHub?secret=GEZDGNBVGY3TQOJQ&digits=10", "The otp field's digits must be 6, 7 or 8.")]
    [InlineData("otpauth://totp/GitHub?issuer=GitHub", "The otp field has no secret.")]
    [InlineData("https://example.com/?secret=GEZDGNBVGY3TQOJQ", "The otp field's secret is not base32.")]
    public void A_setup_TotpSpec_refuses_is_refused_in_its_words_changing_nothing(string setup, string says)
    {
        var path = NewVaultPath();
        using var vault = Seeded(path);
        var fileBefore = File.ReadAllBytes(path);
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.False(TotpSetup.IsAccepted(setup, out var refusal));
        Assert.Equal(says, refusal);
        Assert.Equal(says, Assert.Throws<VaultException>(() => vault.SetTotp(_github, setup)).Message);

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.Fields(_github)!);
        Assert.Empty(vault.ReadHistory(_github)!);
        Assert.Equal(fileBefore, File.ReadAllBytes(path));
    }

    [Fact]
    public void No_refusal_or_edit_carries_the_seed()
    {
        const string seed = "JBSWY3DPEHPK3PXP";
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        string[] refused =
        [
            Assert.Throws<VaultException>(() => vault.SetTotp(_github, $"otpauth://totp/x?secret={seed}&digits=4")).Message,
            Assert.Throws<VaultException>(() => vault.SetTotp(_github, $"otpauth://totp/x?secret={seed}&secret={seed}")).Message,
            Assert.Throws<VaultException>(() => vault.SetTotp(_github, $"otpauth://totp/x?secret={seed}&algorithm=MD5")).Message,
            Assert.Throws<VaultException>(() => vault.SetTotp(_github, seed + "1")).Message,
            Assert.Throws<VaultException>(() => vault.CreateEntry(new VaultEntry { Title = "New", GroupPath = "Work" }, [], [], seed + "!")).Message,
        ];
        Assert.True(vault.SetTotp(_github, seed));
        Assert.True(vault.RemoveTotp(_github));

        Assert.All(refused, message => Assert.DoesNotContain(seed[..8], message, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, edits.Count);
        Assert.All(edits, edit =>
        {
            Assert.Equal([_github], edit.Entries);
            Assert.DoesNotContain(seed, edit.ToString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void A_new_item_is_created_with_its_TOTP_in_one_change_with_no_history_item()
    {
        var path = NewVaultPath();
        var created = new EntryName("Work", "GitLab");

        using (var vault = Seeded(path))
        {
            vault.CreateEntry(new VaultEntry { Title = created.Title, GroupPath = created.GroupPath, Username = "alice" }, [new FieldWrite("PIN", "1234")], [], _sha1Base32);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        using var spec = reopened.ReadTotp(created, out _);

        Assert.Equal($"otpauth://totp/GitLab?secret={_sha1Base32}&period=30&digits=6&issuer=GitLab", reopened.ReadField(created, TotpSpec.OtpField));
        Assert.Contains(new EntryField(TotpSpec.OtpField, true, true), reopened.Fields(created)!);
        Assert.Equal("287082", spec!.Code(_at).Value);
        Assert.Empty(reopened.ReadHistory(created)!);
    }

    [Fact]
    public void A_new_item_whose_TOTP_is_refused_is_not_created()
    {
        using var vault = Seeded(NewVaultPath());
        var created = new EntryName("Work", "GitLab");

        var refused = Assert.Throws<VaultException>(() => vault.CreateEntry(new VaultEntry { Title = created.Title, GroupPath = created.GroupPath }, [], [], "otpauth://totp/x?secret=GEZDGNBV&digits=9"));

        Assert.Equal("The otp field's digits must be 6, 7 or 8.", refused.Message);
        Assert.Null(vault.Find(created));
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void Otp_stays_refused_as_an_ordinary_custom_field()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Throws<VaultException>(() => vault.SetFields(_github, [new FieldWrite(TotpSpec.OtpField, _sha256Uri)]));
        Assert.Throws<VaultException>(() => vault.CreateEntry(new VaultEntry { Title = "GitLab", GroupPath = "Work" }, [new FieldWrite(TotpSpec.OtpField, _sha256Uri)], [], null));
        Assert.True(vault.SetTotp(_github, _sha256Uri));
        Assert.Throws<VaultException>(() => vault.RemoveField(_github, TotpSpec.OtpField));
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
