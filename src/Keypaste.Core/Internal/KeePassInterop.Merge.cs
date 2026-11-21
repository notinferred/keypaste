extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Interfaces;
using KeePass::KeePassLib.Security;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>What keypaste keeps of the vault as it is now, for a later merge to read lineage from.</summary>
    /// <param name="key">The per-unlock key the content digests are keyed with, which stays the caller's.</param>
    /// <returns>Each group's and entry's parent, <c>LocationChanged</c>, keyed content digest and whether it was recycled, and the tombstones; no plaintext.</returns>
    internal MergeBase TakeBase(byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using ContentDigests digests = new(key);
        PwGroup? bin = Bin();

        Dictionary<PwUuid, BaseRecord> groups = new();
        foreach (PwGroup group in GroupsOf(_database))
        {
            groups[group.Uuid] = new BaseRecord(group.ParentGroup?.Uuid ?? PwUuid.Zero, group.LocationChanged, digests.Of(group), IsRecycled(group, bin), []);
        }

        Dictionary<PwUuid, BaseRecord> entries = new();
        foreach (PwEntry entry in _database.RootGroup.GetEntries(true))
        {
            entries[entry.Uuid] = new BaseRecord(entry.ParentGroup.Uuid, entry.LocationChanged, digests.Of(entry), IsRecycled(entry, bin), digests.OfHistoryCopy(entry));
        }

        return new MergeBase(key, groups, entries, Deletions(_database));
    }

    /// <summary>Merges the open copy into this fresh parse of its file, which becomes the result.</summary>
    /// <param name="open">The open copy, which nothing here changes.</param>
    /// <param name="lineage">What the open copy keeps of the file it last read or wrote.</param>
    /// <param name="report">What the merge did, by name and never by value, when it merged.</param>
    /// <returns>Whether it merged; a refusal changes neither copy.</returns>
    internal VaultMergeOutcome MergeFrom(KeePassInterop open, MergeBase lineage, out VaultMergeReport? report)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(open._disposed, open);

        report = null;

        if (!open._database.RootGroup.Uuid.Equals(_database.RootGroup.Uuid))
        {
            return VaultMergeOutcome.AnotherVault;
        }

        if (HoldsDuplicateUuids(open._database) || HoldsDuplicateUuids(_database))
        {
            return VaultMergeOutcome.DuplicateUuids;
        }

        using Merger merger = new(open, this, lineage);
        report = merger.Run();
        _saveCount = new[] { _saveCount, open._saveCount }.Max();
        return VaultMergeOutcome.Merged;
    }

    /// <summary>The root group and every group beneath it, each before its own groups.</summary>
    private static List<PwGroup> GroupsOf(PwDatabase database) => [database.RootGroup, .. database.RootGroup.GetGroups(true)];

    /// <summary>Each tombstone's UUID with the latest deletion recorded for it.</summary>
    private static Dictionary<PwUuid, DateTime> Deletions(PwDatabase database)
    {
        Dictionary<PwUuid, DateTime> deletions = new();
        foreach (PwDeletedObject tombstone in database.DeletedObjects)
        {
            if (!deletions.TryGetValue(tombstone.Uuid, out DateTime known) || tombstone.DeletionTime > known)
            {
                deletions[tombstone.Uuid] = tombstone.DeletionTime;
            }
        }

        return deletions;
    }

    /// <summary>Whether an object sits in the recycle bin or in a group inside it.</summary>
    private static bool IsRecycled(IStructureItem item, PwGroup? bin) =>
        bin is not null && item.ParentGroup is { } parent && (ReferenceEquals(parent, bin) || parent.IsContainedIn(bin));

    /// <summary>Whether two of the vault's objects share a UUID, which KeePass's format forbids.</summary>
    private static bool HoldsDuplicateUuids(PwDatabase database)
    {
        HashSet<PwUuid> seen = [];
        return GroupsOf(database).Exists(group => !seen.Add(group.Uuid))
            || database.RootGroup.GetEntries(true).Any(entry => !seen.Add(entry.Uuid));
    }

    /// <summary>A time in the whole seconds KDBX keeps it in.</summary>
    private static long Second(DateTime time) => time.Ticks / TimeSpan.TicksPerSecond;

    /// <summary>The second after the latest of <paramref name="times"/>.</summary>
    private static DateTime After(IEnumerable<DateTime> times)
    {
        long second = Math.Min(times.Max(Second), (DateTime.MaxValue.Ticks / TimeSpan.TicksPerSecond) - 1);
        return new DateTime((second + 1) * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private static bool Same(byte[] digest, byte[] other) => digest.AsSpan().SequenceEqual(other);

    /// <summary>What keypaste keeps of the file it last read or wrote, for a merge's lineage.</summary>
    /// <param name="Key">The per-unlock key its digests are keyed with.</param>
    /// <param name="Groups">Each group by UUID.</param>
    /// <param name="Entries">Each entry by UUID.</param>
    /// <param name="Deletions">Each tombstone's UUID and latest deletion.</param>
    internal sealed record MergeBase(
        byte[] Key,
        IReadOnlyDictionary<PwUuid, BaseRecord> Groups,
        IReadOnlyDictionary<PwUuid, BaseRecord> Entries,
        IReadOnlyDictionary<PwUuid, DateTime> Deletions);

    /// <summary>One object as the base keeps it.</summary>
    /// <param name="Parent">Its group's UUID, zero for the root group.</param>
    /// <param name="LocationChanged">When it was last moved.</param>
    /// <param name="Digest">Its content's keyed digest.</param>
    /// <param name="Recycled">Whether it was in the recycle bin.</param>
    /// <param name="HistoryCopyDigest">An entry's digest as the version KeePassXC would keep of it; empty for a group.</param>
    internal sealed record BaseRecord(PwUuid Parent, DateTime LocationChanged, byte[] Digest, bool Recycled, byte[] HistoryCopyDigest);

    /// <summary>One version in an entry's history as a merge builds it.</summary>
    /// <param name="Entry">The version.</param>
    /// <param name="Digest">Its content's keyed digest, its second included.</param>
    /// <param name="Kept">Whether a merge moved it there, so no trimming removes it before the entry's next edit.</param>
    private sealed record HistoryVersion(PwEntry Entry, byte[] Digest, bool Kept);

    /// <summary>One merge of the open copy into a fresh parse of its file, which becomes the result.</summary>
    /// <remarks>"Mine" is the open copy, read and never changed; "theirs" is the file's, inside which the result is built.</remarks>
    private sealed class Merger : IDisposable
    {
        private readonly KeePassInterop _open;
        private readonly KeePassInterop _file;
        private readonly PwDatabase _mine;
        private readonly PwDatabase _result;
        private readonly MergeBase _base;
        private readonly ContentDigests _digests;
        private readonly Dictionary<PwUuid, PwGroup> _myGroups;
        private readonly Dictionary<PwUuid, PwEntry> _myEntries;
        private readonly HashSet<PwUuid> _theirGroups;
        private readonly Dictionary<PwUuid, PwEntry> _theirEntries;
        private readonly Dictionary<PwUuid, PwGroup> _groups;
        private readonly Dictionary<PwUuid, DateTime> _myDeletions;
        private readonly Dictionary<PwUuid, DateTime> _theirDeletions;
        private readonly HashSet<PwUuid> _theirPairs;
        private readonly PwGroup? _myBin;
        private readonly PwGroup? _theirBin;
        private readonly List<(PwEntry Entry, bool MineCurrent)> _conflicts = [];
        private readonly List<PwEntry> _combined = [];
        private readonly List<PwEntry> _taken = [];
        private readonly List<MergeKeptItem> _kept = [];
        private readonly List<(PwGroup Group, string LostName)> _renamed = [];

        internal Merger(KeePassInterop open, KeePassInterop file, MergeBase lineage)
        {
            _open = open;
            _file = file;
            _mine = open._database;
            _result = file._database;
            _base = lineage;
            _digests = new ContentDigests(lineage.Key);
            _myGroups = GroupsOf(_mine).ToDictionary(group => group.Uuid);
            _myEntries = _mine.RootGroup.GetEntries(true).ToDictionary(entry => entry.Uuid);
            _groups = GroupsOf(_result).ToDictionary(group => group.Uuid);
            _theirGroups = [.. _groups.Keys];
            _theirEntries = _result.RootGroup.GetEntries(true).ToDictionary(entry => entry.Uuid);
            _myDeletions = Deletions(_mine);
            _theirDeletions = Deletions(_result);
            _theirPairs = [.. _theirDeletions.Keys.Where(id => _theirGroups.Contains(id) || _theirEntries.ContainsKey(id))];
            _myBin = open.Bin();
            _theirBin = file.Bin();
        }

        public void Dispose() => _digests.Dispose();

        /// <summary>Builds the result and says what it did.</summary>
        internal VaultMergeReport Run()
        {
            byte[] before = StateOf(_result);

            MergeGroups();
            AdoptMyBin();
            MergeEntries();
            EmptyMyBin();
            RemoveDeletedGroups();
            MergeDeletions();
            CarryIcons();

            List<MergeConflict> conflicts = [];
            foreach ((PwEntry entry, bool mineCurrent) in _conflicts)
            {
                EntryName name = NameIn(_file, entry);
                if (!ReservedGroups.IsReserved(name.GroupPath))
                {
                    conflicts.Add(new MergeConflict(name, mineCurrent));
                }
            }

            List<MergeRenamedGroup> renamed = [];
            foreach ((PwGroup group, string lost) in _renamed)
            {
                if (_file.PathOf(group) is { } path && !ReservedGroups.IsReserved(path))
                {
                    renamed.Add(new MergeRenamedGroup(path, lost));
                }
            }

            return new VaultMergeReport(!Same(before, StateOf(_result)), conflicts, [.. _kept], renamed)
            {
                Combined = [.. _combined.Select(entry => NameIn(_file, entry))],
                Taken = [.. _taken.Select(entry => NameIn(_file, entry)).Where(name => !ReservedGroups.IsReserved(name.GroupPath))],
            };
        }

        /// <summary>Takes each group both copies hold by the rules for content and place, and adds each only the open copy holds, every parent first.</summary>
        private void MergeGroups()
        {
            List<PwGroup> mine = GroupsOf(_mine);

            foreach (PwGroup group in mine)
            {
                if (_groups.TryGetValue(group.Uuid, out PwGroup? theirs))
                {
                    MergeContent(group, theirs);
                }
                else
                {
                    PwGroup copy = new(false, false) { Uuid = group.Uuid, IsExpanded = group.IsExpanded };
                    copy.AssignProperties(group, false, true);
                    _groups[group.ParentGroup.Uuid].AddGroup(copy, true, false);
                    _groups[copy.Uuid] = copy;
                }
            }

            foreach (PwGroup group in mine.Skip(1))
            {
                if (_theirGroups.Contains(group.Uuid))
                {
                    Place(group, _groups[group.Uuid]);
                }
            }
        }

        /// <summary>A group's content as one whole: a change only the open copy made is its, otherwise the later in whole seconds, the file's on a tie.</summary>
        private void MergeContent(PwGroup mine, PwGroup theirs)
        {
            byte[] myDigest = _digests.Of(mine);
            byte[] theirDigest = _digests.Of(theirs);

            if (Same(myDigest, theirDigest))
            {
                return;
            }

            _base.Groups.TryGetValue(theirs.Uuid, out BaseRecord? known);

            if (known is not null && Same(theirDigest, known.Digest))
            {
                theirs.AssignProperties(mine, false, false);
                return;
            }

            // KDBX keeps no group history, so the file cannot show it descends: time decides, and a changed version that loses is named.
            bool myChange = known is null || !Same(myDigest, known.Digest);
            bool mineWins = Second(mine.LastModificationTime) > Second(theirs.LastModificationTime);
            string lost = mineWins ? theirs.Name : mine.Name;

            if (mineWins)
            {
                theirs.AssignProperties(mine, false, false);
            }

            if ((mineWins || myChange) && theirs.ParentGroup is not null && !string.Equals(lost, theirs.Name, StringComparison.Ordinal))
            {
                _renamed.Add((theirs, lost));
            }
        }

        /// <summary>Whether the open copy's place wins: the copy that moved the object since the base, the later <c>LocationChanged</c> when both did, the file's on a tie.</summary>
        private static bool MyPlaceWins(IStructureItem mine, IStructureItem theirs, BaseRecord? known)
        {
            bool iMoved = known is null || Moved(mine, known);
            bool theyMoved = known is null || Moved(theirs, known);
            return iMoved && (!theyMoved || Second(mine.LocationChanged) > Second(theirs.LocationChanged));
        }

        private static bool Moved(IStructureItem item, BaseRecord known) =>
            !known.Parent.Equals(item.ParentGroup.Uuid) || Second(item.LocationChanged) != Second(known.LocationChanged);

        private void Place(PwGroup mine, PwGroup theirs)
        {
            _base.Groups.TryGetValue(theirs.Uuid, out BaseRecord? known);

            if (!MyPlaceWins(mine, theirs, known))
            {
                return;
            }

            PwGroup target = _groups[mine.ParentGroup.Uuid];

            if (!ReferenceEquals(theirs.ParentGroup, target))
            {
                // A move that would put a group inside itself keeps the file's place.
                if (ReferenceEquals(target, theirs) || target.IsContainedIn(theirs))
                {
                    return;
                }

                theirs.ParentGroup.Groups.Remove(theirs);
                target.AddGroup(theirs, true, false);
            }

            theirs.LocationChanged = mine.LocationChanged;
            theirs.PreviousParentGroup = mine.PreviousParentGroup;
        }

        private void Place(PwEntry mine, PwEntry theirs, BaseRecord? known)
        {
            if (!MyPlaceWins(mine, theirs, known))
            {
                return;
            }

            PwGroup target = _groups[mine.ParentGroup.Uuid];

            if (!ReferenceEquals(theirs.ParentGroup, target))
            {
                theirs.ParentGroup.Entries.Remove(theirs);
                target.AddEntry(theirs, true, false);
            }

            theirs.LocationChanged = mine.LocationChanged;
            theirs.PreviousParentGroup = mine.PreviousParentGroup;
        }

        /// <summary>Takes the open copy's recycle bin as the result's when the file has none.</summary>
        private void AdoptMyBin()
        {
            if (_theirBin is null && _myBin is not null)
            {
                _result.RecycleBinUuid = _myBin.Uuid;
                _result.RecycleBinChanged = _mine.RecycleBinChanged;
            }
        }

        private void MergeEntries()
        {
            foreach (PwEntry mine in _mine.RootGroup.GetEntries(true))
            {
                PwEntry? theirs = _theirEntries.GetValueOrDefault(mine.Uuid);

                if (RevokedBearer(mine, theirs))
                {
                    theirs?.ParentGroup.Entries.Remove(theirs);
                }
                else if (theirs is not null)
                {
                    Merge(mine, theirs);
                }
                else
                {
                    KeepMine(mine);
                }
            }

            foreach (PwEntry theirs in _theirEntries.Values)
            {
                if (_myEntries.ContainsKey(theirs.Uuid))
                {
                    continue;
                }

                if (RevokedBearer(null, theirs))
                {
                    theirs.ParentGroup.Entries.Remove(theirs);
                }
                else
                {
                    KeepTheirs(theirs);
                }
            }
        }

        /// <summary>
        /// Whether either copy keeps the entry in <see cref="ReservedGroups.Tokens"/> or <see cref="ReservedGroups.Browsers"/> and either
        /// holds a tombstone for it, which always wins over a token's or a pairing's record whatever the times, changes or places, so the
        /// result does not hold it.
        /// </summary>
        private bool RevokedBearer(PwEntry? mine, PwEntry? theirs) =>
            (mine ?? theirs) is { } entry
            && (_myDeletions.ContainsKey(entry.Uuid) || _theirDeletions.ContainsKey(entry.Uuid))
            && ((mine is not null && IsBearer(_open, mine)) || (theirs is not null && IsBearer(_file, theirs)));

        private static bool IsBearer(KeePassInterop copy, PwEntry entry) =>
            copy.PathOf(entry.ParentGroup) is ReservedGroups.Tokens or ReservedGroups.Browsers;

        /// <summary>An entry both copies hold, by whole versions: lineage where the base shows it, the later in whole seconds otherwise, and every version kept in its history.</summary>
        private void Merge(PwEntry mine, PwEntry theirs)
        {
            _base.Entries.TryGetValue(theirs.Uuid, out BaseRecord? known);
            byte[] myDigest = _digests.Of(mine);
            byte[] theirDigest = _digests.Of(theirs);
            List<HistoryVersion> history = [.. theirs.History.Select(Held)];
            int theirCount = history.Count;

            bool equal = Same(myDigest, theirDigest);
            bool onlyMine = !equal && ((known is not null && Same(theirDigest, known.Digest)) || IsAncestor(theirs, mine));

            // The file descends from the base when the base's version is in its history: its change is taken whatever the stamps say.
            // So is a later file whose version differs only in place and stamps, as KeePassXC stamps an entry it recycles and keeps no version.
            bool onlyTheirs = !equal && !onlyMine && known is not null && Same(myDigest, known.Digest)
                && (HeldIn(history, known.Digest) || KeptByKeePassXc(theirs, known) || (Second(theirs.LastModificationTime) > Second(mine.LastModificationTime) && SameContent(mine, theirs)));
            bool byTime = !equal && !onlyMine && !onlyTheirs;
            bool tie = byTime && Second(mine.LastModificationTime) == Second(theirs.LastModificationTime);
            bool mineCurrent = onlyMine || (byTime && Second(mine.LastModificationTime) >= Second(theirs.LastModificationTime));
            PwEntry? displaced = onlyMine || byTime ? VersionOf(mineCurrent ? theirs : mine) : null;
            PwEntry? newer = byTime ? VersionOf(mineCurrent ? mine : theirs) : null;
            FieldsToKeep? combination = byTime && !IsReservedEntry(mine, theirs) ? FieldsOnlyTheOlderChanged(mine, theirs, displaced!, newer!) : null;

            foreach (PwEntry version in mine.History)
            {
                Hold(history, version, copy: true, kept: false);
            }

            bool displacedHeld = displaced is not null && Hold(history, displaced, copy: false, kept: true);

            if (combination is not null)
            {
                Hold(history, newer!, copy: false, kept: true);
            }

            if (mineCurrent)
            {
                theirs.AssignProperties(mine, false, false, false);
            }

            combination?.ApplyTo(theirs);

            if (tie || combination is not null)
            {
                // Two changes in one second: the open copy's is current, a second after every version and deletion it competes with.
                theirs.LastModificationTime = After(history.Select(version => version.Entry.LastModificationTime)
                    .Append(mine.LastModificationTime)
                    .Concat(DeletionsOf(theirs.Uuid)));
            }

            if (mineCurrent || combination is not null || history.Count != theirCount)
            {
                SetHistory(theirs, history);
            }

            Place(mine, theirs, known);

            if (mineCurrent || combination is not null)
            {
                _taken.Add(theirs);
            }

            // Recycling moves an entry, so this copy's deletion looks like a place and the file's newer edit would go unmentioned.
            if (known is { Recycled: false } && IsRecycled(mine, _myBin) && !Same(theirDigest, known.Digest))
            {
                Report(NameIn(_file, theirs), deletedInFile: false, recycled: true);
            }

            if (combination is not null)
            {
                _combined.Add(theirs);
            }
            else if (byTime && displacedHeld)
            {
                _conflicts.Add((theirs, mineCurrent));
            }
        }

        /// <summary>Whether a later copy keeps this version in its history, so nothing has happened to it since.</summary>
        private bool IsAncestor(PwEntry version, PwEntry later)
        {
            byte[] digest = _digests.OfHistoryCopy(version);
            return later.History.Any(held => Same(_digests.OfHistoryCopy(held), digest));
        }

        private bool IsReservedEntry(PwEntry mine, PwEntry theirs) =>
            ReservedGroups.IsReserved(_open.PathOf(mine.ParentGroup) ?? string.Empty)
            || ReservedGroups.IsReserved(_file.PathOf(theirs.ParentGroup) ?? string.Empty);

        /// <summary>
        /// What only the older of two changed versions changed since the latest version both keep in history, to join the newer; null
        /// with no such version or nothing to join. A field both changed stays the newer's, the other value being in history.
        /// </summary>
        private FieldsToKeep? FieldsOnlyTheOlderChanged(PwEntry mine, PwEntry theirs, PwEntry older, PwEntry newer)
        {
            if (LatestSharedVersion(mine, theirs) is not { } shared)
            {
                return null;
            }

            List<string> strings =
            [
                .. older.Strings.Select(field => field.Key).Union(shared.Strings.Select(field => field.Key), StringComparer.Ordinal)
                    .Where(name => !SameString(older, shared, name) && SameString(newer, shared, name)),
            ];
            List<string> attachments =
            [
                .. older.Binaries.Select(field => field.Key).Union(shared.Binaries.Select(field => field.Key), StringComparer.Ordinal)
                    .Where(name => !SameAttachment(older, shared, name) && SameAttachment(newer, shared, name)),
            ];
            bool tags = !TagsOf(older).SequenceEqual(TagsOf(shared), StringComparer.Ordinal) && TagsOf(newer).SequenceEqual(TagsOf(shared), StringComparer.Ordinal);

            return strings.Count == 0 && attachments.Count == 0 && !tags ? null : new FieldsToKeep(older, strings, attachments, tags);
        }

        /// <summary>The newest version in both histories, matched without custom data, which KeePassXC leaves out of those it keeps.</summary>
        private PwEntry? LatestSharedVersion(PwEntry mine, PwEntry theirs)
        {
            List<byte[]> held = [.. theirs.History.Select(_digests.OfHistoryCopy)];

            return mine.History
                .Where(version => { byte[] digest = _digests.OfHistoryCopy(version); return held.Exists(other => Same(other, digest)); })
                .OrderByDescending(version => version.LastModificationTime)
                .FirstOrDefault();
        }

        private static bool SameString(PwEntry one, PwEntry other, string name)
        {
            ProtectedString? first = one.Strings.Get(name);
            ProtectedString? second = other.Strings.Get(name);

            return ((first?.IsEmpty ?? true) && (second?.IsEmpty ?? true)) || (first is not null && second is not null && first.Equals(second, false));
        }

        private static bool SameAttachment(PwEntry one, PwEntry other, string name)
        {
            ProtectedBinary? first = one.Binaries.Get(name);
            ProtectedBinary? second = other.Binaries.Get(name);

            return first is null ? second is null : second is not null && first.Equals(second, false);
        }

        /// <summary>The fields of an older version that join the newer one.</summary>
        private sealed record FieldsToKeep(PwEntry Older, IReadOnlyList<string> Strings, IReadOnlyList<string> Attachments, bool Tags)
        {
            internal void ApplyTo(PwEntry entry)
            {
                foreach (string name in Strings)
                {
                    if (Older.Strings.Get(name) is { IsEmpty: false } value)
                    {
                        entry.Strings.Set(name, value);
                    }
                    else
                    {
                        entry.Strings.Remove(name);
                    }
                }

                foreach (string name in Attachments)
                {
                    if (Older.Binaries.Get(name) is { } content)
                    {
                        entry.Binaries.Set(name, content);
                    }
                    else
                    {
                        entry.Binaries.Remove(name);
                    }
                }

                if (Tags)
                {
                    Retag(entry, TagsOf(Older));
                }
            }
        }

        /// <summary>An entry only the open copy holds: added, unless the file deleted it and this copy had not changed it since the base.</summary>
        private void KeepMine(PwEntry mine)
        {
            if (!_theirDeletions.TryGetValue(mine.Uuid, out DateTime deleted))
            {
                PwEntry added = mine.CloneDeep();
                Add(added, _groups[mine.ParentGroup.Uuid]);
                _taken.Add(added);
                return;
            }

            _base.Entries.TryGetValue(mine.Uuid, out BaseRecord? known);

            if (known is not null && !Changed(mine, known))
            {
                return;
            }

            bool recycled = IsRecycled(mine, _myBin);
            bool restored = known is { Recycled: true } && !recycled;
            PwGroup? bin = restored || recycled ? null : KeepingBin();
            PwEntry kept = mine.CloneDeep();

            Add(kept, _groups[mine.ParentGroup.Uuid]);
            Keep(kept, deleted, bin);
            Report(NameIn(_open, mine), deletedInFile: true, recycled: bin is not null || recycled);
        }

        /// <summary>An entry only the file holds: kept, unless the open copy deleted it and the file had not changed it since the base, or a deletion already written outdates it.</summary>
        private void KeepTheirs(PwEntry theirs)
        {
            if (!_myDeletions.TryGetValue(theirs.Uuid, out DateTime deleted) || _theirPairs.Contains(theirs.Uuid))
            {
                return;
            }

            if (_base.Deletions.ContainsKey(theirs.Uuid))
            {
                // A deletion already written removes again an entry the file brings back older than it, as KeePass decides.
                if (Second(theirs.LastModificationTime) < Second(deleted))
                {
                    theirs.ParentGroup.Entries.Remove(theirs);
                }

                return;
            }

            _base.Entries.TryGetValue(theirs.Uuid, out BaseRecord? known);

            if (known is not null && !Changed(theirs, known))
            {
                theirs.ParentGroup.Entries.Remove(theirs);
                return;
            }

            EntryName name = NameIn(_file, theirs);
            bool recycled = IsRecycled(theirs, _theirBin);
            bool restored = known is { Recycled: true } && !recycled;
            PwGroup? bin = restored || recycled ? null : KeepingBin();

            Keep(theirs, deleted, bin);
            Report(name, deletedInFile: false, recycled: bin is not null || recycled);
        }

        private bool Changed(PwEntry entry, BaseRecord known) => !Same(_digests.Of(entry), known.Digest) || Moved(entry, known);

        private static bool SameContent(PwEntry mine, PwEntry theirs) =>
            Unstamped(mine).EqualsEntry(
                Unstamped(theirs),
                PwCompareOptions.IgnoreParentGroup | PwCompareOptions.IgnoreTimes | PwCompareOptions.IgnoreHistory | PwCompareOptions.NullEmptyEquivStd,
                MemProtCmpMode.Full);

        private bool KeptByKeePassXc(PwEntry theirs, BaseRecord known) =>
            theirs.History.Any(version => Same(_digests.OfHistoryCopy(version), known.HistoryCopyDigest));

        private static PwEntry Unstamped(PwEntry entry)
        {
            PwEntry copy = entry.CloneDeep();
            copy.CustomData.Remove(_keePassXcStamp);
            return copy;
        }

        private static void Add(PwEntry entry, PwGroup group) => group.AddEntry(entry, true, false);

        /// <summary>
        /// Keeps an entry one copy deleted without seeing the other's change: stamped a second after the deletion and every version, with the
        /// version it was in its history, and moved into <paramref name="bin"/> unless that is null.
        /// </summary>
        /// <remarks>The version before the stamp stays at its own second, so a later synchronize with the copy that changed it adds nothing.</remarks>
        private void Keep(PwEntry entry, DateTime deleted, PwGroup? bin)
        {
            List<HistoryVersion> history = [.. entry.History.Select(Held)];
            Hold(history, VersionOf(entry), copy: false, kept: true);

            DateTime stamp = After(history.Select(version => version.Entry.LastModificationTime)
                .Append(entry.LastModificationTime)
                .Append(deleted)
                .Concat(DeletionsOf(entry.Uuid)));
            entry.LastModificationTime = stamp;
            SetHistory(entry, history);

            if (bin is not null)
            {
                PwUuid from = entry.ParentGroup.Uuid;
                entry.ParentGroup.Entries.Remove(entry);
                bin.AddEntry(entry, true, false);
                entry.PreviousParentGroup = from;
                entry.LocationChanged = stamp;
            }
        }

        /// <summary>The bin an entry kept against a deletion goes to, made as KeePass makes one when the vault recycles and has none; null when it does not recycle.</summary>
        private PwGroup? KeepingBin()
        {
            if (!_result.RecycleBinEnabled)
            {
                return null;
            }

            PwGroup bin = _file.EnsureBin();
            _groups[bin.Uuid] = bin;
            return bin;
        }

        private void Report(EntryName name, bool deletedInFile, bool recycled)
        {
            if (!ReservedGroups.IsReserved(name.GroupPath))
            {
                _kept.Add(new MergeKeptItem(name, deletedInFile, recycled));
            }
        }

        private IEnumerable<DateTime> DeletionsOf(PwUuid id)
        {
            if (_myDeletions.TryGetValue(id, out DateTime mine))
            {
                yield return mine;
            }

            if (_theirDeletions.TryGetValue(id, out DateTime theirs))
            {
                yield return theirs;
            }
        }

        /// <summary>Adds a version to a history unless one of the same second and content is there already, and says whether it did.</summary>
        /// <remarks>
        /// A version this merge keeps carries KeePassLib's mark, so the merged copy's save and every later open keep it past the history
        /// limits until the entry's next edit.
        /// </remarks>
        private bool Hold(List<HistoryVersion> history, PwEntry version, bool copy, bool kept)
        {
            byte[] digest = _digests.Of(version);

            if (HeldIn(history, digest))
            {
                return false;
            }

            PwEntry held = copy ? version.CloneDeep() : version;

            if (kept)
            {
                held.CustomData.Set(PwEntry.KeptVersionKey, "1");
            }

            history.Add(new HistoryVersion(held, digest, IsKept(held)));
            return true;
        }

        private HistoryVersion Held(PwEntry version) => new(version, _digests.Of(version), IsKept(version));

        private static bool IsKept(PwEntry version) => version.CustomData.Exists(PwEntry.KeptVersionKey);

        private static bool HeldIn(List<HistoryVersion> history, byte[] digest) => history.Exists(held => Same(held.Digest, digest));

        /// <summary>Gives an entry the versions its merge kept, ordered by time and trimmed to the vault's limits oldest first, never a version the merge moved there.</summary>
        private void SetHistory(PwEntry entry, List<HistoryVersion> history)
        {
            List<HistoryVersion> ordered = [.. history.OrderBy(version => Second(version.Entry.LastModificationTime))];
            int items = _result.HistoryMaxItems;
            long size = _result.HistoryMaxSize;

            bool trimming = items >= 0;
            while (trimming && ordered.Count > items)
            {
                trimming = DropOldest(ordered);
            }

            trimming = size >= 0;
            while (trimming && ordered.Aggregate(0UL, (total, version) => total + version.Entry.GetSize()) > (ulong)size)
            {
                trimming = DropOldest(ordered);
            }

            entry.History.Clear();
            foreach (HistoryVersion version in ordered)
            {
                entry.History.Add(version.Entry);
            }
        }

        private static bool DropOldest(List<HistoryVersion> ordered)
        {
            int oldest = ordered.FindIndex(version => !version.Kept);

            if (oldest < 0)
            {
                return false;
            }

            ordered.RemoveAt(oldest);
            return true;
        }

        private static PwEntry VersionOf(PwEntry entry)
        {
            PwEntry version = entry.CloneDeep();
            version.History.Clear();
            return version;
        }

        /// <summary>Moves what the open copy's recycle bin holds into the file's when the two name different bins, keeping where each came from, so nothing deleted becomes listable.</summary>
        private void EmptyMyBin()
        {
            if (_myBin is null || _theirBin is null || _myBin.Uuid.Equals(_theirBin.Uuid) || !_groups.TryGetValue(_myBin.Uuid, out PwGroup? old))
            {
                return;
            }

            foreach (PwGroup group in old.Groups.CloneShallowToList())
            {
                if (!ReferenceEquals(group, _theirBin) && !_theirBin.IsContainedIn(group))
                {
                    old.Groups.Remove(group);
                    _theirBin.AddGroup(group, true, false);
                }
            }

            foreach (PwEntry entry in old.Entries.CloneShallowToList())
            {
                old.Entries.Remove(entry);
                _theirBin.AddEntry(entry, true, false);
            }
        }

        /// <summary>Removes each group one copy deleted that the merge leaves empty and that was not changed after the deletion, deepest first.</summary>
        private void RemoveDeletedGroups()
        {
            foreach (PwGroup group in _result.RootGroup.GetGroups(true).Reverse())
            {
                if (group.Groups.UCount == 0 && group.Entries.UCount == 0 && !group.Uuid.Equals(_result.RecycleBinUuid)
                    && DeletedGroup(group.Uuid) is { } deleted && Second(group.LastModificationTime) < Second(deleted))
                {
                    group.ParentGroup.Groups.Remove(group);
                    _groups.Remove(group.Uuid);
                }
            }
        }

        /// <summary>When the copy that lacks a group the other holds deleted it, or null when neither deleted it.</summary>
        private DateTime? DeletedGroup(PwUuid id)
        {
            bool mine = _myGroups.ContainsKey(id);
            bool theirs = _theirGroups.Contains(id);

            if (mine && !theirs && _theirDeletions.TryGetValue(id, out DateTime byFile))
            {
                return byFile;
            }

            if (theirs && !mine && !_theirPairs.Contains(id) && _myDeletions.TryGetValue(id, out DateTime byMe))
            {
                return byMe;
            }

            return null;
        }

        /// <summary>Gives the result both copies' tombstones less those for objects it keeps, but one the file holds beside its object.</summary>
        private void MergeDeletions()
        {
            HashSet<PwUuid> live = [.. GroupsOf(_result).Select(group => group.Uuid), .. _result.RootGroup.GetEntries(true).Select(entry => entry.Uuid)];
            Dictionary<PwUuid, DateTime> deletions = new(_theirDeletions);

            foreach ((PwUuid id, DateTime deleted) in _myDeletions)
            {
                if (!deletions.TryGetValue(id, out DateTime known) || deleted > known)
                {
                    deletions[id] = deleted;
                }
            }

            _result.DeletedObjects.Clear();

            foreach ((PwUuid id, DateTime deleted) in deletions)
            {
                if (!live.Contains(id) || _theirPairs.Contains(id))
                {
                    _result.DeletedObjects.Add(new PwDeletedObject(id, deleted));
                }
            }
        }

        /// <summary>Adds each of the open copy's custom icons that something in the result uses and the file lacks.</summary>
        private void CarryIcons()
        {
            HashSet<PwUuid> held = [.. _result.CustomIcons.Select(icon => icon.Uuid)];
            IEnumerable<PwUuid> used = GroupsOf(_result).Select(group => group.CustomIconUuid)
                .Concat(_result.RootGroup.GetEntries(true).SelectMany(entry => entry.History.Append(entry)).Select(entry => entry.CustomIconUuid));

            foreach (PwUuid id in used)
            {
                if (!id.IsZero && held.Add(id) && _mine.CustomIcons.Find(icon => icon.Uuid.Equals(id)) is { } icon)
                {
                    _result.CustomIcons.Add(new PwCustomIcon(icon.Uuid, icon.ImageDataPng) { Name = icon.Name, LastModificationTime = icon.LastModificationTime });
                }
            }
        }

        /// <summary>A digest of everything a merge can change, to tell whether the result took anything from the open copy.</summary>
        private byte[] StateOf(PwDatabase database)
        {
            using IncrementalHash state = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (PwGroup group in GroupsOf(database).OrderBy(group => group.Uuid.ToHexString(), StringComparer.Ordinal))
            {
                AppendPlace(state, group);
                state.AppendData(_digests.Of(group));
            }

            foreach (PwEntry entry in database.RootGroup.GetEntries(true).OrderBy(entry => entry.Uuid.ToHexString(), StringComparer.Ordinal))
            {
                AppendPlace(state, entry);
                state.AppendData(_digests.Of(entry));
                AppendNumber(state, entry.History.UCount);

                foreach (PwEntry version in entry.History)
                {
                    state.AppendData(_digests.Of(version));
                }
            }

            foreach ((PwUuid id, DateTime deleted) in Deletions(database).OrderBy(deletion => deletion.Key.ToHexString(), StringComparer.Ordinal))
            {
                state.AppendData(id.UuidBytes);
                AppendNumber(state, Second(deleted));
            }

            state.AppendData(database.RecycleBinUuid.UuidBytes);

            foreach (PwCustomIcon icon in database.CustomIcons.OrderBy(icon => icon.Uuid.ToHexString(), StringComparer.Ordinal))
            {
                state.AppendData(icon.Uuid.UuidBytes);
            }

            return state.GetHashAndReset();
        }

        private static void AppendPlace(IncrementalHash state, IStructureItem item)
        {
            state.AppendData(item.Uuid.UuidBytes);
            state.AppendData((item.ParentGroup?.Uuid ?? PwUuid.Zero).UuidBytes);
            AppendNumber(state, Second(item.LocationChanged));
            state.AppendData(item.PreviousParentGroup.UuidBytes);
        }

        private static void AppendNumber(IncrementalHash state, long value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            state.AppendData(bytes);
        }

        private static EntryName NameIn(KeePassInterop copy, PwEntry entry) =>
            new(copy.PathOf(entry.ParentGroup) ?? string.Empty, entry.Strings.ReadSafe(PwDefs.TitleField));
    }
}
