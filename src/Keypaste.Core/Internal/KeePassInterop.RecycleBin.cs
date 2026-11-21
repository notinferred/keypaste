extern alias KeePass;

using KeePass::KeePassLib;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>What KeePass and KeePassXC call the recycle bin, and so does keypaste.</summary>
    internal const string RecycleBinName = "Recycle Bin";

    /// <summary>Whether a delete in this vault moves the entry to the recycle bin.</summary>
    /// <remarks>
    /// The vault's own setting, not keypaste's. KeePassXC writes it, a person can turn it off
    /// there, and a vault whose owner asked for no recycle bin does not get one because keypaste
    /// would prefer the safety. A caller that must word a confirmation before the act reads this;
    /// <see cref="RemoveEntry"/> reports what actually happened.
    /// </remarks>
    internal bool RecyclesDeletedEntries => _database.RecycleBinEnabled;

    private bool IsInBin(PwGroup group)
    {
        PwGroup? bin = Bin();

        for (PwGroup? at = group; at is not null; at = at.ParentGroup)
        {
            if (ReferenceEquals(at, bin))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns everything in the recycle bin, or an empty list when there is none.</summary>
    internal IReadOnlyList<RecycledEntry> ReadRecycled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Bin() is not { } bin)
        {
            return [];
        }

        List<RecycledEntry> rows = [];
        CollectRecycled(bin, bin, rows);
        return rows;
    }

    /// <summary>Puts a recycled entry back where it was deleted from.</summary>
    /// <param name="id">The recycled entry.</param>
    /// <param name="restored">The name the entry answers to again, or null when nothing moved.</param>
    /// <returns>What happened to the entry.</returns>
    /// <remarks>
    /// An entry that went into the bin inside a group goes back to the path it had, and each group on
    /// that path the vault no longer has is made again, named and iconed as the recycled one, so the
    /// rest of that group stays in the bin until it is restored too. It keeps its own title even where
    /// another item has it, so one of a pair comes back as one of a pair.
    /// </remarks>
    internal RestoreOutcome RestoreRecycled(RecycledEntryId id, out EntryName? restored)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        restored = null;

        if (Bin() is not { } bin || LocateRecycled(bin, id) is not { } found)
        {
            return RestoreOutcome.NothingMatched;
        }

        var place = OriginalPlaceOf(found.Entry, bin);

        var target = new EntryName(place?.Path ?? string.Empty, ReadField(found.Entry, PwDefs.TitleField));

        // keypaste's own groups find their records by title, so only there does a taken name refuse.
        if (ReservedGroups.IsReserved(target.GroupPath) && Matches(target).Count != 0)
        {
            return RestoreOutcome.DestinationOccupied;
        }

        PwGroup destination = place is { } known ? Recreate(known.Parent, known.Recycled, bin) : _database.RootGroup;

        found.Group.Entries.Remove(found.Entry);
        found.Entry.PreviousParentGroup = PwUuid.Zero;
        destination.AddEntry(found.Entry, true, true);
        restored = target;

        return place is null ? RestoreOutcome.RestoredToRoot : RestoreOutcome.Restored;
    }

    /// <summary>Removes one recycled entry and its history for good.</summary>
    /// <returns>Whether an entry was removed.</returns>
    internal bool PurgeRecycled(RecycledEntryId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Bin() is not { } bin || LocateRecycled(bin, id) is not { } found)
        {
            return false;
        }

        found.Group.Entries.Remove(found.Entry);
        _database.DeletedObjects.Add(new PwDeletedObject(found.Entry.Uuid, DateTime.UtcNow));
        return true;
    }

    /// <summary>Removes everything in the recycle bin for good.</summary>
    /// <param name="changed">Whether the bin held anything, a group with no entry included.</param>
    /// <returns>The number of entries removed.</returns>
    internal int EmptyRecycleBin(out bool changed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        changed = false;

        if (Bin() is not { } bin)
        {
            return 0;
        }

        changed = bin.Entries.UCount > 0 || bin.Groups.UCount > 0;
        var removed = (int)bin.GetEntriesCount(true);

        // Tombstones every entry and every subgroup it removes, which is what this bin's
        // contents have earned: they are being deleted, not moved.
        bin.DeleteAllObjects(_database);
        return removed;
    }

    /// <summary>The vault's recycle bin, or null when it has none.</summary>
    /// <remarks>
    /// <para>
    /// <b>One exclusion, at the traversals, rather than a filter at each caller.</b> Everything
    /// downstream — MCP listing, credential release, env injection, <c>keypaste ls</c> and the
    /// desktop entry list — reads through <see cref="ReadEntries"/>, <see cref="ReadGroupPaths"/>
    /// and <see cref="Locate"/>. Filtering there means a deleted credential cannot be released by
    /// a route somebody forgot to update, which is the failure mode a per-caller filter has.
    /// </para>
    /// <para>
    /// The enabled flag is not consulted here. It decides what a delete does; it does not make the
    /// entries already in a bin live again, and a vault whose owner turned the bin off after using
    /// it must not start serving what is in there.
    /// </para>
    /// </remarks>
    private PwGroup? Bin()
    {
        return _database.RecycleBinUuid.IsZero
            ? null
            : _database.RootGroup.FindGroup(_database.RecycleBinUuid, true);
    }

    /// <summary>The vault's recycle bin, creating it when there is none.</summary>
    /// <remarks>
    /// Named, iconed and configured as KeePass and KeePassXC create it, so the group a person sees
    /// there is the one they expect rather than a keypaste invention. A RecycleBinUuid naming a
    /// group that no longer exists is replaced, which is how KeePass treats it too.
    /// </remarks>
    private PwGroup EnsureBin()
    {
        if (Bin() is { } existing)
        {
            return existing;
        }

        var bin = new PwGroup(true, true, RecycleBinName, PwIcon.TrashBin)
        {
            EnableAutoType = false,
            EnableSearching = false,
        };

        _database.RootGroup.AddGroup(bin, true);
        _database.RecycleBinUuid = bin.Uuid;
        _database.RecycleBinChanged = DateTime.UtcNow;
        return bin;
    }

    private void CollectRecycled(PwGroup group, PwGroup bin, List<RecycledEntry> rows)
    {
        foreach (PwEntry entry in group.Entries)
        {
            rows.Add(new RecycledEntry(
                RecycledEntryId.FromUuidHex(entry.Uuid.ToHexString()),
                ReadField(entry, PwDefs.TitleField),
                OriginalPlaceOf(entry, bin)?.Path,
                DeletedAt(entry, bin)));
        }

        foreach (PwGroup child in group.Groups)
        {
            CollectRecycled(child, bin, rows);
        }
    }

    /// <summary>
    /// Where a recycled entry goes back to: the live group it was deleted from and no group to make again,
    /// or, for one that went into the bin inside a group, the live group that group was deleted from and
    /// every recycled group down to the entry's, with the path the entry had. Null when neither is known.
    /// </summary>
    private (PwGroup Parent, List<PwGroup> Recycled, string Path)? OriginalPlaceOf(PwEntry entry, PwGroup bin)
    {
        List<PwGroup> recycled = [];

        for (PwGroup? group = entry.ParentGroup; group is not null && !ReferenceEquals(group, bin); group = group.ParentGroup)
        {
            recycled.Insert(0, group);
        }

        PwUuid from = recycled.Count == 0 ? entry.PreviousParentGroup : recycled[0].PreviousParentGroup;

        if (LiveGroup(from) is not { } parent || PathOf(parent) is not { } path)
        {
            return null;
        }

        return (parent, recycled, recycled.Aggregate(path, (joined, group) => ChildPath(joined, group.Name)));
    }

    /// <summary>When an entry went into the bin: the latest move of it or of any group holding it there.</summary>
    private static DateTime DeletedAt(PwEntry entry, PwGroup bin)
    {
        DateTime at = entry.LocationChanged;

        for (PwGroup? group = entry.ParentGroup; group is not null && !ReferenceEquals(group, bin); group = group.ParentGroup)
        {
            if (group.LocationChanged > at)
            {
                at = group.LocationChanged;
            }
        }

        return at;
    }

    /// <summary>The group under <paramref name="parent"/> named as each recycled group in turn, making each one the vault no longer has.</summary>
    private static PwGroup Recreate(PwGroup parent, List<PwGroup> recycled, PwGroup bin)
    {
        PwGroup current = parent;

        foreach (PwGroup gone in recycled)
        {
            PwGroup? next = current.Groups.FirstOrDefault(child =>
                !ReferenceEquals(child, bin) && string.Equals(child.Name, gone.Name, StringComparison.Ordinal));

            if (next is null)
            {
                next = new PwGroup(true, true, gone.Name, gone.IconId) { CustomIconUuid = gone.CustomIconUuid };
                current.AddGroup(next, true);
            }

            current = next;
        }

        return current;
    }

    private static (PwGroup Group, PwEntry Entry)? LocateRecycled(PwGroup bin, RecycledEntryId id)
    {
        foreach (PwEntry candidate in bin.Entries)
        {
            if (string.Equals(candidate.Uuid.ToHexString(), id.UuidHex, StringComparison.Ordinal))
            {
                return (bin, candidate);
            }
        }

        foreach (PwGroup child in bin.Groups)
        {
            if (LocateRecycled(child, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The live group a recycled object was deleted from, by its <c>PreviousParentGroup</c>, or null when there is none.</summary>
    /// <remarks>
    /// Null covers three cases a restore cannot tell apart and does not need to: the vault records
    /// no previous parent, the group it names is gone, and the group it names is itself recycled.
    /// </remarks>
    private PwGroup? LiveGroup(PwUuid previousParent)
    {
        if (previousParent.IsZero)
        {
            return null;
        }

        PwGroup? original = _database.RootGroup.FindGroup(previousParent, true);
        if (original is null)
        {
            return null;
        }

        return Bin() is { } bin && (ReferenceEquals(original, bin) || original.IsContainedIn(bin))
            ? null
            : original;
    }

    /// <summary>Turns the recycle bin on or off. A test seam; KeePassXC owns this setting.</summary>
    internal void SetRecyclesDeletedEntries(bool recycles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _database.RecycleBinEnabled = recycles;
        _database.RecycleBinChanged = DateTime.UtcNow;
    }
}
