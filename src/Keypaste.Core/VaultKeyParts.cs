namespace Keypaste.Core;

/// <summary>
/// What an open vault's key is made of, as KeePassLib combines it: the SHA-256 of its master password, its keyfile's key
/// and the slot of the hardware key it asks, never that key's answer or the password itself.
/// </summary>
/// <remarks>
/// The desktop's quick unlock keeps these sealed in memory and opens the vault from them again with
/// <see cref="Vault.Open(string, VaultKeyParts, HardwareKeys.HardwareKey?)"/>, which derives the key afresh and asks the
/// hardware key again (D-0550). Disposing zeroes them.
/// </remarks>
public sealed class VaultKeyParts : IDisposable
{
    private const byte _format = 1;
    private const int _longestPart = 1024;

    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private byte[]? _password;
    private byte[]? _keyfile;
    private bool _disposed;

    /// <summary>Parts that take ownership of <paramref name="password"/> and <paramref name="keyfile"/>.</summary>
    internal VaultKeyParts(byte[]? password, byte[]? keyfile, string? keyfilePath, int? hardwareKeySlot)
    {
        if (password is null && keyfile is null && hardwareKeySlot is null)
        {
            throw new ArgumentException("A vault's key has at least one part.", nameof(password));
        }

        _password = password;
        _keyfile = keyfile;
        KeyfilePath = keyfilePath;
        HardwareKeySlot = hardwareKeySlot;
    }

    /// <summary>The keyfile the vault opened with, or <see langword="null"/> when its key has none.</summary>
    public string? KeyfilePath { get; }

    /// <summary>The slot of the hardware key the vault asks, or <see langword="null"/> when its key has none.</summary>
    public int? HardwareKeySlot { get; }

    /// <summary>The master password's SHA-256, or <see langword="null"/> when the key has no password.</summary>
    internal byte[]? Password
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _password;
        }
    }

    /// <summary>The keyfile's key, or <see langword="null"/> when the key has no keyfile.</summary>
    internal byte[]? Keyfile
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _keyfile;
        }
    }

    /// <summary>These parts as bytes, for sealing.</summary>
    /// <returns>A new array, which the caller zeroes.</returns>
    public byte[] ToBytes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var path = KeyfilePath is null ? [] : _strictUtf8.GetBytes(KeyfilePath);
        var bytes = new byte[2 + (3 * sizeof(ushort)) + (_password?.Length ?? 0) + (_keyfile?.Length ?? 0) + path.Length];
        bytes[0] = _format;
        bytes[1] = (byte)(HardwareKeySlot ?? 0);

        var at = 2;
        Write(_password ?? [], bytes, ref at);
        Write(_keyfile ?? [], bytes, ref at);
        Write(path, bytes, ref at);
        return bytes;
    }

    /// <summary>The parts <see cref="ToBytes"/> wrote.</summary>
    /// <param name="bytes">What it wrote; not kept.</param>
    /// <returns>The parts, which the caller disposes.</returns>
    /// <exception cref="VaultException">The bytes are not parts this version of keypaste wrote.</exception>
    public static VaultKeyParts FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes[0] != _format || bytes[1] > 2)
        {
            throw Unreadable();
        }

        var at = 2;
        var password = Read(bytes, ref at);
        var keyfile = Read(bytes, ref at);
        var path = Read(bytes, ref at);

        if (at != bytes.Length)
        {
            throw Unreadable();
        }

        try
        {
            return new VaultKeyParts(
                password.Length == 0 ? null : password.ToArray(),
                keyfile.Length == 0 ? null : keyfile.ToArray(),
                path.Length == 0 ? null : _strictUtf8.GetString(path),
                bytes[1] == 0 ? null : bytes[1]);
        }
        catch (Exception e) when (e is ArgumentException or DecoderFallbackException)
        {
            throw Unreadable();
        }
    }

    /// <summary>Zeroes the parts.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CryptographicOperations.ZeroMemory(_password);
        CryptographicOperations.ZeroMemory(_keyfile);
        _password = null;
        _keyfile = null;
    }

    private static void Write(ReadOnlySpan<byte> part, byte[] bytes, ref int at)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), checked((ushort)part.Length));
        part.CopyTo(bytes.AsSpan(at + sizeof(ushort)));
        at += sizeof(ushort) + part.Length;
    }

    private static ReadOnlySpan<byte> Read(ReadOnlySpan<byte> bytes, ref int at)
    {
        if (bytes.Length - at < sizeof(ushort))
        {
            throw Unreadable();
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[at..]);
        at += sizeof(ushort);

        if (length > _longestPart || bytes.Length - at < length)
        {
            throw Unreadable();
        }

        var part = bytes.Slice(at, length);
        at += length;
        return part;
    }

    private static VaultException Unreadable() => new("Those bytes are not a vault key's parts.");
}
