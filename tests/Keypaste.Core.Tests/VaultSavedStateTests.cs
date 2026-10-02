using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What another process may be answered from: an open vault only while it holds exactly what its
/// file holds, and every change reporting the entries it touched before it is saved (U.3).
/// </summary>
public sealed class VaultSavedStateTests : IDisposable
{
    private const string _masterPassword = "correct horse battery staple";

    private static readonly EntryName _token = new("env/billing", "TOKEN");
    private static readonly EntryName _other = new("env/billing", "OTHER");

    private readonly string _directory;
    private readonly string _path;

    public VaultSavedStateTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-saved-state-tests-").FullName;
        _path = Path.Combine(_directory, "vault.kdbx");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_vault_that_was_never_saved_is_not_read()
    {
        using var vault = Vault.Create(_path, _masterPassword);
        vault.AddEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "v1" });

        Assert.Equal(SavedRead.Unsaved, vault.ReadSaved(out var entries));
        Assert.Null(entries);
    }

    [Fact]
    public void A_saved_vault_and_a_reopened_one_are_read_as_their_file_holds_them()
    {
        using (var vault = Seeded())
        {
            Assert.Equal(SavedRead.Current, vault.ReadSaved(out var entries));
            Assert.Equal("v1", Password(entries!, _token), StringComparer.Ordinal);
        }

        using var reopened = Vault.Open(_path, _masterPassword);

        Assert.Equal(SavedRead.Current, reopened.ReadSaved(out var again));
        Assert.Equal("v1", Password(again!, _token), StringComparer.Ordinal);
    }

    [Fact]
    public void A_change_is_not_read_until_it_is_saved()
    {
        using var vault = Seeded();

        vault.UpdateEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "v2" });

        Assert.Equal(SavedRead.Unsaved, vault.ReadSaved(out var entries));
        Assert.Null(entries);

        vault.Save();

        Assert.Equal(SavedRead.Current, vault.ReadSaved(out entries));
        Assert.Equal("v2", Password(entries!, _token), StringComparer.Ordinal);
    }

    [Fact]
    public void A_change_that_is_refused_leaves_the_vault_current_and_reports_nothing()
    {
        using var vault = Seeded();
        var edits = Watch(vault);

        Assert.Equal(OrganizeOutcome.DestinationMissing, vault.Relocate(_token, new EntryName("nowhere", "TOKEN"), out _));
        Assert.Equal(DeletionOutcome.NothingMatched, vault.RemoveEntry(new EntryName("env/billing", "MISSING")));
        Assert.False(vault.UpdateEntry(new VaultEntry { GroupPath = "env/billing", Title = "MISSING", Password = "x" }));

        Assert.Empty(edits);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void A_file_another_writer_saved_is_refused_until_the_vault_is_opened_again()
    {
        using var vault = Seeded();

        using (var writer = Vault.Open(_path, _masterPassword))
        {
            writer.UpdateEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "external" });
            writer.Save();
        }

        Assert.Equal(SavedRead.ChangedOnDisk, vault.ReadSaved(out var entries));
        Assert.Null(entries);
        Assert.Equal(SavedRead.ChangedOnDisk, vault.ReadSaved(out _));

        using var reopened = Vault.Open(_path, _masterPassword);

        Assert.Equal(SavedRead.Current, reopened.ReadSaved(out entries));
        Assert.Equal("external", Password(entries!, _token), StringComparer.Ordinal);
    }

    [Fact]
    public void An_edit_whose_save_was_refused_is_never_read()
    {
        using var vault = Seeded();

        using (var writer = Vault.Open(_path, _masterPassword))
        {
            writer.UpdateEntry(new VaultEntry { GroupPath = _other.GroupPath, Title = _other.Title, Password = "external" });
            writer.Save();
        }

        var external = File.ReadAllBytes(_path);

        vault.UpdateEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "unsaved" });
        Assert.Throws<VaultChangedOnDiskException>(vault.Save);

        Assert.Equal(SavedRead.ChangedOnDisk, vault.ReadSaved(out var entries));
        Assert.Null(entries);
        Assert.Equal(external, File.ReadAllBytes(_path));
    }

    [Fact]
    public void A_reload_reads_what_another_writer_saved_and_saves_on_from_it()
    {
        using var vault = Seeded();
        ExternalSave(_token, "external");

        using var reloaded = vault.Reload();

        Assert.Equal(SavedRead.ChangedOnDisk, vault.ReadSaved(out _));
        Assert.Equal(SavedRead.Current, reloaded.ReadSaved(out var entries));
        Assert.Equal("external", Password(entries!, _token), StringComparer.Ordinal);

        reloaded.UpdateEntry(new VaultEntry { GroupPath = _other.GroupPath, Title = _other.Title, Password = "o2" });
        reloaded.Save();

        using var reopened = Vault.Open(_path, _masterPassword);
        Assert.Equal(SavedRead.Current, reopened.ReadSaved(out entries));
        Assert.Equal("external", Password(entries!, _token), StringComparer.Ordinal);
        Assert.Equal("o2", Password(entries!, _other), StringComparer.Ordinal);
    }

    [Fact]
    public void A_reload_discards_a_change_whose_save_was_refused()
    {
        using var vault = Seeded();
        ExternalSave(_other, "external");
        vault.UpdateEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "unwritten" });
        Assert.Throws<VaultChangedOnDiskException>(vault.Save);

        using var reloaded = vault.Reload();

        Assert.Equal(SavedRead.Current, reloaded.ReadSaved(out var entries));
        Assert.Equal("v1", Password(entries!, _token), StringComparer.Ordinal);
        Assert.Equal("external", Password(entries!, _other), StringComparer.Ordinal);
    }

    [Fact]
    public void A_reload_after_another_writer_changed_the_password_is_refused_and_changes_nothing()
    {
        using var vault = Seeded();
        using (var writer = Vault.Open(_path, _masterPassword))
        {
            const string next = "a different horse battery staple";
            Assert.Equal(
                VaultAccessOutcome.Changed,
                writer.ChangeAccess(new VaultAccessChange(true, AccessKeyfileChange.Keep), next, next).Outcome);
        }

        var bytes = File.ReadAllBytes(_path);

        Assert.Throws<InvalidMasterPasswordException>(() => vault.Reload().Dispose());
        Assert.Equal(SavedRead.ChangedOnDisk, vault.ReadSaved(out _));
        Assert.Equal(bytes, File.ReadAllBytes(_path));
    }

    [Fact]
    public void A_reload_of_a_keyfile_vault_uses_the_keyfile_it_was_opened_with()
    {
        var keyfile = Path.Combine(_directory, "vault.key");
        File.WriteAllBytes(keyfile, [.. Enumerable.Range(1, 32).Select(i => (byte)i)]);
        using (var created = Vault.CreateWith(_path, _masterPassword, keyfile))
        {
            created.AddEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "v1" });
            created.Save();
        }

        using var vault = Vault.Open(_path, _masterPassword, keyfile);
        using (var writer = Vault.Open(_path, _masterPassword, keyfile))
        {
            writer.UpdateEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "external" });
            writer.Save();
        }

        File.WriteAllBytes(keyfile, [.. Enumerable.Range(101, 32).Select(i => (byte)i)]);

        using var reloaded = vault.Reload();

        Assert.Equal(SavedRead.Current, reloaded.ReadSaved(out var entries));
        Assert.Equal("external", Password(entries!, _token), StringComparer.Ordinal);
    }

    [Fact]
    public void A_reload_of_a_deleted_file_fails_and_leaves_the_vault_as_it_was()
    {
        using var vault = Seeded();
        File.Delete(_path);

        Assert.Throws<VaultException>(() => vault.Reload().Dispose());
        Assert.Equal(SavedRead.Unreadable, vault.ReadSaved(out _));
    }

    [Fact]
    public void A_custom_field_is_read_only_while_the_vault_matches_its_file()
    {
        using var vault = Seeded();
        Assert.True(vault.SetFields(_token, [new FieldWrite("API_KEY", "saved")]));
        vault.Save();

        Assert.Equal(SavedRead.Current, vault.ReadSavedField(_token, "API_KEY", out var value));
        Assert.Equal("saved", value);

        Assert.True(vault.SetFields(_token, [new FieldWrite("API_KEY", "unsaved")]));

        Assert.Equal(SavedRead.Unsaved, vault.ReadSavedField(_token, "API_KEY", out value));
        Assert.Null(value);
    }

    [Fact]
    public void A_custom_field_another_writer_saved_is_refused()
    {
        using var vault = Seeded();
        Assert.True(vault.SetFields(_token, [new FieldWrite("API_KEY", "saved")]));
        vault.Save();

        using (var writer = Vault.Open(_path, _masterPassword))
        {
            Assert.True(writer.SetFields(_token, [new FieldWrite("API_KEY", "external")]));
            writer.Save();
        }

        Assert.Equal(SavedRead.ChangedOnDisk, vault.ReadSavedField(_token, "API_KEY", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_taken_as_unchanged()
    {
        using var vault = Seeded();

        File.Delete(_path);

        Assert.Equal(SavedRead.Unreadable, vault.ReadSaved(out var entries));
        Assert.Null(entries);
    }

    [Fact]
    public void Each_change_names_the_entries_it_touched_while_it_is_still_unsaved()
    {
        using var vault = Seeded();
        var states = new List<SavedRead>();
        var edits = new List<VaultEdit>();

        vault.Edited += (sender, edit) =>
        {
            edits.Add(edit);
            states.Add(vault.ReadSaved(out _));
        };

        vault.UpdateEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "v2" });
        Assert.Equal([_token], Assert.Single(edits).Entries);

        var moved = new EntryName("env/other", "MOVED");
        Assert.Equal(GroupOutcome.Created, vault.CreateGroup("env", "other", out _));
        Assert.Single(edits);

        Assert.Equal(OrganizeOutcome.RenamedAndMoved, vault.Relocate(_token, moved, out _));
        Assert.Equal([_token, moved], edits[^1].Entries);

        Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "finance", out _));
        Assert.Equal([_other, new EntryName("env/finance", "OTHER")], edits[^1].Entries);

        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(moved, out var recycled));
        Assert.Equal([moved], edits[^1].Entries);

        Assert.Equal(RestoreOutcome.Restored, vault.RestoreRecycled(recycled));
        Assert.Equal([moved], edits[^1].Entries);

        Assert.True(vault.RestoreRevision(moved, 0));
        Assert.Equal([moved], edits[^1].Entries);

        vault.AddEntry(new VaultEntry { GroupPath = "env", Title = "NEW", Password = "n" });
        Assert.Equal([new EntryName("env", "NEW")], edits[^1].Entries);

        Assert.Equal(7, edits.Count);
        Assert.All(states, state => Assert.Equal(SavedRead.Unsaved, state));
        Assert.All(edits, edit => Assert.False(edit.IsEverything));
    }

    [Fact]
    public void A_group_rename_names_every_entry_beneath_it_before_and_after()
    {
        using var vault = Seeded();
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing/deep", Title = "INNER", Password = "i" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/billingx", Title = "SIBLING", Password = "s" });
        var edits = Watch(vault);

        Assert.Equal(GroupOutcome.Renamed, vault.RenameGroup("env/billing", "finance", out _));

        var touched = Assert.Single(edits).Entries;
        Assert.Equal(6, touched.Count);
        Assert.Contains(new EntryName("env/billing/deep", "INNER"), touched);
        Assert.Contains(new EntryName("env/finance/deep", "INNER"), touched);
        Assert.Contains(new EntryName("env/finance", "TOKEN"), touched);
        Assert.DoesNotContain(touched, name => name.Title == "SIBLING");
    }

    [Fact]
    public void An_access_change_touches_everything_and_leaves_the_vault_current_once_written()
    {
        using var vault = Seeded();
        var edits = Watch(vault);

        var result = vault.ChangeAccess(
            new VaultAccessChange(SetPassword: true, AccessKeyfileChange.Keep), "a new master password", "a new master password");

        Assert.Equal(VaultAccessOutcome.Changed, result.Outcome);
        Assert.True(Assert.Single(edits).IsEverything);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    private Vault Seeded()
    {
        var vault = Vault.Create(_path, _masterPassword);
        vault.AddEntry(new VaultEntry { GroupPath = _token.GroupPath, Title = _token.Title, Password = "v1" });
        vault.AddEntry(new VaultEntry { GroupPath = _other.GroupPath, Title = _other.Title, Password = "o1" });
        vault.Save();
        return vault;
    }

    private void ExternalSave(EntryName name, string password)
    {
        using var writer = Vault.Open(_path, _masterPassword);
        writer.UpdateEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = password });
        writer.Save();
    }

    private static List<VaultEdit> Watch(Vault vault)
    {
        var edits = new List<VaultEdit>();
        vault.Edited += (_, edit) => edits.Add(edit);
        return edits;
    }

    private static string Password(IReadOnlyList<VaultEntry> entries, EntryName name) =>
        entries.Single(entry => EntryName.Of(entry) == name).Password;
}
