extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Cryptography;
using KeePass::KeePassLib.Cryptography.KeyDerivation;
using KeePass::KeePassLib.Keys;
using Keypaste.Core.HardwareKeys;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>The name of the part a quick unlock's key carries for the master password's SHA-256.</summary>
    private const string _passwordPart = "keypaste.password";

    /// <summary>The name of the part a quick unlock's key carries for the keyfile's key.</summary>
    private const string _keyfilePart = "keypaste.keyfile";

    /// <summary>The parts this vault's key is made of, read from the key it opened with.</summary>
    internal VaultKeyParts ExportKeyParts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = _database.MasterKey;
        return new VaultKeyParts(PasswordPart(key)?.KeyData.ReadData(), KeyfilePart(key)?.KeyData.ReadData(), KeyfilePath, HardwareKey?.Slot);
    }

    /// <summary>The factors this vault was opened with, for <see cref="Reopen(string, KeyCopy)"/> and <see cref="Reopen(string, byte[], KeyCopy)"/>.</summary>
    internal KeyCopy CopyKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A new container over the same components: closing this vault drops its reference to the
        // key and touches none of them.
        CompositeKey key = new();
        foreach (var component in _database.MasterKey.UserKeys)
        {
            key.AddUserKey(component);
        }

        return new KeyCopy(key, KeyfilePath is not null, HardwareKey is not null) { PartsKeyfilePath = _partsKeyfilePath };
    }

    /// <summary>A valid KeePass 2.0 XML keyfile and the key it holds, for <see cref="ReadsXmlKeyfiles"/>.</summary>
    private const string _probeKeyfile =
        """<?xml version="1.0" encoding="UTF-8"?><KeyFile><Meta><Version>2.0</Version></Meta><Key><Data Hash="1E281D73">B243DCB7 D3F97ECC E1DB3620 C8B7D53B A0CE206E 92889C8C 75755038 EE5DCCDD</Data></Key></KeyFile>""";

    private const string _probeKey = "B243DCB7D3F97ECCE1DB3620C8B7D53BA0CE206E92889C8C75755038EE5DCCDD";

    private static readonly Lazy<bool> _readsXmlKeyfiles = new(ProbeXmlKeyfiles);

    /// <summary>Whether this build's keyfile loader reads a KeePass XML keyfile's key.</summary>
    /// <remarks>
    /// <c>KcpKeyFile</c> keys with the SHA-256 of the file whenever its XML parse throws, which is
    /// right for a document it cannot parse and wrong for every XML keyfile when the parser itself
    /// cannot run, as under NativeAOT before the CLI kept the types it reads. Asked of a keyfile whose key is known, so a document
    /// that does not parse in a working build still falls back as KeePassXC's does.
    /// </remarks>
    internal static bool ReadsXmlKeyfiles => _readsXmlKeyfiles.Value;

    /// <summary>A new KeePass XML keyfile, version 2.0, around 32 random bytes, as KeePass's <c>KcpKeyFile.Create</c> writes one with no added entropy.</summary>
    /// <returns>The document's bytes, which the caller zeroes.</returns>
    internal static byte[] NewXmlKeyfile()
    {
        var key = CryptoRandom.Instance.GetRandomBytes(32);
        MemoryStream buffer = new();

        try
        {
            KfxFile.Create(0, key, null).Save(buffer);
            return buffer.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
            buffer.Dispose();
        }
    }

    private static bool ProbeXmlKeyfiles()
    {
        try
        {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(_probeKeyfile), writable: false);
            var key = KfxFile.Load(stream)?.GetKey();
            return key is not null && Convert.ToHexString(key) == _probeKey;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether the vault's key includes a master password.</summary>
    internal bool KeyHasPassword => PasswordPart(_database.MasterKey) is not null;

    /// <summary>The keyfile the vault's key includes, or <see langword="null"/>.</summary>
    internal string? KeyfilePath => KeyfilePart(_database.MasterKey) switch
    {
        KcpKeyFile keyfile => keyfile.Path,
        KcpCustomKey => _partsKeyfilePath,
        _ => null,
    };

    /// <summary>The key's master password, typed or rebuilt from <see cref="VaultKeyParts"/>, or null.</summary>
    private static IUserKey? PasswordPart(CompositeKey key) =>
        key.GetUserKey(typeof(KcpPassword)) ?? key.UserKeys.FirstOrDefault(part => part is KcpCustomKey { Name: _passwordPart });

    /// <summary>The key's keyfile, read from its file or rebuilt from <see cref="VaultKeyParts"/>, or null.</summary>
    private static IUserKey? KeyfilePart(CompositeKey key) =>
        key.GetUserKey(typeof(KcpKeyFile)) ?? key.UserKeys.FirstOrDefault(part => part is KcpCustomKey { Name: _keyfilePart });

    /// <summary>The hardware key the vault's key includes, or <see langword="null"/>.</summary>
    internal HardwareKey? HardwareKey =>
        (_database.MasterKey.GetUserKey(typeof(ChallengeResponseKey)) as ChallengeResponseKey)?.Factor;

    /// <summary>Replaces the vault's key in memory. Nothing is written until a save is given the change.</summary>
    /// <param name="utf8NewPassword">The new password, or <see langword="null"/> to keep the current one if there is one.</param>
    /// <param name="keyfilePath">The keyfile the new key includes, or <see langword="null"/> for none.</param>
    /// <param name="keepCurrentKeyfile">Whether <paramref name="keyfilePath"/> is the keyfile the vault opened with.</param>
    /// <param name="hardwareKey">The hardware key the new key includes, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// A kept keyfile keeps the key material it was opened with, so a file edited since then cannot
    /// slip a different key into the vault; the verifying key re-reads it from disk and refuses
    /// instead. The KDF, cipher and format version are the vault's own and are not touched.
    /// </remarks>
    /// <exception cref="VaultException">The keyfile could not be read.</exception>
    internal KeyChange ChangeKey(byte[]? utf8NewPassword, string? keyfilePath, bool keepCurrentKeyfile, HardwareKey? hardwareKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var current = _database.MasterKey;
        var password = utf8NewPassword is null
            ? PasswordPart(current)
            : new KcpPassword(utf8NewPassword, false);

        CompositeKey next = new();
        CompositeKey verify = new();
        if (password is not null)
        {
            next.AddUserKey(password);
            verify.AddUserKey(password);
        }

        if (keyfilePath is not null)
        {
            next.AddUserKey(keepCurrentKeyfile ? KeyfilePart(current)! : ReadKeyfile(keyfilePath));
            verify.AddUserKey(ReadKeyfile(keyfilePath));
        }

        if (hardwareKey is not null)
        {
            ChallengeResponseKey challenged = new(hardwareKey);
            next.AddUserKey(challenged);
            verify.AddUserKey(challenged);
        }

        var change = new KeyChange(current, _database.MasterKeyChanged, verify);
        _database.MasterKey = next;
        _database.MasterKeyChanged = DateTime.UtcNow;
        return change;
    }

    /// <summary>Puts back the key a change replaced, so this vault saves as it would have before.</summary>
    internal void Revert(KeyChange change)
    {
        _database.MasterKey = change.Previous;
        _database.MasterKeyChanged = change.PreviousChangedAt;
    }

    /// <summary>Opens the file on disk with the changed key's factors, and keeps nothing.</summary>
    /// <exception cref="VaultException">It did not open.</exception>
    internal void ConfirmOnDisk(KeyChange change)
    {
        PwDatabase check = new();
        try
        {
            check.Open(_database.IOConnectionInfo, Reread(change.Verify), null);
        }
        catch (Exception ex)
        {
            throw new VaultException($"'{_database.IOConnectionInfo.Path}' did not open with the new credentials.", ex);
        }
        finally
        {
            check.Close();
        }
    }

    private static KcpKeyFile ReadKeyfile(string path)
    {
        try
        {
            return new KcpKeyFile(path, true);
        }
        catch (Exception ex)
        {
            throw new VaultException($"The key file '{path}' could not be read.", ex);
        }
    }

    /// <summary>The same factors with every keyfile read again from disk.</summary>
    private static CompositeKey Reread(CompositeKey key)
    {
        CompositeKey fresh = new();
        foreach (var component in key.UserKeys)
        {
            fresh.AddUserKey(component is KcpKeyFile keyfile ? ReadKeyfile(keyfile.Path) : component);
        }

        return fresh;
    }

    /// <summary>Builds the composite key from the factors the caller supplied.</summary>
    /// <remarks>
    /// <para>
    /// <b>An empty password is not the same as no password.</b> <see cref="KcpPassword"/> hashes
    /// whatever bytes it is given, so an empty one contributes SHA-256 of nothing — a real 32-byte
    /// component — and <c>CompositeKey.CreateRawCompositeKey32</c> concatenates the components and
    /// hashes the result. Adding an empty password beside a keyfile therefore produces a different
    /// key from the keyfile alone, and would refuse every keyfile-only vault KeePassXC ever wrote.
    /// So when there is a keyfile and nothing was typed, the password is left out entirely.
    /// </para>
    /// <para>
    /// Only then. An empty password with no keyfile still builds the key it always did, because
    /// that is not a passwordless vault, it is a wrong password, and it has to keep being refused
    /// as one. A hardware key counts as the keyfile does, because KeePassXC adds no password
    /// component for an empty field beside either.
    /// </para>
    /// </remarks>
    private static CompositeKey BuildKey(byte[] utf8Password, string? keyfilePath, HardwareKey? hardwareKey = null)
    {
        CompositeKey key = new();

        if (utf8Password.Length > 0 || (string.IsNullOrEmpty(keyfilePath) && hardwareKey is null))
        {
            // bRememberPassword: false — the key material is the SHA-256 of the password; there is
            // no reason to also retain the password itself for the lifetime of the database object.
            key.AddUserKey(new KcpPassword(utf8Password, false));
        }

        if (!string.IsNullOrEmpty(keyfilePath))
        {
            // bThrowIfDbFile: true — a vault offered as its own keyfile is refused by the library
            // as well as by VaultKeyfile, so neither can be the only thing standing between a
            // person and a vault keyed to itself.
            key.AddUserKey(new KcpKeyFile(keyfilePath, true));
        }

        if (hardwareKey is not null)
        {
            key.AddUserKey(new ChallengeResponseKey(hardwareKey));
        }

        return key;
    }

    /// <summary>The composite key <see cref="ExportKeyParts"/> read, rebuilt from its parts as KeePassXC's quick unlock rebuilds one.</summary>
    /// <remarks>
    /// <c>CompositeKey</c> hashes each part's data in the order added, so custom keys holding the password's SHA-256
    /// and the keyfile's key, unhashed, give the key the password and keyfile gave.
    /// </remarks>
    private static CompositeKey BuildKey(VaultKeyParts parts, HardwareKey? hardwareKey)
    {
        CompositeKey key = new();

        if (parts.Password is { } password)
        {
            key.AddUserKey(new KcpCustomKey(_passwordPart, password, false));
        }

        if (parts.Keyfile is { } keyfile)
        {
            key.AddUserKey(new KcpCustomKey(_keyfilePart, keyfile, false));
        }

        if (hardwareKey is not null)
        {
            key.AddUserKey(new ChallengeResponseKey(hardwareKey));
        }

        return key;
    }

    private static readonly PwUuid _aesKdfUuid = new AesKdf().Uuid;
    private static readonly PwUuid _argon2dUuid = new Argon2Kdf(Argon2Type.D).Uuid;
    private static readonly PwUuid _argon2idUuid = new Argon2Kdf(Argon2Type.ID).Uuid;

    private static VaultKeyDerivation Derivation(KdfParameters parameters)
    {
        var function = parameters.KdfUuid.Equals(_aesKdfUuid) ? KeyDerivationFunction.AesKdf
            : parameters.KdfUuid.Equals(_argon2dUuid) ? KeyDerivationFunction.Argon2d
            : parameters.KdfUuid.Equals(_argon2idUuid) ? KeyDerivationFunction.Argon2id
            : KeyDerivationFunction.Unknown;

        return function switch
        {
            KeyDerivationFunction.AesKdf => new(function, parameters.GetUInt64(AesKdf.ParamRounds, 0), 0, 0),
            KeyDerivationFunction.Argon2d or KeyDerivationFunction.Argon2id => new(
                function,
                parameters.GetUInt64(Argon2Kdf.ParamIterations, 0),
                parameters.GetUInt64(Argon2Kdf.ParamMemory, 0),
                parameters.GetUInt32(Argon2Kdf.ParamParallelism, 0)),
            _ => new(function, 0, 0, 0),
        };
    }

    /// <summary>How this vault derives its key, as its next save writes it.</summary>
    internal VaultKeyDerivation KeyDerivation
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Derivation(_database.KdfParameters);
        }
    }

    /// <summary>Gives this vault <see cref="VaultKeyDerivation.Strengthened"/>; its next save writes it.</summary>
    internal void StrengthenKeyDerivation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _database.KdfParameters = Argon2Parameters(KeyDerivation.Strengthened);
    }

    /// <summary>The key factors this vault was opened with.</summary>
    internal IReadOnlyList<string> KeyFactors =>
        [
            .. KeyHasPassword ? ["password"] : Array.Empty<string>(),
            .. KeyfilePath is null ? [] : new[] { "key file" },
            .. HardwareKey is null ? [] : new[] { "YubiKey" },
        ];

    /// <summary>A key replaced in memory and not yet, or just, written.</summary>
    internal sealed class KeyChange
    {
        internal KeyChange(CompositeKey previous, DateTime previousChangedAt, CompositeKey verify)
        {
            Previous = previous;
            PreviousChangedAt = previousChangedAt;
            Verify = verify;
        }

        internal CompositeKey Previous { get; }

        internal DateTime PreviousChangedAt { get; }

        /// <summary>The new key's factors, each keyfile named by the path it is read again from.</summary>
        internal CompositeKey Verify { get; }

        /// <summary>Whether the vault file has been replaced by bytes under the new key.</summary>
        internal bool Committed { get; set; }
    }

    /// <summary>An open vault's key, with the keyfile material it was opened with rather than the file's current bytes.</summary>
    internal sealed class KeyCopy
    {
        internal KeyCopy(CompositeKey key, bool hasKeyfile, bool hasHardwareKey)
        {
            Key = key;
            HasKeyfile = hasKeyfile;
            HasHardwareKey = hasHardwareKey;
        }

        internal CompositeKey Key { get; }

        internal bool HasKeyfile { get; }

        internal bool HasHardwareKey { get; }

        /// <summary>The keyfile a key rebuilt from <see cref="VaultKeyParts"/> was first opened with, or null.</summary>
        internal string? PartsKeyfilePath { get; init; }
    }
}
