using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the Secrets screen says about an entry beyond its name: its kind, where it lives and its
/// reference. Each is read from which fields are filled in or from the entry's path, never from a
/// value; an entry under <c>env/</c> is an ordinary entry (D-0416).
/// </summary>
public sealed class SecretsScreenTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _value = "SENTINEL-SECRETS-SCREEN-93af";

    private readonly string _directory;
    private readonly string _vaultPath;

    public SecretsScreenTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-secrets-screen-").FullName;
        _vaultPath = Path.Combine(_directory, "acme.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);
        vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = _value, Url = "https://github.com", GroupPath = "Work" });
        vault.AddEntry(new VaultEntry { Title = "wifi", Password = _value, GroupPath = "Home" });
        vault.AddEntry(new VaultEntry { Title = "recovery", Notes = "in the safe", GroupPath = "Home" });
        vault.AddEntry(new VaultEntry { Title = "DATABASE_URL", Password = _value, GroupPath = "env/acme-api" });
        vault.AddEntry(new VaultEntry { Title = "DATABASE_URL", Password = _value, GroupPath = "env/acme-api/prod" });
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

    [Theory]
    [InlineData("Work", "github", "Login", "Work")]
    [InlineData("Home", "wifi", "Password", "Home")]
    [InlineData("Home", "recovery", "Secure note", "Home")]
    [InlineData("env/acme-api", "DATABASE_URL", "Password", "env/acme-api")]
    [InlineData("env/acme-api/prod", "DATABASE_URL", "Password", "env/acme-api/prod")]
    public void A_row_says_its_kind_and_where_it_lives(string group, string title, string kind, string place)
    {
        using var context = new Context(_vaultPath);

        var row = context.Entries.Rows.Single(row => row.GroupPath == group && row.Title == title);

        Assert.Equal(kind, row.KindLabel);
        Assert.Equal(place, row.Where);
        Assert.DoesNotContain(_value, row.Where, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_under_env_heads_the_list_as_any_group_does()
    {
        using var context = new Context(_vaultPath);

        Assert.Equal("acme.kdbx", context.Entries.ListTitle);
        Assert.Null(context.Entries.SearchScope);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "env/acme-api/prod");

        Assert.Equal("env/acme-api/prod", context.Entries.ListTitle);
        Assert.Equal("env/acme-api/prod", context.Entries.SearchScope);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "Work");

        Assert.Equal("Work", context.Entries.ListTitle);
    }

    [Fact]
    public void An_untagged_entry_under_env_is_a_password_named_by_its_entry_reference()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api");
        var detail = context.Entries.Detail!;

        Assert.Equal(EntryKind.Password, detail.Kind);
        Assert.Equal("Password · acme.kdbx › env › acme-api", detail.Location);
        Assert.Equal("kp:///env/acme-api/DATABASE_URL", detail.Reference);
    }

    [Fact]
    public void A_login_shows_where_it_lives_and_what_rotating_it_changes()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var detail = context.Entries.Detail!;

        Assert.Equal(EntryKind.Login, detail.Kind);
        Assert.Equal("Login · acme.kdbx › Work", detail.Location);
        Assert.Matches("^uuid [0-9a-f]{4}…[0-9a-f]{4} · field Password$", detail.KdbxEntry);
        Assert.Contains("20-character one? keypaste only changes its copy", detail.RotatePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_under_env_lists_its_subgroups_entries_as_a_folder_does()
    {
        using var context = new Context(_vaultPath);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "env/acme-api");

        Assert.Equal("env/acme-api", context.Entries.ListTitle);
        Assert.Equal(["env/acme-api", "env/acme-api/prod"], context.Entries.Rows.Select(row => row.GroupPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_list_starts_on_everything_and_highlights_it()
    {
        using var context = new Context(_vaultPath);

        Assert.NotNull(context.Entries.SelectedGroup);
        Assert.True(context.Entries.SelectedGroup!.IsEverything);
    }

    [Fact]
    public void An_empty_list_says_why()
    {
        using var context = new Context(_vaultPath);
        Assert.False(context.Entries.ShowsListEmpty);

        context.Entries.Search = "nothing-is-called-this";

        Assert.True(context.Entries.ShowsListEmpty);
        Assert.Equal("No items match “nothing-is-called-this”.", context.Entries.ListEmptyNote);
        Assert.False(context.Entries.ListEmptyOffersNew);
    }

    [Fact]
    public void An_entry_under_env_is_rotated_as_a_password()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api/prod");
        var detail = context.Entries.Detail!;

        Assert.Contains("20-character one? keypaste only changes its copy", detail.RotatePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(_value, detail.RotatePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_login_is_made_whole_in_one_step_and_env_is_a_folder_like_any_other()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var form = NewItemForm.Open(context.Entries, "Work/gitlab");
        Assert.Null(context.Entries.Selected);

        Assert.True(form.ShowsUsername && form.ShowsUrl);
        form.Username = "me";
        form.Url = "https://gitlab.com";
        context.Entries.ConfirmAddCommand.Execute(null);

        var added = context.Session.Unlocked!.Find(new EntryName("Work", "gitlab"))!;
        Assert.Equal("me", added.Username);
        Assert.Equal("https://gitlab.com", added.Url);

        form = NewItemForm.Open(context.Entries);
        Assert.Contains(form.Folders, folder => folder.Path == "env/acme-api");
    }

    /// <summary>A reference names the entry and holds no value, so it is copied as plain text.</summary>
    [Fact]
    public async Task Copying_the_reference_puts_the_reference_and_never_the_value_on_the_clipboard()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api");
        await context.Entries.Detail!.CopyReferenceCommand.ExecuteAsync();

        Assert.Equal("kp:///env/acme-api/DATABASE_URL", context.Clipboard.Content);
        Assert.False(context.Clipboard.ContentWasSetAsASecret);
    }

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock(AppClock.Start));
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
