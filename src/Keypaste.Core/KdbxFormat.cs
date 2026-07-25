namespace Keypaste.Core;

/// <summary>
/// Facts about the KDBX container format, and the key-derivation parameters keypaste writes.
/// </summary>
/// <remarks>
/// <para>
/// The Argon2 values here are stated explicitly rather than inherited from the underlying
/// library's defaults. They happen to agree with KeePass 2.61 today; pinning them means that
/// if an upstream default ever changes, it surfaces as a failing test rather than as a silent
/// change to how every vault keypaste writes is protected.
/// </para>
/// <para>
/// keypaste implements no cryptography (CORE.md law 3.6). These are configuration, not
/// algorithms.
/// </para>
/// </remarks>
public static class KdbxFormat
{
    /// <summary>First four bytes of any KDBX file, as a little-endian 32-bit value.</summary>
    public const uint FileSignature1 = 0x9AA2D903;

    /// <summary>Bytes four through seven of a KDBX 2+ file, as a little-endian 32-bit value.</summary>
    public const uint FileSignature2 = 0xB54BFB67;

    /// <summary>Number of Argon2 passes over memory.</summary>
    public const ulong Argon2Iterations = 2;

    /// <summary>Argon2 memory cost, in bytes (64 MiB).</summary>
    public const ulong Argon2Memory = 64UL * 1024 * 1024;

    /// <summary>Number of Argon2 lanes.</summary>
    public const uint Argon2Parallelism = 2;

    /// <summary>Argon2 algorithm version, 0x13 (1.3).</summary>
    public const uint Argon2Version = 0x13;
}

/// <summary>
/// The unencrypted prefix of a KDBX file: its signature and format version.
/// </summary>
/// <remarks>
/// Reading this requires no key, because these twelve bytes precede everything the master
/// password protects. It exists so that keypaste can assert what it actually wrote to disk —
/// a KDBX 3.1 file and a KDBX 4.x file are both "a working vault" to a caller, but only one
/// of them can carry Argon2, so a silent downgrade is a security regression that no
/// round-trip test would otherwise notice.
/// </remarks>
public readonly record struct KdbxHeader
{
    /// <summary>Major format version. 4 for KDBX4.</summary>
    public required ushort FormatMajorVersion { get; init; }

    /// <summary>Minor format version.</summary>
    public required ushort FormatMinorVersion { get; init; }

    /// <summary>
    /// Reads the format signature and version from a KDBX file without decrypting it.
    /// </summary>
    /// <param name="path">Path of the file to inspect.</param>
    /// <returns>The file's format version.</returns>
    /// <exception cref="VaultException">
    /// The file is shorter than a KDBX header, or does not carry the KDBX signature.
    /// </exception>
    public static KdbxHeader Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Span<byte> prefix = stackalloc byte[12];
        using (FileStream stream = File.OpenRead(path))
        {
            if (stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false) < prefix.Length)
            {
                throw new VaultException($"'{path}' is too short to be a KDBX file.");
            }
        }

        uint signature1 = BinaryPrimitives.ReadUInt32LittleEndian(prefix[..4]);
        uint signature2 = BinaryPrimitives.ReadUInt32LittleEndian(prefix[4..8]);

        if (signature1 != KdbxFormat.FileSignature1 || signature2 != KdbxFormat.FileSignature2)
        {
            throw new VaultException(
                $"'{path}' is not a KDBX file (signature {signature1:X8} {signature2:X8}).");
        }

        return new KdbxHeader
        {
            FormatMinorVersion = BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..10]),
            FormatMajorVersion = BinaryPrimitives.ReadUInt16LittleEndian(prefix[10..12]),
        };
    }
}
