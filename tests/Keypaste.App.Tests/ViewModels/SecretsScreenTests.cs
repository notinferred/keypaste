using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the Secrets screen says about an entry beyond its name: its kind, where it lives, its
/// reference and its profiles. Each is read from which fields are filled in or from the entry's
/// path, never from a value.
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
    [InlineData("Work", "github", "Login · Work")]
    [InlineData("Home", "wifi", "Password · Home")]
    [InlineData("Home", "recovery", "Secure note · Home")]
    [InlineData("env/acme-api", "DATABASE_URL", "Env variable · acme-api · dev")]
    [InlineData("env/acme-api/prod", "DATABASE_URL", "Env variable · acme-api · prod")]
    public void A_row_says_its_kind_and_where_it_lives(string group, string title, string summary)
    {
        using var context = new Context(_vaultPath);

        var row = context.Entries.Rows.Single(row => row.GroupPath == group && row.Title == title);

        Assert.Equal(summary, row.Summary);
        Assert.DoesNotContain(_value, row.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void An_env_profile_group_heads_the_list_with_its_project_and_profile()
    {
        using var context = new Context(_vaultPath);

        Assert.Equal("acme.kdbx", context.Entries.ListTitle);
        Assert.False(context.Entries.HasListProfile);
        Assert.Equal("Filter 5 secrets", context.Entries.FilterHint);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "env/acme-api/prod");

        Assert.Equal("acme-api", context.Entries.ListTitle);
        Assert.Equal("prod", context.Entries.ListProfile);
        Assert.Equal("Filter 1 secret", context.Entries.FilterHint);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "Work");

        Assert.Equal("Work", context.Entries.ListTitle);
        Assert.Null(context.Entries.ListProfile);
    }

    [Fact]
    public void A_variable_shows_its_value_its_reference_and_every_profile_and_a_protected_one_asks()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api");
        var detail = context.Entries.Detail!;

        Assert.True(detail.IsVariable);
        Assert.Equal("Value", detail.ValueLabel);
        Assert.False(detail.ShowsUsername);
        Assert.Equal("Env variable · acme.kdbx › env › acme-api", detail.Location);
        Assert.Equal("kp://acme-api/dev/DATABASE_URL", detail.Reference);
        Assert.Equal(
            [("dev", "set"), ("prod", "approval required")],
            detail.ProfileStates.Select(state => (state.Profile, state.State)));
        Assert.True(detail.ProfileStates[1].IsAmber);
    }

    [Fact]
    public void A_login_shows_its_username_and_url_and_has_no_profiles()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var detail = context.Entries.Detail!;

        Assert.Equal(EntryKind.Login, detail.Kind);
        Assert.Equal("Password", detail.ValueLabel);
        Assert.True(detail.ShowsUsername);
        Assert.True(detail.ShowsUrl);
        Assert.False(detail.HasProfiles);
        Assert.Equal("Login · acme.kdbx › Work", detail.Location);
        Assert.Matches("^uuid [0-9a-f]{4}…[0-9a-f]{4} · field Password$", detail.KdbxEntry);
        Assert.Equal("Password", detail.ValueLabel);
        Assert.Equal("New password", detail.ReplacementPlaceholder);
        Assert.Contains("20-character one? keypaste only changes its copy", detail.RotatePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_env_project_group_lists_its_own_profile_as_its_badge_says()
    {
        using var context = new Context(_vaultPath);

        context.Entries.SelectedGroup = context.Entries.Groups.Single(group => group.Path == "env/acme-api");

        Assert.Equal("acme-api", context.Entries.ListTitle);
        Assert.Equal("dev", context.Entries.ListProfile);
        Assert.Equal("Filter 1 secret", context.Entries.FilterHint);
        Assert.Equal(["env/acme-api"], context.Entries.Rows.Select(row => row.GroupPath));
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
        Assert.Equal("No secrets match “nothing-is-called-this”.", context.Entries.ListEmptyNote);
        Assert.False(context.Entries.ListEmptyOffersNew);
    }

    [Fact]
    public void A_variable_is_edited_and_rotated_as_a_value_in_its_profile()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api/prod");
        var detail = context.Entries.Detail!;

        Assert.Equal("New value", detail.ReplacementPlaceholder);
        Assert.Contains("current value", detail.ReplacementCaption, StringComparison.Ordinal);
        Assert.Contains("DATABASE_URL in acme-api · prod", detail.RotatePrompt, StringComparison.Ordinal);
        Assert.Contains("injects this key in prod", detail.RotatePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(_value, detail.RotatePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_login_is_made_whole_in_one_step_and_a_new_key_under_env_has_no_username()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        context.Entries.BeginAddCommand.Execute(null);
        Assert.Null(context.Entries.Selected);

        context.Entries.NewEntryPath = "Work/gitlab";
        Assert.True(context.Entries.NewEntryIsLogin);
        context.Entries.NewUsername = "me";
        context.Entries.NewUrl = "https://gitlab.com";
        context.Entries.ConfirmAddCommand.Execute(null);

        var added = context.Session.Unlocked!.Find(new EntryName("Work", "gitlab"))!;
        Assert.Equal("me", added.Username);
        Assert.Equal("https://gitlab.com", added.Url);

        context.Entries.BeginAddCommand.Execute(null);
        context.Entries.NewEntryPath = "env/acme-api/API_KEY";
        Assert.False(context.Entries.NewEntryIsLogin);
        Assert.Empty(context.Entries.NewUsername);
    }

    /// <summary>A reference names the entry and holds no value, so it is copied as plain text.</summary>
    [Fact]
    public async Task Copying_the_reference_puts_the_reference_and_never_the_value_on_the_clipboard()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api");
        await context.Entries.Detail!.CopyReferenceCommand.ExecuteAsync();

        Assert.Equal("kp://acme-api/dev/DATABASE_URL", context.Clipboard.Content);
        Assert.False(context.Clipboard.ContentWasSetAsASecret);
    }

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
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
