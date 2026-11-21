extern alias KeePass;

using KeePass::KeePassLib;
using Keypaste.Core.Agents;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>Creates one empty group inside an existing one.</summary>
    internal GroupOutcome CreateGroup(string parentGroupPath, string name, out string created)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        created = string.Empty;

        if (LocateGroup(parentGroupPath) is not { } parent)
        {
            return GroupOutcome.ParentMissing;
        }

        if (!VaultNameRules.IsValidGroupName(name, out _))
        {
            return GroupOutcome.NameRefused;
        }

        if (IsReserved(parentGroupPath, name))
        {
            return GroupOutcome.NameReserved;
        }

        if (HasChildNamed(parent.Group, name, except: null))
        {
            return GroupOutcome.DestinationOccupied;
        }

        // Constructed exactly as EnsureGroup constructs one, so a group keypaste made is the same
        // object however it came to exist.
        var group = new PwGroup(true, true, name, PwIcon.Folder);
        parent.Group.AddGroup(group, true);

        created = ChildPath(parentGroupPath, name);
        return GroupOutcome.Created;
    }

    /// <summary>Renames one group in place, carrying everything under it.</summary>
    internal GroupOutcome RenameGroup(string groupPath, string name, out string renamedPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        renamedPath = string.Empty;

        // The root group has no name a person gave it and no parent to be renamed within.
        if (LocateGroup(groupPath) is not { Parent: { } parent } located)
        {
            return GroupOutcome.NothingMatched;
        }

        if (string.Equals(located.Group.Name, name, StringComparison.Ordinal))
        {
            return GroupOutcome.DestinationUnchanged;
        }

        if (!VaultNameRules.IsValidGroupName(name, out _))
        {
            return GroupOutcome.NameRefused;
        }

        string parentPath = PathOf(parent) ?? string.Empty;

        // Both directions of the same rule: a root group cannot become the env root, and the env
        // root cannot stop being it. Either silently moves a whole subtree into or out of where
        // keypaste writes a project's home entries.
        if (IsReserved(parentPath, name)
            || (parentPath.Length == 0
                && string.Equals(located.Group.Name, EnvConvention.RootGroup, StringComparison.Ordinal)))
        {
            return GroupOutcome.NameReserved;
        }

        if (HasChildNamed(parent, name, except: located.Group))
        {
            return GroupOutcome.DestinationOccupied;
        }

        string target = ChildPath(parentPath, name);

        if (RefuseCollision(Projected(null, null, located.Group, target), movedTogether: true) is { } collision)
        {
            return collision == OrganizeOutcome.DestinationOccupied
                ? GroupOutcome.DestinationOccupied
                : GroupOutcome.DestinationAmbiguous;
        }

        located.Group.Name = name;
        located.Group.Touch(true);

        renamedPath = target;
        return GroupOutcome.Renamed;
    }

    /// <summary>Moves one group, with everything under it, into another parent, mutating it in place.</summary>
    /// <remarks>
    /// Every check is a read and happens before the move, for the reason <see cref="Relocate"/> gives. Like
    /// an entry's move, it records nothing about where the group came from, so the file stays KDBX 4.0.
    /// </remarks>
    internal GroupOutcome MoveGroup(string groupPath, string destinationParentPath, out string movedPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        movedPath = string.Empty;

        if (LocateGroup(groupPath) is not { Parent: { } parent } located)
        {
            return GroupOutcome.NothingMatched;
        }

        if (IsKeypastesOwn(groupPath))
        {
            return GroupOutcome.NameReserved;
        }

        if (LocateGroup(destinationParentPath) is not { } destination)
        {
            return GroupOutcome.ParentMissing;
        }

        if (ReferenceEquals(destination.Group, located.Group) || destination.Group.IsContainedIn(located.Group))
        {
            return GroupOutcome.DestinationInside;
        }

        if (ReferenceEquals(destination.Group, parent))
        {
            return GroupOutcome.DestinationUnchanged;
        }

        string name = located.Group.Name;
        string target = ChildPath(destinationParentPath, name);

        // A move into keypaste's own groups would plant records there, and one out to the root under a
        // reserved name would make a group keypaste assigns meaning to.
        if (ReservedGroups.IsReserved(destinationParentPath)
            || ReservedGroups.IsReserved(target)
            || IsReserved(destinationParentPath, name))
        {
            return GroupOutcome.NameReserved;
        }

        if (HasChildNamed(destination.Group, name, except: null))
        {
            return GroupOutcome.DestinationOccupied;
        }

        if (RefuseCollision(Projected(null, null, located.Group, target), movedTogether: true) is { } collision)
        {
            return collision == OrganizeOutcome.DestinationOccupied
                ? GroupOutcome.DestinationOccupied
                : GroupOutcome.DestinationAmbiguous;
        }

        // Added before it is taken out, so a tree too deep for KeePassLib to hold is refused where it was.
        destination.Group.AddGroup(located.Group, true, true);
        parent.Groups.Remove(located.Group);

        movedPath = target;
        return GroupOutcome.Moved;
    }

    /// <summary>Deletes one group with everything under it: into the recycle bin, or for good when the vault has none and the group is empty.</summary>
    /// <remarks>
    /// <para>
    /// A recycled group keeps its UUID, its entries and their history, and records the group it was in as
    /// <c>PreviousParentGroup</c>, as KeePassXC does; like a recycled entry, that raises the file to KDBX 4.1.
    /// It gets no tombstone, for the reason <see cref="RemoveEntry"/> gives. Each item in it is
    /// listed and restored one at a time, back to the path it had (<see cref="RestoreRecycled"/>).
    /// </para>
    /// <para>
    /// Without a recycle bin only an empty group is deleted, and tombstoned; one holding anything is refused,
    /// because nothing could bring back what it holds.
    /// </para>
    /// </remarks>
    internal GroupOutcome DeleteGroup(string groupPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (LocateGroup(groupPath) is not { Parent: { } parent } located)
        {
            return GroupOutcome.NothingMatched;
        }

        if (IsKeypastesOwn(groupPath) || (Bin() is { } held && held.IsContainedIn(located.Group)))
        {
            return GroupOutcome.NameReserved;
        }

        if (!_database.RecycleBinEnabled)
        {
            if (located.Group.Entries.UCount > 0 || located.Group.Groups.UCount > 0)
            {
                return GroupOutcome.NotEmpty;
            }

            parent.Groups.Remove(located.Group);
            _database.DeletedObjects.Add(new PwDeletedObject(located.Group.Uuid, DateTime.UtcNow));
            return GroupOutcome.DeletedPermanently;
        }

        PwGroup bin = EnsureBin();

        bin.AddGroup(located.Group, true, true);
        parent.Groups.Remove(located.Group);
        located.Group.PreviousParentGroup = parent.Uuid;
        return GroupOutcome.Recycled;
    }

    /// <summary>Moves the group most recently deleted from this path out of the bin, as it went in.</summary>
    internal GroupOutcome RestoreRecycledGroup(string groupPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Bin() is not { } bin)
        {
            return GroupOutcome.NothingMatched;
        }

        PwGroup? recycled = bin.Groups
            .Where(group => LiveGroup(group.PreviousParentGroup) is { } from
                && PathOf(from) is { } path
                && string.Equals(ChildPath(path, group.Name), groupPath, StringComparison.Ordinal))
            .MaxBy(group => group.LocationChanged);

        if (recycled is null || LiveGroup(recycled.PreviousParentGroup) is not { } parent)
        {
            return GroupOutcome.NothingMatched;
        }

        if (HasChildNamed(parent, recycled.Name, except: null))
        {
            return GroupOutcome.DestinationOccupied;
        }

        parent.AddGroup(recycled, true, true);
        bin.Groups.Remove(recycled);
        recycled.PreviousParentGroup = PwUuid.Zero;
        return GroupOutcome.Restored;
    }

    /// <summary>Whether a group is one keypaste assigns meaning to: the env root, or one of its own.</summary>
    private static bool IsKeypastesOwn(string groupPath) =>
        ReservedGroups.IsReserved(groupPath) || string.Equals(groupPath, EnvConvention.RootGroup, StringComparison.Ordinal);

    /// <summary>Sets a group's Auto-Type settings, which only KeePass and KeePassXC write, for a fixture to hold them.</summary>
    internal void SetGroupAutoTypeUnchecked(string groupPath, bool? enabled, string sequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var group = LocateGroup(groupPath)?.Group ?? throw new VaultException($"'{groupPath}' is not a group in this vault.");
        group.EnableAutoType = enabled;
        group.DefaultAutoTypeSequence = sequence;
    }

    /// <summary>
    /// Adds a group without applying any of the rules, for building the shapes KeePassXC can make
    /// and keypaste refuses to.
    /// </summary>
    /// <remarks>
    /// A test seam, visible only through <c>InternalsVisibleTo</c>, so a fixture can build
    /// a vault with its recycle bin switched off: two sibling groups of one name is a vault
    /// keypaste has to have an answer for and has no supported way to write.
    /// </remarks>
    internal void AddGroupUnchecked(string parentGroupPath, string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureGroup(parentGroupPath).AddGroup(new PwGroup(true, true, name, PwIcon.Folder), true);
    }

    /// <summary>The one live group at this path, and the group holding it.</summary>
    /// <returns>
    /// The group and its parent, with a null parent for the root group, or null when no group has
    /// that path. The recycle bin is not a group any of this can reach.
    /// </returns>
    /// <exception cref="VaultException">More than one group answers to that path.</exception>
    /// <remarks>
    /// The counterpart <see cref="EnsureGroup"/> is not: that one creates, and takes the first
    /// child of a matching name. This walks the tree and joins names the way <see cref="Collect"/>
    /// does, so it cannot disagree with the paths a listing reports — the shape of the defect
    /// that duplicate names cause, at group level.
    /// </remarks>
    private (PwGroup? Parent, PwGroup Group)? LocateGroup(string groupPath)
    {
        if (groupPath.Length == 0)
        {
            return (null, _database.RootGroup);
        }

        List<(PwGroup? Parent, PwGroup Group)> found = [];
        PwGroup? bin = Bin();

        Search(_database.RootGroup, string.Empty);

        if (found.Count > 1)
        {
            throw new VaultException(
                $"'{groupPath}' names {found.Count} groups. keypaste will not guess which one you " +
                "meant; rename one of them in KeePassXC.");
        }

        return found.Count == 0 ? null : found[0];

        void Search(PwGroup group, string path)
        {
            foreach (PwGroup child in group.Groups)
            {
                if (ReferenceEquals(child, bin))
                {
                    continue;
                }

                string childPath = ChildPath(path, child.Name);

                if (string.Equals(childPath, groupPath, StringComparison.Ordinal))
                {
                    found.Add((group, child));
                }

                Search(child, childPath);
            }
        }
    }

    /// <summary>
    /// Whether a name means something keypaste assigns rather than something a group may be called.
    /// </summary>
    /// <remarks>
    /// The bin is asked for directly rather than looked for among the siblings, because it is
    /// excluded from every traversal: a sibling scan would happily make a second group of its name
    /// that KeePassXC then draws as two trash cans. Creating <c>env</c> through this surface is
    /// refused while a home entry's write still makes it: it is the group keypaste writes
    /// home entries under, not a folder somebody chose to make.
    /// </remarks>
    private bool IsReserved(string parentGroupPath, string name)
    {
        if (string.Equals(name, RecycleBinName, StringComparison.Ordinal)
            || (Bin() is { } bin && string.Equals(name, bin.Name, StringComparison.Ordinal)))
        {
            return true;
        }

        return parentGroupPath.Length == 0
            && string.Equals(name, EnvConvention.RootGroup, StringComparison.Ordinal);
    }

    private static bool HasChildNamed(PwGroup parent, string name, PwGroup? except)
    {
        foreach (PwGroup child in parent.Groups)
        {
            if (!ReferenceEquals(child, except) && string.Equals(child.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The group's path, or null when it is not connected to the root group.</summary>
    private string? PathOf(PwGroup group)
    {
        List<string> segments = [];

        PwGroup? current = group;
        while (current is not null && !ReferenceEquals(current, _database.RootGroup))
        {
            segments.Add(current.Name);
            current = current.ParentGroup;
        }

        if (current is null)
        {
            return null;
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static string ChildPath(string groupPath, string name)
    {
        return groupPath.Length == 0 ? name : groupPath + "/" + name;
    }

    /// <summary>
    /// Every live entry directly in each group reached from the root by exactly the path's names, one group
    /// per segment, outside the recycle bin, in traversal order.
    /// </summary>
    /// <remarks>
    /// For keypaste's own records, which a group whose own name holds a <c>/</c> must not stand in for: a
    /// root group named <c>.keypaste/agents</c> joins to the same path as <c>agents</c> inside
    /// <c>.keypaste</c>, and only the second is where ACAF v1 keeps agents' settings.
    /// </remarks>
    private List<(PwGroup Group, PwEntry Entry)> InNamedGroups(string groupPath) =>
        [.. NamedLevels(groupPath)[^1].SelectMany(group => group.Entries.Select(entry => (group, entry)))];

    /// <summary>For each segment of the path in turn, every group reached from the root by the names so far, outside the recycle bin.</summary>
    private List<List<PwGroup>> NamedLevels(string groupPath)
    {
        PwGroup? bin = Bin();
        List<PwGroup> level = [_database.RootGroup];
        List<List<PwGroup>> levels = [];

        foreach (string segment in groupPath.Split('/'))
        {
            level =
            [
                .. level.SelectMany(group => group.Groups)
                    .Where(child => !ReferenceEquals(child, bin) && string.Equals(child.Name, segment, StringComparison.Ordinal)),
            ];
            levels.Add(level);
        }

        return levels;
    }

    /// <summary>Resolves a group path, creating any segment that does not exist yet.</summary>
    /// <remarks>
    /// The first child of a matching name wins, which is right here and nowhere else: this answers
    /// "where should a new entry go", and a person holding two sibling groups of one name in
    /// KeePassXC gets the one KeePassXC also lists first. Finding an entry that already exists is
    /// <see cref="Locate"/>'s question, and that one is answered by traversal.
    /// A path that leads into the recycle bin is refused before anything is created: every read
    /// skips the bin, so an entry written there would be reported created and then never found,
    /// and stepping past the bin to a sibling of its name would draw two trash cans.
    /// One of keypaste's own groups is created with the UUID its path derives (<see cref="AcafIds.Group"/>)
    /// where no object holds it, so two copies that each create it create the same group and a merge
    /// leaves one; a group holding it elsewhere, renamed or recycled, is not brought back, since what
    /// it holds was set aside.
    /// </remarks>
    private PwGroup EnsureGroup(string groupPath)
    {
        PwGroup current = _database.RootGroup;
        if (groupPath.Length == 0)
        {
            return current;
        }

        PwGroup? bin = Bin();
        string path = string.Empty;

        foreach (string segment in groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = ChildPath(path, segment);
            PwGroup? next = null;
            foreach (PwGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    next = child;
                    break;
                }
            }

            if (next is not null && ReferenceEquals(next, bin))
            {
                throw new VaultException(
                    $"'{groupPath}' is inside the Trash; keypaste does not write there. Restore the item or choose another group.");
            }

            if (next is null)
            {
                next = new PwGroup(true, true, segment, PwIcon.Folder);

                if (ReservedGroups.IsReserved(path) && ToPwUuid(AcafIds.Group(path)) is var id && !Holds(id))
                {
                    next.Uuid = id;
                    Untombstone(id);
                }

                current.AddGroup(next, true);
            }

            current = next;
        }

        return current;
    }
}
