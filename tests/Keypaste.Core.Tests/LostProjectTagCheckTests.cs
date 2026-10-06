using KeePassLib;
using KeePassLib.Security;
using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V.11: a project tag an entry lost in a version that changed more than its tags is found, and one
/// removed alone, by keypaste or by another program, is not.
/// </summary>
/// <remarks>
/// Another program's edits go through KeePassLib directly, as a KeePass app on a phone or KeePassXC
/// writes them: one revision, the tags and fields as that app leaves them, at a later second.
/// </remarks>
public sealed class LostProjectTagCheckTests : IDisposable
{
    private const string _master = "lost-project-tag-master-pw";
    private const string _oldValue = "sk_live_LOST_TAG_OLD_7d1a";
    private const string _newValue = "sk_live_LOST_TAG_NEW_42c9";

    private static readonly EntryName _stripe = new("services", "Stripe");
    private static readonly DateTime _first = new(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-lost-tag-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    [Fact]
    public void A_tag_dropped_while_the_password_changed_is_found()
    {
        Seed("env:billing:prod", "kept");
        EditElsewhere("Stripe", _first, entry =>
        {
            entry.Tags = ["kept"];
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, _newValue));
        });

        using var vault = Vault.Open(VaultPath, _master);
        var lost = Assert.Single(LostProjectTagCheck.Scan(vault));

        Assert.Equal(_stripe, lost.Entry);
        Assert.Equal(vault.EntryUuid(_stripe), lost.EntryUuid);
        Assert.Equal("env:billing:prod", lost.Tag);
        Assert.Equal(("billing", "prod"), (lost.Project, lost.Environment));
        Assert.True(lost.Protects);
        Assert.Equal(_first, lost.LostUtc);
    }

    [Fact]
    public void A_tag_removed_alone_is_not_found_whether_keypaste_or_another_program_removed_it()
    {
        Seed("env:billing", "env:web");

        using (var vault = Vault.Open(VaultPath, _master))
        {
            Assert.True(vault.RemoveTags(_stripe, ["env:billing"]));
            vault.Save();
        }

        EditElsewhere("Stripe", _first, entry => entry.Tags = []);

        using var reopened = Vault.Open(VaultPath, _master);

        Assert.Equal(4, reopened.ReadHistory(_stripe)!.Count);
        Assert.Empty(LostProjectTagCheck.Scan(reopened));
    }

    [Fact]
    public void Two_tags_for_one_environment_read_alike()
    {
        Seed("env:billing");
        EditElsewhere("Stripe", _first, entry =>
        {
            entry.Tags = ["env:billing:dev"];
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, _newValue));
        });

        using var vault = Vault.Open(VaultPath, _master);

        Assert.Empty(LostProjectTagCheck.Scan(vault));
    }

    [Fact]
    public void Moving_to_another_environment_with_a_change_finds_the_one_left()
    {
        Seed("env:billing:prod");
        EditElsewhere("Stripe", _first, entry =>
        {
            entry.Tags = ["env:billing:staging"];
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "rotated"));
        });

        using var vault = Vault.Open(VaultPath, _master);

        Assert.Equal(["env:billing:prod"], LostProjectTagCheck.Scan(vault).Select(lost => lost.Tag));
    }

    [Fact]
    public void A_tag_taken_back_is_not_found_and_one_lost_again_is_found_anew()
    {
        Seed("env:billing");
        EditElsewhere("Stripe", _first, entry =>
        {
            entry.Tags = [];
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, _newValue));
        });

        string firstKey;

        using (var vault = Vault.Open(VaultPath, _master))
        {
            firstKey = Assert.Single(LostProjectTagCheck.Scan(vault)).Key;
            Assert.True(vault.AddTag(_stripe, "env:billing"));
            vault.Save();
            Assert.Empty(LostProjectTagCheck.Scan(vault));
        }

        var again = _first.AddDays(1);
        EditElsewhere("Stripe", again, entry =>
        {
            entry.Tags = [];
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "billing-bot"));
        });

        using var reopened = Vault.Open(VaultPath, _master);
        var lost = Assert.Single(LostProjectTagCheck.Scan(reopened));

        Assert.Equal(again, lost.LostUtc);
        Assert.NotEqual(firstKey, lost.Key);
    }

    [Fact]
    public void Entries_in_the_recycle_bin_or_keypastes_own_groups_are_not_read()
    {
        using (var vault = Vault.Create(VaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = ".keypaste/tokens", Title = "planted", Password = _oldValue });
            vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Deleted", Password = _oldValue });
            vault.Save();
        }

        foreach (var title in new[] { "planted", "Deleted" })
        {
            EditElsewhere(title, _first.AddMinutes(-1), entry => entry.Tags = ["env:billing"]);
            EditElsewhere(title, _first, entry =>
            {
                entry.Tags = [];
                entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, _newValue));
            });
        }

        using (var vault = Vault.Open(VaultPath, _master))
        {
            Assert.Single(LostProjectTagCheck.Scan(vault));
            vault.RemoveEntry(new EntryName("services", "Deleted"), out _);
            vault.Save();
        }

        using var reopened = Vault.Open(VaultPath, _master);

        Assert.Empty(LostProjectTagCheck.Scan(reopened));
    }

    [Fact]
    public void A_finding_holds_no_value_in_any_public_member_or_its_text()
    {
        Seed("env:billing:prod");
        EditElsewhere("Stripe", _first, entry =>
        {
            entry.Tags = [];
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, _newValue));
        });

        using var vault = Vault.Open(VaultPath, _master);
        var lost = Assert.Single(LostProjectTagCheck.Scan(vault));
        var shown = string.Join("\n", typeof(LostProjectTag).GetProperties()
            .Select(property => Convert.ToString(property.GetValue(lost), System.Globalization.CultureInfo.InvariantCulture))
            .Append(lost.ToString()));

        Assert.DoesNotContain(_oldValue, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(_newValue, shown, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Seed(params string[] tags)
    {
        using var vault = Vault.Create(VaultPath, _master);
        vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = _oldValue });

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(_stripe, tag));
        }

        vault.Save();
    }

    private void EditElsewhere(string title, DateTime at, Action<PwEntry> edit) =>
        ForeignEdit.Apply(VaultPath, _master, title, at, edit);
}
