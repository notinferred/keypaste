using System.Xml;

namespace Keypaste.Core;

/// <summary>Which of KeePass's four keyfile shapes a file turned out to be.</summary>
/// <remarks>
/// <para>
/// The form matters to a person, not only to the reader. Three of these are keyfiles somebody
/// deliberately made; the fourth is any file at all, keyed by the hash of its contents — so a
/// document that gets edited, a photo that gets re-encoded or a file a synchroniser rewrites stops
/// opening the vault, permanently and with no warning beforehand. Naming the form is what lets a
/// front end say so.
/// </para>
/// <para>
/// The order is KeePassLib's, in <c>KcpKeyFile.LoadKeyFile</c>: XML first, then a 32-byte file,
/// then a 64-byte file that is all hex, then the hash of whatever is left. A consequence worth
/// knowing is that an arbitrary file of exactly 32 or 64 bytes is read as <see cref="Binary32"/>
/// or <see cref="Hex64"/> rather than hashed.
/// </para>
/// </remarks>
public enum KeyfileForm
{
    /// <summary>A KeePass XML keyfile, the only form keypaste itself writes.</summary>
    Xml = 0,

    /// <summary>Exactly 32 bytes, used as the key material directly.</summary>
    Binary32 = 1,

    /// <summary>Exactly 64 bytes, all hexadecimal digits, decoded to 32.</summary>
    Hex64 = 2,

    /// <summary>Any other file, keyed by the SHA-256 of its contents.</summary>
    HashedFile = 3,
}

/// <summary>Why a file could not be used as a keyfile.</summary>
/// <remarks>
/// Values rather than exceptions, for the reason <see cref="OrganizeOutcome"/> gives: every one of
/// these is an ordinary thing a person does while pointing at a file, and each front end words it.
/// </remarks>
public enum KeyfileOutcome
{
    /// <summary>The file is readable and was classified.</summary>
    Accepted = 0,

    /// <summary>Nothing is at that path.</summary>
    Missing = 1,

    /// <summary>It is there and could not be read.</summary>
    Unreadable = 2,

    /// <summary>It is there and empty, which no keyfile is.</summary>
    Empty = 3,

    /// <summary>It is a KeePass database. A vault is not its own second factor.</summary>
    IsAVault = 4,
}

/// <summary>What <see cref="VaultKeyfile.Inspect"/> made of a file.</summary>
/// <param name="Outcome">Whether it can be used, and why not when it cannot.</param>
/// <param name="Form">The shape it is, meaningful only once <paramref name="Outcome"/> accepted it.</param>
public readonly record struct KeyfileInspection(KeyfileOutcome Outcome, KeyfileForm Form)
{
    /// <summary>Whether this file can be used as a keyfile.</summary>
    public bool Accepted => Outcome == KeyfileOutcome.Accepted;

    /// <summary>Whether opening with this file is one edit away from losing the vault.</summary>
    public bool IsFragile => Accepted && Form == KeyfileForm.HashedFile;
}

/// <summary>
/// Reads a candidate keyfile far enough to say what it is, and refuses the files that are not one.
/// </summary>
/// <remarks>
/// <para>
/// <b>This derives nothing.</b> docs/PRODUCT.md law 3.6 forbids writing cryptography, and all four
/// forms are already implemented by the vendored <c>KeePassLib.Keys.KcpKeyFile</c>, which is what
/// actually turns the file into key material. What is here is classification and refusal: which
/// shape the bytes are, and whether they are usable at all. The two must agree, so the rules below
/// are read off <c>KcpKeyFile.LoadKeyFile</c> rather than invented, and a test pins them together.
/// </para>
/// <para>
/// <b>Why classify at all, when the library takes any file?</b> Because "any file" is the dangerous
/// answer. keypaste opens a vault keyed to an arbitrary hashed file, because KeePassXC made such
/// vaults for years and refusing them would strand somebody's data — but it says so, and it will
/// never create one. Knowing the form is what makes both halves of that possible.
/// </para>
/// </remarks>
public static class VaultKeyfile
{
    /// <summary>The KDBX file signatures, so a vault is never accepted as its own keyfile.</summary>
    /// <remarks>
    /// The same three pairs <c>KcpKeyFile</c> checks under its <c>bThrowIfDbFile</c> flag: current,
    /// the pre-release header and the KDB1 header. Checked here as well as there so the refusal is
    /// a value with a name rather than an <c>InvalidDataException</c> carrying a localized string.
    /// </remarks>
    private static readonly uint[][] _vaultSignatures =
    [
        [0x9AA2D903u, 0xB54BFB67u],
        [0x9AA2D903u, 0xB54BFB66u],
        [0x9AA2D903u, 0xB54BFB65u],
    ];

    /// <summary>Reads <paramref name="path"/> and says what kind of keyfile it is.</summary>
    /// <remarks>
    /// Reads the whole file, because every form is decided by content and the largest of them is
    /// 64 bytes or a short XML document. Nothing is retained: the bytes are classified and dropped.
    /// </remarks>
    public static KeyfileInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new KeyfileInspection(KeyfileOutcome.Missing, default);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new KeyfileInspection(KeyfileOutcome.Unreadable, default);
        }

        if (bytes.Length == 0)
        {
            return new KeyfileInspection(KeyfileOutcome.Empty, default);
        }

        return IsAVault(bytes)
            ? new KeyfileInspection(KeyfileOutcome.IsAVault, default)
            : new KeyfileInspection(KeyfileOutcome.Accepted, FormOf(bytes));
    }

    /// <summary>Classifies bytes already known to be a usable file.</summary>
    /// <remarks>
    /// The order is <c>KcpKeyFile.LoadKeyFile</c>'s and must stay that way, or keypaste would name
    /// one form while the library keyed the vault with another.
    /// </remarks>
    private static KeyfileForm FormOf(byte[] bytes)
    {
        if (IsXml(bytes))
        {
            return KeyfileForm.Xml;
        }

        if (bytes.Length == 32)
        {
            return KeyfileForm.Binary32;
        }

        return bytes.Length == 64 && IsHex(bytes) ? KeyfileForm.Hex64 : KeyfileForm.HashedFile;
    }

    /// <summary>Whether these bytes are a KeePass XML keyfile.</summary>
    /// <remarks>
    /// A structural check rather than a parse: the library parses it, and a second parser here
    /// would be a second thing to keep in step. What is needed is only whether KeePassLib takes its
    /// XML branch, and it takes that branch for a document whose root element is <c>KeyFile</c>.
    /// External entities are refused because this file is chosen by whoever is at the keyboard and
    /// a keyfile has no business naming a DTD.
    /// </remarks>
    private static bool IsXml(byte[] bytes)
    {
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using XmlReader reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    CloseInput = false,
                });

            return reader.MoveToContent() == XmlNodeType.Element && reader.Name == "KeyFile";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsHex(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            bool digit = (b >= (byte)'0' && b <= (byte)'9')
                || (b >= (byte)'a' && b <= (byte)'f')
                || (b >= (byte)'A' && b <= (byte)'F');

            if (!digit)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAVault(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return false;
        }

        uint first = BitConverter.ToUInt32(bytes, 0);
        uint second = BitConverter.ToUInt32(bytes, 4);

        foreach (uint[] signature in _vaultSignatures)
        {
            if (first == signature[0] && second == signature[1])
            {
                return true;
            }
        }

        return false;
    }
}
