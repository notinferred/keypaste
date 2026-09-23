using System.Security.Cryptography;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The rules both front ends apply before a vault exists.
/// </summary>
/// <remarks>
/// <para>
/// These were <c>keypaste init</c>'s alone until 4.8 gave the desktop a Create button. They live
/// here, rather than duplicated there, because the one that matters most is destructive: a second
/// implementation of "refuse an occupied path" is a second chance to overwrite an encrypted vault
/// nothing can read back (docs/PRODUCT.md law 4.2).
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> dropping the occupied-path refusal; checking
/// the confirmation before the emptiness, so an empty pair reports the wrong thing; creating the
/// parent directory before the rules have passed, so a refused attempt still changes the disk; and
/// returning <c>Created</c> without having called <c>Save</c>, which leaves a vault in memory and
/// nothing on disk.
/// </para>
/// </remarks>
public sealed class VaultCreationTests : IDisposable
{
    private const string _password = "correct horse battery staple";

    private readonly string _directory =
        Directory.CreateTempSubdirectory("keypaste-creation-tests-").FullName;

    [Fact]
    public void A_created_vault_is_on_disk_empty_and_reopens_with_its_password()
    {
        var path = Path.Combine(_directory, "new.kdbx");

        var outcome = VaultCreation.TryCreate(path, _password, _password, out var created, out var failure);

        using (created)
        {
            Assert.Equal(VaultCreationOutcome.Created, outcome);
            Assert.Empty(failure);
            Assert.NotNull(created);
            Assert.True(File.Exists(path), "TryCreate reported Created without saving the file");
        }

        using var reopened = Vault.Open(path, _password);
        Assert.Empty(reopened.ReadEntries());
    }

    [Fact]
    public void An_occupied_path_is_refused_and_the_file_there_is_untouched()
    {
        var path = Path.Combine(_directory, "already.kdbx");

        using (var existing = Vault.Create(path, "the other password"))
        {
            existing.AddEntry(new VaultEntry { Title = "keep me", Password = "secret" });
            existing.Save();
        }

        // The digest, not the timestamp: every save re-randomises the salt and the nonces, so an
        // unchanged digest proves no save happened rather than that two saves agreed.
        var before = Digest(path);

        var outcome = VaultCreation.TryCreate(path, _password, _password, out var created, out _);

        using (created)
        {
            Assert.Null(created);
            Assert.Equal(VaultCreationOutcome.PathAlreadyExists, outcome);
            Assert.Equal(before, Digest(path));
        }

        using var untouched = Vault.Open(path, "the other password");
        Assert.Single(untouched.ReadEntries());
    }

    [Fact]
    public void A_path_occupied_by_something_that_was_never_a_vault_is_refused_too()
    {
        var path = Path.Combine(_directory, "imposter.kdbx");
        File.WriteAllText(path, "a text file wearing the extension");

        var outcome = VaultCreation.TryCreate(path, _password, _password, out var created, out _);

        using (created)
        {
            Assert.Null(created);
            Assert.Equal(VaultCreationOutcome.PathAlreadyExists, outcome);
            Assert.Equal("a text file wearing the extension", File.ReadAllText(path));
        }
    }

    [Fact]
    public void An_empty_password_is_refused_before_anything_is_written()
    {
        var parent = Path.Combine(_directory, "not-created-yet");
        var path = Path.Combine(parent, "new.kdbx");

        var outcome = VaultCreation.TryCreate(path, string.Empty, string.Empty, out var created, out _);

        using (created)
        {
            Assert.Null(created);
            Assert.Equal(VaultCreationOutcome.EmptyPassword, outcome);
            Assert.False(File.Exists(path));

            // The directory is the strongest available witness: creating it is the first thing
            // TryCreate does to the disk, so its absence proves the refusal came first.
            Assert.False(Directory.Exists(parent), "a refused create made its parent directory anyway");
        }
    }

    [Fact]
    public void A_confirmation_that_does_not_match_is_refused_before_anything_is_written()
    {
        var parent = Path.Combine(_directory, "not-created-either");
        var path = Path.Combine(parent, "new.kdbx");

        var outcome = VaultCreation.TryCreate(path, _password, "something else", out var created, out _);

        using (created)
        {
            Assert.Null(created);
            Assert.Equal(VaultCreationOutcome.PasswordsDoNotMatch, outcome);
            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(parent));
        }
    }

    [Fact]
    public void A_missing_confirmation_reads_as_a_mismatch()
    {
        var path = Path.Combine(_directory, "unconfirmed.kdbx");

        var outcome = VaultCreation.TryCreate(path, _password, default, out var created, out _);

        using (created)
        {
            Assert.Null(created);
            Assert.Equal(VaultCreationOutcome.PasswordsDoNotMatch, outcome);
            Assert.False(File.Exists(path));
        }
    }

    /// <summary>
    /// An occupied path is answered before any password rule.
    /// </summary>
    /// <remarks>
    /// The order is the guarantee. If emptiness were checked first, somebody aiming an empty
    /// password at an existing vault would be told to pick a password — and on the retry, with a
    /// real one, would reach the overwrite.
    /// </remarks>
    [Fact]
    public void The_occupied_path_is_answered_before_the_password_rules()
    {
        var path = Path.Combine(_directory, "occupied.kdbx");
        using (var existing = Vault.Create(path, "the other password"))
        {
            existing.Save();
        }

        Assert.Equal(
            VaultCreationOutcome.PathAlreadyExists,
            VaultCreation.TryCreate(path, string.Empty, string.Empty, out _, out _));

        Assert.Equal(
            VaultCreationOutcome.PathAlreadyExists,
            VaultCreation.TryCreate(path, _password, "mismatched", out _, out _));
    }

    /// <summary>
    /// An empty password is answered before the confirmation.
    /// </summary>
    /// <remarks>
    /// Two empty fields match each other as well as being empty. "Cannot be empty" is the useful
    /// half, and it is what <c>keypaste init</c> said before these rules moved here.
    /// </remarks>
    [Fact]
    public void An_empty_password_is_answered_before_the_confirmation()
    {
        var path = Path.Combine(_directory, "empty-first.kdbx");

        Assert.Equal(
            VaultCreationOutcome.EmptyPassword,
            VaultCreation.TryCreate(path, string.Empty, "not empty", out _, out _));

        Assert.Equal(
            VaultCreationOutcome.EmptyPassword,
            VaultCreation.TryCreate(path, string.Empty, string.Empty, out _, out _));
    }

    [Fact]
    public void A_missing_parent_directory_is_created_on_the_way_to_a_vault()
    {
        var path = Path.Combine(_directory, "a", "b", "deep.kdbx");

        var outcome = VaultCreation.TryCreate(path, _password, _password, out var created, out _);

        using (created)
        {
            Assert.Equal(VaultCreationOutcome.Created, outcome);
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Inspect_answers_whether_a_path_is_free_and_writes_nothing()
    {
        var free = Path.Combine(_directory, "nothing-here.kdbx");
        var taken = Path.Combine(_directory, "something-here.kdbx");
        File.WriteAllText(taken, "occupied");

        Assert.Equal(VaultDestination.Free, VaultCreation.Inspect(free));
        Assert.Equal(VaultDestination.Occupied, VaultCreation.Inspect(taken));
        Assert.False(File.Exists(free), "Inspect created the file it was asked about");
    }

    [Fact]
    public void A_vault_created_with_a_keyfile_needs_both_factors_to_reopen()
    {
        var path = Path.Combine(_directory, "keyed.kdbx");
        var keyfile = Path.Combine(_directory, "vault.key");
        File.WriteAllBytes(keyfile, RandomNumberGenerator.GetBytes(32));

        var outcome = VaultCreation.TryCreate(path, _password, _password, keyfile, out var created, out _);

        using (created)
        {
            Assert.Equal(VaultCreationOutcome.Created, outcome);
            Assert.Equal(keyfile, created!.KeyfilePath);
            Assert.True(created.HasPassword);
        }

        using (var reopened = Vault.Open(path, _password, keyfile))
        {
            Assert.Empty(reopened.ReadEntries());
        }

        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, _password));
        Assert.Throws<InvalidMasterPasswordException>(() => Vault.Open(path, string.Empty, keyfile));
    }

    [Theory]
    [InlineData("missing", VaultCreationOutcome.KeyfileUnusable)]
    [InlineData("empty", VaultCreationOutcome.KeyfileUnusable)]
    [InlineData("hashed", VaultCreationOutcome.KeyfileIsFragile)]
    [InlineData("itself", VaultCreationOutcome.KeyfileIsThisVault)]
    public void A_keyfile_keypaste_will_not_attach_is_refused_before_anything_is_written(
        string kind, VaultCreationOutcome expected)
    {
        var parent = Path.Combine(_directory, "not-yet");
        var path = Path.Combine(parent, "new.kdbx");
        var keyfile = kind switch
        {
            "itself" => path,
            _ => Path.Combine(_directory, "candidate"),
        };

        if (kind == "empty")
        {
            File.WriteAllBytes(keyfile, []);
        }
        else if (kind == "hashed")
        {
            File.WriteAllText(keyfile, "an ordinary document someone might edit");
        }

        var outcome = VaultCreation.TryCreate(path, _password, _password, keyfile, out var created, out _);

        using (created)
        {
            Assert.Equal(expected, outcome);
            Assert.Null(created);
        }

        Assert.False(Directory.Exists(parent), "a refused create still changed the disk");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }

    private static string Digest(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
