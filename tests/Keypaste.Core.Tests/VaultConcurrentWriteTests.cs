using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A save from a stale copy is refused rather than allowed to revert somebody else's write.
/// </summary>
/// <remarks>
/// <para>
/// A vault is held in memory and written back whole, so the loser of this race does not merely lose
/// a merge — the winner's entry vanishes with no history item, because it never existed in the
/// saving process's tree for KeePass to snapshot. It is not in KeePassXC's History tab. It is not
/// anywhere. That is why this is a guard rather than a paragraph in SECURITY.md.
/// </para>
/// <para>
/// <b>Every test here asserts on the data, not only on the exception type.</b> An implementation
/// that throws and writes anyway satisfies <c>Assert.Throws</c> perfectly and loses the write
/// regardless, so the exception is checked and then the file is reopened and read.
/// </para>
/// </remarks>
public sealed class VaultConcurrentWriteTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private readonly string _directory;

    public VaultConcurrentWriteTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-concurrent-tests-").FullName;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_save_over_a_file_changed_since_it_was_opened_is_refused()
    {
        var path = NewVault();

        using var first = Vault.Open(path, MasterPassword);

        using (var second = Vault.Open(path, MasterPassword))
        {
            second.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "second" });
            second.Save();
        }

        first.AddEntry(new VaultEntry { Title = "from-the-window", Password = "first" });

        Assert.Throws<VaultChangedOnDiskException>(first.Save);

        // The claim is about the data surviving, so read it.
        using var reopened = Vault.Open(path, MasterPassword);
        Assert.NotNull(reopened.Find("from-the-terminal"));
        Assert.Null(reopened.Find("from-the-window"));
    }

    /// <summary>
    /// The positive control, and the mutation most likely to ship: a stamp taken once at open and
    /// never refreshed makes a user's <em>second</em> edit fail forever on a conflict that is not
    /// there.
    /// </summary>
    [Fact]
    public void A_second_save_in_the_same_session_still_succeeds()
    {
        var path = NewVault();

        using var vault = Vault.Open(path, MasterPassword);

        vault.AddEntry(new VaultEntry { Title = "first-edit", Password = "a" });
        vault.Save();

        vault.AddEntry(new VaultEntry { Title = "second-edit", Password = "b" });
        vault.Save();

        vault.AddEntry(new VaultEntry { Title = "third-edit", Password = "c" });
        vault.Save();

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.NotNull(reopened.Find("first-edit"));
        Assert.NotNull(reopened.Find("second-edit"));
        Assert.NotNull(reopened.Find("third-edit"));
    }

    [Fact]
    public void A_vault_nobody_else_touched_saves_exactly_as_before()
    {
        var path = NewVault();

        using var vault = Vault.Open(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "quiet", Password = "p" });

        Assert.Null(Record.Exception(vault.Save));
        Assert.False(vault.HasFileChangedSinceOpen());
    }

    [Fact]
    public void A_change_is_visible_before_the_save_that_would_lose_it()
    {
        var path = NewVault();

        using var first = Vault.Open(path, MasterPassword);
        Assert.False(first.HasFileChangedSinceOpen());

        using (var second = Vault.Open(path, MasterPassword))
        {
            second.AddEntry(new VaultEntry { Title = "elsewhere", Password = "x" });
            second.Save();
        }

        Assert.True(first.HasFileChangedSinceOpen());
    }

    /// <summary>
    /// A vault rewritten with the same contents is still a change.
    /// </summary>
    /// <remarks>
    /// Every save regenerates the salt and the nonces, so two saves of identical contents produce
    /// different bytes. A detector built on file length would miss this one entirely, and a
    /// second writer saving without changing a field is exactly what "I opened it in KeePassXC and
    /// pressed save" looks like.
    /// </remarks>
    [Fact]
    public void A_rewrite_with_the_same_contents_is_still_a_change()
    {
        var path = NewVault();

        using var first = Vault.Open(path, MasterPassword);

        using (var second = Vault.Open(path, MasterPassword))
        {
            second.Save();
        }

        Assert.True(first.HasFileChangedSinceOpen());
        Assert.Throws<VaultChangedOnDiskException>(first.Save);
    }

    [Fact]
    public void Overwriting_is_available_to_a_caller_that_asked_a_person_first()
    {
        var path = NewVault();

        using var first = Vault.Open(path, MasterPassword);

        using (var second = Vault.Open(path, MasterPassword))
        {
            second.AddEntry(new VaultEntry { Title = "from-the-terminal", Password = "second" });
            second.Save();
        }

        first.AddEntry(new VaultEntry { Title = "from-the-window", Password = "first" });
        first.SaveOverwriting();

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.NotNull(reopened.Find("from-the-window"));

        // Named rather than implied: overwriting discards the other write, which is the whole
        // reason it is a second method with a second name instead of a flag on Save.
        Assert.Null(reopened.Find("from-the-terminal"));
    }

    /// <summary>
    /// An overwrite re-stamps, so the next ordinary save is not still in conflict.
    /// </summary>
    [Fact]
    public void A_save_after_an_overwrite_is_not_refused()
    {
        var path = NewVault();

        using var first = Vault.Open(path, MasterPassword);

        using (var second = Vault.Open(path, MasterPassword))
        {
            second.AddEntry(new VaultEntry { Title = "elsewhere", Password = "x" });
            second.Save();
        }

        first.SaveOverwriting();
        first.AddEntry(new VaultEntry { Title = "afterwards", Password = "y" });

        Assert.Null(Record.Exception(first.Save));
    }

    /// <summary>
    /// A vault that has never been written has nothing to be in conflict with.
    /// </summary>
    /// <remarks>
    /// <c>Vault.Create</c> writes nothing until the first save, so a stamp taken in the constructor
    /// would be a stamp of a file that is not there — and every <c>keypaste init</c> would fail.
    /// </remarks>
    [Fact]
    public void A_freshly_created_vault_saves_without_a_conflict()
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory, "fresh")).FullName, "vault.kdbx");

        using var vault = Vault.Create(path, MasterPassword);
        Assert.False(vault.HasFileChangedSinceOpen());

        vault.AddEntry(new VaultEntry { Title = "new", Password = "p" });
        Assert.Null(Record.Exception(vault.Save));

        vault.AddEntry(new VaultEntry { Title = "newer", Password = "p" });
        Assert.Null(Record.Exception(vault.Save));
    }

    private string NewVault([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        var path = Path.Combine(home, "vault.kdbx");

        using var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "seed", Password = "seed" });
        vault.Save();

        return path;
    }
}
