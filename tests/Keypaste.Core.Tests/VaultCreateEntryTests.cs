using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An item made from a template: its standard fields, custom fields and tags in one change with no
/// history item, in a group that exists, under the field-name and tag rules; a refusal writes nothing (D-0379).
/// </summary>
public sealed class VaultCreateEntryTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _openAi = new("Work", "OpenAI");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-create-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_item_is_created_whole_in_one_edit_and_keeps_no_history()
    {
        var path = NewVaultPath();
        var edits = new List<VaultEdit>();

        using (var vault = Seeded(path))
        {
            vault.Edited += (_, edit) => edits.Add(edit);
            vault.CreateEntry(
                new VaultEntry { Title = "OpenAI", GroupPath = "Work", Notes = "billing account" },
                [new FieldWrite("OPENAI_API_KEY", "sk-proj-1"), new FieldWrite("Host", "db.example", Protect: false)],
                ["env:acme", "ai"]);
            vault.Save();
        }

        var edit = Assert.Single(edits);
        Assert.Equal(_openAi, Assert.Single(edit.Entries));

        using var reopened = Vault.Open(path, _master);
        var entry = reopened.Find(_openAi)!;
        Assert.Equal("billing account", entry.Notes);
        Assert.Equal([new EntryField("Host", false, false), new EntryField("OPENAI_API_KEY", true, false)], reopened.Fields(_openAi));
        Assert.Equal("sk-proj-1", reopened.ReadField(_openAi, "OPENAI_API_KEY"));
        Assert.Equal(["ai", "env:acme"], reopened.Tags(_openAi)!.Order(StringComparer.Ordinal));
        Assert.Empty(reopened.ReadHistory(_openAi)!);
    }

    [Fact]
    public void A_login_keeps_its_password_protected_as_KeePass_does()
    {
        var path = NewVaultPath();

        using (var vault = Seeded(path))
        {
            vault.CreateEntry(new VaultEntry { Title = "github", GroupPath = "Work", Username = "me", Password = "pw-1", Url = "https://github.com" }, [], []);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        var github = reopened.Find(new EntryName("Work", "github"))!;
        Assert.Equal(("me", "pw-1", "https://github.com"), (github.Username, github.Password, github.Url));
        Assert.Empty(reopened.ReadHistory(new EntryName("Work", "github"))!);
    }

    /// <summary>A group under <c>env</c> is an ordinary group, and an item made there joins no project by being there (D-0416).</summary>
    [Fact]
    public void An_item_is_created_in_a_group_under_env_like_any_other()
    {
        var path = NewVaultPath();
        var token = new EntryName("env/acme", "OPENAI_API_KEY");

        using (var vault = Seeded(path))
        {
            vault.AddEntry(new VaultEntry { Title = "OLD_KEY", GroupPath = "env/acme", Password = "old" });
            vault.CreateEntry(new VaultEntry { Title = token.Title, GroupPath = token.GroupPath, Password = "sk-proj-1" }, [], []);
            vault.Save();
        }

        using var reopened = Vault.Open(path, _master);
        Assert.Equal("sk-proj-1", reopened.Find(token)?.Password);
        Assert.Empty(ProjectCatalog.Read(reopened).Projects);
    }

    [Theory]
    [InlineData("Work", "Stripe", "", "", "is already in that group")]
    [InlineData("Work", "", "", "", "")]
    [InlineData("Work", "a/b", "", "", "")]
    [InlineData("Nowhere", "OpenAI", "", "", "There is no group 'Nowhere'")]
    [InlineData(".keypaste", "OpenAI", "", "", "keeps that group for itself")]
    [InlineData("Work", "OpenAI", "otp", "", "")]
    [InlineData("Work", "OpenAI", "Password", "", "")]
    [InlineData("Work", "OpenAI", "", "a,b", "")]
    public void A_refused_item_writes_nothing(string group, string title, string field, string tag, string says)
    {
        var path = NewVaultPath();
        using var vault = Seeded(path);
        var before = File.ReadAllBytes(path);
        var edits = 0;
        vault.Edited += (_, _) => edits++;

        var refused = Assert.Throws<VaultException>(() => vault.CreateEntry(
            new VaultEntry { Title = title, GroupPath = group, Password = "x" },
            field.Length == 0 ? [] : [new FieldWrite(field, "v")],
            tag.Length == 0 ? [] : [tag]));

        Assert.Contains(says, refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, edits);
        Assert.Equal(["Work/Stripe"], vault.ReadEntries().Select(entry => entry.Path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_field_or_tag_named_twice_is_refused()
    {
        using var vault = Seeded(NewVaultPath());

        Assert.Throws<VaultException>(() => vault.CreateEntry(
            new VaultEntry { Title = "OpenAI", GroupPath = "Work" }, [new FieldWrite("KEY", "a"), new FieldWrite("KEY", "b")], []));
        Assert.Throws<VaultException>(() => vault.CreateEntry(
            new VaultEntry { Title = "OpenAI", GroupPath = "Work" }, [], ["ai", "ai"]));
        Assert.Null(vault.Find(_openAi));
    }

    /// <summary>The CLI's create applies the name rules the app's does, and still makes the groups a typed path names (F.55).</summary>
    [Theory]
    [InlineData("Work", "Stripe", "is already in that group")]
    [InlineData("Work", " OpenAI", "cannot begin or end with whitespace")]
    [InlineData("Work", "a\\b", "cannot contain")]
    [InlineData(".keypaste/tokens", "OpenAI", "keeps that group for itself")]
    [InlineData(".Keypaste", "OpenAI", "keeps that group for itself")]
    public void A_typed_path_create_is_refused_as_the_app_refuses_it(string group, string title, string says)
    {
        var path = NewVaultPath();
        using var vault = Seeded(path);
        var edits = 0;
        vault.Edited += (_, _) => edits++;

        var refused = Assert.Throws<VaultException>(() => vault.CreateEntryAtPath(new VaultEntry { Title = title, GroupPath = group, Password = "x" }));

        Assert.Contains(says, refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, edits);
        Assert.Equal(["Work/Stripe"], vault.ReadEntries(includeReserved: true).Select(entry => entry.Path));
    }

    [Fact]
    public void A_typed_path_create_makes_its_groups()
    {
        using var vault = Seeded(NewVaultPath());

        vault.CreateEntryAtPath(new VaultEntry { Title = "OpenAI", GroupPath = "Work/AI", Password = "sk-1" });

        Assert.Equal("sk-1", vault.Find(new EntryName("Work/AI", "OpenAI"))?.Password);
    }

    private static Vault Seeded(string path)
    {
        var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = "Stripe", GroupPath = "Work", Password = "login-password" });
        vault.Save();
        return vault;
    }

    private string NewVaultPath() => Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
}
