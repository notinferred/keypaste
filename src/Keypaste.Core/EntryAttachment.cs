using System.Globalization;

namespace Keypaste.Core;

/// <summary>One attachment of an entry, named and sized and never read.</summary>
/// <param name="Name">The attachment's name, as the file holds it.</param>
/// <param name="Size">Its length in bytes.</param>
public sealed record EntryAttachment(string Name, long Size)
{
    /// <summary>The most bytes keypaste attaches: 5 MiB, the size above which KeePassXC asks before attaching a file (D-0533).</summary>
    /// <remarks>Only adding and replacing are held to it; a larger attachment another program made is listed, kept, removed and saved.</remarks>
    public const int MaximumSize = 5 * 1024 * 1024;

    /// <summary>The size as a person reads it: <c>1 byte</c>, <c>812 bytes</c>, <c>4.0 KiB</c> or <c>2.5 MiB</c>.</summary>
    public string SizeText => Size switch
    {
        1 => "1 byte",
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{Size} bytes"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{Size / 1024.0:0.0} KiB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Size / (1024.0 * 1024.0):0.0} MiB"),
    };
}

/// <summary>What happened when an attachment was saved to a file.</summary>
public enum AttachmentSaveOutcome
{
    /// <summary>The file holds the attachment's bytes, at the path asked for.</summary>
    Saved = 0,

    /// <summary>No entry answers to that name, or it has no attachment so named; nothing was written.</summary>
    NotFound = 1,

    /// <summary>Something is already at that path, and it was not touched.</summary>
    PathAlreadyExists = 2,

    /// <summary>The path was reachable and the write still failed; nothing was left at it.</summary>
    Failed = 3,
}
