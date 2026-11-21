extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Cryptography.KeyDerivation;
using KeePass::KeePassLib.Keys;
using KeePass::KeePassLib.Serialization;
using Keypaste.Core.HardwareKeys;

namespace Keypaste.Core.Internal;

/// <summary>
/// The single point of contact between keypaste and KeePassLib.
/// </summary>
/// <remarks>
/// <para>
/// <b>The files of <c>Internal/</c> are the only ones in the repository permitted to reference KeePassLib.</b>
/// Everything above them speaks in <see cref="VaultEntry"/> and <see cref="VaultException"/>.
/// That rule is what keeps the one-core rule in docs/ARCHITECTURE.md honest — one core library, with the format
/// dependency behind a seam narrow enough that replacing it is a change to one folder rather
/// than an archaeology project.
/// </para>
/// <para>
/// Vendored KeePassLib provenance and local modifications: <c>third_party/KeePassLib/UPSTREAM.md</c>.
/// </para>
/// </remarks>
internal sealed partial class KeePassInterop : IDisposable
{
    private readonly PwDatabase _database;
    private bool _disposed;
    private bool _readOnly;

    /// <summary>The entries of the file this instance last read or wrote, which a save may drop only by recording their deletion.</summary>
    private HashSet<PwUuid> _entriesOnDisk;

    /// <summary>See <see cref="SaveCount"/>.</summary>
    private long? _saveCount;

    /// <summary>The keyfile a vault opened from <see cref="VaultKeyParts"/> was first opened with, which its key no longer names.</summary>
    private string? _partsKeyfilePath;

    // KdfPool fills its static list without synchronization; the runtime runs this once and holds concurrent first callers until it returns (F.11).
    // Nothing reaches KeePassLib's native Argon2 before this has handed it keypaste's library.
    static KeePassInterop()
    {
        NativeArgon2.Register(typeof(PwDatabase).Assembly);
        _ = KdfPool.Engines.Count();
    }

    private KeePassInterop(PwDatabase database)
    {
        _database = database;
        _entriesOnDisk = EntryIds(database);
        _saveCount = SaveCountOf(database);
        ReadForeignData = database.UnknownElementsRead > 0;
    }

    /// <summary>Whether the file this instance was read from held XML KeePassLib does not know, which its saves leave out.</summary>
    internal bool ReadForeignData { get; }

    /// <summary>Creates a new KDBX4 vault protected by the given UTF-8 master password.</summary>
    /// <remarks>The caller owns <paramref name="utf8Password"/> and is responsible for zeroing it.</remarks>
    internal static KeePassInterop Create(
        string path, byte[] utf8Password, string? keyfilePath = null, HardwareKey? hardwareKey = null)
    {
        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), BuildKey(utf8Password, keyfilePath, hardwareKey));
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
    /// <exception cref="HardwareKeyException">The hardware key did not answer.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    internal static KeePassInterop Open(
        string path, byte[] utf8Password, string? keyfilePath = null, HardwareKey? hardwareKey = null)
    {
        var database = OpenDatabase(path, utf8Password, keyfilePath, hardwareKey);
        ApplyWriteSafety(database);
        return new KeePassInterop(database);
    }

    /// <summary>Opens an existing vault with the parts of a key another instance exported.</summary>
    /// <param name="path">The vault file.</param>
    /// <param name="parts">The parts, rebuilt in the order a password unlock adds them, so the composite key is the same.</param>
    /// <param name="hardwareKey">The hardware key <paramref name="parts"/> names the slot of, asked now, or <see langword="null"/>.</param>
    /// <exception cref="InvalidMasterPasswordException">The parts do not open the vault.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    internal static KeePassInterop Open(string path, VaultKeyParts parts, HardwareKey? hardwareKey)
    {
        var key = BuildKey(parts, hardwareKey);
        var database = OpenDatabase(path, () => key, parts.Keyfile is not null, hardwareKey is not null);
        ApplyWriteSafety(database);
        return new KeePassInterop(database) { _partsKeyfilePath = parts.KeyfilePath };
    }

    /// <summary>Opens another vault to copy from. Nothing on the returned instance saves.</summary>
    /// <remarks>The caller owns <paramref name="utf8Password"/> and is responsible for zeroing it.</remarks>
    /// <exception cref="InvalidMasterPasswordException">The factors do not open the vault.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    internal static KeePassInterop OpenReadOnly(string path, byte[] utf8Password, string? keyfilePath, HardwareKey? hardwareKey = null) =>
        new(OpenDatabase(path, utf8Password, keyfilePath, hardwareKey)) { _readOnly = true };

    /// <summary>Opens the vault at <paramref name="path"/> again with a key another instance copied.</summary>
    /// <exception cref="InvalidMasterPasswordException">The key no longer opens the vault.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    internal static KeePassInterop Reopen(string path, KeyCopy key)
    {
        var database = OpenDatabase(path, () => key.Key, key.HasKeyfile, key.HasHardwareKey);
        ApplyWriteSafety(database);
        return new KeePassInterop(database) { _partsKeyfilePath = key.PartsKeyfilePath };
    }

    /// <summary>Opens bytes read from the vault at <paramref name="path"/> with a key another instance copied, as <see cref="Reopen(string, KeyCopy)"/> opens the file.</summary>
    /// <param name="path">The file the bytes were read from, which a save writes.</param>
    /// <param name="bytes">The file's bytes, read once by the caller, which digested them.</param>
    /// <param name="key">The key.</param>
    /// <exception cref="InvalidMasterPasswordException">The key no longer opens the vault.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer.</exception>
    /// <exception cref="VaultException">The bytes are not a whole KeePass database.</exception>
    internal static KeePassInterop Reopen(string path, byte[] bytes, KeyCopy key)
    {
        var database = OpenDatabase(path, () => key.Key, key.HasKeyfile, key.HasHardwareKey, bytes);
        ApplyWriteSafety(database);
        return new KeePassInterop(database) { _partsKeyfilePath = key.PartsKeyfilePath };
    }

    private static PwDatabase OpenDatabase(
        string path, byte[] utf8Password, string? keyfilePath, HardwareKey? hardwareKey = null) =>
        OpenDatabase(
            path,
            () => BuildKey(utf8Password, keyfilePath, hardwareKey),
            !string.IsNullOrEmpty(keyfilePath),
            hardwareKey is not null);

    private static PwDatabase OpenDatabase(string path, Func<CompositeKey> key, bool hasKeyfile, bool hasHardwareKey, byte[]? bytes = null)
    {
        PwDatabase database = new();
        try
        {
            if (bytes is null)
            {
                database.Open(IOConnectionInfo.FromPath(path), key(), null);
            }
            else
            {
                // PwDatabase.Open's load, over the bytes the caller read and digested rather than a second read of the file.
                database.New(IOConnectionInfo.FromPath(path), key());
                new KdbxFile(database).Load(new MemoryStream(bytes, writable: false), KeePass::KeePassLib.Serialization.KdbxFormat.Default, null);
                database.Modified = false;
            }
        }
        catch (InvalidCompositeKeyException ex)
        {
            database.Close();

            // Which factor was wrong is not knowable — the library compares one derived key — but
            // which factors were OFFERED is, and a message naming only the password sends somebody
            // with a good password and the wrong keyfile to retype the one thing that was right.
            throw new InvalidMasterPasswordException(
                (!hasKeyfile, !hasHardwareKey) switch
                {
                    (true, true) => "The master password is incorrect, or the vault is not a readable KeePass database.",
                    (false, true) => "The master password or the key file is incorrect, or the vault is not a readable KeePass database.",
                    (true, false) => "The master password or the hardware key is incorrect, or the vault is not a readable KeePass database.",
                    (false, false) => "The master password, the key file or the hardware key is incorrect, or the vault is not a readable KeePass database.",
                },
                ex);
        }
        catch (HardwareKeyException)
        {
            database.Close();
            throw;
        }
        catch (Exception ex)
        {
            database.Close();

            // KeePassLib reports an unreadable container, a failed HMAC, and a bad key through
            // several exception types. Anything that is not plainly an I/O problem is treated
            // as "this did not open" and nothing partial is handed back (SECURITY.md law 7).
            throw ex is IOException or UnauthorizedAccessException
                ? new VaultException($"Could not read '{path}'.", ex)
                : new VaultException($"'{path}' could not be opened as a KeePass database.", ex);
        }

        return database;
    }

    /// <summary>The root group's UUID as KeePassXC writes it for its own database hash: its 16 bytes as the file stores them, in lowercase hex.</summary>
    internal string RootGroupHex => Convert.ToHexStringLower(_database.RootGroup.Uuid.UuidBytes);

    /// <summary>
    /// Whether saves go through a temporary file. Exists so the regression test for the
    /// open-path defect can assert the mechanism directly; observing the effect is not practical,
    /// because an in-place save also leaves no debris behind.
    /// </summary>
    internal bool UsesFileTransactions => _database.UseFileTransactions;

    /// <summary>KeePassLib's unsaved-changes flag. A test seam.</summary>
    internal bool Modified
    {
        get => _database.Modified;
        set => _database.Modified = value;
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

    /// <summary>
    /// Derives one small Argon2d key from constant bytes through KeePassLib's own native call, as every unlock and save does,
    /// and returns the library that call was bound to, or <see langword="null"/> when the key came from KeePassLib's managed
    /// Argon2.
    /// </summary>
    internal static string? DeriveArgon2Once()
    {
        Argon2Kdf kdf = new(Argon2Type.D);
        KdfParameters parameters = kdf.GetDefaultParameters();
        parameters.SetByteArray(Argon2Kdf.ParamSalt, new byte[32]);
        parameters.SetUInt64(Argon2Kdf.ParamIterations, 1);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, 64 * 1024);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, 1);

        _ = kdf.Transform(new byte[32], parameters);
        return NativeArgon2.Bound;
    }

    private static void ApplyKeypasteFormatSettings(PwDatabase database) =>
        database.KdfParameters = Argon2Parameters(VaultKeyDerivation.Keypastes);

    /// <summary>Argon2 at the parameters keypaste states rather than inherits, with a fresh salt.</summary>
    /// <remarks>See <see cref="KdbxFormat"/> for why these are pinned here instead of taken from <c>GetDefaultParameters</c>.</remarks>
    private static KdfParameters Argon2Parameters(VaultKeyDerivation derivation)
    {
        Argon2Kdf kdf = new(derivation.Function == KeyDerivationFunction.Argon2id ? Argon2Type.ID : Argon2Type.D);
        KdfParameters parameters = kdf.GetDefaultParameters();
        kdf.Randomize(parameters);

        parameters.SetUInt64(Argon2Kdf.ParamIterations, derivation.Iterations);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, derivation.Memory);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, derivation.Parallelism);
        parameters.SetUInt32(Argon2Kdf.ParamVersion, KdbxFormat.Argon2Version);
        return parameters;
    }

    /// <summary>
    /// Writes through a temporary file and moves it into place, so an interrupted save cannot
    /// truncate a vault that was previously readable.
    /// </summary>
    /// <remarks>
    /// This must be applied on the <see cref="Open(string, byte[], string?, HardwareKey?)"/> paths as well as <see cref="Create"/>, and
    /// separately from <see cref="ApplyKeypasteFormatSettings"/>. KeePassLib defaults the flag to
    /// <see langword="false"/> and <c>PwDatabase.Close()</c> — which <c>Open()</c> calls first —
    /// resets it, so an open-modify-save cycle would otherwise write in place. The format settings
    /// cannot simply be re-applied on open instead: they would replace the vault's own key derivation
    /// with keypaste's on every save, which only an explicit strengthen may do.
    /// </remarks>
    private static void ApplyWriteSafety(PwDatabase database)
    {
        database.UseFileTransactions = true;
    }
}
