using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the Secrets screen says about an entry beyond its name: its kind, where it lives and its
/// reference. Each is read from which fields are filled in or from the entry's path, never from a
/// value; an entry under <c>env/</c> is an ordinary entry.
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

        using var vault = ClaimedVault.Create(_vaultPath, _master);
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
    [InlineData("Work", "github", "Login")]
    [InlineData("Home", "wifi", "Password")]
    [InlineData("Home", "recovery", "Note")]
    [InlineData("env/acme-api", "DATABASE_URL", "Password")]
    [InlineData("env/acme-api/prod", "DATABASE_URL", "Password")]
    public void A_row_says_its_kind_and_not_its_group(string group, string title, string kind)
    {
        using var context = new UnlockedScreens(_vaultPath, _master);

        var row = context.Entries.Rows.Single(row => row.GroupPath == group && row.Title == title);

        Assert.Equal(kind, row.KindLabel);
        Assert.DoesNotContain(group, row.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_untagged_entry_under_env_is_a_password_named_by_its_entry_reference()
    {
        using var context = new UnlockedScreens(_vaultPath, _master);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api");
        var detail = context.Entries.Detail!;

        Assert.Equal(EntryKind.Password, detail.Kind);
        Assert.Equal("env › acme-api", detail.Location);
        Assert.Equal("kp:///env/acme-api/DATABASE_URL", detail.Reference);
    }

    [Fact]
    public void A_login_shows_where_it_lives_and_what_rotating_it_changes()
    {
        using var context = new UnlockedScreens(_vaultPath, _master);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var detail = context.Entries.Detail!;

        Assert.Equal(EntryKind.Login, detail.Kind);
        Assert.Equal("Work", detail.Location);
        Assert.True(detail.HasKeePassId);
        Assert.Equal("Rotate github's password? Change it on github.com too.", detail.RotatePrompt);
    }

    [Fact]
    public void An_empty_list_says_why()
    {
        using var context = new UnlockedScreens(_vaultPath, _master);
        Assert.False(context.Entries.ShowsListEmpty);

        context.Entries.Search = "nothing-is-called-this";

        Assert.True(context.Entries.ShowsListEmpty);
        Assert.Equal("No items match “nothing-is-called-this”.", context.Entries.ListEmptyNote);
        Assert.False(context.Entries.ShowsPlaceholder);
    }

    [Fact]
    public void An_entry_under_env_is_rotated_as_a_password()
    {
        using var context = new UnlockedScreens(_vaultPath, _master);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api/prod");
        var detail = context.Entries.Detail!;

        Assert.EndsWith("password? Change it where you use it too.", detail.RotatePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(_value, detail.RotatePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_login_is_made_whole_in_one_step_at_the_top_level()
    {
        using var context = new UnlockedScreens(_vaultPath, _master);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.Title == "github");
        var form = NewItemForm.Open(context.Entries, "gitlab");
        Assert.Null(context.Entries.Selected);

        Assert.True(form.ShowsUsername && form.ShowsUrl);
        form.Username = "me";
        form.Url = "https://gitlab.com";
        context.Entries.ConfirmAddCommand.Press();

        var added = context.Session.Unlocked!.Find(new EntryName(string.Empty, "gitlab"))!;
        Assert.Equal("me", added.Username);
        Assert.Equal("https://gitlab.com", added.Url);
    }

    /// <summary>A reference names the entry and holds no value, so it is copied as plain text.</summary>
    [Fact]
    public async Task Copying_the_reference_puts_the_reference_and_never_the_value_on_the_clipboard()
    {
        using var context = new UnlockedScreens(_vaultPath, _master);

        context.Entries.Selected = context.Entries.Rows.Single(row => row.GroupPath == "env/acme-api");
        await context.Entries.Detail!.CopyReferenceCommand.PressAsync();

        Assert.Equal("kp:///env/acme-api/DATABASE_URL", context.Clipboard.Content);
        Assert.False(context.Clipboard.ContentWasSetAsASecret);
    }
}
