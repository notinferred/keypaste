using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The entry pane's history: what it lists, what it reveals, and what a restore leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EntryHistoryViewModel"/> names no Avalonia type, so the list, the reveal slot and the
/// restore are all assertable against a real KDBX with no application and no window. What only a
/// visual tree can answer — dots until a pointer holds them, and what the automation tree carries
/// while it does — is <c>Controls/HistoryRevealAutomationTests</c>.
/// </para>
/// <para>
/// The seeded entry is changed three times rather than once, because the order of revisions is the
/// thing most easily got wrong: a reopened KDBX stores times to the second, so several revisions
/// can share one and a list sorted by time alone comes back backwards (DECISIONS.md D-0229).
/// </para>
/// </remarks>
public sealed class EntryHistoryTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _production = new("servers", "production");

    private readonly string _directory;
    private readonly string _vaultPath;

    public EntryHistoryTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-entry-history-tests-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);
        vault.AddEntry(new VaultEntry
        {
            Title = "production",
            GroupPath = "servers",
            Username = "u0",
            Url = "https://zero.test",
            Notes = "the first one",
            Password = "v0",
        });

        foreach (var revision in new[] { 1, 2, 3 })
        {
            vault.UpdateEntry(new VaultEntry
            {
                Title = "production",
                GroupPath = "servers",
                Username = $"u{revision}",
                Url = $"https://{revision}.test",
                Notes = $"number {revision}",
                Password = $"v{revision}",
            });
        }

        vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh" });
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
    public void History_is_read_when_it_is_asked_for_and_not_before()
    {
        using var context = New();
        var history = Select("servers/production", context);

        Assert.False(history.IsOpen);
        Assert.Empty(history.Rows);
        Assert.False(history.ShowsEmptyNote);

        history.ToggleCommand.Execute(null);

        Assert.True(history.IsOpen);
        Assert.Equal(3, history.Rows.Count);
    }

    /// <summary>
    /// The list is core's answer, in core's order, position for position.
    /// </summary>
    /// <remarks>
    /// Compared against a second, independent reading of the same file rather than a literal, so a
    /// screen that re-sorted what core handed it fails even where the two orders agree by accident.
    /// </remarks>
    [Fact]
    public void An_entry_with_three_revisions_lists_them_newest_first()
    {
        using var context = New();
        var history = Opened("servers/production", context);

        using var reopened = Vault.Open(_vaultPath, _master);
        var revisions = reopened.ReadHistory(_production);

        Assert.NotNull(revisions);
        Assert.Equal(revisions.Count, history.Rows.Count);
        Assert.Equal(
            revisions.Select(revision => revision.Fields.Username),
            history.Rows.Select(row => row.DisplayUsername));
        Assert.Equal([0, 1, 2], history.Rows.Select(row => row.Index));
        Assert.Equal(["u2", "u1", "u0"], history.Rows.Select(row => row.DisplayUsername));
    }

    [Fact]
    public void Each_revision_carries_the_time_the_vault_recorded()
    {
        using var context = New();
        var history = Opened("servers/production", context);

        using var reopened = Vault.Open(_vaultPath, _master);
        var revisions = reopened.ReadHistory(_production);

        Assert.NotNull(revisions);
        Assert.Equal(revisions.Select(revision => revision.ModifiedUtc), history.Rows.Select(row => row.ModifiedUtc));
        Assert.All(history.Rows, row => Assert.Equal(DateTimeKind.Utc, row.ModifiedUtc.Kind));
        Assert.All(history.Rows, row => Assert.NotEmpty(row.When));
    }

    [Fact]
    public void A_selected_revision_shows_its_own_fields_beside_the_current_ones()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        var detail = context.Entries.Detail!;

        history.Selected = history.Rows[^1];

        Assert.True(history.HasSelection);
        Assert.Equal("u0", history.Selected!.DisplayUsername);
        Assert.Equal("https://zero.test", history.Selected.DisplayUrl);
        Assert.Equal("the first one", history.Selected.DisplayNotes);
        Assert.Equal("v0".Length, history.Selected.MaskedLength);

        Assert.Equal("u3", detail.DisplayUsername);
        Assert.Equal("https://3.test", detail.DisplayUrl);
        Assert.Equal("v3".Length, detail.PasswordLength);
    }

    [Fact]
    public void Nothing_is_revealed_until_it_is_held()
    {
        using var context = New();
        var history = Opened("servers/production", context);

        history.Selected = history.Rows[0];

        Assert.Empty(history.RevealedWhen);
    }

    /// <summary>
    /// One revision at a time, and the hold is what keeps it.
    /// </summary>
    /// <remarks>
    /// The same claim <c>SecretHygieneTests</c> makes of the env table. A slot that is taken and
    /// never given back leaves the next row unable to say it is the revealed one, and a slot that is
    /// never taken lets two values sit on screen together.
    /// </remarks>
    [Fact]
    public void Revealing_a_revision_is_one_at_a_time_and_ends_with_the_hold()
    {
        using var context = New();
        var history = Opened("servers/production", context);

        var newest = history.Rows[0];
        var oldest = history.Rows[^1];

        Assert.Equal("v2", newest.Reveal());
        Assert.Equal(newest.When, history.RevealedWhen);

        Assert.Equal("v0", oldest.Reveal());
        Assert.Equal(oldest.When, history.RevealedWhen);

        // The row that lost the slot releasing later must not take the current one down with it.
        newest.Conceal();
        Assert.Equal(oldest.When, history.RevealedWhen);

        oldest.Conceal();
        Assert.Empty(history.RevealedWhen);
    }

    [Fact]
    public void A_reveal_through_a_locked_vault_returns_nothing_and_still_ends_cleanly()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        var row = history.Rows[0];

        context.Session.Lock(VaultLockReason.Manual);

        Assert.Null(row.Reveal());

        row.Conceal();
        Assert.Empty(history.RevealedWhen);
    }

    [Fact]
    public void Restoring_the_oldest_makes_it_current_and_reloads_the_pane()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        var detail = context.Entries.Detail!;

        history.Selected = history.Rows[^1];
        history.RestoreCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.Equal("u0", detail.DisplayUsername);
        Assert.Equal("https://zero.test", detail.DisplayUrl);
        Assert.Equal("v0".Length, detail.PasswordLength);

        Assert.Equal(4, history.Rows.Count);
        Assert.Equal("u3", history.Rows[0].DisplayUsername);
        Assert.Null(history.Selected);
        Assert.Empty(history.RevealedWhen);
    }

    /// <summary>
    /// The restore reaches the file, not just the screen.
    /// </summary>
    /// <remarks>
    /// Read after the screens are gone, from a fresh open, because
    /// <c>VaultHistoryTests.RestoreRevision_LeavesTheFileAloneUntilSave</c> makes forgetting the
    /// save a live way to pass every in-memory assertion above.
    /// </remarks>
    [Fact]
    public void The_restored_value_is_what_the_file_holds_afterwards()
    {
        using (var context = New())
        {
            var history = Opened("servers/production", context);
            history.Selected = history.Rows[^1];
            history.RestoreCommand.Execute(null);
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal("v0", reopened.Find(_production)?.Password);
        Assert.Equal(
            ["v3", "v2", "v1", "v0"],
            reopened.ReadHistory(_production)!.Select(revision => revision.Fields.Password));
    }

    [Fact]
    public void Restoring_a_vault_something_else_changed_says_so_and_writes_nothing()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        history.Selected = history.Rows[^1];

        using (var elsewhere = Vault.Open(_vaultPath, _master))
        {
            elsewhere.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "x" });
            elsewhere.Save();
        }

        var before = Digest(_vaultPath);
        history.RestoreCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.Contains("changed this vault", context.Entries.Error, StringComparison.Ordinal);
        Assert.Equal(before, Digest(_vaultPath));

        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.Equal("v3", reopened.Find(_production)?.Password);
    }

    /// <summary>
    /// A revision the reading no longer holds at that index is refused rather than guessed at.
    /// </summary>
    /// <remarks>
    /// An index is an ordinal within one reading (D-0229), and any change to the entry renumbers
    /// every one of them. Restoring position 2 of a list that has moved on would put back a revision
    /// nobody chose, silently and correctly-looking. The change here is made on the open vault
    /// rather than through the pane, which is what a merge or a second window does; the pane's own
    /// edit refreshes the list instead.
    /// </remarks>
    [Fact]
    public void A_revision_the_reading_no_longer_holds_is_refused_and_the_list_refreshes()
    {
        using var context = New();
        var history = Opened("servers/production", context);

        history.Selected = history.Rows[^1];

        context.Session.Unlocked!.UpdateEntry(new VaultEntry
        {
            Title = "production",
            GroupPath = "servers",
            Username = "u4",
            Password = "v4",
        });

        history.RestoreCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.Contains("changed since its history was read", context.Entries.Error, StringComparison.Ordinal);
        Assert.Equal(4, history.Rows.Count);
        Assert.Null(history.Selected);
        Assert.Equal("v3", reopenedPassword());

        string reopenedPassword()
        {
            using var reopened = Vault.Open(_vaultPath, _master);
            return reopened.Find(_production)?.Password ?? string.Empty;
        }
    }

    /// <summary>
    /// An edit made on the pane refreshes the list under it, rather than leaving it stale.
    /// </summary>
    /// <remarks>
    /// The list a moment earlier named each revision by a position the edit has just moved. It is
    /// refused rather than acted on either way, but a person reading an open list should not be
    /// reading one that is one revision behind the entry beside it.
    /// </remarks>
    [Fact]
    public void An_edit_on_the_pane_refreshes_an_open_history()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        var detail = context.Entries.Detail!;

        Assert.Equal(3, history.Rows.Count);

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "u4";
        detail.SaveCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.Equal(4, history.Rows.Count);
        Assert.Equal("u3", history.Rows[0].DisplayUsername);
        Assert.Null(history.Selected);
    }

    /// <summary>A closed history reads nothing when an edit is saved.</summary>
    [Fact]
    public void An_edit_leaves_a_closed_history_closed_and_unread()
    {
        using var context = New();
        var history = Select("servers/production", context);
        var detail = context.Entries.Detail!;

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "u4";
        detail.SaveCommand.Execute(null);

        Assert.False(history.IsOpen);
        Assert.Empty(history.Rows);
    }

    [Fact]
    public void Restoring_with_the_vault_locked_says_so()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        history.Selected = history.Rows[^1];

        context.Session.Lock(VaultLockReason.Manual);
        history.RestoreCommand.Execute(null);

        Assert.Equal("The vault is locked.", context.Entries.Error);
    }

    [Fact]
    public void Restoring_an_entry_that_is_no_longer_there_says_so()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        history.Selected = history.Rows[^1];

        context.Session.Unlocked!.RemoveEntry(_production);
        history.RestoreCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.Contains("no longer in this vault", context.Entries.Error, StringComparison.Ordinal);
        Assert.Empty(history.Rows);
        Assert.False(history.ShowsEmptyNote);
    }

    /// <summary>
    /// An entry nobody has changed says so, rather than showing an empty list.
    /// </summary>
    /// <remarks>
    /// Core tells "no history" apart from "no such entry" for this reason, and a screen that
    /// collapsed the two would tell somebody their history was empty when their entry was gone.
    /// </remarks>
    [Fact]
    public void An_entry_with_no_history_says_so_rather_than_showing_an_empty_list()
    {
        using var context = New();
        var history = Opened("github", context);

        Assert.Empty(history.Rows);
        Assert.True(history.ShowsEmptyNote);
        Assert.Contains("No earlier values yet", history.EmptyNote, StringComparison.Ordinal);
        Assert.Null(context.Entries.Error);
    }

    /// <summary>
    /// A name two entries answer to is refused, and does not read as "never changed".
    /// </summary>
    /// <remarks>
    /// The pane itself cannot be built for an ambiguous name — <c>EntriesViewModel.Build</c> refuses
    /// first — so the way this is reached is a second entry of that name arriving while the pane is
    /// open, which is what a merge or another window will eventually do. Core refuses to guess, and
    /// the section has to repeat that rather than show an empty list.
    /// </remarks>
    [Fact]
    public void A_name_two_entries_answer_to_is_refused_rather_than_shown_empty()
    {
        using var context = New();
        var history = Select("servers/production", context);

        context.Session.Unlocked!.AddEntry(new VaultEntry
        {
            Title = "production",
            GroupPath = "servers",
            Password = "a second one",
        });

        history.ToggleCommand.Execute(null);

        Assert.Empty(history.Rows);
        Assert.False(history.ShowsEmptyNote);
        Assert.NotNull(context.Entries.Error);
    }

    [Fact]
    public void A_locked_vault_leaves_the_history_empty_rather_than_stale()
    {
        using var context = New();
        var history = Opened("servers/production", context);

        Assert.NotEmpty(history.Rows);

        context.Session.Lock(VaultLockReason.Manual);
        history.Load();

        Assert.Empty(history.Rows);
        Assert.Null(history.Selected);
        Assert.Empty(history.RevealedWhen);
        Assert.False(history.ShowsEmptyNote);
    }

    [Fact]
    public void Closing_the_section_lets_go_of_what_it_read()
    {
        using var context = New();
        var history = Opened("servers/production", context);
        history.Selected = history.Rows[0];

        history.ToggleCommand.Execute(null);

        Assert.False(history.IsOpen);
        Assert.Empty(history.Rows);
        Assert.Null(history.Selected);
        Assert.False(history.ShowsEmptyNote);
    }

    /// <summary>
    /// At the history cap the list says what the vault now holds, not what it held plus one.
    /// </summary>
    /// <remarks>
    /// <c>CreateBackup</c> evicts the oldest revision to stay within <c>HistoryMaxItems</c>, so a
    /// restore near the end of a full list drops the revision it just restored. A screen counting
    /// its own rows up would be wrong the moment somebody used it on a long-lived entry.
    /// </remarks>
    [Fact]
    public void Restoring_at_the_history_cap_shows_what_the_vault_now_holds()
    {
        var path = Path.Combine(_directory, "capped.kdbx");

        using (var vault = Vault.Create(path, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "long-lived", Password = "p0" });

            foreach (var revision in Enumerable.Range(1, 12))
            {
                vault.UpdateEntry(new VaultEntry { Title = "long-lived", Password = $"p{revision}" });
            }

            vault.Save();
        }

        using var context = New(path);
        var history = Opened("long-lived", context);

        Assert.Equal(10, history.Rows.Count);

        history.Selected = history.Rows[9];
        history.RestoreCommand.Execute(null);

        using var reopened = Vault.Open(path, _master);
        var revisions = reopened.ReadHistory(new EntryName(string.Empty, "long-lived"));

        Assert.NotNull(revisions);
        Assert.Equal(revisions.Count, history.Rows.Count);
        Assert.Equal(
            revisions.Select(revision => revision.ModifiedUtc),
            history.Rows.Select(row => row.ModifiedUtc));
    }

    private static string Digest(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static EntryHistoryViewModel Select(string path, Context context)
    {
        context.Entries.Selected = context.Entries.Rows.Single(row => row.Path == path);

        return context.Entries.Detail!.History;
    }

    private static EntryHistoryViewModel Opened(string path, Context context)
    {
        var history = Select(path, context);
        history.ToggleCommand.Execute(null);

        return history;
    }

    private Context New() => new(_vaultPath);

    private static Context New(string vaultPath) => new(vaultPath);

    /// <summary>An unlocked session, a fake clipboard and a screen looking at them.</summary>
    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
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
