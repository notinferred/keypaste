namespace Keypaste.Core;

/// <summary>Whether a path can receive a new vault.</summary>
public enum VaultDestination
{
    /// <summary>Nothing is there.</summary>
    Free = 0,

    /// <summary>A file is already there.</summary>
    Occupied = 1,
}

/// <summary>What happened when a new vault was asked for.</summary>
/// <remarks>
/// A closed set rather than exceptions, for the reason <see cref="Keypaste.Core.Vault"/>'s callers
/// already meet elsewhere: every one of these is an ordinary thing a person does while making a
/// vault, and none of them is an error either front end should present as one. The CLI maps each to
/// its own stderr line and exit code, the desktop to one calm sentence, and neither has to know the
/// other's words.
/// </remarks>
public enum VaultCreationOutcome
{
    /// <summary>The vault exists on disk and is open.</summary>
    Created = 0,

    /// <summary>A file is already at that path, and it was not touched.</summary>
    PathAlreadyExists = 1,

    /// <summary>No password was given.</summary>
    EmptyPassword = 2,

    /// <summary>The confirmation did not match.</summary>
    PasswordsDoNotMatch = 3,

    /// <summary>The path was reachable and the write still failed.</summary>
    Failed = 4,

    /// <summary>The keyfile is missing, unreadable, empty, a vault or one this build cannot read.</summary>
    KeyfileUnusable = 5,

    /// <summary>The keyfile would be keyed by its hash, which one edit to it destroys (D-0287).</summary>
    KeyfileIsFragile = 6,

    /// <summary>The keyfile is the new vault's own path or lies in its backup directory.</summary>
    KeyfileIsThisVault = 7,
}

/// <summary>
/// The rules for making a new vault, in one place for both front ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not in <see cref="Vault.Create"/>.</b> That method is the KDBX operation: given a
/// path and a password, build a vault object. These are the product's rules about when a person is
/// allowed to ask for one, and they were the CLI's alone until the desktop grew a Create button.
/// Two copies of "refuse an occupied path" is exactly the shape of bug where one front end quietly
/// destroys a vault the other protects, so docs/PRODUCT.md law 4.2 puts them here and leaves each
/// surface to say what it likes about them.
/// </para>
/// <para>
/// <b>Refusing an occupied path is not politeness.</b> The file already there is an encrypted vault
/// whose contents nothing can see, and replacing it destroys every secret in it irrecoverably.
/// </para>
/// </remarks>
public static class VaultCreation
{
    /// <summary>Whether <paramref name="path"/> can receive a new vault. Writes nothing.</summary>
    /// <remarks>
    /// Separate from <see cref="TryCreate(string, ReadOnlySpan{char}, ReadOnlySpan{char}, string?, out Vault?, out string)"/> so each front end keeps its own order of asking. The
    /// CLI refuses an occupied path before it prompts for a password, and the desktop says so the
    /// moment the picker comes back, rather than after two fields have been filled in.
    /// </remarks>
    public static VaultDestination Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return File.Exists(path) ? VaultDestination.Occupied : VaultDestination.Free;
    }

    /// <summary>
    /// Applies every creation rule and, if they all pass, writes the vault and hands it back open.
    /// </summary>
    /// <param name="path">Where the vault goes. Its parent directory is created if it is missing.</param>
    /// <param name="password">The new master password.</param>
    /// <param name="confirmation">The same password, typed again.</param>
    /// <param name="created">The open vault, or <see langword="null"/> on every refusal.</param>
    /// <param name="failure">
    /// What the filesystem or the format said, when the outcome is
    /// <see cref="VaultCreationOutcome.Failed"/>; empty otherwise. The CLI prints it, because a
    /// person at a terminal can act on "access denied" and cannot act on "it failed".
    /// </param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// <para>
    /// The destination is checked again here even though <see cref="Inspect"/> exists, because the
    /// two are separated by however long a person takes to type a password twice, and something else
    /// can arrive at that path in the meantime.
    /// </para>
    /// <para>
    /// <b>Nothing is written before every rule has passed.</b> The parent directory is created only
    /// on the way to a vault that is actually going to exist, so a refused attempt leaves the
    /// filesystem exactly as it found it.
    /// </para>
    /// <para>
    /// The vault's ownership passes to the caller, which is a lifetime CA2000 cannot see leaving the
    /// method — the same shape, and the same suppression, as the desktop session's unlock path.
    /// </para>
    /// </remarks>
    public static VaultCreationOutcome TryCreate(
        string path,
        ReadOnlySpan<char> password,
        ReadOnlySpan<char> confirmation,
        out Vault? created,
        out string failure) =>
        TryCreate(path, password, confirmation, keyfilePath: null, out created, out failure);

    /// <summary>
    /// <see cref="TryCreate(string, ReadOnlySpan{char}, ReadOnlySpan{char}, out Vault?, out string)"/>,
    /// protected by an existing keyfile as well as the password.
    /// </summary>
    /// <param name="path">Where the vault goes.</param>
    /// <param name="password">The new master password, which is still required: keypaste makes no vault a keyfile alone opens.</param>
    /// <param name="confirmation">The same password, typed again.</param>
    /// <param name="keyfilePath">An existing keyfile, refused on the grounds an access change refuses one; null for none.</param>
    /// <param name="created">The open vault, or <see langword="null"/> on every refusal.</param>
    /// <param name="failure">What the filesystem or the format said, when the outcome is <see cref="VaultCreationOutcome.Failed"/>.</param>
    public static VaultCreationOutcome TryCreate(
        string path,
        ReadOnlySpan<char> password,
        ReadOnlySpan<char> confirmation,
        string? keyfilePath,
        out Vault? created,
        out string failure)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        created = null;
        failure = string.Empty;

        if (Inspect(path) == VaultDestination.Occupied)
        {
            return VaultCreationOutcome.PathAlreadyExists;
        }

        if (password.IsEmpty)
        {
            return VaultCreationOutcome.EmptyPassword;
        }

        if (!password.SequenceEqual(confirmation))
        {
            return VaultCreationOutcome.PasswordsDoNotMatch;
        }

        if (keyfilePath is not null)
        {
            keyfilePath = Path.GetFullPath(keyfilePath);

            if (Vault.RefuseAttaching(Path.GetFullPath(path), keyfilePath) is { } refused)
            {
                return refused.Outcome switch
                {
                    VaultAccessOutcome.KeyfileIsFragile => VaultCreationOutcome.KeyfileIsFragile,
                    VaultAccessOutcome.KeyfileIsThisVault => VaultCreationOutcome.KeyfileIsThisVault,
                    _ => VaultCreationOutcome.KeyfileUnusable,
                };
            }
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

#pragma warning disable CA2000
            var vault = Vault.CreateWith(path, password, keyfilePath);
#pragma warning restore CA2000

            try
            {
                vault.Save();
            }
            catch
            {
                vault.Dispose();
                throw;
            }

            created = vault;
            return VaultCreationOutcome.Created;
        }
        catch (VaultException ex)
        {
            failure = ex.Message;
            return VaultCreationOutcome.Failed;
        }
        catch (IOException ex)
        {
            failure = ex.Message;
            return VaultCreationOutcome.Failed;
        }
        catch (UnauthorizedAccessException ex)
        {
            failure = ex.Message;
            return VaultCreationOutcome.Failed;
        }
    }
}
