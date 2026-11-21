extern alias KeePass;

using KeePass::KeePassLib;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>
    /// What <see cref="WriteRecord"/> for this name can change, taken before it: each entry it may edit or
    /// move, where it was and a copy of it, the groups at the path, every group's times and the tombstones.
    /// </summary>
    /// <param name="name">Where the record belongs, and its title.</param>
    /// <param name="idOf">The UUID of the entry for a title.</param>
    /// <returns>What <see cref="Restore(RecordSnapshot)"/> puts back.</returns>
    internal RecordSnapshot Snapshot(EntryName name, Func<string, Guid> idOf)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IEnumerable<PwEntry> candidates = InNamedGroups(name.GroupPath).Select(found => found.Entry);

        if (_database.RootGroup.FindEntry(ToPwUuid(idOf(name.Title)), true) is { } kept)
        {
            candidates = candidates.Prepend(kept);
        }

        List<PwGroup> groups = [_database.RootGroup, .. _database.RootGroup.GetGroups(true)];

        return new RecordSnapshot(
            [
                .. candidates
                    .Distinct<PwEntry>(ReferenceEqualityComparer.Instance)
                    .Select(entry => new EntrySnapshot(entry, entry.ParentGroup, entry.ParentGroup.Entries.IndexOf(entry), entry.CloneDeep())),
            ],
            name.GroupPath,
            new HashSet<PwGroup>(NamedLevels(name.GroupPath).SelectMany(level => level), ReferenceEqualityComparer.Instance),
            [.. groups.Select(group => new GroupTimes(group, group.LastModificationTime, group.LastAccessTime, group.UsageCount))],
            [.. _database.DeletedObjects]);
    }

    /// <summary>Puts back what a record write changed since <see cref="Snapshot"/>, for a write no save could keep.</summary>
    /// <param name="snapshot">What the vault held before the write.</param>
    internal void Restore(RecordSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (EntrySnapshot held in snapshot.Entries)
        {
            if (!ReferenceEquals(held.Entry.ParentGroup, held.Parent))
            {
                held.Entry.ParentGroup?.Entries.Remove(held.Entry);
                held.Parent.AddEntry(held.Entry, true, false);
                held.Parent.Entries.Remove(held.Entry);
                held.Parent.Entries.Insert((uint)Math.Clamp(held.Index, 0, (int)held.Parent.Entries.UCount), held.Entry);
            }

            held.Entry.AssignProperties(held.Copy, false, true, true);
        }

        HashSet<PwEntry> known = new(snapshot.Entries.Select(held => held.Entry), ReferenceEqualityComparer.Instance);

        foreach (PwGroup group in NamedLevels(snapshot.Path)[^1])
        {
            foreach (PwEntry made in group.Entries.Where(entry => !known.Contains(entry)).ToList())
            {
                group.Entries.Remove(made);
            }
        }

        // Deepest first, so a group the write made inside another it made goes before its parent.
        foreach (PwGroup made in NamedLevels(snapshot.Path).SelectMany(level => level).Where(group => !snapshot.Groups.Contains(group)).Reverse().ToList())
        {
            made.ParentGroup?.Groups.Remove(made);
        }

        foreach (GroupTimes times in snapshot.Times)
        {
            times.Group.LastModificationTime = times.Modified;
            times.Group.LastAccessTime = times.Accessed;
            times.Group.UsageCount = times.Used;
        }

        _database.DeletedObjects.Clear();

        foreach (PwDeletedObject tombstone in snapshot.Deleted)
        {
            _database.DeletedObjects.Add(tombstone);
        }
    }

    /// <summary>
    /// What any edit can change, taken before one: every entry, where it sits and a copy of it, every
    /// group and its times, the tombstones and where the recycle bin is.
    /// </summary>
    /// <returns>What <see cref="Restore(VaultSnapshot)"/> puts back.</returns>
    internal VaultSnapshot Checkpoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<PwGroup> groups = [_database.RootGroup, .. _database.RootGroup.GetGroups(true)];

        return new VaultSnapshot(
            [
                .. _database.RootGroup.GetEntries(true)
                    .Select(entry => new EntrySnapshot(entry, entry.ParentGroup, entry.ParentGroup.Entries.IndexOf(entry), entry.CloneDeep())),
            ],
            new HashSet<PwGroup>(groups, ReferenceEqualityComparer.Instance),
            [.. groups.Select(group => new GroupTimes(group, group.LastModificationTime, group.LastAccessTime, group.UsageCount))],
            [.. _database.DeletedObjects],
            _database.RecycleBinUuid,
            _database.RecycleBinChanged);
    }

    /// <summary>Puts the vault back as <see cref="Checkpoint"/> found it, for an edit no save could keep.</summary>
    /// <param name="checkpoint">What the vault held before the edit.</param>
    internal void Restore(VaultSnapshot checkpoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HashSet<PwEntry> known = new(checkpoint.Entries.Select(held => held.Entry), ReferenceEqualityComparer.Instance);

        foreach (PwEntry made in _database.RootGroup.GetEntries(true).Where(entry => !known.Contains(entry)).ToList())
        {
            made.ParentGroup?.Entries.Remove(made);
        }

        foreach (EntrySnapshot held in checkpoint.Entries)
        {
            // A deleted entry keeps its parent's reference while no longer among its entries, so where it
            // sits is read from the group, not from the entry.
            if (!ReferenceEquals(held.Entry.ParentGroup, held.Parent) || held.Parent.Entries.IndexOf(held.Entry) < 0)
            {
                held.Entry.ParentGroup?.Entries.Remove(held.Entry);
                held.Parent.AddEntry(held.Entry, true, false);
                held.Parent.Entries.Remove(held.Entry);
                held.Parent.Entries.Insert((uint)Math.Clamp(held.Index, 0, (int)held.Parent.Entries.UCount), held.Entry);
            }

            held.Entry.AssignProperties(held.Copy, false, true, true);
        }

        // Deepest first, so a group the edit made inside another it made goes before its parent.
        foreach (PwGroup made in _database.RootGroup.GetGroups(true).Where(group => !checkpoint.Groups.Contains(group)).Reverse().ToList())
        {
            made.ParentGroup?.Groups.Remove(made);
        }

        foreach (GroupTimes times in checkpoint.Times)
        {
            times.Group.LastModificationTime = times.Modified;
            times.Group.LastAccessTime = times.Accessed;
            times.Group.UsageCount = times.Used;
        }

        _database.DeletedObjects.Clear();

        foreach (PwDeletedObject tombstone in checkpoint.Deleted)
        {
            _database.DeletedObjects.Add(tombstone);
        }

        _database.RecycleBinUuid = checkpoint.RecycleBin;
        _database.RecycleBinChanged = checkpoint.RecycleBinChanged;
    }

    /// <summary>What a record write can change, as <see cref="Snapshot"/> took it before the write.</summary>
    /// <param name="Entries">Each entry the write may edit or move.</param>
    /// <param name="Path">The record's group path.</param>
    /// <param name="Groups">The groups at each step of that path then.</param>
    /// <param name="Times">Every group's times then, which an edit touches up to the root.</param>
    /// <param name="Deleted">The vault's tombstones then.</param>
    internal sealed record RecordSnapshot(
        IReadOnlyList<EntrySnapshot> Entries,
        string Path,
        IReadOnlySet<PwGroup> Groups,
        IReadOnlyList<GroupTimes> Times,
        IReadOnlyList<PwDeletedObject> Deleted);

    /// <summary>What any edit can change, as <see cref="Checkpoint"/> took it before the edit.</summary>
    /// <param name="Entries">Every entry, the recycled ones included.</param>
    /// <param name="Groups">Every group then.</param>
    /// <param name="Times">Every group's times then.</param>
    /// <param name="Deleted">The vault's tombstones then.</param>
    /// <param name="RecycleBin">The recycle bin's UUID then, zero when there was none.</param>
    /// <param name="RecycleBinChanged">When the recycle bin last changed then.</param>
    internal sealed record VaultSnapshot(
        IReadOnlyList<EntrySnapshot> Entries,
        IReadOnlySet<PwGroup> Groups,
        IReadOnlyList<GroupTimes> Times,
        IReadOnlyList<PwDeletedObject> Deleted,
        PwUuid RecycleBin,
        DateTime RecycleBinChanged);

    /// <summary>One entry as it was: where it sat, and a copy of everything in it, its history included.</summary>
    internal sealed record EntrySnapshot(PwEntry Entry, PwGroup Parent, int Index, PwEntry Copy);

    /// <summary>One group's times as they were.</summary>
    internal sealed record GroupTimes(PwGroup Group, DateTime Modified, DateTime Accessed, ulong Used);
}
