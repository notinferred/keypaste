using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Rotate replaces a password with a generated one, keeps the old one in history (D-0014) and ends
/// what was released from it (D-0318); "rotated" is when the current password was set.
/// </summary>
public sealed class EntryRotationTests : IDisposable
{
    private const string _old = "SENTINEL-OLD-PASSWORD";
    private static readonly EntryName _name = new("env/acme-api", "STRIPE_KEY");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-rotate-").FullName;
    private readonly Vault _vault;

    public EntryRotationTests()
    {
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), VaultHistoryTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Username = "billing", Password = _old });
        _vault.AddEntry(new VaultEntry { GroupPath = ".keypaste/tokens", Title = "t1", Password = "verifier" });
        _vault.Save();
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Rotate_ReplacesThePassword_KeepingTheOldInHistory()
    {
        Assert.Equal(RotateOutcome.Rotated, EntryRotation.Rotate(_vault, _name, SecretRecipe.Default));

        var current = _vault.Find(_name)!;
        Assert.NotEqual(_old, current.Password);
        Assert.Equal(PasswordGenerator.DefaultLength, current.Password.Length);
        Assert.Equal("billing", current.Username);
        Assert.Contains(_vault.ReadHistory(_name)!, revision => revision.Fields.Password == _old);
    }

    [Fact]
    public void Rotate_FollowsTheRecipe()
    {
        var words = SecretRecipe.For(new PassphraseRecipe { WordCount = 7, Separator = '.' });

        EntryRotation.Rotate(_vault, _name, words);

        Assert.Equal(7, _vault.Find(_name)!.Password.Split('.').Length);
    }

    [Fact]
    public void Rotate_Reserved_AndNotFound()
    {
        Assert.Equal(RotateOutcome.Reserved, EntryRotation.Rotate(_vault, new EntryName(".keypaste/tokens", "t1"), SecretRecipe.Default));
        Assert.Equal(RotateOutcome.NotFound, EntryRotation.Rotate(_vault, new EntryName("env", "absent"), SecretRecipe.Default));
        Assert.Equal("verifier", _vault.Find(new EntryName(".keypaste/tokens", "t1"))!.Password);
    }

    [Fact]
    public void Rotate_RaisesEdited()
    {
        List<VaultEdit> edits = [];
        _vault.Edited += (_, edit) => edits.Add(edit);

        EntryRotation.Rotate(_vault, _name, SecretRecipe.Default);

        Assert.Equal([_name], Assert.Single(edits).Entries);
    }

    [Fact]
    public void LastRotated_IsWhenTheCurrentPasswordWasSet()
    {
        Thread.Sleep(1100);
        EntryRotation.Rotate(_vault, _name, SecretRecipe.Default);
        _vault.Save();
        var rotated = _vault.ReadTimes(_name)!.Modified;

        Thread.Sleep(1100);
        _vault.UpdateEntry(_vault.Find(_name)! with { Username = "someone-else" });
        _vault.Save();

        Assert.True(_vault.ReadTimes(_name)!.Modified > rotated);
        Assert.Equal(rotated, EntryRotation.LastRotated(_vault, _name));
    }

    [Fact]
    public void LastRotated_WithoutAChange_IsCreated()
    {
        Assert.Equal(_vault.ReadTimes(_name)!.Created, EntryRotation.LastRotated(_vault, _name));
        Assert.Null(EntryRotation.LastRotated(_vault, new EntryName("env", "absent")));
    }
}
