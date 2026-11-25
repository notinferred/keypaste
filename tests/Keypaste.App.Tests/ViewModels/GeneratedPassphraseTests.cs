using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Generating a passphrase from the desktop, on both forms that generate a secret.
/// </summary>
/// <remarks>
/// <para>
/// The claim is what the file holds afterwards, not what the screen said: each test reopens the
/// vault from nothing and reads the value back, because a form that displayed a passphrase and
/// stored something else would pass any assertion made against the view model.
/// </para>
/// <para>
/// The env form is here as well as the entry form because both screens generate, and the entry
/// form's twenty-character test says nothing about the shape of a value the env form generates.
/// </para>
/// </remarks>
public sealed class GeneratedPassphraseTests : IDisposable
{
    internal const string Master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;

    public GeneratedPassphraseTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-passphrase-tests-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = ClaimedVault.Create(_vaultPath, Master);
        vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh" });
        ProjectVariables.Set(vault, "billing", "STRIPE_KEY", "sk");
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
    public void An_added_entry_can_have_a_generated_passphrase()
    {
        using (var context = New())
        {
            var form = NewItemForm.Open(context.Entries, "database");
            form.Generator.UseWords = true;
            context.Entries.ConfirmAddCommand.Press();

            Assert.Null(context.Entries.Error);
        }

        AssertIsAPassphrase(Reread("database"), 6, PasswordGenerator.DefaultSeparator);
    }

    [Fact]
    public void An_added_variable_can_have_a_generated_passphrase()
    {
        using (var context = New())
        {
            var project = Billing(context);

            project.BeginAddCommand.Press();
            project.NewKey = "DATABASE_URL";
            project.Generator.UseWords = true;
            project.Generator.WordCount = 8;
            project.ConfirmAddCommand.Press();

            Assert.Null(context.EnvSets.Error);
        }

        AssertIsAPassphrase(RereadHomeField("DATABASE_URL"), 8, PasswordGenerator.DefaultSeparator);
    }

    [Fact]
    public void The_chosen_separator_is_what_the_vault_holds()
    {
        using (var context = New())
        {
            var form = NewItemForm.Open(context.Entries, "database");
            form.Generator.UseWords = true;
            form.Generator.Separator = "_";
            context.Entries.ConfirmAddCommand.Press();

            Assert.Null(context.Entries.Error);
        }

        var stored = Reread("database");

        AssertIsAPassphrase(stored, 6, '_');
        Assert.DoesNotContain(PasswordGenerator.DefaultSeparator, stored);
    }

    /// <summary>
    /// A word count the core refuses writes nothing and says so.
    /// </summary>
    /// <remarks>
    /// The alternative an implementation falls into is generating the default instead, which hands
    /// somebody who asked for four words a six-word passphrase and never mentions it.
    /// </remarks>
    [Fact]
    public void A_refused_word_count_writes_nothing_and_reports_it()
    {
        using (var context = New())
        {
            var form = NewItemForm.Open(context.Entries, "database");
            form.Generator.UseWords = true;
            form.Generator.WordCount = 3;
            context.Entries.ConfirmAddCommand.Press();

            Assert.NotNull(form.Error);
            Assert.True(context.Entries.IsAdding);
        }

        using var reopened = ClaimedVault.Open(_vaultPath, Master);
        Assert.Null(reopened.Find("database"));
    }

    private static void AssertIsAPassphrase(string value, int words, char separator)
    {
        var pieces = value.Split(separator);

        Assert.Equal(words, pieces.Length);
        Assert.All(pieces, piece => Assert.NotEmpty(piece));
        Assert.All(pieces, piece => Assert.True(
            WordList.Words.Contains(piece),
            $"'{piece}' is not a word from the list"));
    }

    private static EnvProjectViewModel Billing(UnlockedScreens context)
    {
        context.EnvSets.OpenCommand.Press("billing");
        return context.EnvSets.OpenProject!;
    }

    /// <summary>Opens the file again, from nothing, and reads a key the screen added to billing's home entry.</summary>
    private string RereadHomeField(string key)
    {
        using var vault = ClaimedVault.Open(_vaultPath, Master);
        var value = vault.ReadField(new EntryName("env/billing", ".env"), key);

        Assert.NotNull(value);
        return value;
    }

    /// <summary>Opens the file again, from nothing, and reads one entry's password.</summary>
    private string Reread(string path)
    {
        using var vault = ClaimedVault.Open(_vaultPath, Master);
        var found = vault.Find(path);

        Assert.NotNull(found);
        return found.Password;
    }

    private UnlockedScreens New() => new(_vaultPath, Master, envSets: true);
}
