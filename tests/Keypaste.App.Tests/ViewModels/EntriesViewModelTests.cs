using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The Entries screen, driven without a display.
/// </summary>
/// <remarks>
/// <see cref="EntriesViewModel"/> names no Avalonia type, so everything it does — searching, the
/// group tree, adding, editing, deleting — is assertable against a real KDBX with no application and
/// no window. The headless session is for claims that are only true of a visual tree.
/// </remarks>
public sealed class EntriesViewModelTests : IDisposable
{
    internal const string Master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;

    public EntriesViewModelTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-entries-tests-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, Master);
        vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh" });
        vault.AddEntry(new VaultEntry { Title = "production", Password = "p", GroupPath = "servers" });
        vault.AddEntry(new VaultEntry { Title = "staging", Password = "s", GroupPath = "servers" });
        vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = "sk", GroupPath = "env/billing" });
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
    public void Every_entry_is_listed()
    {
        using var context = New();

        Assert.Equal(4, context.Entries.Rows.Count);
        Assert.Equal(4, context.Entries.TotalCount);
    }

    /// <summary>
    /// The group tree is built from the flat paths core hands back, parents included.
    /// </summary>
    /// <remarks>
    /// <c>env/billing</c> is the case that matters: core lists it, but never lists <c>env</c> as a
    /// separate row when nothing is directly in it. A tree that took the paths literally would draw
    /// one row called "env/billing" rather than a group inside a group.
    /// </remarks>
    [Fact]
    public void The_group_tree_has_a_row_for_every_level()
    {
        using var context = New();

        var labels = context.Entries.Groups.Select(group => group.Label).ToArray();

        Assert.Equal(["All entries", "env", "billing", "servers"], labels);
        Assert.Equal([0, 1, 2, 1], context.Entries.Groups.Select(group => group.Depth).ToArray());
    }

    [Fact]
    public void Selecting_a_group_shows_what_is_inside_it_and_inside_its_children()
    {
        using var context = New();

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "env");

        Assert.Equal(["STRIPE_KEY"], context.Entries.Rows.Select(row => row.Title));
    }

    [Fact]
    public void Selecting_everything_shows_everything_again()
    {
        using var context = New();

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "servers");
        Assert.Equal(2, context.Entries.Rows.Count);

        context.Entries.SelectedGroup = context.Entries.Groups[0];
        Assert.Equal(4, context.Entries.Rows.Count);
    }

    [Theory]
    [InlineData("prod", "production")]
    [InlineData("PROD", "production")]
    [InlineData("servers", "production")]
    [InlineData("hub", "github")]
    public void Search_matches_a_title_or_a_group_either_case(string search, string expected)
    {
        using var context = New();

        context.Entries.Search = search;

        Assert.Contains(context.Entries.Rows, row => row.Title == expected);
    }

    [Fact]
    public void Search_narrows_within_the_selected_group()
    {
        using var context = New();

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "servers");
        context.Entries.Search = "github";

        Assert.Empty(context.Entries.Rows);
    }

    [Fact]
    public void Selecting_an_entry_reads_its_fields()
    {
        using var context = New();

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");

        Assert.NotNull(context.Entries.Detail);
        Assert.Equal("me", context.Entries.Detail.Username);
        Assert.Equal(2, context.Entries.Detail.PasswordLength);
    }

    [Fact]
    public void An_added_entry_is_in_the_file_with_a_generated_password()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = "servers/database";
        context.Entries.ConfirmAddCommand.Execute(null);

        Assert.Null(context.Entries.Error);
        Assert.Contains(context.Entries.Rows, row => row.Path == "servers/database");

        using var reopened = Vault.Open(_vaultPath, Master);
        var written = reopened.Find("servers/database");

        Assert.NotNull(written);
        Assert.Equal(PasswordGenerator.DefaultLength, written.Password.Length);
    }

    [Fact]
    public void An_added_entry_without_a_generated_password_has_none()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = "blank";
        context.Entries.GeneratePassword = false;
        context.Entries.ConfirmAddCommand.Execute(null);

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.Equal(string.Empty, reopened.Find("blank")?.Password);
    }

    [Fact]
    public void Adding_a_name_that_already_exists_says_so_and_writes_nothing()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = "github";
        context.Entries.ConfirmAddCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.Equal(4, context.Entries.TotalCount);
    }

    [Fact]
    public void Adding_nothing_says_so()
    {
        using var context = New();

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = "   ";
        context.Entries.ConfirmAddCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.True(context.Entries.IsAdding);
    }

    /// <summary>
    /// Deleting takes two clicks, because there is nothing to undo.
    /// </summary>
    [Fact]
    public void Deleting_needs_confirming_first()
    {
        using var context = New();

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "staging");
        context.Entries.DeleteCommand.Execute(null);

        Assert.True(context.Entries.IsConfirmingDelete);
        Assert.Contains("servers/staging", context.Entries.DeletePrompt, StringComparison.Ordinal);

        // Still there: asking is not doing.
        using (var untouched = Vault.Open(_vaultPath, Master))
        {
            Assert.NotNull(untouched.Find("servers/staging"));
        }

        context.Entries.ConfirmDeleteCommand.Execute(null);

        Assert.DoesNotContain(context.Entries.Rows, row => row.Title == "staging");

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.Null(reopened.Find("servers/staging"));
    }

    [Fact]
    public void Changing_the_mind_about_a_delete_keeps_the_entry()
    {
        using var context = New();

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "staging");
        context.Entries.DeleteCommand.Execute(null);
        context.Entries.CancelDeleteCommand.Execute(null);

        Assert.False(context.Entries.IsConfirmingDelete);

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.NotNull(reopened.Find("servers/staging"));
    }

    [Fact]
    public void An_inline_edit_is_written_to_the_file()
    {
        using var context = New();

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var detail = context.Entries.Detail!;

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "someone-else";
        detail.DraftUrl = "https://github.example";
        detail.SaveCommand.Execute(null);

        Assert.False(detail.IsEditing);
        Assert.Equal("someone-else", detail.Username);

        using var reopened = Vault.Open(_vaultPath, Master);
        var written = reopened.Find("github");

        Assert.Equal("someone-else", written?.Username);
        Assert.Equal("https://github.example", written?.Url);

        // The password the edit did not touch is the password that is still there.
        Assert.Equal("gh", written?.Password);
    }

    [Fact]
    public void Cancelling_an_edit_writes_nothing()
    {
        using var context = New();

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var detail = context.Entries.Detail!;

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "someone-else";
        detail.CancelCommand.Execute(null);

        Assert.Equal("me", detail.Username);

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.Equal("me", reopened.Find("github")?.Username);
    }

    /// <summary>
    /// A write refused because something else changed the file says so, calmly, and loses nothing.
    /// </summary>
    /// <remarks>
    /// The GUI is where this actually happens: it holds a vault for up to eight hours while somebody
    /// alt-tabs to a terminal, which <c>docs/desktop.md</c> describes as normal. The CLI's window is
    /// milliseconds.
    /// </remarks>
    [Fact]
    public void An_edit_over_a_file_something_else_changed_is_refused_and_says_so()
    {
        using var context = New();

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var detail = context.Entries.Detail!;

        using (var elsewhere = Vault.Open(_vaultPath, Master))
        {
            elsewhere.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "x" });
            elsewhere.Save();
        }

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "someone-else";
        detail.SaveCommand.Execute(null);

        Assert.NotNull(context.Entries.Error);
        Assert.Contains("changed this vault", context.Entries.Error, StringComparison.Ordinal);

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.NotNull(reopened.Find("from-the-terminal"));
        Assert.Equal("me", reopened.Find("github")?.Username);
    }

    [Fact]
    public void A_locked_vault_leaves_an_empty_screen_rather_than_a_stale_one()
    {
        using var context = New();

        Assert.NotEmpty(context.Entries.Rows);

        context.Session.Lock(VaultLockReason.Manual);
        context.Entries.Reload();

        Assert.Empty(context.Entries.Rows);
        Assert.Null(context.Entries.Detail);
    }

    private Context New() => new(_vaultPath);

    /// <summary>An unlocked session, a fake clipboard and a screen looking at them.</summary>
    /// <remarks>
    /// Everything is built inside the constructor rather than handed in, so no disposable escapes
    /// the method that made it — which is what satisfies CA2000, an error in this repository, by
    /// construction rather than by suppression.
    /// </remarks>
    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(Master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock());
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal FakeClipboard Clipboard { get; }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
