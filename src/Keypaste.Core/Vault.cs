using Keypaste.Core.Internal;

namespace Keypaste.Core;

/// <summary>
/// A KDBX4 vault: create it, add entries, save it, reopen it.
/// </summary>
/// <remarks>
/// <para>
/// The file format is KDBX4 with Argon2d key derivation — see <see cref="KdbxFormat"/> for the
/// exact parameters. keypaste never invents a format and never writes its own cryptography
/// (CORE.md laws 2 and 3.6); everything here delegates to the vendored KeePassLib.
/// </para>
/// <para>
/// Master passwords are passed as <see cref="ReadOnlySpan{T}"/> of <see cref="char"/> rather
/// than <c>SecureString</c>. <c>SecureString</c> does not encrypt on Linux or macOS and
/// Microsoft advises against it in new code, so using it here would be a gesture rather than a
/// protection. keypaste copies the span into a UTF-8 buffer, hands that to the key derivation,
/// and zeroes the buffer in a <c>finally</c>. The caller owns the lifetime of whatever backs
/// the span.
/// </para>
/// </remarks>
public sealed class Vault : IDisposable
{
    private readonly KeePassInterop _interop;
    private byte[]? _stamp;
    private bool _disposed;

    private Vault(KeePassInterop interop, string path, bool stamp)
    {
        _interop = interop;
        Path = path;
        _stamp = stamp ? Digest(path) : null;
    }

    /// <summary>The path of the file backing this vault.</summary>
    public string Path { get; }

    /// <summary>
    /// Whether saves write through a temporary file rather than in place. Internal: this is a
    /// regression seam for the defect where the flag was applied on create but not on open.
    /// </summary>
    internal bool UsesFileTransactions => _interop.UsesFileTransactions;

    /// <summary>
    /// The number of history items the entry at <paramref name="entryPath"/> carries, or -1 if
    /// there is no such entry. A test seam; keypaste itself has no feature that reads history.
    /// </summary>
    internal int CountHistoryItems(string entryPath) => _interop.CountHistoryItems(entryPath);

    /// <summary>
    /// Creates a new, empty vault protected by <paramref name="masterPassword"/>.
    /// </summary>
    /// <remarks>
    /// Nothing is written to disk until <see cref="Save"/> is called.
    /// </remarks>
    /// <param name="path">Where the vault will be saved.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <returns>The new vault.</returns>
    /// <exception cref="VaultException">The vault could not be created.</exception>
    public static Vault Create(string path, ReadOnlySpan<char> masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // No stamp: there is no file yet, and a Create aimed at an occupied path is a caller
        // saying "make a new vault here" rather than a stale copy of one. `keypaste init` is what
        // refuses to overwrite, and it does so before reaching this.
        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Create(path, utf8)),
            path,
            stamp: false);
    }

    /// <summary>
    /// Opens an existing vault.
    /// </summary>
    /// <param name="path">Path of the vault file.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <returns>The opened vault.</returns>
    /// <exception cref="InvalidMasterPasswordException">The password does not open the vault.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Open(path, utf8)),
            path,
            stamp: true);
    }

    /// <summary>
    /// Adds an entry, creating any groups named by <see cref="VaultEntry.GroupPath"/> that do
    /// not exist yet. Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="entry">The entry to add.</param>
    /// <exception cref="VaultException">The entry could not be added.</exception>
    public void AddEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        _interop.AddEntry(entry);
    }

    /// <summary>
    /// Overwrites the fields of the entry at <paramref name="entry"/>'s
    /// <see cref="VaultEntry.Path"/>. Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path identifies the entry, so this cannot rename one — an entry with a new title is a
    /// different entry. Everything the entry carries that <see cref="VaultEntry"/> does not model
    /// (its UUID, creation time, attachments, and any custom string fields added in KeePassXC) is
    /// preserved: the underlying entry is edited in place, never replaced.
    /// </para>
    /// <para>
    /// The previous field values are retained as a KeePass history item, which KeePassXC shows in
    /// the entry's History tab. That is the format's native behaviour, and it means overwriting a
    /// secret does not erase the old one — see DECISIONS.md D-0014. Only
    /// <see cref="RemoveEntry"/> removes a value outright.
    /// </para>
    /// </remarks>
    /// <param name="entry">The replacement field values, located by their path.</param>
    /// <returns>
    /// <see langword="true"/> if an entry was updated, <see langword="false"/> if no entry has
    /// that path.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public bool UpdateEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        return _interop.UpdateEntry(entry) > 0;
    }

    /// <summary>
    /// Returns every entry in the vault, depth-first from the root group.
    /// </summary>
    /// <returns>The entries.</returns>
    public IReadOnlyList<VaultEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadEntries();
    }

    /// <summary>
    /// Finds a single entry by its full path, for example <c>servers/production</c>.
    /// </summary>
    /// <param name="entryPath">The entry's <see cref="VaultEntry.Path"/>.</param>
    /// <returns>The entry, or <see langword="null"/> if no entry has that path.</returns>
    public VaultEntry? Find(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryPath);

        foreach (VaultEntry entry in _interop.ReadEntries())
        {
            if (string.Equals(entry.Path, entryPath, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns every group path in the vault, excluding the root group, as slash-separated
    /// paths such as <c>servers/production</c>.
    /// </summary>
    /// <remarks>
    /// Groups holding no entries appear here and nowhere in <see cref="ReadEntries"/>, so a
    /// listing that wants to match KeePassXC's view of the same file needs both.
    /// </remarks>
    /// <returns>The group paths.</returns>
    public IReadOnlyList<string> ReadGroupPaths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadGroupPaths();
    }

    /// <summary>
    /// Removes the entry at <paramref name="entryPath"/>. Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="entryPath">The entry's <see cref="VaultEntry.Path"/>.</param>
    /// <returns>
    /// <see langword="true"/> if an entry was removed, <see langword="false"/> if nothing matched
    /// that path. Removing nothing is not an error here; the caller decides whether it is one.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="entryPath"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public bool RemoveEntry(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryPath);

        return _interop.RemoveEntry(entryPath) > 0;
    }

    /// <summary>
    /// Whether something else has written to <see cref="Path"/> since this vault read it.
    /// </summary>
    /// <returns><see langword="true"/> when the file no longer matches what was last read or written.</returns>
    /// <remarks>
    /// <para>
    /// <b>A detector, not a lock.</b> A write landing between this call and the one that follows it
    /// is still lost. Closing that window needs a file lock, which KDBX does not define and
    /// KeePassXC does not take. What this catches is the case that actually happens: a vault held
    /// open in a window for hours while somebody edits the same file from a terminal.
    /// </para>
    /// <para>
    /// <see langword="false"/> for a vault from <see cref="Create"/> that has never been saved,
    /// because there is nothing yet to have changed, and <see langword="false"/> for a file that
    /// could not be read at all — see <see cref="Digest"/> for why absence is not a conflict.
    /// </para>
    /// </remarks>
    public bool HasFileChangedSinceOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _stamp is { } stamp
            && Digest(Path) is { } current
            && !stamp.AsSpan().SequenceEqual(current);
    }

    /// <summary>
    /// Writes the vault to <see cref="Path"/>, encrypted, unless something else wrote there first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Saving the same content twice produces two files that differ byte for byte: the salt,
    /// the nonces, and the derived key material are regenerated on every write. That is a
    /// property of the format, not a defect — and it is also what makes the change detector cheap,
    /// since every save moves the digest whether or not any field moved.
    /// </para>
    /// <para>
    /// On <see cref="VaultChangedOnDiskException"/> <b>nothing is written</b>. A caller that has
    /// asked a person and been told to go ahead calls <see cref="SaveOverwriting"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">Something else wrote to <see cref="Path"/>.</exception>
    /// <exception cref="VaultException">The vault could not be written.</exception>
    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (HasFileChangedSinceOpen())
        {
            throw new VaultChangedOnDiskException();
        }

        Write();
    }

    /// <summary>
    /// Writes the vault to <see cref="Path"/>, discarding whatever else was written there.
    /// </summary>
    /// <remarks>
    /// The escape hatch for a caller that has put the choice to a person and been told to proceed.
    /// It exists so <see cref="Save"/> can refuse without leaving anyone stuck, and for no other
    /// reason: a caller reaching for this to avoid handling the exception has turned an audible
    /// data loss back into a silent one.
    /// </remarks>
    /// <exception cref="VaultException">The vault could not be written.</exception>
    public void SaveOverwriting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Write();
    }

    private void Write()
    {
        _interop.Save();
        _stamp = Digest(Path);
    }

    /// <summary>
    /// A digest of the file, or <see langword="null"/> when it could not be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole file rather than its modification time. Several filesystems round mtime to a
    /// second or two, so a rewrite inside the same second would be missed — and a rewrite inside
    /// the same second is the common case here, not the exotic one. A vault is kilobytes; hashing
    /// it is not worth optimising around.
    /// </para>
    /// <para>
    /// <b>A file that cannot be read is not a conflict, and treating it as one was a bug.</b> "This
    /// changed underneath you" is a claim about a file that exists and now holds something else. A
    /// file that is missing, locked, or on a directory that momentarily went away is a write
    /// problem, and the save path already has a retry and an error message naming what the
    /// operating system said — both of which the first version of this method skipped straight
    /// past, breaking the transient-failure absorption D-0017 exists for.
    /// </para>
    /// <para>
    /// The cost, stated rather than discovered: on Windows a file another process holds open for
    /// writing cannot be read, so a save racing a concurrent writer that narrowly is not detected.
    /// The replace then fails and is retried, so this is loud rather than silent — and it is the
    /// sub-second window <see cref="HasFileChangedSinceOpen"/> already says it does not close.
    /// </para>
    /// </remarks>
    private static byte[]? Digest(string path)
    {
        try
        {
            return SHA256.HashData(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Releases the vault's key material and decrypted contents.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _interop.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Encodes the password to UTF-8, runs <paramref name="use"/>, and zeroes the buffer
    /// whether or not that succeeded.
    /// </summary>
    private static KeePassInterop WithUtf8Password(
        ReadOnlySpan<char> masterPassword,
        Func<byte[], KeePassInterop> use)
    {
        byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(masterPassword)];
        try
        {
            Encoding.UTF8.GetBytes(masterPassword, utf8);
            return use(utf8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }
}
