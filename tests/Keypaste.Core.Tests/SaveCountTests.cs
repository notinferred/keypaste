using System.Globalization;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The save count keypaste keeps in the vault and the newest one each home remembers: every save counts once, a merge counts past
/// both copies, and an unlock says when the file is a copy older than one this home saw, once, but not after keypaste's own restore,
/// another program's save, or for a vault that carries no count.
/// </summary>
public sealed class SaveCountTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-save-count-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string Home => Path.Combine(_directory, "home");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void EachSave_CountsOnce_HoweverManyAttemptsItTakes_AndASaveThatFailedCountsNothing()
    {
        Create();

        Under(vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "retried", Password = "r", GroupPath = "work" });
            vault.SaveWaiting(waitBetweenAttempts: _ => { }, duringAttempt: attempt =>
            {
                if (attempt == 1)
                {
                    throw new IOException("held by a scanner");
                }
            });
            Assert.Equal(2, vault.SaveCount);

            vault.AddEntry(new VaultEntry { Title = "failed", Password = "f", GroupPath = "work" });
            Assert.Throws<VaultException>(() => vault.SaveWaiting(
                waitBetweenAttempts: _ => { }, attempts: 1, duringAttempt: _ => throw new IOException("held")));
            vault.Save();
            Assert.Equal(3, vault.SaveCount);
        });

        Assert.Equal(3, Under(vault => vault.SaveCount));
    }

    [Fact]
    public void AMergedSave_CountsPastBothCopies()
    {
        Create();
        using var open = ClaimedVault.Open(VaultPath, _master);
        open.AddEntry(new VaultEntry { Title = "mine", Password = "m", GroupPath = "work" });

        using (var other = ClaimedVault.Open(VaultPath, _master))
        {
            other.AddEntry(new VaultEntry { Title = "theirs", Password = "t", GroupPath = "work" });
            other.Save();
            other.UpdateEntry(new VaultEntry { Title = "theirs", Password = "t2", GroupPath = "work" });
            other.Save();
        }

        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.Equal(4, copy!.SaveCount);
    }

    [Fact]
    public void AMergeWithAnOlderFile_CountsPastTheOpenCopy()
    {
        Create();
        var older = File.ReadAllBytes(VaultPath);
        using var open = ClaimedVault.Open(VaultPath, _master);
        open.AddEntry(new VaultEntry { Title = "mine", Password = "m", GroupPath = "work" });
        open.Save();
        open.UpdateEntry(new VaultEntry { Title = "mine", Password = "m2", GroupPath = "work" });
        open.Save();
        File.WriteAllBytes(VaultPath, older);
        open.AddEntry(new VaultEntry { Title = "later", Password = "l", GroupPath = "work" });

        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.Equal(4, copy!.SaveCount);
    }

    [Fact]
    public void AnOlderCopyPutBack_IsSaidAtTheNextUnlock_Once()
    {
        Create();
        var older = File.ReadAllBytes(VaultPath);
        Edit("newer");
        File.WriteAllBytes(VaultPath, older);

        Assert.True(Under(vault => vault.IsOlderCopy));
        Assert.False(Under(vault => vault.IsOlderCopy));
    }

    [Fact]
    public void AnOlderCopyOpenedToRead_IsSaidWhenTheOpenNamesAHome_AndNeverRemembered()
    {
        Create();
        var older = File.ReadAllBytes(VaultPath);
        Edit("newer");
        File.WriteAllBytes(VaultPath, older);

        using (var unnamed = Vault.OpenReadOnly(VaultPath, _master))
        {
            Assert.False(unnamed.IsOlderCopy);
        }

        using var read = Vault.OpenReadOnly(VaultPath, _master, home: Home);
        Assert.True(read.IsOlderCopy);
    }

    [Fact]
    public void TheCopyLastSaved_AndAnotherProgramsSaveOverIt_AreNotOlder()
    {
        Create();
        Edit("newer");

        Assert.False(Under(vault => vault.IsOlderCopy));

        ForeignEdit.Apply(VaultPath, _master, "token", DateTime.UtcNow, entry =>
            entry.Strings.Set(KeePassLib.PwDefs.PasswordField, new KeePassLib.Security.ProtectedString(true, "theirs")));

        Assert.False(Under(vault => vault.IsOlderCopy));
    }

    [Fact]
    public void ARestoredBackup_IsNotOlder()
    {
        Create();
        var older = File.ReadAllBytes(VaultPath);
        Edit("newer");
        var backup = Plant(older);

        var summary = VaultBackups.Inspect(VaultPath, backup, _master);
        using (var claim = Claim())
        {
            VaultBackups.Restore(claim, summary);
        }

        Assert.Equal(older, File.ReadAllBytes(VaultPath));
        Assert.False(Under(vault => vault.IsOlderCopy));
    }

    [Fact]
    public void AVaultWithoutTheCount_IsNotOlder_AndItsFirstSaveStartsIt()
    {
        Create();
        Edit("newer");
        ForeignEdit.InFile(VaultPath, _master, database => database.RootGroup.CustomData.Remove("keypaste.save-count"));

        Under(vault =>
        {
            Assert.False(vault.IsOlderCopy);
            Assert.Null(vault.SaveCount);
            Edit(vault, "again");
            Assert.Equal(1, vault.SaveCount);
        });
    }

    private void Create()
    {
        using var claim = Claim();
        using var vault = Vault.Create(claim, VaultPath, _master);
        vault.AddEntry(new VaultEntry { Title = "token", Password = "first", GroupPath = "work" });
        vault.Save();
        Assert.Equal(1, vault.SaveCount);
    }

    private void Edit(string password) => Under(vault => Edit(vault, password));

    private static void Edit(Vault vault, string password)
    {
        vault.UpdateEntry(new VaultEntry { Title = "token", Password = password, GroupPath = "work" });
        vault.Save();
    }

    private void Under(Action<Vault> act) => Under(vault =>
    {
        act(vault);
        return 0;
    });

    private T Under<T>(Func<Vault, T> act)
    {
        using var claim = Claim();
        using var vault = Vault.Open(claim, VaultPath, _master);
        return act(vault);
    }

    private VaultClaim Claim() =>
        VaultClaim.TryAcquire(Home, VaultPath, OwnerKind.CommandLine, out var claim, out var refusal)
            ? claim
            : throw new InvalidOperationException(refusal);

    private VaultBackup Plant(byte[] bytes)
    {
        var directory = Directory.CreateDirectory(VaultBackups.DirectoryFor(VaultPath)).FullName;
        var stamp = DateTimeOffset.UtcNow.AddHours(-1).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        File.WriteAllBytes(Path.Combine(directory, $"vault.{stamp}.kdbx"), bytes);
        return VaultBackups.List(VaultPath).Single(backup => backup.TakenAt < DateTimeOffset.UtcNow.AddMinutes(-30));
    }
}
