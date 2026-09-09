using Keypaste.Core.Internal;

namespace Keypaste.Core;

/// <summary>A KDBX4 vault: create it, add entries, save it, reopen it.</summary>
/// <remarks>
/// Master passwords are spans rather than <c>SecureString</c>, which does not encrypt on Linux or
/// macOS. The span is copied into a UTF-8 buffer, used, and zeroed in a <c>finally</c>; the caller
/// owns the lifetime of whatever backs it.
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
        _stamp = stamp ? SourceSnapshot.Digest(path) : null;
    }

    /// <summary>The path of the file backing this vault.</summary>
    public string Path { get; }

    /// <summary>Whether saves write through a temporary file. A test seam; nothing else reads it.</summary>
    internal bool UsesFileTransactions => _interop.UsesFileTransactions;

    /// <summary>History items on the entry called <paramref name="name"/>, or -1 if none has that
    /// name. A test seam; keypaste has no feature that reads history.</summary>
    internal int CountHistoryItems(EntryName name) => _interop.CountHistoryItems(name);

    /// <summary>Creates a new, empty vault protected by <paramref name="masterPassword"/>. Nothing
    /// is written to disk until <see cref="Save"/>.</summary>
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

    /// <summary>Opens an existing vault.</summary>
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Open(path, utf8)),
            path,
            stamp: true);
    }

    /// <summary>Adds an entry, creating any groups <see cref="VaultEntry.GroupPath"/> names that do
    /// not exist yet. Call <see cref="Save"/> to persist it.</summary>
    public void AddEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        _interop.AddEntry(entry);
    }

    /// <summary>Overwrites the fields of the entry at <paramref name="entry"/>'s
    /// <see cref="VaultEntry.Path"/>. Call <see cref="Save"/> to persist it.</summary>
    /// <remarks>
    /// The name identifies the entry, so this cannot rename one, and the entry is edited in place
    /// rather than replaced — its UUID, timestamps, attachments and any custom KeePassXC fields
    /// survive. The previous values are kept as a KeePass history item, so overwriting a secret does
    /// not erase the old one (DECISIONS.md D-0014); only <see cref="RemoveEntry(EntryName)"/> does.
    /// </remarks>
    /// <returns><see langword="true"/> if an entry was updated.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public bool UpdateEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        return _interop.UpdateEntry(entry) > 0;
    }

    /// <summary>Every entry in the vault, depth-first from the root group.</summary>
    public IReadOnlyList<VaultEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadEntries();
    }

    /// <summary>Finds the one entry with this group path and title, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The unambiguous form, and the one every mutation goes through. <see cref="Find(string)"/> takes
    /// the two joined, and joining is lossy: a title <c>b/c</c> in group <c>a</c> and a title <c>c</c>
    /// in group <c>a/b</c> produce one string, so a caller holding both parts must not join them.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public VaultEntry? Find(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.FindEntry(name);
    }

    /// <summary>Finds the one entry at <paramref name="entryPath"/>, for example
    /// <c>servers/production</c>, or <see langword="null"/>.</summary>
    /// <remarks>
    /// A path is what a person types and what a policy file holds, so it stays addressable, but it is
    /// not an identity: one two entries answer to is refused rather than resolved to whichever the
    /// file lists first. <see cref="Find(EntryName)"/> tells them apart.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that path.</exception>
    public VaultEntry? Find(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryPath);

        return ResolveByPath(entryPath);
    }

    /// <summary>Every group path in the vault except the root, slash-separated.</summary>
    /// <remarks>
    /// Groups holding no entries appear here and nowhere in <see cref="ReadEntries"/>, so a listing
    /// that wants to match KeePassXC's view of the same file needs both.
    /// </remarks>
    public IReadOnlyList<string> ReadGroupPaths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadGroupPaths();
    }

    /// <summary>Removes the one entry with this name. Call <see cref="Save"/> to persist it.</summary>
    /// <returns>
    /// <see langword="true"/> if an entry was removed. Removing nothing is not an error here; the
    /// caller decides whether it is one.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public bool RemoveEntry(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.RemoveEntry(name) > 0;
    }

    /// <summary>Removes the entry at <paramref name="entryPath"/>. Call <see cref="Save"/> to
    /// persist it.</summary>
    /// <returns>
    /// <see langword="true"/> if an entry was removed. Removing nothing is not an error here; the
    /// caller decides whether it is one.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that path.</exception>
    public bool RemoveEntry(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryPath);

        return ResolveByPath(entryPath) is { } found && _interop.RemoveEntry(EntryName.Of(found)) > 0;
    }

    /// <summary>Whether something else has written to <see cref="Path"/> since this vault read
    /// it.</summary>
    /// <remarks>
    /// A detector, not a lock: a write landing between this call and the one that follows it is still
    /// lost, and closing that window needs a file lock KDBX does not define.
    /// <see langword="false"/> for a vault from <see cref="Create"/> that has never been saved and for
    /// a file that could not be read at all — see <see cref="SourceSnapshot.Digest"/>, and D-0017 for
    /// the transient-failure absorption that depends on it.
    /// On Windows a file another process holds open for writing cannot be read, so a save racing a
    /// concurrent writer narrowly is not detected; the replace then fails and is retried, so it is loud.
    /// </remarks>
    public bool HasFileChangedSinceOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _stamp is { } stamp
            && SourceSnapshot.Digest(Path) is { } current
            && !stamp.AsSpan().SequenceEqual(current);
    }

    /// <summary>Writes the vault to <see cref="Path"/>, encrypted, unless something else wrote
    /// there first.</summary>
    /// <remarks>
    /// Two saves of the same content differ byte for byte: the salt, the nonces and the derived key
    /// material are regenerated on every write.
    /// On <see cref="VaultChangedOnDiskException"/> nothing is written; a caller that has asked a
    /// person and been told to go ahead calls <see cref="SaveOverwriting"/>.
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">Something else wrote to <see cref="Path"/>.</exception>
    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (HasFileChangedSinceOpen())
        {
            throw new VaultChangedOnDiskException();
        }

        Write();
    }

    /// <summary>Writes the vault to <see cref="Path"/>, discarding whatever else was written
    /// there.</summary>
    /// <remarks>
    /// For a caller that has put the choice to a person and been told to proceed, and for nothing
    /// else: one reaching for this to avoid handling <see cref="VaultChangedOnDiskException"/> has
    /// turned an audible data loss back into a silent one.
    /// </remarks>
    public void SaveOverwriting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Write();
    }

    /// <summary>
    /// The whole of what a typed path means, in one place. Reading and removing ask this same question
    /// so they cannot reach different answers about the same file — three resolvers that disagreed is
    /// the defect D-0091 found, and a fourth would be the same mistake again.
    /// </summary>
    private VaultEntry? ResolveByPath(string entryPath)
    {
        VaultEntry? found = null;

        foreach (VaultEntry entry in _interop.ReadEntries())
        {
            if (!string.Equals(entry.Path, entryPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                throw new VaultException(
                    $"'{entryPath}' names more than one entry: a title containing a separator and " +
                    "a group of that name produce the same path. Rename one of them in KeePassXC.");
            }

            found = entry;
        }

        return found;
    }

    private void Write()
    {
        _interop.Save();
        _stamp = SourceSnapshot.Digest(Path);
    }

    /// <summary>Releases the vault's key material and decrypted contents.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _interop.Dispose();
        _disposed = true;
    }

    /// <summary>Encodes the password to UTF-8, runs <paramref name="use"/>, and zeroes the buffer
    /// whether or not that succeeded.</summary>
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
