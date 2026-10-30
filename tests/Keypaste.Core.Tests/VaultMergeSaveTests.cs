using System.Reflection;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Another program's save of the open vault's own file merged with the open copy and saved over it atomically (1.4b2, D-0543):
/// the file read once, parsed with the unlock's key and refused from its bytes below KDBX 4, a save landing meanwhile merged on the
/// next attempt, and every refusal or failure leaving the open copy and the file as they were. The other program's saves are made
/// through KeePassLib directly, a revision of the entry before each change as KeePass saves one.
/// </summary>
public sealed class VaultMergeSaveTests : IDisposable
{
    private const string _password = "correct horse battery staple";

    private static readonly EntryName _stripe = new("work", "stripe");
    private static readonly EntryName _github = new("work", "github");
    private static readonly EntryName _gitlab = new("work", "gitlab");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-merge-save-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>The save replaces the other program's file and keeps it, though this unlock's own backup is minutes old (D-0543).</summary>
    [Fact]
    public void AnotherProgramsSave_IsMergedWithTheChangeThisCopyHeld_AndTheFileItReplacedIsKept()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "first");
        open.Save();
        Assert.Single(VaultBackups.List(VaultPath));
        Update(open, _stripe, "mine");
        InFile(database => Edit(database, _github, "theirs"));
        var theirs = File.ReadAllBytes(VaultPath);

        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal((VaultMergeSaveOutcome.Saved, string.Empty), (merged.Outcome, merged.Reason));
        Assert.True(merged.Report!.TookFromOpenCopy);
        Assert.Equal(VaultSaveStatus.Saved, copy!.SaveState().Status);
        Assert.Equal(("mine", "theirs"), Passwords(VaultPath));

        Assert.Equal(2, VaultBackups.List(VaultPath).Count);
        Assert.Equal(theirs, File.ReadAllBytes(merged.Kept!.Path));
        Assert.Equal(merged.Kept.Path, VaultBackups.List(VaultPath)[0].Path);
        Assert.Equal("mine", open.Find(_stripe)!.Password);
        Assert.Equal("github-0", open.Find(_github)!.Password);
    }

    /// <summary>
    /// The other program saves again while the merged copy's save waits to retry a write it could not make, as a writer holding the
    /// file refuses one; the retry finds the file changed, and the merge starts again from what both saves wrote (D-0119, D-0543).
    /// </summary>
    [Fact]
    public void ASaveLandingBeforeTheMergedCopysSave_IsMergedOnTheNextAttempt()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        InFile(database => Edit(database, _github, "theirs"));
        var held = 0;
        var landed = 0;

        var merged = open.MergeAnotherSave(
            waitBetweenAttempts: _ =>
            {
                if (landed++ == 0)
                {
                    InFile(database => AddEntry(database, _gitlab, "theirs-again"));
                }
            },
            duringAttempt: _ =>
            {
                if (held++ == 0)
                {
                    throw new IOException("another program holds the vault file");
                }
            });
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.Equal(1, landed);
        Assert.Equal(2, held);
        Assert.Equal(("mine", "theirs"), Passwords(VaultPath));
        using var reopened = Vault.Open(VaultPath, _password);
        Assert.Equal("theirs-again", reopened.Find(_gitlab)!.Password);
        Assert.Equal(VaultSaveStatus.Saved, copy!.SaveState().Status);
    }

    /// <summary>One detection starts the merge again at most three times; then it waits for the next change, both copies as they were (D-0543).</summary>
    [Fact]
    public void ASaveLandingBeforeEveryAttempt_LeavesTheMergeToTheNextChange()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        InFile(database => Edit(database, _github, "theirs-0"));
        var saves = 0;

        var merged = open.MergeAnotherSave(
            waitBetweenAttempts: _ => InFile(database => Edit(database, _github, $"theirs-{++saves}")),
            duringAttempt: attempt =>
            {
                if (attempt == 1)
                {
                    throw new IOException("another program holds the vault file");
                }
            });

        Assert.Equal(VaultMergeSaveOutcome.ChangedAgain, merged.Outcome);
        Assert.Null(merged.Copy);
        Assert.Equal(Vault.MergeAttempts, saves);
        Assert.Equal(("stripe-0", $"theirs-{saves}"), Passwords(VaultPath));
        Assert.Equal("mine", open.Find(_stripe)!.Password);
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, open.SaveState().Status);
    }

    [Fact]
    public void AMergeThatFails_LeavesTheOpenCopyAndTheFileAsTheyWere()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        InFile(database =>
        {
            var twin = Entry(database, _github).CloneDeep();
            twin.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "twin"));
            Entry(database, _github).ParentGroup.AddEntry(twin, true);
        });
        var before = File.ReadAllBytes(VaultPath);

        var merged = open.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.DuplicateUuids, merged.Outcome);
        AssertNothingChanged(open, merged, before);
    }

    [Fact]
    public void AMergedCopyWhoseSaveFails_LeavesTheOpenCopyAndTheFileAsTheyWere()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        InFile(database => Edit(database, _github, "theirs"));
        var before = File.ReadAllBytes(VaultPath);
        File.WriteAllText(VaultBackups.DirectoryFor(VaultPath), "not a directory");

        var merged = open.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.SaveFailed, merged.Outcome);
        AssertNothingChanged(open, merged, before);
    }

    [Fact]
    public void AKeyThatNoLongerOpensTheFile_IsRefused_AndChangesNothing()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        InFile(database =>
        {
            Edit(database, _github, "theirs");
            database.MasterKey = Key("another password");
            database.MasterKeyChanged = DateTime.UtcNow;
        });
        var before = File.ReadAllBytes(VaultPath);

        var merged = open.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.KeyRefused, merged.Outcome);
        AssertNothingChanged(open, merged, before);
    }

    /// <summary>A file whose root group is not the open vault's is another vault, opened by the same password or not (D-0543).</summary>
    [Fact]
    public void AnotherVaultsFile_IsRefused()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        var other = Path.Combine(_directory, "other.kdbx");
        using (var vault = Vault.Create(other, _password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "other" });
            vault.Save();
        }

        File.Copy(other, VaultPath, overwrite: true);
        var before = File.ReadAllBytes(VaultPath);

        var merged = open.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.AnotherVault, merged.Outcome);
        AssertNothingChanged(open, merged, before);
    }

    /// <summary>The same vault saved as KDBX 3.1 under the same key, which opens as any vault does, is refused from its header before it is parsed (D-0541).</summary>
    [Fact]
    public void TheSameVaultSavedAsKdbx31_IsRefusedFromItsBytes()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        SaveAsKdbx31(database => Edit(database, _github, "theirs"));
        var before = File.ReadAllBytes(VaultPath);
        Assert.Equal(3, KdbxHeader.Read(VaultPath).FormatMajorVersion);
        Assert.Equal(("stripe-0", "theirs"), Passwords(VaultPath));

        var merged = open.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.BelowKdbx4, merged.Outcome);
        Assert.Contains("KDBX 3.1", merged.Reason, StringComparison.Ordinal);
        AssertNothingChanged(open, merged, before);
    }

    /// <summary>A file caught while the other program writes it is refused, and merged at the next change, once it is whole (D-0543).</summary>
    [Fact]
    public void ATruncatedFile_IsRefused_AndMergedOnceComplete()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        Update(open, _stripe, "mine");
        InFile(database => Edit(database, _github, "theirs"));
        var whole = File.ReadAllBytes(VaultPath);
        File.WriteAllBytes(VaultPath, whole[..(whole.Length / 2)]);
        var before = File.ReadAllBytes(VaultPath);

        var truncated = open.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.Unreadable, truncated.Outcome);
        AssertNothingChanged(open, truncated, before);

        File.WriteAllBytes(VaultPath, whole);
        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Saved, merged.Outcome);
        Assert.Equal(("mine", "theirs"), Passwords(VaultPath));
    }

    /// <summary>With nothing of its own to carry, the merged copy is the file's, and nothing is written (D-0543).</summary>
    [Fact]
    public void AFileThatHoldsEverythingThisCopyDoes_IsTakenAsItIs_AndNothingIsWritten()
    {
        Seed();
        using var open = Vault.Open(VaultPath, _password);
        InFile(database => Edit(database, _github, "theirs"));
        var before = File.ReadAllBytes(VaultPath);

        var merged = open.MergeAnotherSave();
        using var copy = merged.Copy;

        Assert.Equal(VaultMergeSaveOutcome.Updated, merged.Outcome);
        Assert.False(merged.Report!.TookFromOpenCopy);
        Assert.Null(merged.Kept);
        Assert.Equal(before, File.ReadAllBytes(VaultPath));
        Assert.Equal(VaultSaveStatus.Saved, copy!.SaveState().Status);
        Assert.Equal("theirs", copy.Find(_github)!.Password);
        Assert.Empty(VaultBackups.List(VaultPath));
    }

    private static void AssertNothingChanged(Vault open, VaultMergeSave merged, byte[] before)
    {
        Assert.Null(merged.Copy);
        Assert.Null(merged.Kept);
        Assert.NotEmpty(merged.Reason);
        Assert.Equal(before, File.ReadAllBytes(open.Path));
        Assert.Equal("mine", open.Find(_stripe)!.Password);
        Assert.Equal("github-0", open.Find(_github)!.Password);
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, open.SaveState().Status);
    }

    /// <summary>Writes the vault every test starts from: <c>work/stripe</c> and <c>work/github</c>, each holding its first value.</summary>
    private void Seed()
    {
        using var vault = Vault.Create(VaultPath, _password);
        vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Username = "user", Password = "stripe-0" });
        vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Username = "user", Password = "github-0" });
        vault.Save();
    }

    private static void Update(Vault vault, EntryName name, string password) =>
        Assert.True(vault.UpdateEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Username = "user", Password = password }));

    /// <summary>The passwords of <c>work/stripe</c> and <c>work/github</c> as the file at <paramref name="path"/> holds them.</summary>
    private static (string? Stripe, string? GitHub) Passwords(string path)
    {
        using var vault = Vault.Open(path, _password);
        return (vault.Find(_stripe)?.Password, vault.Find(_github)?.Password);
    }

    /// <summary>Changes the vault's file as another KeePass program would, and saves it there.</summary>
    private void InFile(Action<PwDatabase> work)
    {
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(VaultPath), Key(_password), null);
            work(database);
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>Changes the vault's file and saves it as KDBX 3.1 under AES-KDF, through the vendored library's internal version switch.</summary>
    private void SaveAsKdbx31(Action<PwDatabase> work)
    {
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(VaultPath), Key(_password), null);
            work(database);
            database.DataCipherUuid = StandardAesEngine.AesUuid;
            database.KdfParameters = new AesKdf().GetDefaultParameters();
            database.KdfParameters.SetUInt64(AesKdf.ParamRounds, 1000);

            KdbxFile writer = new(database);
            typeof(KdbxFile).GetProperty("ForceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(writer, 0x00030001u);

            using var stream = File.Create(VaultPath);
            writer.Save(stream, null, KeePassLib.Serialization.KdbxFormat.Default, null);
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>An edit as KeePass saves one: the entry as it was into its history, then the change, stamped now.</summary>
    private static void Edit(PwDatabase database, EntryName name, string password)
    {
        var entry = Entry(database, name);
        entry.CreateBackup(database);
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        entry.LastModificationTime = DateTime.UtcNow;
    }

    private static void AddEntry(PwDatabase database, EntryName name, string password)
    {
        PwEntry entry = new(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, name.Title));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        Entry(database, _stripe).ParentGroup.AddEntry(entry, true);
    }

    private static PwEntry Entry(PwDatabase database, EntryName name) =>
        database.RootGroup.GetEntries(true).Single(entry =>
            entry.Strings.ReadSafe(PwDefs.TitleField) == name.Title && entry.ParentGroup.Name == name.GroupPath);

    private static CompositeKey Key(string password)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(password), false));
        return key;
    }
}
