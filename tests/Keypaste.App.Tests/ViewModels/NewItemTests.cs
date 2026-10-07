using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// New item starts from a template, takes a folder from the vault's groups, and makes the item whole
/// in one write with no history item (N.4, D-0379); a refusal writes nothing and keeps the form open.
/// </summary>
public sealed class NewItemTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-new-item-tests-").FullName;
    private readonly string _vaultPath;
    private readonly AppVaultSession _session = new(new ManualClock(AppClock.Start));
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));
    private readonly EntriesViewModel _entries;

    public NewItemTests()
    {
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using (var vault = Vault.Create(_vaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "github", GroupPath = "Work", Password = "gh" });
            vault.AddEntry(new VaultEntry { Title = "old", GroupPath = "Work/Archive", Password = "o" });
            vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", GroupPath = "env/billing", Password = "sk" });
            vault.Save();
        }

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vaultPath, master.Value));
        }

        _entries = new EntriesViewModel(_session, _countdown);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _countdown.Dispose();
        _session.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_folder_is_chosen_from_the_vaults_groups_env_among_them()
    {
        var form = NewItemForm.Open(_entries);

        Assert.Equal(["", "Work", "Work/Archive", "env", "env/billing"], form.Folders.Select(folder => folder.Path));
        Assert.Equal("Top level", form.Folders[0].Label);
        Assert.Equal(ItemTemplate.Login, form.Template);
    }

    [Fact]
    public void A_login_is_its_username_password_and_web_address()
    {
        var form = NewItemForm.Open(_entries, "Work/gitlab");
        form.Username = "me";
        form.Url = "https://gitlab.com";
        form.Notes = "the work account";
        form.DraftTag = "work";
        form.AddTagCommand.Execute(null);
        _entries.ConfirmAddCommand.Execute(null);

        Assert.False(_entries.IsAdding);
        Assert.Equal("gitlab", _entries.Selected?.Title);
        var item = Reopened(new EntryName("Work", "gitlab"));
        Assert.Equal(("me", "https://gitlab.com", "the work account"), (item.Entry.Username, item.Entry.Url, item.Entry.Notes));
        Assert.Equal(PasswordGenerator.DefaultLength, item.Entry.Password.Length);
        Assert.Equal(["work"], item.Tags);
        Assert.Empty(item.Fields);
        Assert.Equal(0, item.Revisions);
    }

    [Fact]
    public void An_API_key_is_its_env_named_key_held_as_a_protected_field()
    {
        var form = NewItemForm.Open(_entries, "Work/OpenAI");
        form.Template = ItemTemplate.ApiKey;
        form.KeyName = "OPENAI_API_KEY";
        Type(form.KeyValue, "sk-proj-n4");
        form.DraftTag = "env:acme";
        form.AddTagCommand.Execute(null);
        _entries.ConfirmAddCommand.Execute(null);

        var item = Reopened(new EntryName("Work", "OpenAI"));
        Assert.Equal([new EntryField("OPENAI_API_KEY", true, false)], item.Fields);
        Assert.Equal("sk-proj-n4", item.Read("OPENAI_API_KEY"));
        Assert.Equal((string.Empty, string.Empty), (item.Entry.Username, item.Entry.Password));
        Assert.Equal(["env:acme"], item.Tags);
        Assert.Equal(0, item.Revisions);
    }

    [Theory]
    [InlineData("Database", "db.example.com:5432")]
    [InlineData("Server", "build.example.com")]
    public void A_database_or_server_is_its_host_username_and_password(string template, string host)
    {
        var form = NewItemForm.Open(_entries, "Work/box");
        form.Template = Enum.Parse<ItemTemplate>(template);
        form.Host = host;
        form.Username = "admin";
        form.GeneratePassword = false;
        Type(form.Password, "box-pw");
        _entries.ConfirmAddCommand.Execute(null);

        var item = Reopened(new EntryName("Work", "box"));
        Assert.Equal([new EntryField(NewItemViewModel.HostField, false, false)], item.Fields);
        Assert.Equal(host, item.Read(NewItemViewModel.HostField));
        Assert.Equal(("admin", "box-pw", string.Empty), (item.Entry.Username, item.Entry.Password, item.Entry.Url));
        Assert.Equal(0, item.Revisions);
    }

    [Fact]
    public void A_secure_note_is_its_notes_alone()
    {
        var form = NewItemForm.Open(_entries, "recovery codes");
        form.Template = ItemTemplate.SecureNote;
        form.Username = "left over from the login";
        form.Notes = "1234-5678";
        _entries.ConfirmAddCommand.Execute(null);

        var item = Reopened(new EntryName(string.Empty, "recovery codes"));
        Assert.Equal("1234-5678", item.Entry.Notes);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (item.Entry.Username, item.Entry.Password, item.Entry.Url));
        Assert.Empty(item.Fields);
    }

    [Fact]
    public void Switching_templates_keeps_the_title_folder_tags_and_notes()
    {
        var form = NewItemForm.Open(_entries, "Work/thing");
        form.Notes = "kept";
        form.DraftTag = "kept";
        form.AddTagCommand.Execute(null);

        form.Template = ItemTemplate.Server;
        form.SelectedTemplate = form.Templates.Single(choice => choice.Template == ItemTemplate.SecureNote);

        Assert.Equal(("thing", "Work", "kept"), (form.Title, form.Folder.Path, form.Notes));
        Assert.Equal(["kept"], form.Tags.Select(chip => chip.Tag));
        Assert.False(form.ShowsPassword || form.ShowsHost || form.ShowsKey || form.ShowsUrl);
    }

    [Fact]
    public void A_choice_announces_the_fields_and_the_button_that_read_it()
    {
        var form = NewItemForm.Open(_entries);
        var raised = new List<string>();
        form.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        var asked = 0;
        form.AddTagCommand.CanExecuteChanged += (_, _) => asked++;

        form.Template = ItemTemplate.ApiKey;

        string[] shown =
        [
            nameof(NewItemViewModel.Template),
            nameof(NewItemViewModel.SelectedTemplate),
            nameof(NewItemViewModel.ShowsUsername),
            nameof(NewItemViewModel.ShowsPassword),
            nameof(NewItemViewModel.ShowsUrl),
            nameof(NewItemViewModel.ShowsHost),
            nameof(NewItemViewModel.ShowsKey),
        ];
        Assert.Equal(shown.Order(StringComparer.Ordinal), raised.Order(StringComparer.Ordinal));

        form.DraftTag = "a,b";
        form.AddTagCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.Contains(nameof(NewItemViewModel.HasError), raised);
        Assert.True(form.HasError);
    }

    [Theory]
    [InlineData("github", "otp", "is already in that group")]
    [InlineData("", "OPENAI_API_KEY", "An item needs a title.")]
    [InlineData("OpenAI", "otp", "Name the key as a project reads it")]
    [InlineData("OpenAI", "api key", "Name the key as a project reads it")]
    [InlineData("OpenAI", "KPXC_KEY", "Name the key as a project reads it")]
    public void A_refused_item_writes_nothing_and_keeps_the_form_open(string title, string key, string says)
    {
        var before = File.ReadAllBytes(_vaultPath);
        var form = NewItemForm.Open(_entries, "Work/" + title);
        form.Template = title == "github" ? ItemTemplate.Login : ItemTemplate.ApiKey;
        form.KeyName = key;
        Type(form.KeyValue, "sk-refused");
        _entries.ConfirmAddCommand.Execute(null);

        Assert.True(_entries.IsAdding);
        Assert.Contains(says, form.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_vaultPath));
    }

    [Fact]
    public void Cancelling_or_locking_disposes_what_was_typed()
    {
        var form = NewItemForm.Open(_entries);
        form.Template = ItemTemplate.ApiKey;
        Type(form.KeyValue, "sk-typed");

        _entries.CancelAddCommand.Execute(null);

        Assert.Null(_entries.NewItem);
        Assert.True(form.KeyValue.IsZeroed);
    }

    private static void Type(SecretField field, string text)
    {
        foreach (var c in text)
        {
            field.Type(c);
        }
    }

    private Item Reopened(EntryName name)
    {
        using var vault = Vault.Open(_vaultPath, _master);
        var entry = vault.Find(name) ?? throw new Xunit.Sdk.XunitException($"no '{name}' in the file");
        var fields = vault.Fields(name)!;
        return new Item(
            entry,
            fields,
            vault.Tags(name)!,
            vault.ReadHistory(name)!.Count,
            fields.ToDictionary(field => field.Name, field => vault.ReadField(name, field.Name)!));
    }

    private sealed record Item(VaultEntry Entry, IReadOnlyList<EntryField> Fields, IReadOnlyList<string> Tags, int Revisions, Dictionary<string, string> Values)
    {
        internal string Read(string field) => Values[field];
    }
}
