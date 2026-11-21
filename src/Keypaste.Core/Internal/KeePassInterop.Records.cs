extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Security;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>Each live entry directly in each group at this path by its groups' own names, by UUID and title, with one of its string fields, in traversal order.</summary>
    internal IReadOnlyList<ReservedRecord> ReadRecords(string groupPath, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return
        [
            .. InNamedGroups(groupPath).Select(found => new ReservedRecord(
                new Guid(found.Entry.Uuid.UuidBytes, bigEndian: true),
                ReadField(found.Entry, PwDefs.TitleField),
                found.Entry.Strings.Get(field)?.ReadString())),
        ];
    }

    /// <summary>
    /// Sets one unprotected string field of the entry with the UUID of this name's title, keeping it
    /// directly in the name's group under that title, and the same field of every other entry there
    /// with the title.
    /// </summary>
    /// <param name="name">Where the entry belongs, and its title.</param>
    /// <param name="idOf">The UUID of the entry for a title.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">What it holds.</param>
    /// <returns>Whether anything changed: false when every entry with the title already holds the value where it belongs.</returns>
    /// <remarks>
    /// <para>
    /// Every copy of the vault that writes the title writes that one entry, so a merge, KeePass's or
    /// KeePassXC's, settles it by time and keeps the older version in its history. It is found wherever
    /// it is: from the recycle bin or another group it is moved back, stamped as moved so a merge
    /// carries the move too. Titled otherwise in the group it is that title's record, so before it is
    /// retitled a copy keeps the other title there, under that title's UUID where no object holds it.
    /// Created, it takes the UUID unless a group holds it, and any record of its deletion is dropped; where
    /// a group holds it, the entry made under a random one is found again by its title in the group, so a
    /// later write changes it rather than making another.
    /// </para>
    /// <para>
    /// Every other entry with the title, which a person or another program made, is given the value
    /// rather than a tombstone: KeePassXC's merge applies no deletion and would bring it back,
    /// while one holding the value settles by time like the first.
    /// </para>
    /// </remarks>
    internal bool WriteRecord(EntryName name, Func<string, Guid> idOf, string field, string value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PwUuid id = ToPwUuid(idOf(name.Title));
        List<(PwGroup Group, PwEntry Entry)> inGroup = InNamedGroups(name.GroupPath);
        PwEntry? kept = _database.RootGroup.FindEntry(id, true)
            ?? (Holds(id) ? inGroup.Select(found => found.Entry).FirstOrDefault(IsTitled) : null);
        bool placed = kept is not null && inGroup.Exists(found => ReferenceEquals(found.Entry, kept));
        bool titled = kept is not null && IsTitled(kept);
        List<PwEntry> others = [.. inGroup.Select(found => found.Entry).Where(entry => !ReferenceEquals(entry, kept) && IsTitled(entry))];

        if (kept is not null && placed && titled && !Tombstoned(kept.Uuid) && others.Prepend(kept).All(HoldsValue))
        {
            return false;
        }

        if (kept is null)
        {
            kept = new PwEntry(false, true) { Uuid = Holds(id) ? new PwUuid(true) : id };
            SetField(kept, PwDefs.TitleField, name.Title);
            SetField(kept, PwDefs.UserNameField, string.Empty);
            SetField(kept, PwDefs.PasswordField, string.Empty);
            SetField(kept, PwDefs.UrlField, string.Empty);
            SetField(kept, PwDefs.NotesField, string.Empty);
            kept.Strings.Set(field, new ProtectedString(false, value));
            EnsureGroup(name.GroupPath).AddEntry(kept, true);
        }
        else
        {
            if (placed && !titled)
            {
                PwUuid otherId = ToPwUuid(idOf(ReadField(kept, PwDefs.TitleField)));
                PwEntry copy = kept.CloneDeep();
                copy.SetUuid(Holds(otherId) ? new PwUuid(true) : otherId, true);
                Untombstone(copy.Uuid);
                kept.ParentGroup.AddEntry(copy, true, true);
            }

            if (!placed)
            {
                kept.ParentGroup.Entries.Remove(kept);
                kept.PreviousParentGroup = PwUuid.Zero;
                EnsureGroup(name.GroupPath).AddEntry(kept, true, true);
            }

            if (!titled || !HoldsValue(kept))
            {
                kept.CreateBackup(_database);
                SetField(kept, PwDefs.TitleField, name.Title);
                kept.Strings.Set(field, new ProtectedString(false, value));
                MarkEdited(kept);
            }
        }

        Untombstone(kept.Uuid);

        foreach (PwEntry other in others.Where(entry => !HoldsValue(entry)))
        {
            other.CreateBackup(_database);
            other.Strings.Set(field, new ProtectedString(false, value));
            MarkEdited(other);
        }

        return true;

        bool IsTitled(PwEntry entry) => string.Equals(ReadField(entry, PwDefs.TitleField), name.Title, StringComparison.Ordinal);

        bool HoldsValue(PwEntry entry) =>
            entry.Strings.Get(field) is { IsProtected: false } current
            && string.Equals(current.ReadString(), value, StringComparison.Ordinal);

        bool Tombstoned(PwUuid uuid) => _database.DeletedObjects.Any(tombstone => tombstone.Uuid.Equals(uuid));
    }

    private static PwUuid ToPwUuid(Guid id) => new(id.ToByteArray(bigEndian: true));

    /// <summary>Whether any group or entry of the vault, the root and the recycle bin's included, has this UUID.</summary>
    private bool Holds(PwUuid id) =>
        _database.RootGroup.Uuid.Equals(id) || _database.RootGroup.FindGroup(id, true) is not null || _database.RootGroup.FindEntry(id, true) is not null;

    /// <summary>Drops every record that the object with this UUID was deleted, as KeePass's own merge drops one an object has outlived.</summary>
    private void Untombstone(PwUuid id)
    {
        foreach (PwDeletedObject tombstone in _database.DeletedObjects.Where(deleted => deleted.Uuid.Equals(id)).ToList())
        {
            _database.DeletedObjects.Remove(tombstone);
        }
    }
}
