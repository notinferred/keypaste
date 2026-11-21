extern alias KeePass;

using KeePass::KeePassLib;
using KeePass::KeePassLib.Security;
using KeePass::KeePassLib.Utility;
using Keypaste.Core.Recommendations;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    // The standard fields an agent may ask for, under the names it uses, in CredentialFields.All order.
    private static readonly KeyValuePair<string, string>[] _standardFields =
    [
        new("password", PwDefs.PasswordField),
        new("username", PwDefs.UserNameField),
        new("url", PwDefs.UrlField),
        new("notes", PwDefs.NotesField),
    ];

    /// <summary>The custom fields of the one entry with this name, by name and flag, or null when there is no such entry.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<EntryField>? ReadFields(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found ? FieldsOf(found.Entry) : null;
    }

    /// <summary>An entry's or a revision's custom fields by name and flag, in ordinal order of name.</summary>
    private static List<EntryField> FieldsOf(PwEntry entry) =>
    [
        .. entry.Strings
            .Where(field => !PwDefs.IsStandardField(field.Key))
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => new EntryField(field.Key, field.Value.IsProtected, !FieldNameRules.IsWritable(field.Key, out _))),
    ];

    /// <summary>One custom field's value, or null when there is no such entry or no such field.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal string? ReadCustomField(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found && !PwDefs.IsStandardField(field)
            ? found.Entry.Strings.Get(field)?.ReadString()
            : null;
    }

    /// <summary>The values of the one entry's custom fields whose names <paramref name="include"/> accepts, or null when there is no such entry.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal Dictionary<string, string>? ReadCustomFields(EntryName name, Func<string, bool> include)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found
            ? found.Entry.Strings
                .Where(field => !PwDefs.IsStandardField(field.Key) && include(field.Key))
                .ToDictionary(field => field.Key, field => Uncached(field.Value), StringComparer.Ordinal)
            : null;
    }

    /// <summary>The names of the one entry's other fields, standard or custom, whose value holds a <c>{S:…}</c> naming <paramref name="field"/>, in ordinal order; null when there is no such entry.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<string>? FieldsNaming(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        return
        [
            .. found.Entry.Strings
                .Where(other => !string.Equals(other.Key, field, StringComparison.Ordinal)
                    && KeePassPlaceholders.Names(Uncached(other.Value), field))
                .Select(other => other.Key)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// For each custom field <paramref name="include"/> accepts whose value holds a KeePass placeholder, on the one entry
    /// with this name or on one of its revisions, what it fills in or the first placeholder that refuses it, judged by
    /// name as a terminal edit's new value is; in ordinal order of name, or null when there is no such entry or revision.
    /// </summary>
    /// <param name="name">The entry.</param>
    /// <param name="revision">A position in <see cref="ReadHistory"/>'s order to judge that revision's fields, or null for the entry's own.</param>
    /// <param name="include">Which custom fields are judged.</param>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<KeyValuePair<string, PlantedPlaceholders>>? ReadPlantedPlaceholders(EntryName name, int? revision, Func<string, bool> include)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        PwEntry entry = found.Entry;

        if (revision is { } index)
        {
            uint[] order = HistoryNewestFirst(found.Entry);

            if (index < 0 || index >= order.Length)
            {
                return null;
            }

            entry = found.Entry.History.GetAt(order[index]);
        }

        List<string> names = [.. entry.Strings.Select(field => field.Key).Where(key => !PwDefs.IsStandardField(key))];
        List<KeyValuePair<string, PlantedPlaceholders>> planted = [];

        foreach (var field in entry.Strings
            .Where(field => !PwDefs.IsStandardField(field.Key) && include(field.Key))
            .OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            byte[] utf8 = field.Value.ReadUtf8();

            try
            {
                if (!utf8.AsSpan().Contains((byte)'{'))
                {
                    continue;
                }

                var judged = KeePassPlaceholders.Planted(Encoding.UTF8.GetString(utf8), names, field.Key);

                if (judged.Refused is not null || judged.Fills.Count > 0)
                {
                    planted.Add(KeyValuePair.Create(field.Key, judged));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(utf8);
            }
        }

        return planted;
    }

    /// <summary>Whether a field a TOTP is read from holds anything on the one entry with this name, judged by the length KeePassLib keeps so no value is decrypted.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal bool MayHoldTotp(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found && Totp.TotpSpec.MayHold(field => found.Entry.Strings.Get(field) is { IsEmpty: false });
    }

    /// <summary>
    /// The Auto-Type settings of the one entry with this name and of each group above it, as KeePass 2 and KeePassXC
    /// keep them, with whether its username and password hold anything, judged by length so no value is read; or null
    /// when there is no such entry.
    /// </summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal AutoType.AutoTypeSettings? ReadAutoType(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        PwEntry entry = found.Entry;
        List<AutoType.GroupAutoType> groups = [];

        for (PwGroup? group = found.Group; group is not null; group = group.ParentGroup)
        {
            groups.Add(new AutoType.GroupAutoType(group.EnableAutoType, group.DefaultAutoTypeSequence));
        }

        return new AutoType.AutoTypeSettings(
            entry.AutoType.Enabled,
            entry.AutoType.ObfuscationOptions != KeePass::KeePassLib.Collections.AutoTypeObfuscationOptions.None,
            entry.AutoType.DefaultSequence,
            [.. entry.AutoType.Associations.Select(association => new AutoType.AutoTypeAssociation(association.WindowName, association.Sequence))],
            groups,
            entry.Strings.Get(PwDefs.UserNameField) is { IsEmpty: false },
            entry.Strings.Get(PwDefs.PasswordField) is { IsEmpty: false },
            entry.Expires ? new DateTimeOffset(TimeUtil.ToUtc(entry.ExpiryTime, true)) : null);
    }

    /// <summary>Whether the one entry with this name has a field of this name, standard or custom, matched exactly.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal bool HasField(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found && (PwDefs.IsStandardField(field) || found.Entry.Strings.Exists(field));
    }

    /// <summary>
    /// One field's stored value on the one entry with this name, standard or custom, matched exactly, and left protected in
    /// the open database; a standard field the entry does not hold is empty. Null when there is no such entry or custom field.
    /// </summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal string? ReadStoredField(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        return found.Entry.Strings.Get(field) is { } value ? Uncached(value)
            : PwDefs.IsStandardField(field) ? string.Empty
            : null;
    }

    /// <summary>A copy of the value that leaves it protected in the open database, where <see cref="ProtectedString.ReadString"/> would keep the plaintext for as long as the database stays open.</summary>
    private static string Uncached(ProtectedString value)
    {
        char[] chars = value.ReadChars();

        try
        {
            return new string(chars);
        }
        finally
        {
            Array.Clear(chars);
        }
    }

    /// <summary>What a placeholder in a value of the one entry with this name may fill in from, or null when there is no such entry.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal PlaceholderFields? ReadPlaceholderFields(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found ? PlaceholderFieldsOf(found.Entry, PathOf(found.Group)!) : null;
    }

    /// <summary>
    /// What a placeholder in a value of the revision at this position in <see cref="ReadHistory"/>'s order
    /// may fill in from, or null when there is no such entry or revision.
    /// </summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal PlaceholderFields? ReadPlaceholderFields(EntryName name, int revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        uint[] order = HistoryNewestFirst(found.Entry);

        return revision >= 0 && revision < order.Length
            ? PlaceholderFieldsOf(found.Entry.History.GetAt(order[revision]), PathOf(found.Group)!)
            : null;
    }

    /// <summary>
    /// Whether the revision at this position in <see cref="ReadHistory"/>'s order holds exactly this password; false
    /// when there is no such entry or revision. The revision's copy is zeroed before this returns.
    /// </summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal bool RevisionHoldsPassword(EntryName name, int revision, string password)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return false;
        }

        uint[] order = HistoryNewestFirst(found.Entry);

        if (revision < 0 || revision >= order.Length)
        {
            return false;
        }

        char[] held = found.Entry.History.GetAt(order[revision]).Strings.GetSafe(PwDefs.PasswordField).ReadChars();

        try
        {
            return held.AsSpan().SequenceEqual(password.AsSpan());
        }
        finally
        {
            Array.Clear(held);
        }
    }

    /// <summary>Applies every write to one entry as one edit with one history revision.</summary>
    /// <returns>0 when there is no such entry, otherwise 1.</returns>
    /// <remarks>The caller has checked the names; a write that keeps a value names a field that exists.</remarks>
    /// <exception cref="VaultException">More than one entry answers to that name, or a write keeps the value of a field the entry does not have. Nothing is changed.</exception>
    internal int SetFields(EntryName name, IReadOnlyList<FieldWrite> writes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        PwEntry entry = found.Entry;

        if (writes.FirstOrDefault(write => write.Value is null && !entry.Strings.Exists(write.Name)) is { } missing)
        {
            throw new VaultException($"'{name.Title}' has no field '{missing.Name}'.");
        }

        entry.CreateBackup(_database);

        foreach (FieldWrite write in writes)
        {
            ProtectedString? existing = entry.Strings.Get(write.Name);
            bool protect = write.Protect ?? existing?.IsProtected ?? true;
            entry.Strings.Set(write.Name, new ProtectedString(protect, write.Value ?? existing!.ReadString()));
        }

        MarkEdited(entry);
        return 1;
    }

    /// <summary>
    /// Sets or removes the one entry's TOTP as KeePassXC 2.8's <c>Entry::setTotp</c> does, as one edit with one history
    /// revision: its <c>otp</c>, <c>TOTP Seed</c> and <c>TOTP Settings</c> go, and a URI given is kept as a protected <c>otp</c>.
    /// </summary>
    /// <param name="name">The entry.</param>
    /// <param name="uri">The <c>otpauth://totp/</c> URI the caller has checked, or null to remove the TOTP.</param>
    /// <returns>0 when there is no such entry, or nothing to remove; otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int SetTotp(EntryName name, string? uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        PwEntry entry = found.Entry;
        string[] held = [.. Totp.TotpSpec.KeePassXcFields.Where(entry.Strings.Exists)];

        if (uri is null && held.Length == 0)
        {
            return 0;
        }

        entry.CreateBackup(_database);

        foreach (string field in held)
        {
            entry.Strings.Remove(field);
        }

        if (uri is not null)
        {
            entry.Strings.Set(Totp.TotpSpec.OtpField, new ProtectedString(true, uri));
        }

        MarkEdited(entry);
        return 1;
    }

    /// <summary>Removes one custom field as an edit with one history revision.</summary>
    /// <returns>0 when there is no such entry or field, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int RemoveField(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found || !found.Entry.Strings.Exists(field))
        {
            return 0;
        }

        found.Entry.CreateBackup(_database);
        found.Entry.Strings.Remove(field);
        MarkEdited(found.Entry);
        return 1;
    }

    /// <summary>Gives one custom field another name, keeping its value and its protection, as an edit with one history revision.</summary>
    /// <returns>0 when there is no such entry or field, otherwise 1.</returns>
    /// <remarks>The caller has checked both names.</remarks>
    /// <exception cref="VaultException">Another field of the entry has the new name in any case, or more than one entry answers to that name. Nothing is changed.</exception>
    internal int RenameField(EntryName name, string field, string newName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found || found.Entry.Strings.Get(field) is not { } value)
        {
            return 0;
        }

        // Case counts as taken, because a project refuses two variables that differ only in case.
        if (found.Entry.Strings.Any(other =>
            !string.Equals(other.Key, field, StringComparison.Ordinal)
            && string.Equals(other.Key, newName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new VaultException($"'{name.Title}' already has a field called {EntryNameSanitizer.Sanitize(newName).Text}. Choose another name.");
        }

        found.Entry.CreateBackup(_database);
        found.Entry.Strings.Remove(field);
        found.Entry.Strings.Set(newName, value);
        MarkEdited(found.Entry);
        return 1;
    }

    /// <summary>Every live entry with notes, by the traversal <see cref="Collect"/> uses.</summary>
    internal IReadOnlyList<EntryNotes> ReadNotes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<EntryNotes> found = [];
        CollectNotes(_database.RootGroup, string.Empty, found, Bin());
        return found;
    }

    /// <summary>The one entry's identifier and notes, or null when there is no such entry.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal EntryNotes? ReadNotes(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found
            ? new EntryNotes(name, found.Entry.Uuid.ToHexString(), ReadField(found.Entry, PwDefs.NotesField))
            : null;
    }

    /// <summary>Writes protected fields, replaces the notes and adds a tag, when given, as one edit with one history revision.</summary>
    /// <returns>0 when there is no such entry, otherwise 1.</returns>
    /// <remarks>The caller has checked the names, the notes and the tag; the notes keep their protection flag.</remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int MoveToFields(EntryName name, IReadOnlyList<FieldWrite> writes, string notes, string? tag)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        PwEntry entry = found.Entry;
        entry.CreateBackup(_database);

        foreach (FieldWrite write in writes)
        {
            entry.Strings.Set(write.Name, new ProtectedString(true, write.Value!));
        }

        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(entry.Strings.Get(PwDefs.NotesField)?.IsProtected ?? false, notes));

        if (tag is not null)
        {
            AddTagTo(entry, tag);
        }

        MarkEdited(entry);
        return 1;
    }

    private static void SetField(PwEntry entry, string field, string value)
    {
        // Only the password is marked protected, matching KeePass's own default. The flag
        // controls in-memory protection and the KDBX inner-stream encryption of that field;
        // marking Title or URL protected would make the file open oddly in other KeePass
        // clients for no security gain (SECURITY.md law 14, compatibility is sacred).
        bool protect = string.Equals(field, PwDefs.PasswordField, StringComparison.Ordinal);
        entry.Strings.Set(field, new ProtectedString(protect, value));
    }

    private static void CollectNotes(PwGroup group, string groupPath, List<EntryNotes> found, PwGroup? bin)
    {
        foreach (PwEntry entry in group.Entries)
        {
            if (ReadField(entry, PwDefs.NotesField) is { Length: > 0 } notes)
            {
                found.Add(new EntryNotes(new EntryName(groupPath, ReadField(entry, PwDefs.TitleField)) { Uuid = entry.Uuid.ToHexString() }, entry.Uuid.ToHexString(), notes));
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            if (!ReferenceEquals(child, bin))
            {
                CollectNotes(child, ChildPath(groupPath, child.Name), found, bin);
            }
        }
    }

    private static string ReadField(PwEntry entry, string field)
    {
        return entry.Strings.ReadSafe(field);
    }

    // A field keypaste never releases is named, never read.
    private static PlaceholderFields PlaceholderFieldsOf(PwEntry entry, string groupPath)
    {
        List<string> names = [.. entry.Strings.Select(field => field.Key).Where(key => !PwDefs.IsStandardField(key))];

        return PlaceholderFields.Of(
            Read(entry, groupPath),
            names.Where(Approval.CredentialFields.IsCustom).Select(name => new KeyValuePair<string, string>(name, ReadField(entry, name))),
            names);
    }
}
