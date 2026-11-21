extern alias KeePass;

using KeePass::KeePassLib.Security;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>The one entry's attachments by name and length, in ordinal order of name; nothing is decrypted.</summary>
    /// <returns>Null when there is no such entry.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<EntryAttachment>? ReadAttachments(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found
            ? [.. found.Entry.Binaries
                .Select(pair => new EntryAttachment(pair.Key, pair.Value.Length))
                .OrderBy(attachment => attachment.Name, StringComparer.Ordinal)]
            : null;
    }

    /// <summary>A copy of one attachment's bytes, which the caller zeroes.</summary>
    /// <returns>Null when there is no such entry or attachment.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal byte[]? ReadAttachment(EntryName name, string attachment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name)?.Entry.Binaries.Get(attachment)?.ReadData();
    }

    /// <summary>Sets one attachment to the first <paramref name="length"/> bytes of <paramref name="content"/>, protected in memory, as an edit with one history revision.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="attachment">The attachment's name, which the caller has checked.</param>
    /// <param name="content">The bytes, which KeePassLib copies and the caller zeroes.</param>
    /// <param name="length">How many of them.</param>
    /// <param name="keptInHistory">Whether the vault's history limits kept the revision this edit made.</param>
    /// <returns>0 when there is no such entry, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int SetAttachment(EntryName name, string attachment, byte[] content, int length, out bool keptInHistory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        keptInHistory = false;

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        keptInHistory = BackUp(found.Entry);
        found.Entry.Binaries.Set(attachment, new ProtectedBinary(true, content, 0, length));
        MarkEdited(found.Entry);
        return 1;
    }

    /// <summary>Removes one attachment as an edit with one history revision.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="attachment">The attachment's name, matched exactly.</param>
    /// <param name="keptInHistory">Whether the vault's history limits kept the revision this edit made.</param>
    /// <returns>0 when there is no such entry or attachment, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int RemoveAttachment(EntryName name, string attachment, out bool keptInHistory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        keptInHistory = false;

        if (Locate(name) is not { } found || found.Entry.Binaries.Get(attachment) is null)
        {
            return 0;
        }

        keptInHistory = BackUp(found.Entry);
        found.Entry.Binaries.Remove(attachment);
        MarkEdited(found.Entry);
        return 1;
    }
}
