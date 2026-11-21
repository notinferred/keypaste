extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Security;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    // An entry's custom data, which KeePassXC keeps and shows nowhere, naming its UUID and the agent whose approved save created it (T-57).
    private const string _agentCreated = "acaf.agent-created";

    /// <summary>Adds an entry, creating any missing groups along its group path.</summary>
    internal void AddEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PwGroup group = EnsureGroup(entry.GroupPath);

        PwEntry pwEntry = new(true, true);
        SetField(pwEntry, PwDefs.TitleField, entry.Title);
        SetField(pwEntry, PwDefs.UserNameField, entry.Username);
        SetField(pwEntry, PwDefs.PasswordField, entry.Password);
        SetField(pwEntry, PwDefs.UrlField, entry.Url);
        SetField(pwEntry, PwDefs.NotesField, entry.Notes);

        group.AddEntry(pwEntry, true);
    }

    /// <summary>
    /// Creates an entry with its fields and tags as one mutation, with no history item. The caller
    /// has checked every name.
    /// </summary>
    /// <param name="entry">The entry's standard fields and its group.</param>
    /// <param name="fields">Its custom fields.</param>
    /// <param name="tags">Its tags.</param>
    /// <param name="createGroup">Whether a missing group is created, as <see cref="AddEntry"/> creates one.</param>
    /// <exception cref="VaultException">The group is missing and not to be created, or names more than one, or the entry's name is taken. Nothing is changed.</exception>
    internal void CreateEntry(VaultEntry entry, IReadOnlyList<FieldWrite> fields, IReadOnlyList<string> tags, bool createGroup = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var destination = LocateGroup(entry.GroupPath)?.Group;

        if (destination is null && !createGroup)
        {
            throw new VaultException($"There is no group '{entry.GroupPath}'. Choose one the vault has.");
        }

        var projected = Projected(null, null, null, null);
        projected.Add((entry.GroupPath, entry.Title, true));

        switch (RefuseCollision(projected))
        {
            case OrganizeOutcome.DestinationOccupied:
                throw new VaultException($"'{entry.Title}' is already in that group. Choose another title.");
            case OrganizeOutcome.DestinationAmbiguous:
                throw new VaultException($"'{ChildPath(entry.GroupPath, entry.Title)}' would name two items. Choose another title.");
        }

        // Nothing above this line writes. The entry is whole before it joins the group, so a
        // refusal can never leave half of one behind.
        PwEntry pwEntry = new(true, true);
        SetField(pwEntry, PwDefs.TitleField, entry.Title);
        SetField(pwEntry, PwDefs.UserNameField, entry.Username);
        SetField(pwEntry, PwDefs.PasswordField, entry.Password);
        SetField(pwEntry, PwDefs.UrlField, entry.Url);
        SetField(pwEntry, PwDefs.NotesField, entry.Notes);

        if (entry.Kind != ItemKind.None)
        {
            pwEntry.IconId = IconOf(entry.Kind);
        }

        foreach (FieldWrite write in fields)
        {
            pwEntry.Strings.Set(write.Name, new ProtectedString(write.Protect ?? true, write.Value!));
        }

        foreach (string tag in tags)
        {
            AddTagTo(pwEntry, tag);
        }

        (destination ?? EnsureGroup(entry.GroupPath)).AddEntry(pwEntry, true);
    }

    /// <summary>Overwrites the fields of the one entry with this entry's name.</summary>
    /// <returns>The number of entries updated: 0 if nothing matched, otherwise 1.</returns>
    /// <remarks>
    /// The name identifies the entry, so this cannot rename one or move it; it changes field
    /// values in place.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int UpdateEntry(EntryName name, VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        PwEntry pwEntry = found.Entry;

        // Mutated in place: re-adding mints a new UUID and drops what keypaste does not model (law 14).
        pwEntry.CreateBackup(_database);

        SetField(pwEntry, PwDefs.TitleField, entry.Title);
        SetField(pwEntry, PwDefs.UserNameField, entry.Username);
        SetField(pwEntry, PwDefs.PasswordField, entry.Password);
        SetField(pwEntry, PwDefs.UrlField, entry.Url);
        SetField(pwEntry, PwDefs.NotesField, entry.Notes);

        MarkEdited(pwEntry);
        return 1;
    }

    /// <summary>Deletes the one entry with this name, reversibly where the vault allows it.</summary>
    /// <param name="name">The entry to delete.</param>
    /// <param name="recycled">
    /// The identity of what is now in the bin, when this recycled; otherwise the default.
    /// </param>
    /// <returns>What happened to the entry.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// <para>
    /// A recycled entry keeps its UUID, its fields and its whole history, and records the group it
    /// came from in <c>PreviousParentGroup</c> so <see cref="RestoreRecycled"/> can put it back.
    /// It gets no tombstone: a tombstone says an object was deleted, and a merge that believed one
    /// would delete the entry in the other copy of the vault. <see cref="PurgeRecycled"/> is where
    /// the tombstone belongs, and it is what this method used to do unconditionally.
    /// </para>
    /// <para>
    /// The cost of that change is stated where it matters: a deleted value is still in the file,
    /// so erasing one is now two deliberate steps rather than one. The alternative was the defect
    /// V.3a exists to repair — an ordinary mis-click taking an entry and its history with it.
    /// </para>
    /// </remarks>
    internal DeletionOutcome RemoveEntry(EntryName name, out RecycledEntryId recycled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        recycled = default;

        if (Locate(name) is not { } found)
        {
            return DeletionOutcome.NothingMatched;
        }

        if (!_database.RecycleBinEnabled)
        {
            found.Group.Entries.Remove(found.Entry);
            _database.DeletedObjects.Add(new PwDeletedObject(found.Entry.Uuid, DateTime.UtcNow));
            return DeletionOutcome.DeletedPermanently;
        }

        PwGroup bin = EnsureBin();

        found.Group.Entries.Remove(found.Entry);
        found.Entry.PreviousParentGroup = found.Group.Uuid;

        // The three-argument overload stamps LocationChanged, which is when the entry was
        // deleted, and leaves LastModificationTime and History alone: a delete is not an edit.
        bin.AddEntry(found.Entry, true, true);

        // The same identity ReadRecycled will list for this entry, from the same UUID.
        recycled = RecycledEntryId.FromUuidHex(found.Entry.Uuid.ToHexString());
        return DeletionOutcome.Recycled;
    }

    /// <summary>Renames the one entry with this name, mutating it in place.</summary>
    internal OrganizeOutcome RenameEntry(EntryName name, string title, out EntryName? renamed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Relocate(name, new EntryName(name.GroupPath, title), out renamed);
    }

    /// <summary>Moves the one entry with this name into another group, mutating it in place.</summary>
    internal OrganizeOutcome MoveEntry(EntryName name, string destinationGroupPath, out EntryName? moved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Relocate(name, new EntryName(destinationGroupPath, name.Title), out moved);
    }

    /// <summary>Sets an entry's expiry as an edit with one history revision.</summary>
    /// <returns>Whether there was such an entry.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal bool SetExpiry(EntryName name, DateTimeOffset expires)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return false;
        }

        found.Entry.CreateBackup(_database);
        found.Entry.Expires = true;
        found.Entry.ExpiryTime = expires.UtcDateTime;
        MarkEdited(found.Entry);
        return true;
    }

    /// <summary>Removes an entry and its history for good, whether or not the vault's Trash is on, and tombstones it for a merge.</summary>
    /// <returns>Whether there was such an entry.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal bool DeleteForGood(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return false;
        }

        found.Group.Entries.Remove(found.Entry);
        _database.DeletedObjects.Add(new PwDeletedObject(found.Entry.Uuid, DateTime.UtcNow));
        return true;
    }

    /// <summary>Records on an entry an approved save just created that the agent named asked for it, bound to the entry's own UUID so a copy is not marked.</summary>
    /// <exception cref="VaultException">No entry, or more than one, answers to that name.</exception>
    internal void MarkAgentCreated(EntryName name, string agent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            throw new VaultException($"'{name.Title}' is not in this vault.");
        }

        found.Entry.CustomData.Set(_agentCreated, $"{found.Entry.Uuid.ToHexString()} {agent}");
    }

    /// <summary>The agent whose approved save created this entry, or null when its mark is missing or belongs to another entry's UUID.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal string? AgentThatCreated(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found || found.Entry.CustomData.Get(_agentCreated) is not { } mark)
        {
            return null;
        }

        var uuid = found.Entry.Uuid.ToHexString();

        return mark.Length > uuid.Length + 1 && mark.StartsWith(uuid + " ", StringComparison.Ordinal) ? mark[(uuid.Length + 1)..] : null;
    }

    /// <summary>Sets an entry's expiry, which only KeePassXC writes, for a fixture to hold one.</summary>
    internal void SetExpiryUnchecked(EntryName name, DateTimeOffset? expires)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            throw new VaultException($"'{name.Title}' is not in this vault.");
        }

        found.Entry.Expires = expires is not null;
        found.Entry.ExpiryTime = expires?.UtcDateTime ?? found.Entry.ExpiryTime;
    }

    /// <summary>Sets an entry's Auto-Type settings, which only KeePass and KeePassXC write, for a fixture to hold them.</summary>
    internal void SetAutoTypeUnchecked(EntryName name, bool enabled, bool obfuscated, string sequence, IReadOnlyList<(string Window, string Sequence)> associations)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            throw new VaultException($"'{name.Title}' is not in this vault.");
        }

        var config = new KeePass::KeePassLib.Collections.AutoTypeConfig
        {
            Enabled = enabled,
            ObfuscationOptions = obfuscated ? KeePass::KeePassLib.Collections.AutoTypeObfuscationOptions.UseClipboard : KeePass::KeePassLib.Collections.AutoTypeObfuscationOptions.None,
            DefaultSequence = sequence,
        };

        foreach (var (window, keys) in associations)
        {
            config.Add(new KeePass::KeePassLib.Collections.AutoTypeAssociation(window, keys));
        }

        found.Entry.AutoType = config;
    }

    /// <summary>
    /// The one write that changes an entry's name, whichever half of it varies — or both at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every check is a read, and they all happen before the single mutation at the bottom. The
    /// alternative — mutate, check, undo — leaves the in-memory tree wrong on any path that throws
    /// between the two, and the next unrelated save writes that out. <see cref="EnsureGroup"/> is
    /// the specific trap: it is the obvious way to find a destination and it creates every missing
    /// segment, so a refused move made with it would leave an empty group tree behind.
    /// </para>
    /// <para>
    /// The entry is mutated, never removed and re-added as a new <see cref="PwEntry"/>, for the
    /// reason <see cref="UpdateEntry"/> gives: re-adding mints a new UUID and discards timestamps,
    /// attachments and the custom string fields keypaste does not model.
    /// </para>
    /// </remarks>
    internal OrganizeOutcome Relocate(EntryName source, EntryName target, out EntryName? result)
    {
        result = null;

        if (RelocationRefusal(source, target) is { } refusal)
        {
            return refusal;
        }

        var found = Locate(source)!.Value;
        var destination = LocateGroup(target.GroupPath)!.Value;
        bool moves = !ReferenceEquals(found.Group, destination.Group);
        bool renames = !string.Equals(ReadField(found.Entry, PwDefs.TitleField), target.Title, StringComparison.Ordinal);

        // The two halves of the move stay adjacent so an entry never belongs to no group.
        if (moves)
        {
            found.Group.Entries.Remove(found.Entry);

            // A move stamps only LocationChanged, and writes no PreviousParentGroup, which raises the file to KDBX 4.1.
            destination.Group.AddEntry(found.Entry, true, true);
        }

        if (renames)
        {
            SetField(found.Entry, PwDefs.TitleField, target.Title);

            // Newer so a merge keeps the title; no revision, because a rename overwrites no value.
            MarkEdited(found.Entry);
        }

        result = target with { Uuid = source.Uuid };

        // A combined change is neither a rename nor a move on its own.
        return (renames, moves) switch
        {
            (true, true) => OrganizeOutcome.RenamedAndMoved,
            (true, false) => OrganizeOutcome.Renamed,
            _ => OrganizeOutcome.Moved,
        };
    }

    /// <summary>Why <see cref="Relocate"/> would refuse this, or null when it would write.</summary>
    internal OrganizeOutcome? RelocationRefusal(EntryName source, EntryName target)
    {
        if (Locate(source) is not { } found)
        {
            return OrganizeOutcome.NothingMatched;
        }

        if (LocateGroup(target.GroupPath) is not { } destination)
        {
            return OrganizeOutcome.DestinationMissing;
        }

        if (ReferenceEquals(found.Group, destination.Group) && string.Equals(ReadField(found.Entry, PwDefs.TitleField), target.Title, StringComparison.Ordinal))
        {
            return OrganizeOutcome.DestinationUnchanged;
        }

        return VaultNameRules.IsValidTitle(target.Title, out _)
            ? RefuseCollision(Projected(found.Entry, target, null, null))
            : OrganizeOutcome.NameRefused;
    }

    /// <summary>
    /// Copies the one entry with this name beside it, as a new entry under the first free title of
    /// <c>&lt;title&gt; (copy)</c>, <c>&lt;title&gt; (copy 2)</c> and so on.
    /// </summary>
    /// <param name="source">The entry to copy.</param>
    /// <param name="copy">The copy's name, or null when nothing was written.</param>
    /// <param name="dropped">The tags the copy did not take, in the entry's order.</param>
    /// <returns>What happened, including every reason it did not.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// The copy keeps every field, attachment, icon and setting, with a new UUID, no history, no record of
    /// where anything came from, and its times set to now. It does not take a tag starting <c>env:</c>,
    /// because a project refuses two entries giving one variable.
    /// </remarks>
    internal OrganizeOutcome CloneEntry(EntryName source, out EntryName? copy, out IReadOnlyList<string> dropped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        copy = null;
        dropped = [];

        if (Locate(source) is not { } found)
        {
            return OrganizeOutcome.NothingMatched;
        }

        string sourceTitle = ReadField(found.Entry, PwDefs.TitleField);
        string groupPath = PathOf(found.Group)!;

        if (!VaultNameRules.IsValidTitle(CopyTitle(sourceTitle, 1), out _))
        {
            return OrganizeOutcome.NameRefused;
        }

        var projected = Projected(null, null, null, null);
        string title;

        // Each candidate is a distinct title, so at most one per entry in the vault can be taken.
        for (var copies = 1; ; copies++)
        {
            title = CopyTitle(sourceTitle, copies);
            projected.Add((groupPath, title, true));

            if (RefuseCollision(projected) is null)
            {
                break;
            }

            projected.RemoveAt(projected.Count - 1);
        }

        PwEntry clone = found.Entry.CloneDeep();
        clone.SetUuid(new PwUuid(true), false);
        clone.History.Clear();
        clone.PreviousParentGroup = PwUuid.Zero;
        clone.UsageCount = 0;

        DateTime now = DateTime.UtcNow;
        clone.CreationTime = now;
        clone.LastModificationTime = now;
        clone.LastAccessTime = now;

        List<string> projectTags = [.. TagsOf(clone).Where(tag => ProjectTag.Read(tag).Kind != ProjectTagKind.None)];

        foreach (string tag in projectTags)
        {
            RemoveTagFrom(clone, tag);
        }

        SetField(clone, PwDefs.TitleField, title);
        found.Group.AddEntry(clone, true, true);

        copy = new EntryName(groupPath, title) { Uuid = source.Uuid is null ? null : clone.Uuid.ToHexString() };
        dropped = projectTags;
        return OrganizeOutcome.Copied;

        static string CopyTitle(string title, int copies) =>
            copies == 1 ? $"{title} (copy)" : Invariant($"{title} (copy {copies})");
    }

    /// <summary>
    /// Every live entry's name as it would be <em>after</em> a write, without performing it, with
    /// the names that write produces marked.
    /// </summary>
    private List<(string GroupPath, string Title, bool Written)> Projected(
        PwEntry? movedEntry,
        EntryName? movedEntryName,
        PwGroup? renamedGroup,
        string? renamedGroupPath)
    {
        List<(string GroupPath, string Title, bool Written)> names = [];
        PwGroup? bin = Bin();

        Walk(_database.RootGroup, string.Empty, false);
        return names;

        void Walk(PwGroup group, string groupPath, bool written)
        {
            foreach (PwEntry entry in group.Entries)
            {
                names.Add(ReferenceEquals(entry, movedEntry)
                    ? (movedEntryName!.GroupPath, movedEntryName.Title, true)
                    : (groupPath, ReadField(entry, PwDefs.TitleField), written));
            }

            foreach (PwGroup child in group.Groups)
            {
                if (ReferenceEquals(child, bin))
                {
                    continue;
                }

                bool renamed = ReferenceEquals(child, renamedGroup);
                Walk(child, renamed ? renamedGroupPath! : ChildPath(groupPath, child.Name), written || renamed);
            }
        }
    }

    /// <summary>
    /// Whether a projected set of names holds a collision that this write would create.
    /// </summary>
    /// <remarks>
    /// Only a collision involving a written name counts. A vault can already hold the pair keypaste
    /// refuses to create — <c>EntryIdentityTests</c>' fixtures are what KeePassXC puts in a file —
    /// and trapping such a vault so nothing else in it could be reorganized would be worse than the
    /// ambiguity. Reading stays permissive; only the new ambiguity is refused.
    /// </remarks>
    /// <param name="projected">Every entry after the change, each marked if the change moves it.</param>
    /// <param name="movedTogether">Whether the moved entries keep their places relative to each other, as a group's do, so only an entry left where it was can collide with one.</param>
    private static OrganizeOutcome? RefuseCollision(List<(string GroupPath, string Title, bool Written)> projected, bool movedTogether = false)
    {
        Dictionary<(string, string), int> byName = [];
        Dictionary<string, int> byPath = [];
        int allowed = movedTogether ? 0 : 1;

        foreach (var row in projected.Where(row => !movedTogether || !row.Written))
        {
            byName[(row.GroupPath, row.Title)] = byName.GetValueOrDefault((row.GroupPath, row.Title)) + 1;
            string joined = ChildPath(row.GroupPath, row.Title);
            byPath[joined] = byPath.GetValueOrDefault(joined) + 1;
        }

        foreach (var row in projected)
        {
            if (row.Written && byName.GetValueOrDefault((row.GroupPath, row.Title)) > allowed)
            {
                return OrganizeOutcome.DestinationOccupied;
            }
        }

        foreach (var row in projected)
        {
            if (row.Written && byPath.GetValueOrDefault(ChildPath(row.GroupPath, row.Title)) > allowed)
            {
                return OrganizeOutcome.DestinationAmbiguous;
            }
        }

        return null;
    }

    /// <summary>The entry's KDBX UUID as hex, or null if no entry has that name.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal string? EntryUuid(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name)?.Entry.Uuid.ToHexString();
    }

    /// <summary>The name of the live entry outside keypaste's own groups whose KDBX UUID, as hex, is <paramref name="uuidHex"/>, or null.</summary>
    internal EntryName? EntryNameOf(string uuidHex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Two items holding one UUID have no answer to which one it means, and only a name can say.
        return LiveHolders(uuidHex) is [var found]
            ? new EntryName(PathOf(found.Group)!, ReadField(found.Entry, PwDefs.TitleField)) { Uuid = uuidHex }
            : null;
    }

    private List<(PwGroup Group, PwEntry Entry)> LiveHolders(string uuidHex)
    {
        List<(PwGroup Group, PwEntry Entry)> holders = [];
        CollectLiveHolders(_database.RootGroup, string.Empty, uuidHex, Bin(), holders);
        return holders;
    }

    private static void CollectLiveHolders(PwGroup group, string groupPath, string uuidHex, PwGroup? bin, List<(PwGroup Group, PwEntry Entry)> holders)
    {
        foreach (PwEntry entry in group.Entries)
        {
            if (string.Equals(entry.Uuid.ToHexString(), uuidHex, StringComparison.Ordinal))
            {
                holders.Add((group, entry));
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            var childPath = ChildPath(groupPath, child.Name);

            if (!ReferenceEquals(child, bin) && !ReservedGroups.IsReserved(childPath))
            {
                CollectLiveHolders(child, childPath, uuidHex, bin, holders);
            }
        }
    }

    /// <summary>How many deleted-object tombstones the vault carries. A test seam.</summary>
    internal int TombstoneCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return (int)_database.DeletedObjects.UCount;
        }
    }

    // KeePassXC's merge keeps one version of an entry per whole second, so each edit takes a later second than every version it keeps.
    private static void MarkEdited(PwEntry entry)
    {
        DateTime previous = entry.LastModificationTime;

        entry.Touch(true);
        StampAfter(entry, entry.History.Select(revision => revision.LastModificationTime).Append(previous));
    }

    /// <summary>Moves an entry's modification time to the second after the latest of <paramref name="times"/>, unless it is already later.</summary>
    private static void StampAfter(PwEntry entry, IEnumerable<DateTime> times)
    {
        long second = times.Max().Ticks / TimeSpan.TicksPerSecond;

        if (entry.LastModificationTime.Ticks / TimeSpan.TicksPerSecond <= second
            && second < DateTime.MaxValue.Ticks / TimeSpan.TicksPerSecond)
        {
            entry.LastModificationTime = new DateTime((second + 1) * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }
    }

    private static readonly (ItemKind Kind, PwIcon Icon)[] _kindIcons =
    [
        (ItemKind.Login, PwIcon.World),
        (ItemKind.ApiKey, PwIcon.MultiKeys),
        (ItemKind.Database, PwIcon.Disk),
        (ItemKind.Server, PwIcon.NetworkServer),
        (ItemKind.SecureNote, PwIcon.Notepad),
        (ItemKind.Card, PwIcon.Money),
        (ItemKind.Identity, PwIcon.Identity),
        (ItemKind.SshKey, PwIcon.TerminalEncrypted),
        (ItemKind.Wifi, PwIcon.IRCommunication),
        (ItemKind.Environment, PwIcon.Console),
    ];

    private static ItemKind KindOf(PwEntry entry) =>
        entry.CustomIconUuid.Equals(PwUuid.Zero)
            ? _kindIcons.FirstOrDefault(pair => pair.Icon == entry.IconId).Kind
            : ItemKind.None;

    private static PwIcon IconOf(ItemKind kind) => _kindIcons.First(pair => pair.Kind == kind).Icon;

    /// <summary>Keeps the entry's kind as its standard icon, as an edit with one history revision.</summary>
    /// <returns>0 when there is no such entry or it already has that kind, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name, or the entry has a custom icon, which is kept.</exception>
    internal int SetKind(EntryName name, ItemKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        PwEntry entry = found.Entry;

        if (!entry.CustomIconUuid.Equals(PwUuid.Zero))
        {
            throw new VaultException("This item has its own icon from another app, which keypaste keeps. Change its icon there.");
        }

        PwIcon icon = IconOf(kind);

        if (entry.IconId == icon)
        {
            return 0;
        }

        entry.CreateBackup(_database);
        entry.IconId = icon;
        MarkEdited(entry);
        return 1;
    }

    /// <summary>Locates the one entry with this name, and the group holding it.</summary>
    /// <returns>The entry and its group, or null when no entry has that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// <para>
    /// <b>The same traversal as <see cref="Collect"/>, and that is the whole point.</b> An entry's
    /// group path is what walking the tree and joining names produces. Re-splitting a joined path
    /// on its last slash is a second rule, and the two disagreed: an entry titled
    /// <c>nested/TOKEN</c> in <c>env/dev</c> and an entry titled <c>TOKEN</c> in
    /// <c>env/dev/nested</c> are both <c>env/dev/nested/TOKEN</c>, so a listing found one and a
    /// removal deleted the other. Resolving the group by name instead would move the same defect
    /// one level up: KDBX permits two sibling groups called <c>nested</c>, and the walker sees
    /// entries in both while a name lookup sees only the first.
    /// </para>
    /// <para>
    /// A recycled entry is not a candidate: the bin is skipped exactly as it is in
    /// <see cref="Collect"/>, so a deleted entry cannot be found, read or removed by name.
    /// </para>
    /// <para>
    /// A name carrying a UUID finds the item holding it alone, for a screen showing it. Otherwise,
    /// two entries answering to one name are refused rather than resolved to whichever came first,
    /// because there is no answer that is not a guess (SECURITY.md law 7). KDBX permits that
    /// within one group and KeePassXC will make it; <see cref="EntryHandle"/> is what keeps each of
    /// them individually addressable.
    /// </para>
    /// </remarks>
    private (PwGroup Group, PwEntry Entry)? Locate(EntryName name)
    {
        List<(PwGroup Group, PwEntry Entry)> matches = name.Uuid is { } uuid ? LiveHolders(uuid) : Matches(name);

        // A file can hold one UUID twice, which the format forbids and opening does not refuse, and then the name has to agree too.
        if (name.Uuid is not null && matches.Count > 1)
        {
            matches = [.. matches.Where(found => string.Equals(PathOf(found.Group), name.GroupPath, StringComparison.Ordinal)
                && string.Equals(ReadField(found.Entry, PwDefs.TitleField), name.Title, StringComparison.Ordinal))];
        }

        if (matches.Count > 1)
        {
            string where = name.GroupPath.Length == 0 ? "the top level" : name.GroupPath;
            throw new VaultException(
                $"'{name.Title}' in '{where}' names {matches.Count} items. keypaste will not guess " +
                "which one you meant; rename one of them in KeePassXC.");
        }

        return matches.Count == 0 ? null : matches[0];
    }

    internal bool NameIsShared(EntryName name) => Matches(name).Count > 1;

    /// <summary>Whether the item a name reaches is still called that, which a UUID alone would not notice.</summary>
    internal bool IsStillCalled(EntryName name) =>
        Locate(name) is { } found
        && string.Equals(PathOf(found.Group), name.GroupPath, StringComparison.Ordinal)
        && string.Equals(ReadField(found.Entry, PwDefs.TitleField), name.Title, StringComparison.Ordinal);

    /// <summary>Every live entry answering to this name, in traversal order.</summary>
    /// <remarks>
    /// Split out of <see cref="Locate"/> rather than written twice, for the reason that method's
    /// own documentation gives: two traversals that could disagree about what a name means is the
    /// shape of the duplicate-name defect. <see cref="RestoreRecycled"/> needs the count without the
    /// refusal, because it is asking whether putting an entry back would create the ambiguity
    /// rather than whether one is already there.
    /// </remarks>
    private List<(PwGroup Group, PwEntry Entry)> Matches(EntryName name) => InGroup(name.GroupPath, name.Title);

    /// <summary>Every live entry directly in the group at this path, or only those with this title, in traversal order.</summary>
    private List<(PwGroup Group, PwEntry Entry)> InGroup(string groupPath, string? title)
    {
        List<(PwGroup Group, PwEntry Entry)> found = [];
        PwGroup? bin = Bin();

        Search(_database.RootGroup, string.Empty);
        return found;

        void Search(PwGroup group, string path)
        {
            if (string.Equals(path, groupPath, StringComparison.Ordinal))
            {
                foreach (PwEntry candidate in group.Entries)
                {
                    if (title is null || string.Equals(ReadField(candidate, PwDefs.TitleField), title, StringComparison.Ordinal))
                    {
                        found.Add((group, candidate));
                    }
                }
            }

            foreach (PwGroup child in group.Groups)
            {
                if (ReferenceEquals(child, bin))
                {
                    continue;
                }

                Search(child, ChildPath(path, child.Name));
            }
        }
    }
}
