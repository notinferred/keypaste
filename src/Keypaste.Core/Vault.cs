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
    private bool _disposed;

    private Vault(KeePassInterop interop, string path)
    {
        _interop = interop;
        Path = path;
    }

    /// <summary>The path of the file backing this vault.</summary>
    public string Path { get; }

    /// <summary>
    /// Whether saves write through a temporary file rather than in place. Internal: this is a
    /// regression seam for the defect where the flag was applied on create but not on open.
    /// </summary>
    internal bool UsesFileTransactions => _interop.UsesFileTransactions;

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

        return new Vault(WithUtf8Password(masterPassword, utf8 => KeePassInterop.Create(path, utf8)), path);
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

        return new Vault(WithUtf8Password(masterPassword, utf8 => KeePassInterop.Open(path, utf8)), path);
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
    /// Writes the vault to <see cref="Path"/>, encrypted.
    /// </summary>
    /// <remarks>
    /// Saving the same content twice produces two files that differ byte for byte: the salt,
    /// the nonces, and the derived key material are regenerated on every write. That is a
    /// property of the format, not a defect.
    /// </remarks>
    /// <exception cref="VaultException">The vault could not be written.</exception>
    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _interop.Save();
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
