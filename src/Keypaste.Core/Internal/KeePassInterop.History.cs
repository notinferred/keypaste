extern alias KeePass;

using System.Buffers;
using KeePass::KeePassLib;
using KeePass::KeePassLib.Utility;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>Returns the history of the one entry with this name, newest first, or null when
    /// the vault holds no entry of that name.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<EntryRevision>? ReadHistory(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        uint[] order = HistoryNewestFirst(found.Entry);
        var revisions = new EntryRevision[order.Length];

        for (var index = 0; index < order.Length; index++)
        {
            PwEntry revision = found.Entry.History.GetAt(order[index]);
            revisions[index] = new EntryRevision(
                index, revision.LastModificationTime, Read(revision, PathOf(found.Group)!), TagsOf(revision), FieldsOf(revision));
        }

        return revisions;
    }

    /// <summary>When the one entry with this name was created and last modified, and when it expires, or null when there is none.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal EntryTimes? ReadTimes(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        return new EntryTimes(
            new DateTimeOffset(DateTime.SpecifyKind(found.Entry.CreationTime, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(found.Entry.LastModificationTime, DateTimeKind.Utc)),
            found.Entry.Expires ? new DateTimeOffset(TimeUtil.ToUtc(found.Entry.ExpiryTime, true)) : null);
    }

    /// <summary>Puts the revision at this position in <see cref="ReadHistory"/>'s order back as the
    /// entry's current values.</summary>
    /// <returns>The number of entries restored: 0 if nothing matched, otherwise 1.</returns>
    /// <remarks>
    /// The value being replaced becomes a history item rather than being lost,
    /// which is what <c>RestoreFromBackup</c> does when it is given the database's history settings.
    /// The entry is mutated in place, so it keeps its UUID, but <c>RestoreFromBackup</c> assigns the
    /// revision's strings, attachments, custom data, tags and auto-type: the entry becomes the revision
    /// whole, as it does in KeePass and KeePassXC, and what it drops is in the revision just made (9.4). Since
    /// that puts back the revision's project tags, a front end first asks what <see cref="ProjectTagChange.Restoring"/>
    /// says whenever they differ from the entry's.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The entry has no revision at that position. Nothing is changed.
    /// </exception>
    internal int RestoreRevision(EntryName name, int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        // Resolved before the restore's own backup can evict the item at this raw position.
        uint[] order = HistoryNewestFirst(found.Entry);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, order.Length);

        PwEntry pwEntry = found.Entry;

        // A revision is values, never identity, so the restore keeps the current title.
        string title = ReadField(pwEntry, PwDefs.TitleField);

        pwEntry.RestoreFromBackup(order[index], _database);

        SetField(pwEntry, PwDefs.TitleField, title);

        // The restore assigned the revision's old times back.
        MarkEdited(pwEntry);
        return 1;
    }

    /// <summary>The raw history positions of one entry, newest first.</summary>
    /// <remarks>
    /// <para>
    /// The one ordering rule, so a read and a restore cannot reach different answers about the same
    /// index — the shape of the duplicate-name defect in entry identity.
    /// </para>
    /// <para>
    /// <b>The tiebreak is the rule, not a detail.</b> KDBX4 stores a timestamp to the second, so
    /// several revisions written inside one second come back from a reopened file with identical
    /// modification times; sorting is stable, so without the position they would be returned oldest
    /// first — the exact inversion. A backup is appended at the end of the list and read back in
    /// document order, so the later position is the later write.
    /// </para>
    /// </remarks>
    private static uint[] HistoryNewestFirst(PwEntry entry)
    {
        return Enumerable.Range(0, (int)entry.History.UCount)
            .OrderByDescending(position => entry.History.GetAt((uint)position).LastModificationTime)
            .ThenByDescending(position => position)
            .Select(position => (uint)position)
            .ToArray();
    }

    /// <summary>Keeps the entry as it is as its newest revision, as <c>CreateBackup(_database)</c> does, and says whether the vault's history limits kept it.</summary>
    /// <remarks>
    /// KeePassLib drops the oldest revisions while the history holds more than the vault's item limit or bytes than its size limit,
    /// 6 MiB unless the vault says otherwise, attachments counted in every revision that holds them, so a revision larger than
    /// the size limit is dropped at once, as KeePassXC drops it.
    /// </remarks>
    private bool BackUp(PwEntry entry)
    {
        entry.CreateBackup(null);
        PwEntry revision = entry.History.GetAt(entry.History.UCount - 1);
        entry.MaintainBackups(_database);
        return entry.History.IndexOf(revision) >= 0;
    }
}
