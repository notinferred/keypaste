using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
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
/// The env form is here as well as the entry form because both screens generate, and until V.6
/// nothing asserted the shape of a value the env form generated at all — the entry form's
/// twenty-character test stood alone.
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

        using var vault = Vault.Create(_vaultPath, Master);
        vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh" });
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
    public void An_added_entry_can_have_a_generated_passphrase()
    {
        using (var context = New())
        {
            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "servers/database";
            context.Entries.Generator.UseWords = true;
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        AssertIsAPassphrase(Reread("servers/database"), 6, PasswordGenerator.DefaultSeparator);
    }

    [Fact]
    public void An_added_variable_can_have_a_generated_passphrase()
    {
        using (var context = New())
        {
            var project = Billing(context);

            project.BeginAddCommand.Execute(null);
            project.NewKey = "DATABASE_URL";
            project.Generator.UseWords = true;
            project.Generator.WordCount = 8;
            project.ConfirmAddCommand.Execute(null);

            Assert.Null(context.EnvSets.Error);
        }

        AssertIsAPassphrase(Reread("env/billing/DATABASE_URL"), 8, PasswordGenerator.DefaultSeparator);
    }

    [Fact]
    public void The_chosen_separator_is_what_the_vault_holds()
    {
        using (var context = New())
        {
            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "servers/database";
            context.Entries.Generator.UseWords = true;
            context.Entries.Generator.Separator = "_";
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
        }

        var stored = Reread("servers/database");

        AssertIsAPassphrase(stored, 6, '_');
        Assert.DoesNotContain(PasswordGenerator.DefaultSeparator, stored);
    }

    /// <summary>
    /// The character choice still generates exactly what it generated before V.6.
    /// </summary>
    /// <remarks>
    /// The recipe moved behind <see cref="GeneratorViewModel"/>, so the default path is worth
    /// re-asserting on this form: a generator whose default silently became words would leave the
    /// existing twenty-character test as the only thing saying otherwise.
    /// </remarks>
    [Fact]
    public void The_default_is_still_a_twenty_character_password_on_both_forms()
    {
        using (var context = New())
        {
            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "servers/database";
            context.Entries.ConfirmAddCommand.Execute(null);

            var project = Billing(context);
            project.BeginAddCommand.Execute(null);
            project.NewKey = "DATABASE_URL";
            project.ConfirmAddCommand.Execute(null);

            Assert.Null(context.Entries.Error);
            Assert.Null(context.EnvSets.Error);
        }

        Assert.Equal(PasswordGenerator.DefaultLength, Reread("servers/database").Length);
        Assert.Equal(PasswordGenerator.DefaultLength, Reread("env/billing/DATABASE_URL").Length);
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
            context.Entries.BeginAddCommand.Execute(null);
            context.Entries.NewEntryPath = "servers/database";
            context.Entries.Generator.UseWords = true;
            context.Entries.Generator.WordCount = 3;
            context.Entries.ConfirmAddCommand.Execute(null);

            Assert.NotNull(context.Entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, Master);
        Assert.Null(reopened.Find("servers/database"));
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

    private static EnvProjectViewModel Billing(Context context)
    {
        context.EnvSets.OpenCommand.Execute("billing");
        return context.EnvSets.OpenProject!;
    }

    /// <summary>Opens the file again, from nothing, and reads one entry's password.</summary>
    private string Reread(string path)
    {
        using var vault = Vault.Open(_vaultPath, Master);
        var found = vault.Find(path);

        Assert.NotNull(found);
        return found.Password;
    }

    private Context New() => new(_vaultPath);

    /// <summary>An unlocked session with both generating screens on it.</summary>
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
            EnvSets = new EnvSetsViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal EnvSetsViewModel EnvSets { get; }

        internal FakeClipboard Clipboard { get; }

        public void Dispose()
        {
            EnvSets.Dispose();
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
