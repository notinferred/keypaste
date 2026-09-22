using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
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
/// V.3b claims. So the entry deletions run through <see cref="EntriesViewModel"/> and the variable
/// deletion through <see cref="EnvProjectViewModel"/>, exactly as a person reaches them.
/// </para>
/// <para>
/// <b>What the file holds is asserted by opening it again.</b> A restore that only changed the
/// vault in memory would satisfy every screen in this file, so each recovery is read back through
/// a separate <see cref="Vault.Open(string, ReadOnlySpan{char})"/> after the session has gone.
/// </para>
/// <para>
/// There is no automation test beside this one, the way <c>HistoryRevealAutomationTests</c> sits
/// beside the history pane. Nothing on this screen draws a secret: a
/// <see cref="Keypaste.Core.RecycledEntry"/> carries no field value, so there is no masked control
/// to hold, no reveal to end and no drawn value to prove (D-0099, D-0232).
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

        using var vault = Vault.Create(_vaultPath, _master);

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

        var store = new EnvStore(vault);
        store.TrySet("billing", "STRIPE_KEY", "sk-live", out _);
        store.TrySet("dev", "STRIPE_KEY", "sk-test", out _);

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
        using var context = new Context(_vaultPath);

        Assert.True(context.Trash.IsEmpty);
        Assert.Empty(context.Trash.Rows);
        Assert.Equal(TrashViewModel.RecycledNote, context.Trash.Note);
        Assert.Contains("KDBX 4.1", context.Trash.ReaderFloor, StringComparison.Ordinal);
    }

    /// <summary>
    /// The list is what the deletion produced, named and placed — and nothing from inside the
    /// entry, which is <c>SecretHygieneTests</c>' claim and this row's shape.
    /// </summary>
    [Fact]
    public void A_deletion_made_in_the_app_appears_in_the_trash()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");

        var trash = context.NewTrash();
        var row = Assert.Single(trash.Rows);

        Assert.Equal("production", row.DisplayTitle, StringComparer.Ordinal);
        Assert.Equal("servers", row.Where, StringComparer.Ordinal);
        Assert.True(row.CameFromALiveGroup);
        Assert.Contains("from servers", row.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deleted_password_is_restored_and_the_file_holds_it_again()
    {
        using (var context = new Context(_vaultPath))
        {
            Delete(context, "servers/production");

            var trash = context.NewTrash();
            trash.Selected = Assert.Single(trash.Rows);
            trash.RestoreCommand.Execute(null);

            Assert.Equal("production is back in servers.", trash.Notice, StringComparer.Ordinal);
            Assert.Null(trash.Error);
            Assert.True(trash.IsEmpty);
        }

        using var reopened = Vault.Open(_vaultPath, _master);
        var entry = reopened.Find(new EntryName("servers", "production"));

        Assert.NotNull(entry);
        Assert.Equal("v1", entry.Password, StringComparer.Ordinal);

        // The history came back with it, which is what distinguishes a recycled entry from a
        // re-created one.
        var revisions = reopened.ReadHistory(new EntryName("servers", "production"));
        Assert.NotNull(revisions);
        Assert.Equal("v0", Assert.Single(revisions).Fields.Password, StringComparer.Ordinal);
    }

    [Fact]
    public void A_deleted_variable_is_restored_and_the_project_holds_it_again()
    {
        using (var context = new Context(_vaultPath))
        {
            RemoveVariable(context, "billing", "STRIPE_KEY");

            var trash = context.NewTrash();
            trash.Selected = Assert.Single(trash.Rows);
            trash.RestoreCommand.Execute(null);

            Assert.Equal("STRIPE_KEY is back in env/billing.", trash.Notice, StringComparer.Ordinal);
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal(
            "sk-live",
            Assert.Single(new EnvStore(reopened).Read("billing")).Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Two variables of one name from two projects land in one bin. The row a person selected is
    /// the row that comes back, because the address is an identity and not a title (D-0250).
    /// </summary>
    [Fact]
    public void Duplicate_names_restore_the_selected_identity()
    {
        using (var context = new Context(_vaultPath))
        {
            RemoveVariable(context, "billing", "STRIPE_KEY");
            RemoveVariable(context, "dev", "STRIPE_KEY");

            var trash = context.NewTrash();
            Assert.Equal(2, trash.Rows.Count);

            trash.Selected = trash.Rows.Single(row => row.Where == "env/dev");
            trash.RestoreCommand.Execute(null);

            Assert.Equal("STRIPE_KEY is back in env/dev.", trash.Notice, StringComparer.Ordinal);
            Assert.Equal("env/billing", Assert.Single(trash.Rows).Where, StringComparer.Ordinal);
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal(
            "sk-test",
            Assert.Single(new EnvStore(reopened).Read("dev")).Value,
            StringComparer.Ordinal);

        Assert.Empty(new EnvStore(reopened).Read("billing"));
    }

    [Fact]
    public void Permanent_deletion_needs_its_own_confirmation()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");

        var trash = context.NewTrash();
        trash.Selected = Assert.Single(trash.Rows);

        // Asking is not doing: the file is untouched until the second act.
        var armed = Digest(_vaultPath);
        trash.PurgeCommand.Execute(null);

        Assert.True(trash.IsConfirmingPurge);
        Assert.Contains("cannot be undone", trash.PurgePrompt, StringComparison.Ordinal);
        Assert.Equal(armed, Digest(_vaultPath));

        trash.CancelPurgeCommand.Execute(null);

        Assert.False(trash.IsConfirmingPurge);
        Assert.Equal(armed, Digest(_vaultPath));
        Assert.Single(trash.Rows);

        trash.PurgeCommand.Execute(null);
        trash.ConfirmPurgeCommand.Execute(null);

        Assert.Equal("production and its history are gone.", trash.Notice, StringComparer.Ordinal);
        Assert.True(trash.IsEmpty);
        Assert.NotEqual(armed, Digest(_vaultPath));
    }

    /// <summary>
    /// The refusal D-0249 requires, worded for somebody who cannot rename in the app yet, and
    /// writing nothing.
    /// </summary>
    [Fact]
    public void A_restore_is_refused_when_the_name_has_been_taken_again()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");
        Add(context, "servers/production");

        var trash = context.NewTrash();
        trash.Selected = Assert.Single(trash.Rows);

        var before = Digest(_vaultPath);
        trash.RestoreCommand.Execute(null);

        Assert.Null(trash.Notice);
        Assert.NotNull(trash.Error);
        Assert.Contains("cannot go back yet", trash.Error, StringComparison.Ordinal);
        Assert.Equal(before, Digest(_vaultPath));
        Assert.Single(trash.Rows);
    }

    /// <summary>
    /// The positive control for the digest: a check that never sees the bytes change would pass
    /// for a screen that writes nothing at all.
    /// </summary>
    [Fact]
    public void A_restore_that_works_does_change_the_file()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");

        var trash = context.NewTrash();
        var before = Digest(_vaultPath);

        trash.Selected = Assert.Single(trash.Rows);
        trash.RestoreCommand.Execute(null);

        Assert.NotEqual(before, Digest(_vaultPath));
    }

    [Fact]
    public void A_row_whose_entry_has_gone_is_reported_rather_than_acted_on()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");

        var trash = context.NewTrash();
        trash.Selected = Assert.Single(trash.Rows);

        // Erased by something else holding the same vault — here, the session the screen is not
        // looking at.
        var vault = context.Session.Unlocked!;
        Assert.True(vault.PurgeRecycled(trash.Selected!.Id));
        vault.Save();

        trash.RestoreCommand.Execute(null);

        Assert.Equal("production is not in the trash any more.", trash.Error, StringComparer.Ordinal);
        Assert.True(trash.IsEmpty);
    }

    [Fact]
    public void A_vault_with_no_recycle_bin_says_so_rather_than_showing_an_empty_list()
    {
        var path = Path.Combine(_directory, "no-bin.kdbx");

        using (var vault = Vault.Create(path, _master))
        {
            vault.SetRecyclesDeletedEntries(false);
            vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            vault.Save();
        }

        using var context = new Context(path);

        Delete(context, "github");

        var trash = context.NewTrash();

        Assert.False(trash.RecyclesDeletedEntries);
        Assert.Equal(TrashViewModel.NoBinNote, trash.Note);
        Assert.Equal("Nothing can be recovered from this vault.", trash.EmptyNote, StringComparer.Ordinal);
        Assert.True(trash.IsEmpty);
    }

    [Fact]
    public void Locking_clears_the_list_the_selection_and_the_outcome()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");

        var trash = context.NewTrash();
        trash.Selected = Assert.Single(trash.Rows);
        trash.PurgeCommand.Execute(null);

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
    public void The_entry_screen_states_the_outcome_and_can_undo_it()
    {
        using (var context = new Context(_vaultPath))
        {
            Delete(context, "servers/production");

            Assert.Equal("Moved servers/production to the trash.", context.Entries.Notice, StringComparer.Ordinal);
            Assert.True(context.Entries.CanUndoDelete);
            Assert.DoesNotContain(context.Entries.Rows, row => row.Path == "servers/production");

            context.Entries.UndoDeleteCommand.Execute(null);

            Assert.False(context.Entries.CanUndoDelete);
            Assert.Null(context.Entries.Notice);
            Assert.Null(context.Entries.Error);
            Assert.Contains(context.Entries.Rows, row => row.Path == "servers/production");
            Assert.Empty(context.NewTrash().Rows);
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal(
            "v1",
            reopened.Find(new EntryName("servers", "production"))?.Password,
            StringComparer.Ordinal);
    }

    [Fact]
    public void A_vault_with_no_recycle_bin_offers_no_undo()
    {
        var path = Path.Combine(_directory, "no-bin.kdbx");

        using (var vault = Vault.Create(path, _master))
        {
            vault.SetRecyclesDeletedEntries(false);
            vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
            vault.Save();
        }

        using var context = new Context(path);

        Delete(context, "github");

        Assert.Equal(
            "Deleted github. This vault has no recycle bin, so nothing can put it back.",
            context.Entries.Notice,
            StringComparer.Ordinal);

        Assert.False(context.Entries.CanUndoDelete);
    }

    [Fact]
    public void The_offer_to_undo_is_dropped_by_the_next_action()
    {
        using var context = new Context(_vaultPath);

        Delete(context, "servers/production");
        Assert.True(context.Entries.CanUndoDelete);

        context.Entries.BeginAddCommand.Execute(null);

        Assert.False(context.Entries.CanUndoDelete);
        Assert.Null(context.Entries.Notice);
    }

    [Fact]
    public void The_env_card_states_where_the_variable_went()
    {
        using var context = new Context(_vaultPath);

        RemoveVariable(context, "billing", "STRIPE_KEY");

        Assert.Equal(
            "Moved STRIPE_KEY to the trash. Restore it there.",
            context.Env.Notice,
            StringComparer.Ordinal);
    }

    private static void Delete(Context context, string path)
    {
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Path == path);
        context.Entries.DeleteCommand.Execute(null);
        context.Entries.ConfirmDeleteCommand.Execute(null);
    }

    private static void Add(Context context, string path)
    {
        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = path;
        context.Entries.ConfirmAddCommand.Execute(null);

        Assert.Null(context.Entries.Error);
    }

    private static void RemoveVariable(Context context, string project, string key)
    {
        context.Env.OpenCommand.Execute(project);

        var open = context.Env.OpenProject!;
        open.Variables.Single(row => row.Key == key).RemoveCommand.Execute(null);
        open.ConfirmRemoveCommand.Execute(null);
    }

    private static string Digest(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>An unlocked session and the three screens that reach the bin.</summary>
    private sealed class Context : IDisposable
    {
        private TrashViewModel? _trash;

        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
            Entries = new EntriesViewModel(Session, Countdown);
            Env = new EnvSetsViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal EnvSetsViewModel Env { get; }

        /// <summary>The Trash screen as a navigation builds it: read fresh, never cached.</summary>
        internal TrashViewModel Trash => _trash ??= new TrashViewModel(Session);

        /// <summary>Navigates to Trash again, which is what a person does after deleting.</summary>
        internal TrashViewModel NewTrash()
        {
            _trash?.Dispose();
            _trash = new TrashViewModel(Session);
            return _trash;
        }

        public void Dispose()
        {
            _trash?.Dispose();
            Env.Dispose();
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
