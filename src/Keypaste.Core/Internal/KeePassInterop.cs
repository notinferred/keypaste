using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace Keypaste.Core.Internal;

/// <summary>
/// The single point of contact between keypaste and KeePassLib.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only file in the repository permitted to reference KeePassLib.</b>
/// Everything above it speaks in <see cref="VaultEntry"/> and <see cref="VaultException"/>.
/// That rule is what keeps CORE.md law 4.3 honest — one core library, with the format
/// dependency behind a seam narrow enough that replacing it is a single-file change rather
/// than an archaeology project.
/// </para>
/// <para>
/// Vendored KeePassLib provenance and local modifications: <c>third_party/KeePassLib/UPSTREAM.md</c>.
/// </para>
/// </remarks>
internal sealed class KeePassInterop : IDisposable
{
    private readonly PwDatabase _database;
    private bool _disposed;

    private KeePassInterop(PwDatabase database)
    {
        _database = database;
    }

    /// <summary>Creates a new KDBX4 vault protected by the given UTF-8 master password.</summary>
    /// <remarks>The caller owns <paramref name="utf8Password"/> and is responsible for zeroing it.</remarks>
    internal static KeePassInterop Create(string path, byte[] utf8Password)
    {
        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), BuildKey(utf8Password));
            ApplyKeypasteFormatSettings(database);
            ApplyWriteSafety(database);
        }
        catch
        {
            database.Close();
            throw;
        }

        return new KeePassInterop(database);
    }

    /// <summary>Opens an existing vault.</summary>
    /// <remarks>The caller owns <paramref name="utf8Password"/> and is responsible for zeroing it.</remarks>
    /// <exception cref="InvalidMasterPasswordException">The password does not open the vault.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    internal static KeePassInterop Open(string path, byte[] utf8Password)
    {
        PwDatabase database = new();
        try
        {
            database.Open(IOConnectionInfo.FromPath(path), BuildKey(utf8Password), null);
        }
        catch (InvalidCompositeKeyException ex)
        {
            database.Close();
            throw new InvalidMasterPasswordException(
                "The master password is incorrect, or the vault is not a readable KDBX file.", ex);
        }
        catch (Exception ex)
        {
            database.Close();

            // KeePassLib reports an unreadable container, a failed HMAC, and a bad key through
            // several exception types. Anything that is not plainly an I/O problem is treated
            // as "this did not open" and nothing partial is handed back (CORE.md law 3.7).
            throw ex is IOException or UnauthorizedAccessException
                ? new VaultException($"Could not read '{path}'.", ex)
                : new VaultException($"'{path}' could not be opened as a KDBX vault.", ex);
        }

        ApplyWriteSafety(database);
        return new KeePassInterop(database);
    }

    /// <summary>Adds an entry, creating any missing groups along its group path.</summary>
    internal void AddEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PwGroup group = ResolveGroup(entry.GroupPath, createMissing: true)
            ?? throw new VaultException($"Could not create group '{entry.GroupPath}'.");

        PwEntry pwEntry = new(true, true);
        SetField(pwEntry, PwDefs.TitleField, entry.Title);
        SetField(pwEntry, PwDefs.UserNameField, entry.Username);
        SetField(pwEntry, PwDefs.PasswordField, entry.Password);
        SetField(pwEntry, PwDefs.UrlField, entry.Url);
        SetField(pwEntry, PwDefs.NotesField, entry.Notes);

        group.AddEntry(pwEntry, true);
    }

    /// <summary>Returns every entry in the vault, depth-first from the root group.</summary>
    internal IReadOnlyList<VaultEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<VaultEntry> entries = [];
        Collect(_database.RootGroup, string.Empty, entries);
        return entries;
    }

    /// <summary>Returns every group path in the vault, excluding the root group.</summary>
    /// <remarks>
    /// Separate from <see cref="ReadEntries"/> because a group holding no entries is invisible in
    /// an entry listing, and <c>keepassxc-cli ls -R -f</c> shows it. A listing that silently drops
    /// empty groups would disagree with KeePassXC about the shape of the same file.
    /// </remarks>
    internal IReadOnlyList<string> ReadGroupPaths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<string> paths = [];
        CollectGroups(_database.RootGroup, string.Empty, paths);
        return paths;
    }

    /// <summary>Removes the entry at the given path.</summary>
    /// <returns>The number of entries removed: 0 if nothing matched, otherwise 1.</returns>
    internal int RemoveEntry(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int lastSlash = entryPath.LastIndexOf('/');
        string groupPath = lastSlash < 0 ? string.Empty : entryPath[..lastSlash];
        string title = lastSlash < 0 ? entryPath : entryPath[(lastSlash + 1)..];

        PwGroup? group = ResolveGroup(groupPath, createMissing: false);
        if (group is null)
        {
            return 0;
        }

        foreach (PwEntry candidate in group.Entries)
        {
            if (!string.Equals(ReadField(candidate, PwDefs.TitleField), title, StringComparison.Ordinal))
            {
                continue;
            }

            // Removed outright rather than moved to the recycle bin: a vault the user asked to
            // delete from should not keep a readable copy of the secret (CORE.md law 3.4).
            group.Entries.Remove(candidate);
            _database.DeletedObjects.Add(new PwDeletedObject(candidate.Uuid, DateTime.UtcNow));
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Whether saves go through a temporary file. Exists so the regression test for the
    /// open-path defect can assert the mechanism directly; observing the effect is not practical,
    /// because an in-place save also leaves no debris behind.
    /// </summary>
    internal bool UsesFileTransactions => _database.UseFileTransactions;

    /// <summary>Writes the vault to its backing file.</summary>
    internal void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _database.Save(null);
        }
        catch (Exception ex) when (ex is not VaultException)
        {
            throw new VaultException($"Could not save '{_database.IOConnectionInfo.Path}'.", ex);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Close() releases the composite key and the decrypted entry data it holds.
        _database.Close();
        _disposed = true;
    }

    private static CompositeKey BuildKey(byte[] utf8Password)
    {
        CompositeKey key = new();

        // bRememberPassword: false — the key material is the SHA-256 of the password; there is
        // no reason to also retain the password itself for the lifetime of the database object.
        key.AddUserKey(new KcpPassword(utf8Password, false));
        return key;
    }

    private static void ApplyKeypasteFormatSettings(PwDatabase database)
    {
        // KDBX4 with Argon2d, at parameters keypaste states rather than inherits. See
        // KdbxFormat for why these are pinned here instead of taken from GetDefaultParameters.
        Argon2Kdf kdf = new(Argon2Type.D);
        KdfParameters parameters = kdf.GetDefaultParameters();
        kdf.Randomize(parameters);

        parameters.SetUInt64(Argon2Kdf.ParamIterations, KdbxFormat.Argon2Iterations);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, KdbxFormat.Argon2Memory);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, KdbxFormat.Argon2Parallelism);
        parameters.SetUInt32(Argon2Kdf.ParamVersion, KdbxFormat.Argon2Version);

        database.KdfParameters = parameters;
    }

    /// <summary>
    /// Writes through a temporary file and moves it into place, so an interrupted save cannot
    /// truncate a vault that was previously readable.
    /// </summary>
    /// <remarks>
    /// This must be applied on the <see cref="Open"/> path as well as <see cref="Create"/>, and
    /// separately from <see cref="ApplyKeypasteFormatSettings"/>. KeePassLib defaults the flag to
    /// <see langword="false"/> and <c>PwDatabase.Close()</c> — which <c>Open()</c> calls first —
    /// resets it, so an open-modify-save cycle would otherwise write in place. The format settings
    /// cannot simply be re-applied on open instead: they re-randomise the KDF salt, which would
    /// rewrite the key derivation of an existing vault on every save.
    /// </remarks>
    private static void ApplyWriteSafety(PwDatabase database)
    {
        database.UseFileTransactions = true;
    }

    private static void SetField(PwEntry entry, string field, string value)
    {
        // Only the password is marked protected, matching KeePass's own default. The flag
        // controls in-memory protection and the KDBX inner-stream encryption of that field;
        // marking Title or URL protected would make the file open oddly in other KeePass
        // clients for no security gain (CORE.md law 4.6, compatibility is sacred).
        bool protect = string.Equals(field, PwDefs.PasswordField, StringComparison.Ordinal);
        entry.Strings.Set(field, new ProtectedString(protect, value));
    }

    private static void Collect(PwGroup group, string groupPath, List<VaultEntry> entries)
    {
        foreach (PwEntry entry in group.Entries)
        {
            entries.Add(new VaultEntry
            {
                Title = ReadField(entry, PwDefs.TitleField),
                Username = ReadField(entry, PwDefs.UserNameField),
                Password = ReadField(entry, PwDefs.PasswordField),
                Url = ReadField(entry, PwDefs.UrlField),
                Notes = ReadField(entry, PwDefs.NotesField),
                GroupPath = groupPath,
            });
        }

        foreach (PwGroup child in group.Groups)
        {
            string childPath = groupPath.Length == 0 ? child.Name : groupPath + "/" + child.Name;
            Collect(child, childPath, entries);
        }
    }

    private static void CollectGroups(PwGroup group, string groupPath, List<string> paths)
    {
        foreach (PwGroup child in group.Groups)
        {
            string childPath = groupPath.Length == 0 ? child.Name : groupPath + "/" + child.Name;
            paths.Add(childPath);
            CollectGroups(child, childPath, paths);
        }
    }

    private static string ReadField(PwEntry entry, string field)
    {
        return entry.Strings.ReadSafe(field);
    }

    private PwGroup? ResolveGroup(string groupPath, bool createMissing)
    {
        PwGroup current = _database.RootGroup;
        if (groupPath.Length == 0)
        {
            return current;
        }

        foreach (string segment in groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            PwGroup? next = null;
            foreach (PwGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                if (!createMissing)
                {
                    return null;
                }

                next = new PwGroup(true, true, segment, PwIcon.Folder);
                current.AddGroup(next, true);
            }

            current = next;
        }

        return current;
    }
}
