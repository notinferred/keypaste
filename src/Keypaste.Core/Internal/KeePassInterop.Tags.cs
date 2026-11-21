extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Utility;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>The one entry's own tags, or null when there is no such entry.</summary>
    /// <remarks>Never <c>GetTagsInherited</c>: a group's tags are not the entry's (C.1a).</remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<string>? ReadTags(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found ? TagsOf(found.Entry) : null;
    }

    /// <summary>Every live entry carrying a tag of its own, by the traversal <see cref="Collect"/> uses.</summary>
    internal IReadOnlyList<EntryTags> ReadTags()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<EntryTags> tagged = [];
        CollectTags(_database.RootGroup, string.Empty, tagged, Bin());
        return tagged;
    }

    /// <summary>Adds one tag as an edit with one history revision.</summary>
    /// <returns>0 when there is no such entry or it carries the tag already, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int AddTag(EntryName name, string tag)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found || HasTag(found.Entry, tag))
        {
            return 0;
        }

        found.Entry.CreateBackup(_database);
        AddTagTo(found.Entry, tag);
        MarkEdited(found.Entry);
        return 1;
    }

    /// <summary>Removes every one of the tags the entry carries, as one edit with one history revision.</summary>
    /// <returns>0 when there is no such entry or it carries none of them, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int RemoveTags(EntryName name, IReadOnlyList<string> tags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found || !tags.Any(found.Entry.HasTag))
        {
            return 0;
        }

        found.Entry.CreateBackup(_database);

        foreach (string tag in tags)
        {
            RemoveTagFrom(found.Entry, tag);
        }

        MarkEdited(found.Entry);
        return 1;
    }

    /// <summary>Held for every read and write of a KeePassLib tag list, since Security's scan reads tags on a worker thread while the window reads and writes them.</summary>
    private static readonly Lock _tagsGate = new();

    /// <summary>When the version that added <paramref name="tag"/> to the entry was saved, or null when every version its history keeps carries it.</summary>
    private static DateTimeOffset? TaggedSince(PwEntry entry, string tag)
    {
        var since = entry.LastModificationTime;

        foreach (var position in HistoryNewestFirst(entry))
        {
            var version = entry.History.GetAt(position);

            if (!HasTag(version, tag))
            {
                return new DateTimeOffset(TimeUtil.ToUtc(since, true));
            }

            since = version.LastModificationTime;
        }

        return null;
    }

    /// <summary>A copy of the entry's tags, so no other thread's write reaches the list a caller walks.</summary>
    private static List<string> TagsOf(PwEntry entry)
    {
        lock (_tagsGate)
        {
            return [.. entry.Tags];
        }
    }

    private static List<string> TagsOf(PwGroup group)
    {
        lock (_tagsGate)
        {
            return [.. group.Tags];
        }
    }

    private static bool HasTag(PwEntry entry, string tag)
    {
        lock (_tagsGate)
        {
            return entry.HasTag(tag);
        }
    }

    private static bool AddTagTo(PwEntry entry, string tag)
    {
        lock (_tagsGate)
        {
            var added = entry.AddTag(tag);

            // Reading normalizes the list while the lock is held, so a later read outside it writes nothing.
            _ = entry.Tags;
            return added;
        }
    }

    private static bool RemoveTagFrom(PwEntry entry, string tag)
    {
        lock (_tagsGate)
        {
            return entry.RemoveTag(tag);
        }
    }

    private static void Retag(PwEntry entry, List<string> tags)
    {
        lock (_tagsGate)
        {
            entry.Tags = tags;
            _ = entry.Tags;
        }
    }

    private static HashSet<(string Project, string Environment)> Memberships(IEnumerable<string> tags) =>
        [.. tags.Select(ProjectTag.Read)
            .Where(read => read.Kind == ProjectTagKind.Member)
            .Select(read => (read.Project!, read.Environment!))];

    /// <summary>Whether two versions differ in anything but their tags, times and history.</summary>
    private static bool ChangedMoreThanTags(PwEntry before, PwEntry after)
    {
        var retagged = before.CloneDeep();
        Retag(retagged, TagsOf(after));

        // A merge's mark on a version it kept is no change to the version.
        retagged.CustomData.Remove(PwEntry.KeptVersionKey);
        if (after.CustomData.Exists(PwEntry.KeptVersionKey))
        {
            retagged.CustomData.Set(PwEntry.KeptVersionKey, after.CustomData.Get(PwEntry.KeptVersionKey));
        }

        return !retagged.EqualsEntry(
            after,
            PwCompareOptions.IgnoreParentGroup | PwCompareOptions.IgnoreTimes | PwCompareOptions.IgnoreHistory,
            MemProtCmpMode.Full);
    }

    private static void CollectTags(PwGroup group, string groupPath, List<EntryTags> tagged, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            if (TagsOf(entry) is { Count: > 0 } tags)
            {
                tagged.Add(new EntryTags(new EntryName(groupPath, ReadField(entry, PwDefs.TitleField)), tags));
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectTags(child, ChildPath(groupPath, child.Name), tagged, bin);
            }
        }
    }
}
