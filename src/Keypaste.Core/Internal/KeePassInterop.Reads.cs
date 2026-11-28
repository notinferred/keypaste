extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Utility;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>Returns every entry in the vault, depth-first from the root group.</summary>
    /// <remarks>
    /// The recycle bin is not walked. See <see cref="Bin"/> for why that one exclusion is here
    /// rather than at each of the callers that must not see a deleted entry.
    /// </remarks>
    internal IReadOnlyList<VaultEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<VaultEntry> entries = [];
        Collect(_database.RootGroup, string.Empty, entries, Bin());
        return entries;
    }

    /// <summary>Each entry <see cref="ReadEntries"/> returns whose <see cref="VaultEntry.Path"/> is <paramref name="entryPath"/>, in its order; no other entry's value is read.</summary>
    internal IReadOnlyList<VaultEntry> ReadEntriesAt(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<VaultEntry> entries = [];
        CollectAt(_database.RootGroup, string.Empty, entryPath, entries, Bin());
        return entries;
    }

    /// <summary>
    /// Each entry <see cref="ReadEntries"/> returns whose <see cref="VaultEntry.GroupPath"/> is <paramref name="groupPath"/>, in its
    /// order, and the name of each of them whose UUID the vault also holds a tombstone for; no other entry's value is read.
    /// </summary>
    internal IReadOnlyList<VaultEntry> ReadEntriesIn(string groupPath, out IReadOnlySet<EntryName> tombstoned)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HashSet<PwUuid> deleted = [.. _database.DeletedObjects.Select(tombstone => tombstone.Uuid)];
        List<VaultEntry> entries = [];
        HashSet<EntryName> names = [];

        CollectIn(_database.RootGroup, string.Empty, groupPath, entries, deleted, names, Bin());
        tombstoned = names;
        return entries;
    }

    /// <summary>Every entry whose title, group path, username, URL or one of whose own tags contains the query.</summary>
    /// <remarks>
    /// The same traversal as <see cref="Collect"/>, so a result can never name a path a listing
    /// would not report, and the recycle bin is skipped for the reason <see cref="Bin"/> gives.
    /// Only the four fields and the tags are read: <see cref="PwDefs.PasswordField"/> and
    /// <see cref="PwDefs.NotesField"/> are never touched here, which is what makes
    /// <see cref="MatchedFields"/>'s claim about them structural rather than a filter.
    /// </remarks>
    internal IReadOnlyList<EntryMatch> Search(string query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<EntryMatch> matches = [];
        CollectMatches(_database.RootGroup, string.Empty, query, matches, Bin());
        return matches;
    }

    /// <summary>Returns the one entry with this name, or null when the vault holds none.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal VaultEntry? FindEntry(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found ? Read(found.Entry, PathOf(found.Group)!) : null;
    }

    /// <summary>Returns every group path in the vault, excluding the root group.</summary>
    /// <remarks>
    /// Separate from <see cref="ReadEntries"/> because a group holding no entries is invisible in
    /// an entry listing, and <c>keepassxc-cli ls -R -f</c> shows it. A listing that silently drops
    /// empty groups would disagree with KeePassXC about the shape of the same file.
    /// </remarks>
    internal IReadOnlyList<string> ReadGroupPaths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<string> paths = [];
        CollectGroups(_database.RootGroup, string.Empty, paths, Bin());
        return paths;
    }

    /// <summary>Each distinct username a live entry outside keypaste's own groups holds, with how many hold it, by the traversal <see cref="Collect"/> uses; no other field is read.</summary>
    internal IReadOnlyDictionary<string, int> ReadUsernameUses()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Dictionary<string, int> uses = new(StringComparer.Ordinal);
        CollectUsernames(_database.RootGroup, string.Empty, uses, Bin());
        return uses;
    }

    /// <summary>When each live entry outside keypaste's own groups last changed, by its UUID as hex, by the traversal <see cref="Collect"/> uses; no field is read.</summary>
    internal IReadOnlyDictionary<string, DateTimeOffset> ReadModifiedTimes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Dictionary<string, DateTimeOffset> times = new(StringComparer.Ordinal);
        CollectModifiedTimes(_database.RootGroup, string.Empty, times, Bin());
        return times;
    }

    /// <summary>Every live entry as a listing names it, by the traversal <see cref="Collect"/> uses; no value is read.</summary>
    internal IReadOnlyList<ListedEntry> ReadListing()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<ListedEntry> listed = [];
        CollectListing(_database.RootGroup, string.Empty, listed, Bin());
        return listed;
    }

    /// <summary>
    /// Every live entry outside keypaste's own groups as a search names it, by the traversal <see cref="Collect"/> uses; only its URL,
    /// when <paramref name="showsUsername"/>, its username, and when <paramref name="showsLinkedHosts"/>, its host field and every
    /// field's references, are read.
    /// </summary>
    internal IReadOnlyList<IndexedEntry> ReadIndex(bool showsUsername, bool showsLinkedHosts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<IndexedEntry> indexed = [];
        CollectIndex(_database.RootGroup, string.Empty, indexed, Bin(), showsUsername, showsLinkedHosts);
        return indexed;
    }

    /// <summary>A live entry outside the recycle bin and keypaste's own groups by its UUID, as its fields and its own placeholders' fields, or null when there is none.</summary>
    internal (VaultEntry Entry, PlaceholderFields Fields)? ReadLiveByUuid(string uuidHex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (uuidHex.Length != 32 || !uuidHex.All(char.IsAsciiHexDigit)
            || _database.RootGroup.FindEntry(new PwUuid(Convert.FromHexString(uuidHex)), true) is not { } entry
            || PathOf(entry.ParentGroup) is not { } groupPath
            || ReservedGroups.IsReserved(groupPath)
            || IsInBin(entry.ParentGroup))
        {
            return null;
        }

        return (Read(entry, groupPath), PlaceholderFieldsOf(entry, groupPath));
    }

    /// <summary>Every live entry with its own tags, and the variable fields of each one a project tag names, by the traversal <see cref="Collect"/> uses.</summary>
    internal IReadOnlyList<EnvEntry> ReadEnvEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<EnvEntry> found = [];
        CollectEnvEntries(_database.RootGroup, string.Empty, found, Bin());
        return found;
    }

    private static void Collect(PwGroup group, string groupPath, List<VaultEntry> entries, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            entries.Add(Read(entry, groupPath));
        }

        foreach (PwGroup child in group.Groups)
        {
            if (ReferenceEquals(child, bin))
            {
                continue;
            }

            Collect(child, ChildPath(groupPath, child.Name), entries, bin);
        }
    }

    private static void CollectAt(PwGroup group, string groupPath, string entryPath, List<VaultEntry> entries, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            string title = ReadField(entry, PwDefs.TitleField);

            // Joined as VaultEntry.Path joins it, so this finds exactly what comparing every read entry's path found.
            if (string.Equals(groupPath.Length == 0 ? title : groupPath + "/" + title, entryPath, StringComparison.Ordinal))
            {
                entries.Add(Read(entry, groupPath));
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectAt(child, ChildPath(groupPath, child.Name), entryPath, entries, bin);
            }
        }
    }

    private static void CollectIn(
        PwGroup group,
        string groupPath,
        string wanted,
        List<VaultEntry> entries,
        HashSet<PwUuid> deleted,
        HashSet<EntryName> tombstoned,
        PwGroup? bin)
    {
        if (string.Equals(groupPath, wanted, StringComparison.Ordinal))
        {
            foreach (PwEntry entry in group.Entries)
            {
                var read = Read(entry, groupPath);
                entries.Add(read);

                if (deleted.Contains(entry.Uuid))
                {
                    tombstoned.Add(new EntryName(groupPath, read.Title));
                }
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (ReferenceEquals(child, bin))
            {
                continue;
            }

            var childPath = ChildPath(groupPath, child.Name);

            if (MayLeadTo(childPath, wanted))
            {
                CollectIn(child, childPath, wanted, entries, deleted, tombstoned, bin);
            }
        }
    }

    /// <summary>Whether a group or one below it can have the path wanted, as <see cref="ChildPath"/> joins paths.</summary>
    /// <remarks>Below a group whose path is empty a path is its child's name alone; below any other it starts with the group's path and a <c>/</c>.</remarks>
    private static bool MayLeadTo(string groupPath, string wanted) =>
        groupPath.Length == 0
        || (wanted.StartsWith(groupPath, StringComparison.Ordinal)
            && (wanted.Length == groupPath.Length || wanted[groupPath.Length] == '/'));

    private static void CollectUsernames(PwGroup group, string groupPath, Dictionary<string, int> uses, PwGroup? bin)
    {
        if (ReservedGroups.IsReserved(groupPath))
        {
            return;
        }

        foreach (PwEntry entry in group.Entries)
        {
            if (ReadField(entry, PwDefs.UserNameField).Trim() is { Length: > 0 } username)
            {
                uses[username] = uses.GetValueOrDefault(username) + 1;
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectUsernames(child, ChildPath(groupPath, child.Name), uses, bin);
            }
        }
    }

    private static void CollectModifiedTimes(PwGroup group, string groupPath, Dictionary<string, DateTimeOffset> times, PwGroup? bin)
    {
        if (ReservedGroups.IsReserved(groupPath))
        {
            return;
        }

        foreach (PwEntry entry in group.Entries)
        {
            times[entry.Uuid.ToHexString()] = new DateTimeOffset(DateTime.SpecifyKind(entry.LastModificationTime, DateTimeKind.Utc));
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectModifiedTimes(child, ChildPath(groupPath, child.Name), times, bin);
            }
        }
    }

    private static void CollectListing(PwGroup group, string groupPath, List<ListedEntry> listed, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            listed.Add(Listed(entry, new EntryName(groupPath, ReadField(entry, PwDefs.TitleField))));
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectListing(child, ChildPath(groupPath, child.Name), listed, bin);
            }
        }
    }

    private static void CollectIndex(PwGroup group, string groupPath, List<IndexedEntry> indexed, PwGroup? bin, bool showsUsername, bool showsLinkedHosts)
    {
        if (ReservedGroups.IsReserved(groupPath))
        {
            return;
        }

        foreach (PwEntry entry in group.Entries)
        {
            var name = new EntryName(groupPath, ReadField(entry, PwDefs.TitleField)) { Uuid = entry.Uuid.ToHexString() };
            var username = showsUsername ? ReadField(entry, PwDefs.UserNameField) : string.Empty;
            var origin = OriginOf(ReadField(entry, PwDefs.UrlField))
                ?? (showsLinkedHosts && entry.Strings.Get(IndexedEntry.HostField) is { } host ? OriginOf(Uncached(host)) : null);

            indexed.Add(new IndexedEntry(Listed(entry, name), KindOf(entry), origin, username.Length > 0 ? username : null)
            {
                References = showsLinkedHosts ? ReferencesOf(entry) : [],
            });
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectIndex(child, ChildPath(groupPath, child.Name), indexed, bin, showsUsername, showsLinkedHosts);
            }
        }
    }

    /// <summary>The other items an entry's fields reference by UUID, each value read and cleared in place, never kept.</summary>
    private static List<string> ReferencesOf(PwEntry entry)
    {
        HashSet<string> uuids = new(StringComparer.Ordinal);

        foreach (var field in entry.Strings)
        {
            char[] chars = field.Value.ReadChars();

            try
            {
                FieldReferences.Collect(chars, uuids);
            }
            finally
            {
                Array.Clear(chars);
            }
        }

        uuids.Remove(entry.Uuid.ToHexString());
        return [.. uuids.Order(StringComparer.Ordinal)];
    }

    private static ListedEntry Listed(PwEntry entry, EntryName name)
    {
        // Emptiness is read from the length KeePassLib keeps, so no value is decrypted.
        List<string> fields =
        [
            .. _standardFields.Where(field => entry.Strings.Get(field.Value) is { IsEmpty: false }).Select(field => field.Key),
            .. Totp.TotpSpec.MayHold(source => entry.Strings.Get(source) is { IsEmpty: false }) ? [Approval.CredentialFields.Totp] : Array.Empty<string>(),
            .. entry.Strings.Select(field => field.Key)
                .Where(key => !PwDefs.IsStandardField(key) && Approval.CredentialFields.IsAgentCustom(key))
                .Order(StringComparer.Ordinal),
        ];

        return new ListedEntry(name, fields, [.. TagsOf(entry).Where(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member)]);
    }

    private static void CollectEnvEntries(PwGroup group, string groupPath, List<EnvEntry> found, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            List<string> tags = TagsOf(entry);
            List<KeyValuePair<string, string>> fields = [];
            List<string> names = [];

            // A value is read only from an entry a project tag names, so no other entry's fields are held.
            if (tags.Any(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member))
            {
                fields.AddRange(entry.Strings
                    .Where(field => !PwDefs.IsStandardField(field.Key) && EnvConvention.IsEnvNamedField(field.Key))
                    .OrderBy(field => field.Key, StringComparer.Ordinal)
                    .Select(field => new KeyValuePair<string, string>(field.Key, field.Value.ReadString())));
                names.AddRange(entry.Strings
                    .Select(field => field.Key)
                    .Where(key => !PwDefs.IsStandardField(key))
                    .Order(StringComparer.Ordinal));
            }

            found.Add(new EnvEntry(Read(entry, groupPath), tags, fields) { FieldNames = names });
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectEnvEntries(child, ChildPath(groupPath, child.Name), found, bin);
            }
        }
    }

    private static void CollectMatches(
        PwGroup group,
        string groupPath,
        string query,
        List<EntryMatch> matches,
        PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            string title = ReadField(entry, PwDefs.TitleField);
            MatchedFields fields = Matched(title, groupPath, entry, query);
            List<string> tags = query.Length == 0 ? [] : [.. TagsOf(entry).Where(tag => Contains(tag, query))];

            if (tags.Count > 0)
            {
                fields |= MatchedFields.Tag;
            }

            if (query.Length == 0 || fields != MatchedFields.None)
            {
                matches.Add(new EntryMatch(new EntryName(groupPath, title), fields) { Uuid = entry.Uuid.ToHexString(), Tags = tags });
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (ReferenceEquals(child, bin))
            {
                continue;
            }

            CollectMatches(child, ChildPath(groupPath, child.Name), query, matches, bin);
        }
    }

    /// <summary>Which of the four readable fields contain the query.</summary>
    /// <remarks>
    /// Every field is compared, rather than stopping at the first hit, because a caller is told
    /// which ones matched and "the first one in this method's order" is not an answer about the
    /// entry. The comparison is culture-insensitive: a search that changed its results with the
    /// machine's locale would make two people disagree about the contents of one vault.
    /// </remarks>
    private static MatchedFields Matched(string title, string groupPath, PwEntry entry, string query)
    {
        if (query.Length == 0)
        {
            return MatchedFields.None;
        }

        MatchedFields fields = MatchedFields.None;

        if (Contains(title, query))
        {
            fields |= MatchedFields.Title;
        }

        if (Contains(groupPath, query))
        {
            fields |= MatchedFields.GroupPath;
        }

        if (Contains(ReadField(entry, PwDefs.UserNameField), query))
        {
            fields |= MatchedFields.Username;
        }

        if (Contains(ReadField(entry, PwDefs.UrlField), query))
        {
            fields |= MatchedFields.Url;
        }

        return fields;
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void CollectGroups(PwGroup group, string groupPath, List<string> paths, PwGroup? bin)
    {
        foreach (PwGroup child in group.Groups)
        {
            if (ReferenceEquals(child, bin))
            {
                continue;
            }

            string childPath = ChildPath(groupPath, child.Name);
            paths.Add(childPath);
            CollectGroups(child, childPath, paths, bin);
        }
    }

    private static VaultEntry Read(PwEntry entry, string groupPath)
    {
        return new VaultEntry
        {
            Title = ReadField(entry, PwDefs.TitleField),
            Username = ReadField(entry, PwDefs.UserNameField),
            Password = ReadField(entry, PwDefs.PasswordField),
            Url = ReadField(entry, PwDefs.UrlField),
            Notes = ReadField(entry, PwDefs.NotesField),
            GroupPath = groupPath,
            Expires = entry.Expires
                ? new DateTimeOffset(TimeUtil.ToUtc(entry.ExpiryTime, true))
                : null,
            Uuid = entry.Uuid.ToHexString(),
            Kind = KindOf(entry),
        };
    }
}
