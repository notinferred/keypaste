using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Custom fields: listed by name, read one at a time, and written as KeePassXC reads them.
/// </summary>
public sealed class VaultFieldTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _stripe = new("api", "Stripe");
    private static readonly EntryName _checking = new("Banking", "Checking");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-field-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_new_field_is_protected_unless_its_write_says_plain_and_both_survive_a_save()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            Assert.True(vault.SetFields(_stripe, [new FieldWrite("STRIPE_SECRET_KEY", "sk_test_1"), new FieldWrite("Region", "eu", Protect: false)]));
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);

        Assert.Equal(
            [new EntryField("Region", false, false), new EntryField("STRIPE_SECRET_KEY", true, false)],
            reopened.Fields(_stripe));
        Assert.Equal("sk_test_1", reopened.ReadField(_stripe, "STRIPE_SECRET_KEY"));
        Assert.Equal("eu", reopened.ReadField(_stripe, "Region"));
    }

    [Fact]
    public void An_existing_field_keeps_its_flag_unless_a_write_names_one()
    {
        using var vault = Seeded(NewVaultPath());
        vault.SetFields(_stripe, [new FieldWrite("Region", "eu", Protect: false), new FieldWrite("KEY", "k1")]);

        vault.SetFields(_stripe, [new FieldWrite("Region", "us"), new FieldWrite("KEY", "k2")]);
        Assert.Equal([new EntryField("KEY", true, false), new EntryField("Region", false, false)], vault.Fields(_stripe));

        vault.SetFields(_stripe, [new FieldWrite("Region", Value: null, Protect: true), new FieldWrite("KEY", Value: null, Protect: false)]);
        Assert.Equal([new EntryField("KEY", false, false), new EntryField("Region", true, false)], vault.Fields(_stripe));
        Assert.Equal("us", vault.ReadField(_stripe, "Region"));
        Assert.Equal("k2", vault.ReadField(_stripe, "KEY"));
    }

    [Fact]
    public void Setting_three_fields_is_one_revision_and_one_edit_naming_the_entry()
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.True(vault.SetFields(_stripe, [new FieldWrite("A", "1"), new FieldWrite("B", "2"), new FieldWrite("C", "3")]));

        Assert.Single(vault.ReadHistory(_stripe)!);
        Assert.Equal([_stripe], Assert.Single(edits).Entries);
        Assert.Equal(SavedRead.Unsaved, vault.ReadSaved(out _));
    }

    [Fact]
    public void Removing_a_field_is_one_revision_and_its_value_stays_in_history()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            vault.SetFields(_stripe, [new FieldWrite("KEY", "removed-value")]);
            vault.Save();

            Assert.True(vault.RemoveField(_stripe, "KEY"));
            vault.Save();
        }

        using var reopened = KeePassInteropFor(path);
        var facts = reopened.FactsUnchecked(_stripe)!;
        Assert.False(facts.Strings.ContainsKey("KEY"));
        Assert.Equal(2, facts.HistoryUuids.Count);
    }

    [Fact]
    public void Removing_a_field_the_entry_does_not_have_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.False(vault.RemoveField(_stripe, "ABSENT"));
        Assert.False(vault.RemoveField(new EntryName("api", "Nobody"), "KEY"));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.ReadHistory(_stripe)!);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("TITLE")]
    [InlineData("UserName")]
    [InlineData("url")]
    [InlineData("Notes")]
    [InlineData("otp")]
    [InlineData("OTP")]
    [InlineData("TOTP Seed")]
    [InlineData("TOTP Settings")]
    [InlineData("_EXEC_CMD")]
    [InlineData("KP2A_URL")]
    [InlineData("KP2A_URL_1")]
    [InlineData("KPEX_PASSKEY_USERNAME")]
    [InlineData("KPXC_DECRYPTED")]
    public void A_refused_name_changes_nothing(string name)
    {
        using var vault = Seeded(NewVaultPath());
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);

        Assert.False(FieldNameRules.IsWritable(name, out var error));
        Assert.NotEmpty(error);
        Assert.Throws<VaultException>(() => vault.SetFields(_stripe, [new FieldWrite("FINE", "1"), new FieldWrite(name, "2")]));
        Assert.Throws<VaultException>(() => vault.RemoveField(_stripe, name));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.Fields(_stripe)!);
    }

    [Fact]
    public void A_name_written_twice_or_a_kept_value_of_a_missing_field_changes_nothing()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Throws<VaultException>(() => vault.SetFields(_stripe, [new FieldWrite("KEY", "1"), new FieldWrite("KEY", "2")]));
        Assert.Throws<VaultException>(() => vault.SetFields(_stripe, [new FieldWrite("NEW", "1"), new FieldWrite("ABSENT", Value: null, Protect: true)]));
        Assert.Throws<VaultException>(() => vault.SetFields(_stripe, []));

        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.Empty(vault.Fields(_stripe)!);
        Assert.Empty(vault.ReadHistory(_stripe)!);
    }

    [Fact]
    public void An_entry_that_is_not_there_is_answered_with_null_or_false()
    {
        using var vault = Seeded(NewVaultPath());
        var nobody = new EntryName("api", "Nobody");

        Assert.Null(vault.Fields(nobody));
        Assert.Null(vault.ReadField(nobody, "KEY"));
        Assert.Null(vault.ReadField(_stripe, "KEY"));
        Assert.False(vault.SetFields(nobody, [new FieldWrite("KEY", "1")]));
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void A_standard_field_is_never_read_as_a_custom_one()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Throws<ArgumentException>(() => vault.ReadField(_stripe, "Password"));
        Assert.Throws<ArgumentException>(() => vault.ReadField(_stripe, "password"));
        Assert.DoesNotContain(vault.Fields(_stripe)!, field => FieldNameRules.IsStandard(field.Name));
    }

    [Fact]
    public void Fields_another_client_wrote_are_listed_and_readable_and_KeePassXC_attributes_are_read_only()
    {
        var path = NewVaultPath();
        KdbxImportTests.WriteForeign(path, _master);

        using var vault = Vault.Open(path, _master);

        Assert.Equal([new EntryField("PIN", true, false), new EntryField("otp", true, true)], vault.Fields(_checking));
        Assert.Equal("4321", vault.ReadField(_checking, "PIN"));
        Assert.StartsWith("otpauth://", vault.ReadField(_checking, "otp"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_write_leaves_the_other_fields_attachments_and_tags_as_they_were()
    {
        var path = NewVaultPath();
        KdbxImportTests.WriteForeign(path, _master);

        using (var vault = Vault.Open(path, _master))
        {
            vault.SetFields(_checking, [new FieldWrite("PIN", "9876"), new FieldWrite("IBAN", "DE00")]);
            vault.Save();
        }

        using var reopened = KeePassInteropFor(path);
        var facts = reopened.FactsUnchecked(_checking)!;
        Assert.Equal("9876", facts.Strings["PIN"]);
        Assert.Equal("DE00", facts.Strings["IBAN"]);
        Assert.StartsWith("otpauth://", facts.Strings["otp"], StringComparison.Ordinal);
        Assert.Equal("v2", facts.Strings["Password"]);
        Assert.Equal("holder", facts.Strings["UserName"]);
        Assert.Contains("statement.txt", facts.Attachments.Keys);
        Assert.Equal(["finance"], facts.Tags);
    }

    [Fact]
    public void UpdateEntry_never_writes_a_custom_field_or_its_flag()
    {
        using var vault = Seeded(NewVaultPath());
        vault.SetFields(_stripe, [new FieldWrite("KEY", "k"), new FieldWrite("Region", "eu", Protect: false)]);

        var entry = vault.Find(_stripe)!;
        Assert.True(vault.UpdateEntry(entry with { Password = "rotated", Notes = "changed" }));

        Assert.Equal([new EntryField("KEY", true, false), new EntryField("Region", false, false)], vault.Fields(_stripe));
        Assert.Equal("k", vault.ReadField(_stripe, "KEY"));
        Assert.Equal("eu", vault.ReadField(_stripe, "Region"));
    }

    [Fact]
    public void A_write_is_never_shown_by_its_value()
    {
        var write = new FieldWrite("KEY", "the-value");

        Assert.DoesNotContain("the-value", write.ToString(), StringComparison.Ordinal);
    }

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = _stripe.Title, GroupPath = _stripe.GroupPath, Password = "login-password" });
        vault.Save();
        return vault;
    }

    private static KeePassInterop KeePassInteropFor(string path) =>
        KeePassInterop.Open(path, System.Text.Encoding.UTF8.GetBytes(_master));

    private string NewVaultPath() => System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
}
