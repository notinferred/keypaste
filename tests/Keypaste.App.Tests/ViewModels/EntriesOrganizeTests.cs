using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
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
    /// The one case where renaming a group also renames something a command line addresses.
    /// </summary>
    [Fact]
    public void Renaming_a_project_says_what_it_costs_and_an_ordinary_group_does_not()
    {
        using var context = New();
        var entries = context.Entries;

        entries.SelectedGroup = Group(entries, "servers");
        entries.BeginRenameGroupCommand.Execute(null);
        Assert.False(entries.ShowsProjectRenameNote);

        entries.SelectedGroup = Group(entries, "env/billing");
        entries.BeginRenameGroupCommand.Execute(null);

        Assert.True(entries.ShowsProjectRenameNote);
        Assert.Contains("keypaste run billing", entries.ProjectRenameNote, StringComparison.Ordinal);
        Assert.Contains("env/billing", entries.ProjectRenameNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// Organizing can change which standing authorization covers an entry, wherever it happens.
    /// </summary>
    [Fact]
    public void Both_organize_forms_say_what_a_path_change_does_to_access()
    {
        using var context = New();

        Assert.Contains("Policy rules and agent exposures", EntriesViewModel.AccessNoteText, StringComparison.Ordinal);
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

    // ---------------------------------------------------------------- refusals

    [Theory]
    [InlineData("staging", "servers")]
    [InlineData("a/b", "servers")]
    [InlineData("lower-case", "env/billing")]
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

            // The rename is fine on its own; the destination is what refuses.
            entries.DraftTitle = "production-db";
            entries.MoveTarget = Group(entries, "env/billing");
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.NotNull(entries.Error);

            entries.BeginAddCommand.Execute(null);
            entries.NewEntryPath = "unrelated";
            entries.ConfirmAddCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, Master);

        // The save happened, so the sweep below is not a sweep of a file nothing wrote.
        Assert.NotNull(reopened.Find(new EntryName(string.Empty, "unrelated")));

        Assert.NotNull(reopened.Find(new EntryName("servers", "production")));
        Assert.Null(reopened.Find(new EntryName("servers", "production-db")));
        Assert.Null(reopened.Find(new EntryName("env/billing", "production-db")));
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
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(Master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
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
