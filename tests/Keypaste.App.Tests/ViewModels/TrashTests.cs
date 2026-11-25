using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The Trash screen: what a deletion made through the app puts there, what a restore gets back,
/// and the second confirmation that erases one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row here is made by deleting something through a screen.</b> Seeding the bin directly
/// would test a reader against a fixture, and the deletion that produces the row is half of what
/// is under test. So the entry deletions run through <see cref="EntriesViewModel"/> and the variable
/// deletion through <see cref="EnvProjectViewModel"/>, exactly as a person reaches them.
/// </para>
/// <para>
/// <b>What the file holds is asserted by opening it again.</b> A restore that only changed the
/// vault in memory would satisfy every screen in this file, so each recovery is read back through
/// a separate <see cref="ClaimedVault.Open(string, ReadOnlySpan{char})"/> after the session has gone.
/// </para>
/// <para>
/// There is no automation test beside this one, the way <c>HistoryRevealAutomationTests</c> sits
/// beside the history pane. Nothing on this screen draws a secret: a
/// <see cref="Keypaste.Core.RecycledEntry"/> carries no field value, so there is no masked control
/// to hold, no reveal to end and no drawn value to prove.
/// </para>
/// </remarks>
public sealed class TrashTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;

    public TrashTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-trash-tests-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = ClaimedVault.Create(_vaultPath, _master);

        vault.AddEntry(new VaultEntry
        {
            Title = "production",
            GroupPath = "servers",
            Username = "root",
            Password = "v0",
        });

        // Changed once, so the entry has a history for the recycle bin to carry and give back.
        vault.UpdateEntry(new VaultEntry
        {
            Title = "production",
            GroupPath = "servers",
            Username = "root",
            Password = "v1",
        });

        ProjectVariables.Set(vault, "billing", "STRIPE_KEY", "sk-live");
        ProjectVariables.Set(vault, "dev", "STRIPE_KEY", "sk-test");

        vault.Save();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void An_untouched_vault_has_an_empty_trash()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);
        using var trash = new TrashViewModel(context.Session);

        Assert.True(trash.IsEmpty);
        Assert.Empty(trash.Rows);
        Assert.Equal(TrashViewModel.RecycledNote, trash.Note);
        Assert.DoesNotContain("format 4.1", trash.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The list is what the deletion produced, named and placed — and nothing from inside the
    /// entry, which is <c>SecretHygieneTests</c>' claim and this row's shape.
    /// </summary>
    [Fact]
    public void A_deletion_made_in_the_app_appears_in_the_trash()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        var row = Assert.Single(trash.Rows);

        Assert.Equal("production", row.DisplayTitle, StringComparer.Ordinal);
        Assert.Equal("servers", row.Where, StringComparer.Ordinal);
        Assert.True(row.CameFromALiveGroup);
        Assert.Contains("from servers", row.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deleted_password_is_restored_and_the_file_holds_it_again()
    {
        using (var context = new UnlockedScreens(_vaultPath, _master, envSets: true))
        {
            Delete(context, "servers/production");

            using var trash = new TrashViewModel(context.Session);
            trash.Selected = Assert.Single(trash.Rows);
            trash.RestoreCommand.Press();

            Assert.Equal("production is back in servers.", trash.Notice, StringComparer.Ordinal);
            Assert.Null(trash.Error);
            Assert.True(trash.IsEmpty);
        }

        using var reopened = ClaimedVault.Open(_vaultPath, _master);
        var entry = reopened.Find(new EntryName("servers", "production"));

        Assert.NotNull(entry);
        Assert.Equal("v1", entry.Password, StringComparer.Ordinal);

        // The history came back with it, which tells a recycled entry from a re-created one.
        var revisions = reopened.ReadHistory(new EntryName("servers", "production"));
        Assert.NotNull(revisions);
        Assert.Equal("v0", Assert.Single(revisions).Fields.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// A group another program deleted lists each of its items here with the path it had, and restoring one
    /// puts it back there with its history, making the group again.
    /// </summary>
    [Fact]
    public void A_deleted_groups_items_are_listed_and_one_restores_to_its_path()
    {
        using (var context = new UnlockedScreens(_vaultPath, _master, envSets: true))
        {
            var write = context.Session.Write(vault => vault.DeleteGroup("servers"), outcome => outcome == GroupOutcome.Recycled);

            Assert.Equal(WriteOutcome.Saved, write.Outcome);

            using var trash = new TrashViewModel(context.Session);
            var row = Assert.Single(trash.Rows);
            Assert.Equal("production", row.DisplayTitle, StringComparer.Ordinal);
            Assert.Equal("servers", row.Where, StringComparer.Ordinal);

            trash.Selected = row;
            trash.RestoreCommand.Press();

            Assert.Equal("production is back in servers.", trash.Notice, StringComparer.Ordinal);
            Assert.True(trash.IsEmpty);
        }

        using var reopened = ClaimedVault.Open(_vaultPath, _master);
        var name = new EntryName("servers", "production");

        Assert.Equal("v1", reopened.Find(name)?.Password, StringComparer.Ordinal);
        Assert.Equal("v0", Assert.Single(reopened.ReadHistory(name)!).Fields.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// Two home entries of one title from two projects land in one bin. The row a person selected is
    /// the row that comes back, because the address is an identity and not a title.
    /// </summary>
    [Fact]
    public void Duplicate_names_restore_the_selected_identity()
    {
        using (var context = new UnlockedScreens(_vaultPath, _master, envSets: true))
        {
            Delete(context, "env/billing/.env");
            Delete(context, "env/dev/.env");

            using var trash = new TrashViewModel(context.Session);
            Assert.Equal(2, trash.Rows.Count);

            trash.Selected = trash.Rows.Single(row => row.Where == "env/dev");
            trash.RestoreCommand.Press();

            Assert.Equal(".env is back in env/dev.", trash.Notice, StringComparer.Ordinal);
            Assert.Equal("env/billing", Assert.Single(trash.Rows).Where, StringComparer.Ordinal);
        }

        using var reopened = ClaimedVault.Open(_vaultPath, _master);

        Assert.Equal(
            "sk-test",
            Assert.Single(EnvResolution.List(reopened, "dev", "dev").Variables).Value,
            StringComparer.Ordinal);

        Assert.Empty(EnvResolution.List(reopened, "billing", "dev").Variables);
    }

    [Fact]
    public void Permanent_deletion_needs_its_own_confirmation()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);

        // Asking is not doing: the file is untouched until the second act.
        var armed = Digest(_vaultPath);
        trash.PurgeCommand.Press();

        Assert.True(trash.IsConfirmingPurge);
        Assert.EndsWith("for good?", trash.PurgePrompt, StringComparison.Ordinal);
        Assert.Equal(armed, Digest(_vaultPath));

        trash.CancelPurgeCommand.Press();

        Assert.False(trash.IsConfirmingPurge);
        Assert.Equal(armed, Digest(_vaultPath));
        Assert.Single(trash.Rows);

        trash.PurgeCommand.Press();
        trash.ConfirmPurgeCommand.Press();

        Assert.Equal("production and its history are gone.", trash.Notice, StringComparer.Ordinal);
        Assert.True(trash.IsEmpty);
        Assert.NotEqual(armed, Digest(_vaultPath));
    }

    /// <summary>
    /// A selection announces the lines and buttons that read it, or a binding keeps a stale prompt
    /// or a greyed-out Restore.
    /// </summary>
    [Fact]
    public void Selecting_a_row_announces_the_lines_and_buttons_that_read_it()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        var raised = Record(trash);
        var asked = new List<string>();
        trash.RestoreCommand.CanExecuteChanged += (_, _) => asked.Add("restore");
        trash.PurgeCommand.CanExecuteChanged += (_, _) => asked.Add("purge");

        trash.Selected = Assert.Single(trash.Rows);

        Assert.Contains(nameof(TrashViewModel.HasSelection), raised);
        Assert.Contains(nameof(TrashViewModel.PurgePrompt), raised);
        Assert.Equal(["purge", "restore"], asked.Order(StringComparer.Ordinal));
        Assert.True(trash.RestoreCommand.CanExecute(null));
    }

    [Fact]
    public void Arming_the_permanent_deletion_announces_which_buttons_can_be_pressed()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);
        var asked = new List<string>();
        trash.RestoreCommand.CanExecuteChanged += (_, _) => asked.Add("restore");
        trash.PurgeCommand.CanExecuteChanged += (_, _) => asked.Add("purge");
        trash.ConfirmPurgeCommand.CanExecuteChanged += (_, _) => asked.Add("confirm");
        trash.CancelPurgeCommand.CanExecuteChanged += (_, _) => asked.Add("cancel");

        trash.PurgeCommand.Press();

        Assert.Equal(["cancel", "confirm", "purge", "restore"], asked.Order(StringComparer.Ordinal));
        Assert.False(trash.RestoreCommand.CanExecute(null));
        Assert.True(trash.ConfirmPurgeCommand.CanExecute(null));
    }

    [Fact]
    public void Reading_the_bin_again_restates_its_notes()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        using var trash = new TrashViewModel(context.Session);
        var raised = Record(trash);

        trash.Load();

        Assert.Contains(nameof(TrashViewModel.Note), raised);
        Assert.Contains(nameof(TrashViewModel.EmptyNote), raised);
    }

    [Fact]
    public void An_outcome_announces_that_there_is_one()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);
        var raised = Record(trash);

        trash.RestoreCommand.Press();

        Assert.Contains(nameof(TrashViewModel.HasNotice), raised);
        Assert.True(trash.HasNotice);
    }

    [Fact]
    public void A_refusal_announces_that_there_is_one()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);
        context.Session.Lock(VaultLockReason.Manual);
        var raised = Record(trash);

        trash.RestoreCommand.Press();

        Assert.Contains(nameof(TrashViewModel.HasError), raised);
        Assert.True(trash.HasError);
    }

    /// <summary>A restore whose name another item has taken since goes back under its own title, beside that item, with its history.</summary>
    [Fact]
    public void A_restore_goes_back_under_its_own_title_when_another_item_has_taken_it()
    {
        string restoredUuid;

        using (var context = new UnlockedScreens(_vaultPath, _master, envSets: true))
        {
            Delete(context, "servers/production");
            Add(context, "servers/production");

            using var trash = new TrashViewModel(context.Session);
            trash.Selected = Assert.Single(trash.Rows);
            restoredUuid = trash.Selected.Id.UuidHex;
            trash.RestoreCommand.Press();

            Assert.Equal("production is back in servers.", trash.Notice, StringComparer.Ordinal);
            Assert.Null(trash.Error);
            Assert.True(trash.IsEmpty);
        }

        using var reopened = ClaimedVault.Open(_vaultPath, _master);
        var back = new EntryName("servers", "production") { Uuid = restoredUuid };

        Assert.Equal(2, reopened.ReadEntries().Count(entry => entry.GroupPath == "servers" && entry.Title == "production"));
        Assert.Equal("v1", reopened.Find(back)?.Password, StringComparer.Ordinal);
        Assert.Equal("v0", Assert.Single(reopened.ReadHistory(back)!).Fields.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// An item recycled after Empty Trash asked, as a terminal's <c>keypaste rm</c> sent through the app recycles one,
    /// was never counted by the confirmation, so nothing is erased and the list is read again.
    /// </summary>
    [Fact]
    public void Emptying_erases_nothing_when_the_trash_changed_after_it_asked()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.EmptyCommand.Press();

        var vault = context.Session.Unlocked!;
        Assert.Equal(DeletionOutcome.Recycled, vault.RemoveEntry(new EntryName("env/billing", ".env")));
        vault.Save();
        var before = Digest(_vaultPath);

        trash.ConfirmEmptyCommand.Press();

        Assert.Equal(
            "The Trash changed after you asked, so nothing was deleted. Check what is in it and empty it again.",
            trash.Error,
            StringComparer.Ordinal);
        Assert.Null(trash.Notice);
        Assert.False(trash.IsConfirmingEmpty);
        Assert.Equal(2, trash.Rows.Count);
        Assert.Equal(2, vault.ReadRecycled().Count);
        Assert.Equal(before, Digest(_vaultPath));
    }

    /// <summary>
    /// Emptying the bin is the same second deliberate act as erasing one entry: asking names how many go and
    /// writes nothing, Cancel writes nothing, and only the confirmation erases every entry and its history.
    /// </summary>
    [Fact]
    public void Emptying_the_trash_needs_its_own_confirmation_naming_how_many_go()
    {
        using (var context = new UnlockedScreens(_vaultPath, _master, envSets: true))
        {
            Delete(context, "servers/production");
            Delete(context, "env/billing/.env");

            using var trash = new TrashViewModel(context.Session);
            Assert.Equal(2, trash.Rows.Count);
            Assert.True(trash.EmptyCommand.CanExecute(null));
            trash.Selected = trash.Rows[0];

            var armed = Digest(_vaultPath);
            trash.EmptyCommand.Press();

            Assert.True(trash.IsConfirmingEmpty);
            Assert.Equal(
                "Delete all 2 items in the Trash and their history for good? This cannot be undone.",
                trash.EmptyPrompt,
                StringComparer.Ordinal);
            Assert.False(trash.PurgeCommand.CanExecute(null));
            Assert.False(trash.RestoreCommand.CanExecute(null));
            Assert.Equal(armed, Digest(_vaultPath));

            trash.CancelEmptyCommand.Press();

            Assert.False(trash.IsConfirmingEmpty);
            Assert.Equal(armed, Digest(_vaultPath));
            Assert.Equal(2, trash.Rows.Count);

            trash.EmptyCommand.Press();
            trash.ConfirmEmptyCommand.Press();

            Assert.Equal("The Trash is empty. 2 items and their history are gone.", trash.Notice, StringComparer.Ordinal);
            Assert.Null(trash.Error);
            Assert.False(trash.IsConfirmingEmpty);
            Assert.True(trash.IsEmpty);
            Assert.False(trash.EmptyCommand.CanExecute(null));
        }

        using var reopened = ClaimedVault.Open(_vaultPath, _master);

        Assert.Empty(reopened.ReadRecycled());
        Assert.Null(reopened.Find(new EntryName("servers", "production")));
        Assert.Null(reopened.ReadHistory(new EntryName("servers", "production")));
        Assert.Empty(EnvResolution.List(reopened, "billing", "dev").Variables);
    }

    [Fact]
    public void Empty_trash_is_disabled_while_the_trash_is_empty()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        using (var before = new TrashViewModel(context.Session))
        {
            Assert.True(before.IsEmpty);
            Assert.False(before.EmptyCommand.CanExecute(null));
            Assert.Equal(string.Empty, before.EmptyPrompt, StringComparer.Ordinal);
        }

        Delete(context, "servers/production");
        using var trash = new TrashViewModel(context.Session);
        Assert.True(trash.EmptyCommand.CanExecute(null));
        Assert.Equal(
            "Delete the one item in the Trash and its history for good? This cannot be undone.",
            trash.EmptyPrompt,
            StringComparer.Ordinal);

        trash.Selected = Assert.Single(trash.Rows);
        trash.RestoreCommand.Press();

        Assert.True(trash.IsEmpty);
        Assert.False(trash.EmptyCommand.CanExecute(null));
    }

    /// <summary>The list's changes announce whether Empty Trash can be pressed, or the button keeps the state it was drawn in.</summary>
    [Fact]
    public void Reading_the_bin_announces_whether_it_can_be_emptied()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);
        var asked = 0;
        trash.EmptyCommand.CanExecuteChanged += (_, _) => asked++;
        var raised = Record(trash);

        trash.RestoreCommand.Press();

        Assert.True(asked > 0);
        Assert.Contains(nameof(TrashViewModel.EmptyPrompt), raised);
    }

    [Fact]
    public void A_row_whose_entry_has_gone_is_reported_rather_than_acted_on()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);

        // Erased by another holder of the same vault: the session the screen is not looking at.
        var vault = context.Session.Unlocked!;
        Assert.True(vault.PurgeRecycled(trash.Selected!.Id));
        vault.Save();

        trash.RestoreCommand.Press();

        Assert.Equal("production is not in the Trash any more. Check the list.", trash.Error, StringComparer.Ordinal);
        Assert.True(trash.IsEmpty);
    }

    [Fact]
    public void A_vault_with_no_recycle_bin_says_so_rather_than_showing_an_empty_list()
    {
        var path = Path.Combine(_directory, "no-bin.kdbx");

        using (var vault = ClaimedVault.Create(path, _master))
        {
            vault.SetRecyclesDeletedEntries(false);
            vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            vault.Save();
        }

        using var context = new UnlockedScreens(path, _master, envSets: true);

        Delete(context, "github");

        using var trash = new TrashViewModel(context.Session);

        Assert.False(trash.RecyclesDeletedEntries);
        Assert.Equal(TrashViewModel.NoBinNote, trash.Note);
        Assert.Equal("Nothing can be recovered from this vault.", trash.EmptyNote, StringComparer.Ordinal);
        Assert.True(trash.IsEmpty);
    }

    [Fact]
    public void Locking_clears_the_list_the_selection_and_the_outcome()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");

        using var trash = new TrashViewModel(context.Session);
        trash.Selected = Assert.Single(trash.Rows);
        trash.PurgeCommand.Press();

        context.Session.Lock(VaultLockReason.Manual);
        trash.Dispose();

        Assert.Empty(trash.Rows);
        Assert.Null(trash.Selected);
        Assert.False(trash.IsConfirmingPurge);
        Assert.Null(trash.Notice);
        Assert.Null(trash.Error);
        Assert.Equal(string.Empty, trash.PurgePrompt, StringComparer.Ordinal);
    }

    /// <summary>
    /// The outcome the entry screen states, and the way back it offers for the deletion a person
    /// is still looking at.
    /// </summary>
    [Fact]
    public void A_delete_that_raises_the_format_says_only_where_the_item_went()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Assert.True(KdbxHeader.IsOlderThan41(_vaultPath));
        Delete(context, "servers/production");

        Assert.Equal("Moved production to Trash", context.Entries.Notice, StringComparer.Ordinal);
        Assert.False(KdbxHeader.IsOlderThan41(_vaultPath));
    }

    [Fact]
    public void The_entry_screen_states_the_outcome_and_can_undo_it()
    {
        using (var context = new UnlockedScreens(_vaultPath, _master, envSets: true))
        {
            Delete(context, "servers/production");

            Assert.Equal("Moved production to Trash", context.Entries.Notice, StringComparer.Ordinal);
            Assert.True(context.Entries.CanUndo);
            Assert.DoesNotContain(context.Entries.Rows, row => row.Path == "servers/production");

            context.Entries.UndoCommand.Press();

            Assert.False(context.Entries.CanUndo);
            Assert.Null(context.Entries.Notice);
            Assert.Null(context.Entries.Error);
            Assert.Contains(context.Entries.Rows, row => row.Path == "servers/production");

            using var trash = new TrashViewModel(context.Session);
            Assert.Empty(trash.Rows);
        }

        using var reopened = ClaimedVault.Open(_vaultPath, _master);

        Assert.Equal(
            "v1",
            reopened.Find(new EntryName("servers", "production"))?.Password,
            StringComparer.Ordinal);
    }

    [Fact]
    public void A_vault_with_no_recycle_bin_offers_no_undo()
    {
        var path = Path.Combine(_directory, "no-bin.kdbx");

        using (var vault = ClaimedVault.Create(path, _master))
        {
            vault.SetRecyclesDeletedEntries(false);
            vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            vault.Save();
        }

        using var context = new UnlockedScreens(path, _master, envSets: true);

        Delete(context, "github");

        Assert.Equal("Deleted github.", context.Entries.Notice, StringComparer.Ordinal);

        Assert.False(context.Entries.CanUndo);
    }

    [Fact]
    public void Opening_a_form_keeps_the_undo_and_the_next_write_ends_it()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Delete(context, "servers/production");
        Assert.True(context.Entries.CanUndo);

        NewItemForm.Open(context.Entries);

        Assert.True(context.Entries.CanUndo);
        Assert.Null(context.Entries.Notice);

        context.Entries.CancelAddCommand.Press();
        context.Entries.Selected = context.Entries.Rows[0];
        context.Entries.DuplicateCommand.Press();
        var copy = context.Entries.Selected!.Name;

        context.Entries.UndoCommand.Press();

        Assert.DoesNotContain(context.Entries.Rows, row => row.Name == copy);
        Assert.False(context.Entries.CanUndo);
    }

    [Fact]
    public void Restore_puts_every_selected_row_back_in_one_save_a_taken_name_included()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        Add(context, "servers/staging");
        Delete(context, "servers/production");
        Delete(context, "servers/staging");
        Add(context, "servers/staging");

        using var trash = new TrashViewModel(context.Session);
        trash.Load();
        Assert.Equal(2, trash.Rows.Count);
        trash.SelectedRows.AddRange(trash.Rows);

        Assert.False(trash.PurgeCommand.CanExecute(null));

        trash.Selected = trash.Rows[0];
        trash.RestoreCommand.Press();

        Assert.Equal("2 items are back.", trash.Notice);
        Assert.Null(trash.Error);
        Assert.Empty(trash.SelectedRows);
        Assert.True(trash.IsEmpty);

        using var reopened = ClaimedVault.Open(_vaultPath, _master);
        Assert.NotNull(reopened.Find("servers/production"));
        Assert.Equal(2, reopened.ReadEntries().Count(entry => entry.GroupPath == "servers" && entry.Title == "staging"));
    }

    /// <summary>A key removed on the env card leaves its entry, whose history keeps it, and puts nothing in the bin.</summary>
    [Fact]
    public void A_key_removed_on_the_env_card_puts_nothing_in_the_trash()
    {
        using var context = new UnlockedScreens(_vaultPath, _master, envSets: true);

        RemoveVariable(context, "billing", "STRIPE_KEY");

        using var trash = new TrashViewModel(context.Session);
        Assert.Empty(trash.Rows);
    }

    private static void Delete(UnlockedScreens context, string path)
    {
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Path == path);
        context.Entries.DeleteCommand.Press();

        if (context.Entries.IsConfirmingDelete)
        {
            context.Entries.ConfirmDeleteCommand.Press();
        }
    }

    // A group's item, as KeePassXC adds one: the app adds items only at a vault's top level.
    private static void Add(UnlockedScreens context, string path)
    {
        var slash = path.LastIndexOf('/');
        var entry = new VaultEntry { GroupPath = path[..slash], Title = path[(slash + 1)..], Password = "added" };

        Assert.Equal(WriteOutcome.Saved, context.Session.Write(vault => { vault.AddEntry(entry); return true; }, _ => true).Outcome);
    }

    private static void RemoveVariable(UnlockedScreens context, string project, string key)
    {
        context.EnvSets.OpenCommand.Press(project);

        var open = context.EnvSets.OpenProject!;
        open.Variables.Single(row => row.Key == key).RemoveCommand.Press();
        open.ConfirmRemoveCommand.Press();
    }

    private static List<string> Record(TrashViewModel trash)
    {
        var raised = new List<string>();
        trash.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    private static string Digest(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
