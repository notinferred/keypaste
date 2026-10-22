using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Organizing and finding from the Entries screen, against a real KDBX.
/// </summary>
/// <remarks>
/// <para>
/// Two claims carry this file. <b>A write goes through and is in the file afterwards</b>, which is
/// checked by reopening rather than by reading the screen back; and <b>a refusal leaves the file
/// byte-identical</b>, which is checked by a digest taken before it.
/// </para>
/// <para>
/// The second has a sharper form that the digest alone does not reach, and it is the reason the
/// screen calls <see cref="Vault.Relocate"/> once rather than composing a rename and a move:
/// <see cref="A_refused_organize_is_not_in_the_vault_the_next_save_writes"/> performs an unrelated
/// save afterwards, so a change left in the open vault by a refusal would be written out and caught.
/// </para>
/// </remarks>
public sealed class EntriesOrganizeTests : IDisposable
{
    internal const string Master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;

    public EntriesOrganizeTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-organize-screen-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, Master);

        vault.AddEntry(new VaultEntry
        {
            Title = "production",
            Username = "dba@example.test",
            Password = "first",
            Url = "https://db.example.test/admin",
            Notes = "recovery-code-in-the-notes",
            GroupPath = "servers",
        });

        // Replaced, so the entry has a history for an organize to carry.
        vault.UpdateEntry(new VaultEntry
        {
            Title = "production",
            Username = "dba@example.test",
            Password = "second",
            Url = "https://db.example.test/admin",
            Notes = "recovery-code-in-the-notes",
            GroupPath = "servers",
        });

        vault.AddEntry(new VaultEntry { Title = "staging", Password = "s", GroupPath = "servers" });
        vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = "sk", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
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

    // ---------------------------------------------------------------- groups

    [Fact]
    public void A_created_group_is_in_the_file()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            entries.SelectedGroup = Group(entries, string.Empty);
            entries.BeginCreateGroupCommand.Execute(null);
            entries.DraftGroupName = "archive";
            entries.ConfirmCreateGroupCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.False(entries.IsCreatingGroup);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.Contains("archive", reopened.ReadGroupPaths());
    }

    [Fact]
    public void A_group_is_created_inside_the_one_that_is_selected()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            entries.SelectedGroup = Group(entries, "servers");
            entries.BeginCreateGroupCommand.Execute(null);
            entries.DraftGroupName = "retired";
            entries.ConfirmCreateGroupCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.Contains("servers/retired", reopened.ReadGroupPaths());
    }

    /// <summary>
    /// The root is not a group somebody named, so there is nothing there to rename.
    /// </summary>
    [Fact]
    public void All_entries_cannot_be_renamed()
    {
        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, string.Empty);

        Assert.False(entries.BeginRenameGroupCommand.CanExecute(null));
    }

    [Fact]
    public void A_renamed_group_carries_its_entries_and_their_history()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            entries.SelectedGroup = Group(entries, "servers");
            entries.BeginRenameGroupCommand.Execute(null);
            entries.DraftGroupName = "hosts";
            entries.ConfirmRenameGroupCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        var name = new EntryName("hosts", "production");

        Assert.Null(reopened.Find(new EntryName("servers", "production")));
        Assert.Equal("second", reopened.Find(name)!.Password, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "first" },
            reopened.ReadHistory(name)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// Renaming a group directly under <c>env</c> moves where keypaste puts a project's new keys; an
    /// ordinary group's rename says nothing of projects.
    /// </summary>
    [Fact]
    public void Renaming_a_projects_home_group_says_where_new_keys_go_and_an_ordinary_group_does_not()
    {
        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, "servers");
        entries.BeginRenameGroupCommand.Execute(null);
        Assert.False(entries.ShowsProjectRenameNote);

        entries.SelectedGroup = Group(entries, "env/billing");
        entries.BeginRenameGroupCommand.Execute(null);

        Assert.True(entries.ShowsProjectRenameNote);
        Assert.Contains("New keys for billing go to items in env/billing", entries.ProjectRenameNote, StringComparison.Ordinal);
        Assert.Contains("Moved items keep their tags", entries.ProjectRenameNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// A moved group carries its items and their history in one write, and the list follows it to its
    /// new path, as it follows a renamed one.
    /// </summary>
    [Fact]
    public void A_moved_group_carries_its_items_and_the_list_follows_it()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            entries.SelectedGroup = Group(entries, "servers");
            entries.BeginMoveGroupCommand.Execute(null);

            Assert.True(entries.IsMovingGroup);
            Assert.Equal(string.Empty, entries.GroupMoveTarget?.Path);
            Assert.Same(entries.MoveTargets, entries.Groups);

            entries.GroupMoveTarget = Group(entries, "env");
            entries.ConfirmMoveGroupCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.False(entries.IsMovingGroup);
            Assert.Equal("env/servers", entries.SelectedGroup?.Path);
            Assert.Equal("Moved servers to env.", entries.Notice);
            Assert.Equal(["production", "staging"], entries.Rows.Select(row => row.Title).Order(StringComparer.Ordinal));
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        var name = new EntryName("env/servers", "production");

        Assert.Null(reopened.Find(new EntryName("servers", "production")));
        Assert.Equal("second", reopened.Find(name)!.Password, StringComparer.Ordinal);
        Assert.Equal(new[] { "first" }, reopened.ReadHistory(name)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>The move offers the item move's own destinations, and one core refuses says why and writes nothing.</summary>
    [Theory]
    [InlineData("env/billing", "env/billing", "inside itself")]
    [InlineData("servers", "", "already there")]
    [InlineData("env", "servers", "keypaste assigns")]
    public void A_refused_group_move_says_why_and_leaves_the_bytes_unchanged(string group, string target, string said)
    {
        var before = Digest(_vaultPath);

        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, group);
        entries.BeginMoveGroupCommand.Execute(null);
        entries.GroupMoveTarget = Group(entries, target);
        entries.ConfirmMoveGroupCommand.Execute(null);

        Assert.Contains(said, entries.Error, StringComparison.Ordinal);
        Assert.True(entries.IsMovingGroup);
        Assert.Equal(before, Digest(_vaultPath));
    }

    /// <summary>
    /// Deleting a group asks first, naming how many items go with it; asking writes nothing, and the
    /// delete puts the group and its items in the recycle bin and shows the group it was in.
    /// </summary>
    [Fact]
    public void Deleting_a_group_asks_first_naming_its_items_and_recycles_them()
    {
        var before = Digest(_vaultPath);

        using (var context = New())
        {
            var entries = context.Entries;

            entries.SelectedGroup = Group(entries, "env/billing");
            entries.BeginDeleteGroupCommand.Execute(null);

            Assert.True(entries.IsConfirmingGroupDelete);
            Assert.Equal(
                "Delete env/billing? It and the 1 item in it go to the vault's recycle bin, and Trash can put each item back.",
                entries.GroupDeletePrompt);
            Assert.Equal(before, Digest(_vaultPath));

            entries.SelectedGroup = Group(entries, "servers");
            Assert.False(entries.IsConfirmingGroupDelete);

            entries.BeginDeleteGroupCommand.Execute(null);
            Assert.Contains("the 2 items in it", entries.GroupDeletePrompt, StringComparison.Ordinal);

            entries.ConfirmDeleteGroupCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.True(entries.SelectedGroup?.IsEverything);
            Assert.Equal("Moved servers and the 2 items in it to the trash. Trash puts each one back.", entries.Notice);
            Assert.DoesNotContain(entries.Groups, node => node.Path == "servers");
        }

        using var reopened = Vault.Open(_vaultPath, Master);

        Assert.Null(reopened.Find(new EntryName("servers", "production")));
        Assert.Equal(
            [("production", "servers"), ("staging", "servers")],
            reopened.ReadRecycled().Select(recycled => (recycled.Title, recycled.OriginalGroupPath)).Order());
    }

    /// <summary>The env group is keypaste's to assign, so it is not deleted, and the screen says why.</summary>
    [Fact]
    public void The_env_group_is_not_deleted_and_says_why()
    {
        var before = Digest(_vaultPath);

        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, "env");
        entries.BeginDeleteGroupCommand.Execute(null);
        entries.ConfirmDeleteGroupCommand.Execute(null);

        Assert.Contains("keypaste assigns", entries.Error, StringComparison.Ordinal);
        Assert.Equal(before, Digest(_vaultPath));
    }

    /// <summary>
    /// Organizing can change which places an agent sees cover an entry, wherever it happens.
    /// </summary>
    [Fact]
    public void Both_organize_forms_say_what_a_path_change_does_to_access()
    {
        using var context = New();

        Assert.Contains("What an agent can see by place is matched against paths", EntriesViewModel.AccessNoteText, StringComparison.Ordinal);
        Assert.Equal(EntriesViewModel.AccessNoteText, context.Entries.AccessNote, StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------- one entry, one write

    [Fact]
    public void A_renamed_entry_keeps_its_history_and_stays_selected()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            Select(entries, "servers", "production");
            entries.OrganizeCommand.Execute(null);
            entries.DraftTitle = "production-db";
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.Equal(new EntryName("servers", "production-db"), entries.Selected?.Name);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        var name = new EntryName("servers", "production-db");

        Assert.Equal("second", reopened.Find(name)!.Password, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "first" },
            reopened.ReadHistory(name)!.Select(revision => revision.Fields.Password));
    }

    [Fact]
    public void A_moved_entry_is_at_its_new_path_and_stays_selected()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            Select(entries, "servers", "production");
            entries.OrganizeCommand.Execute(null);
            entries.MoveTarget = Group(entries, string.Empty);
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.Equal(new EntryName(string.Empty, "production"), entries.Selected?.Name);
        }

        using var reopened = Vault.Open(_vaultPath, Master);

        Assert.Null(reopened.Find(new EntryName("servers", "production")));
        Assert.Equal("second", reopened.Find(new EntryName(string.Empty, "production"))!.Password, StringComparer.Ordinal);
    }

    /// <summary>
    /// The combined write, which is what one Save on the form performs.
    /// </summary>
    [Fact]
    public void One_confirm_renames_and_moves_in_a_single_write()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            Select(entries, "servers", "production");
            entries.OrganizeCommand.Execute(null);
            entries.DraftTitle = "PROD_DB";
            entries.MoveTarget = Group(entries, "env/billing");
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.Equal(new EntryName("env/billing", "PROD_DB"), entries.Selected?.Name);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        var name = new EntryName("env/billing", "PROD_DB");

        Assert.Null(reopened.Find(new EntryName("servers", "production")));
        Assert.Equal("second", reopened.Find(name)!.Password, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "first" },
            reopened.ReadHistory(name)!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// A filter the entry has just left would hide what somebody is looking at, so it follows.
    /// </summary>
    [Fact]
    public void The_group_filter_follows_a_moved_entry()
    {
        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, "servers");
        Select(entries, "servers", "production");

        entries.OrganizeCommand.Execute(null);
        entries.MoveTarget = Group(entries, "env/billing");
        entries.ConfirmOrganizeCommand.Execute(null);

        Assert.Null(entries.Error);
        Assert.Equal("env/billing", entries.SelectedGroup?.Path);
        Assert.Contains(entries.Rows, row => row.Name == new EntryName("env/billing", "production"));
    }

    /// <summary>
    /// A duplicate sits beside the item under a new title, with its values and no history, and is
    /// selected; it leaves out the project tags and says so, because a project takes one item for each
    /// variable (D-0337).
    /// </summary>
    [Fact]
    public void A_duplicate_is_selected_beside_the_item_and_names_the_project_tags_it_left_out()
    {
        using (var vault = Vault.Open(_vaultPath, Master))
        {
            var key = new EntryName("env/billing", "STRIPE_KEY");
            Assert.True(vault.AddTag(key, "env:billing"));
            Assert.True(vault.AddTag(key, "finance"));
            vault.Save();
        }

        using (var context = New())
        {
            var entries = context.Entries;

            Select(entries, "env/billing", "STRIPE_KEY");
            entries.DuplicateCommand.Execute(null);

            Assert.Null(entries.Error);
            Assert.Equal(new EntryName("env/billing", "STRIPE_KEY (copy)"), entries.Selected?.Name);
            Assert.Equal(
                "Duplicated STRIPE_KEY as STRIPE_KEY (copy). The copy leaves out the project tag env:billing, because a project takes one item for each variable. Tag the copy to put it in another environment.",
                entries.Notice);

            Select(entries, "servers", "production");
            entries.DuplicateCommand.Execute(null);

            Assert.Equal("Duplicated production as production (copy).", entries.Notice);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        var copy = new EntryName("env/billing", "STRIPE_KEY (copy)");

        Assert.Equal("sk", reopened.Find(copy)!.Password, StringComparer.Ordinal);
        Assert.Equal(["finance"], reopened.Tags(copy)!);
        Assert.Equal("second", reopened.Find(new EntryName("servers", "production (copy)"))!.Password, StringComparer.Ordinal);
        Assert.Empty(reopened.ReadHistory(new EntryName("servers", "production (copy)"))!);
    }

    // ---------------------------------------------------------------- refusals

    [Theory]
    [InlineData("staging", "servers")]
    [InlineData("a/b", "servers")]
    [InlineData("production", "servers")]
    public void A_refused_organize_leaves_the_bytes_unchanged(string title, string group)
    {
        var before = Digest(_vaultPath);

        using var context = New();
        var entries = context.Entries;

        Select(entries, "servers", "production");
        entries.OrganizeCommand.Execute(null);
        entries.DraftTitle = title;
        entries.MoveTarget = Group(entries, group);
        entries.ConfirmOrganizeCommand.Execute(null);

        Assert.NotNull(entries.Error);
        Assert.Equal(before, Digest(_vaultPath));
    }

    [Fact]
    public void A_cancelled_organize_leaves_the_bytes_unchanged()
    {
        var before = Digest(_vaultPath);

        using var context = New();
        var entries = context.Entries;

        Select(entries, "servers", "production");
        entries.OrganizeCommand.Execute(null);
        entries.DraftTitle = "something-else";
        entries.CancelOrganizeCommand.Execute(null);

        Assert.False(entries.IsOrganizing);
        Assert.Equal(before, Digest(_vaultPath));
    }

    /// <summary>
    /// The regression that holds the one-core-call rule, and the reason the form has one Save.
    /// </summary>
    /// <remarks>
    /// A screen that renamed and then moved would have committed the rename to the open vault
    /// before the move was refused. Nothing on screen would say so, the digest immediately after
    /// would still match — and the next save of that same vault would write the rename out. So this
    /// performs one: an ordinary add, through the screen, after the refusal.
    /// </remarks>
    [Fact]
    public void A_refused_organize_is_not_in_the_vault_the_next_save_writes()
    {
        using (var context = New())
        {
            var entries = context.Entries;

            Select(entries, "servers", "production");
            entries.OrganizeCommand.Execute(null);

            // The rename is fine on its own; the destination, where the title is taken, is what refuses.
            entries.DraftTitle = "STRIPE_KEY";
            entries.MoveTarget = Group(entries, "env/billing");
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.NotNull(entries.Error);

            var form = NewItemForm.Open(entries, "unrelated");
            entries.ConfirmAddCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, Master);

        // The save happened, so the sweep below is not a sweep of a file nothing wrote.
        Assert.NotNull(reopened.Find(new EntryName(string.Empty, "unrelated")));

        Assert.NotNull(reopened.Find(new EntryName("servers", "production")));
        Assert.Null(reopened.Find(new EntryName("servers", "STRIPE_KEY")));
        Assert.Equal("sk", reopened.Find(new EntryName("env/billing", "STRIPE_KEY"))!.Password, StringComparer.Ordinal);
    }

    [Fact]
    public void A_refused_group_name_leaves_the_bytes_unchanged()
    {
        var before = Digest(_vaultPath);

        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, string.Empty);
        entries.BeginCreateGroupCommand.Execute(null);
        entries.DraftGroupName = "servers";
        entries.ConfirmCreateGroupCommand.Execute(null);

        Assert.NotNull(entries.Error);
        Assert.Equal(before, Digest(_vaultPath));

        // The name keypaste assigns meaning to is refused too, and says which.
        entries.DraftGroupName = "env";
        entries.ConfirmCreateGroupCommand.Execute(null);

        Assert.Contains("keypaste assigns", entries.Error!, StringComparison.Ordinal);
        Assert.Equal(before, Digest(_vaultPath));
    }

    // ---------------------------------------------------------------- finding

    [Fact]
    public void Search_finds_an_entry_by_its_username()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "dba@example.test";

        var row = Assert.Single(entries.Rows);
        Assert.Equal("production", row.Title);
        Assert.Equal("username", row.Why);
    }

    [Fact]
    public void Search_finds_an_entry_by_its_url()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "db.example.test";

        var row = Assert.Single(entries.Rows);
        Assert.Equal("production", row.Title);
        Assert.Equal("URL", row.Why);
    }

    [Fact]
    public void Search_does_not_find_an_entry_by_its_password()
    {
        using var context = New();
        var entries = context.Entries;

        // The positive control: the entry holding it is findable by something else.
        entries.Search = "production";
        Assert.NotEmpty(entries.Rows);

        entries.Search = "second";
        Assert.Empty(entries.Rows);
    }

    [Fact]
    public void Search_does_not_find_an_entry_by_its_notes()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "production";
        Assert.NotEmpty(entries.Rows);

        entries.Search = "recovery-code-in-the-notes";
        Assert.Empty(entries.Rows);
    }

    /// <summary>
    /// A title or a group match shows its own evidence, so only the invisible fields are named.
    /// </summary>
    [Fact]
    public void A_row_matched_on_a_visible_field_says_nothing_extra()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "production";

        var row = Assert.Single(entries.Rows);
        Assert.Empty(row.Why);
    }

    [Fact]
    public void A_row_matched_on_both_invisible_fields_names_both()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "example";

        var row = Assert.Single(entries.Rows);
        Assert.Equal("username, URL", row.Why);
    }

    /// <summary>A tag the query was in labels the row with that tag, which is a label and not a value (D-0485).</summary>
    [Fact]
    public void A_row_matched_on_a_tag_names_the_tag()
    {
        using (var vault = Vault.Open(_vaultPath, Master))
        {
            Assert.True(vault.AddTag(new EntryName(string.Empty, "github"), "billing-ops"));
            vault.Save();
        }

        using var context = New();
        var entries = context.Entries;

        entries.Search = "OPS";

        var row = Assert.Single(entries.Rows);
        Assert.Equal("github", row.Title);
        Assert.Equal("tag billing-ops", row.Why);
    }

    [Fact]
    public void Clearing_the_query_lists_everything_again()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "dba@example.test";
        Assert.Single(entries.Rows);

        entries.Search = string.Empty;
        Assert.Equal(4, entries.Rows.Count);
        Assert.All(entries.Rows, row => Assert.Empty(row.Why));
    }

    // ---------------------------------------------------------------- what the selection survives

    [Fact]
    public void The_selection_survives_a_search_that_hides_its_row()
    {
        using var context = New();
        var entries = context.Entries;

        Select(entries, "servers", "production");
        Assert.NotNull(entries.Detail);

        entries.Search = "STRIPE";

        Assert.DoesNotContain(entries.Rows, row => row.Title == "production");
        Assert.NotNull(entries.Detail);
        Assert.Equal("production", entries.Detail!.Title);
    }

    [Fact]
    public void The_selection_survives_a_group_change()
    {
        using var context = New();
        var entries = context.Entries;

        Select(entries, "servers", "production");
        entries.SelectedGroup = Group(entries, "env/billing");

        Assert.DoesNotContain(entries.Rows, row => row.Title == "production");
        Assert.NotNull(entries.Detail);
        Assert.Equal("production", entries.Detail!.Title);
    }

    /// <summary>
    /// A rename can take the entry out of the result that found it, and it stays where it is being
    /// looked at until a different question is asked.
    /// </summary>
    [Fact]
    public void A_renamed_entry_stays_listed_until_the_query_changes()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "production";
        Select(entries, "servers", "production");

        entries.OrganizeCommand.Execute(null);
        entries.DraftTitle = "renamed-away";
        entries.ConfirmOrganizeCommand.Execute(null);

        Assert.Null(entries.Error);
        Assert.Contains(entries.Rows, row => row.Title == "renamed-away");
        Assert.Equal(new EntryName("servers", "renamed-away"), entries.Selected?.Name);

        // A different question, and the entry that no longer answers it goes.
        entries.Search = "staging";
        Assert.DoesNotContain(entries.Rows, row => row.Title == "renamed-away");
    }

    // ---------------------------------------------------------------- the lock

    [Fact]
    public void Locking_clears_the_query_the_results_and_the_selection()
    {
        using var context = New();
        var entries = context.Entries;

        entries.Search = "dba@example.test";
        Select(entries, "servers", "production");
        entries.OrganizeCommand.Execute(null);

        Assert.True(entries.IsOrganizing);

        context.Session.Lock(VaultLockReason.Manual);
        entries.Reload();

        Assert.Empty(entries.Search);
        Assert.Empty(entries.Rows);
        Assert.Null(entries.Selected);
        Assert.Null(entries.Detail);
        Assert.False(entries.IsOrganizing);
        Assert.Empty(entries.DraftTitle);
        Assert.Empty(entries.MoveTargets);
    }

    // ---------------------------------------------------------------- helpers

    private static GroupNode Group(EntriesViewModel entries, string path) =>
        entries.Groups.Single(node => string.Equals(node.Path, path, StringComparison.Ordinal));

    private static void Select(EntriesViewModel entries, string groupPath, string title) =>
        entries.Selected = entries.Rows.Single(row => row.Name == new EntryName(groupPath, title));

    private static string Digest(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private Context New() => new(_vaultPath);

    /// <summary>An unlocked session and a screen looking at it.</summary>
    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(Master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
