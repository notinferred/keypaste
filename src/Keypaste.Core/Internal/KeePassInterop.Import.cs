extern alias KeePass;

using System.Buffers;
using KeePass::KeePassLib;
using KeePass::KeePassLib.Cryptography.Cipher;
using KeePass::KeePassLib.Cryptography.KeyDerivation;
using KeePass::KeePassLib.Security;
using KeePass::KeePassLib.Serialization;
using Keypaste.Core.Import;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>The longest outer header a probe reads.</summary>
    private const int _probeLimit = 1024 * 1024;

    private static readonly PwUuid _chaCha20Uuid = new ChaCha20Engine().CipherUuid;

    /// <summary>Reads a KDBX file's unencrypted outer header: its version, cipher and KDF.</summary>
    /// <returns>What the header says, or null with <paramref name="error"/> set.</returns>
    internal static KdbxProbe? Probe(string path, out string error)
    {
        byte[] header;
        try
        {
            using var stream = File.OpenRead(path);
            header = new byte[(int)Math.Min(stream.Length, _probeLimit)];
            header = header[..stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"'{path}' could not be read: {ex.Message}";
            return null;
        }

        if (header.Length < 12
            || BinaryPrimitives.ReadUInt32LittleEndian(header) != KdbxFormat.FileSignature1
            || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != KdbxFormat.FileSignature2)
        {
            error = $"'{path}' is not a KeePass database.";
            return null;
        }

        var minor = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
        var major = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
        if (major is not (3 or 4))
        {
            error = $"'{path}' is a KeePass database {major}.{minor}, which keypaste does not read.";
            return null;
        }

        var sizeWidth = major >= 4 ? 4 : 2;
        var cipher = "unknown";
        VaultKeyDerivation? derivation = null;
        var position = 12;

        while (true)
        {
            if (position + 1 + sizeWidth > header.Length)
            {
                error = $"'{path}' ends inside its header.";
                return null;
            }

            var field = header[position];
            var size = sizeWidth == 4
                ? BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(position + 1))
                : BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(position + 1));
            position += 1 + sizeWidth;

            if (size < 0 || size > header.Length - position)
            {
                error = $"'{path}' ends inside its header.";
                return null;
            }

            var data = header.AsSpan(position, size);
            position += size;

            switch (field)
            {
                case 0:
                    error = string.Empty;
                    return new KdbxProbe(path, Path.GetFileName(path), $"KeePass database {major}.{minor}", cipher, derivation?.Name ?? "unknown", null)
                    {
                        KeyDerivation = derivation,
                    };
                case 2 when size == PwUuid.UuidSize:
                    cipher = CipherName(new PwUuid(data.ToArray()));
                    break;
                case 6 when major == 3 && size == sizeof(ulong):
                    derivation = new VaultKeyDerivation(KeyDerivationFunction.AesKdf, BinaryPrimitives.ReadUInt64LittleEndian(data), 0, 0);
                    break;
                case 11:
                    derivation = Derivation(data.ToArray());
                    break;
            }
        }
    }

    private static string CipherName(PwUuid uuid) =>
        uuid.Equals(StandardAesEngine.AesUuid) ? "AES-256"
        : uuid.Equals(_chaCha20Uuid) ? "ChaCha20"
        : uuid.Equals(TwofishCipherEngine.TwofishUuid) ? "Twofish"
        : "unknown";

    private static VaultKeyDerivation? Derivation(byte[] parameters)
    {
        try
        {
            return KdfParameters.DeserializeExt(parameters) is { } read ? Derivation(read) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The group tree an import copies from, as names and titles only.</summary>
    internal ImportNode DescribeForImport()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PwGroup? bin = Bin();
        return Describe(_database.RootGroup);

        ImportNode Describe(PwGroup group) => new(
            group.Uuid.ToHexString(),
            group.Name,
            [.. group.Entries.Select(entry => ReadField(entry, PwDefs.TitleField))],
            [.. group.Groups.Select(Describe)],
            ReferenceEquals(group, bin));
    }

    /// <summary>Copies groups and entries of another vault into this one, all of them or none.</summary>
    /// <param name="source">The vault copied from, which is not changed.</param>
    /// <param name="pieces">What to copy and where; every destination already checked.</param>
    /// <returns>The name of every entry copied.</returns>
    /// <remarks>
    /// Every copy is made before anything is attached, so a failure leaves this vault as it was. A
    /// copy keeps every field, attachment, icon, time and history item, takes new UUIDs throughout,
    /// with field references repointed to match, and leaves the source's recycle bin behind; where
    /// the source came from is not recorded.
    /// </remarks>
    internal IReadOnlyList<EntryName> ImportFrom(KeePassInterop source, IReadOnlyList<ImportPiece> pieces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(source._disposed, source);

        PwUuid binUuid = source.Bin()?.Uuid ?? PwUuid.Zero;
        List<(ImportPiece Piece, PwGroup Copy)> copies = [];
        HashSet<PwUuid> icons = [];
        Dictionary<string, string> renewed = new(StringComparer.OrdinalIgnoreCase);

        foreach (var piece in pieces)
        {
            PwGroup original = source.ImportableGroup(piece.SourceId);
            PwGroup copy = original.CloneDeep();

            if (piece.Scope == ImportScope.WholeGroup)
            {
                WithoutGroup(copy, binUuid);
            }
            else
            {
                copy.Groups.Clear();
            }

            Renew(copy, icons, renewed);
            copies.Add((piece, copy));
        }

        foreach (var (_, copy) in copies)
        {
            Repoint(copy, renewed);
        }

        foreach (var uuid in icons)
        {
            if (_database.GetCustomIconIndex(uuid) < 0
                && source._database.CustomIcons.Find(icon => icon.Uuid.Equals(uuid)) is { } icon)
            {
                _database.CustomIcons.Add(new PwCustomIcon(icon.Uuid, icon.ImageDataPng)
                {
                    Name = icon.Name,
                    LastModificationTime = icon.LastModificationTime,
                });
            }
        }

        List<EntryName> names = [];
        foreach (var (piece, copy) in copies)
        {
            Name(copy, piece.Destination, names);

            if (piece.Scope == ImportScope.EntriesOnly)
            {
                Merge(copy, EnsureImportGroup(piece.Destination, Bin()));
            }
            else if (LocateGroup(piece.Destination) is { } existing)
            {
                Merge(copy, existing.Group);
            }
            else
            {
                int slash = piece.Destination.LastIndexOf('/');
                copy.Name = piece.Destination[(slash + 1)..];
                EnsureImportGroup(slash < 0 ? string.Empty : piece.Destination[..slash], Bin()).AddGroup(copy, true);
            }
        }

        _database.Modified = true;
        return names;

        static void Name(PwGroup group, string path, List<EntryName> names)
        {
            names.AddRange(group.Entries.Select(entry => new EntryName(path, ReadField(entry, PwDefs.TitleField))));
            foreach (PwGroup child in group.Groups)
            {
                Name(child, ChildPath(path, child.Name), names);
            }
        }
    }

    /// <summary>Adds the items an export import built, every one whole before any joins the vault, in groups made as a copy makes them.</summary>
    /// <param name="entries">The items, every name already checked.</param>
    /// <returns>The name of every item added.</returns>
    /// <remarks>
    /// Each takes a new UUID. Its earlier versions are its history, oldest first, with its UUID and their own times, and it
    /// is stamped a later second than every one. Attachments are copied, protected in memory, from the export's bytes.
    /// </remarks>
    internal IReadOnlyList<EntryName> AddImported(IReadOnlyList<ImportedEntry> entries)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<(string GroupPath, PwEntry Entry)> built = [];

        foreach (ImportedEntry imported in entries)
        {
            PwEntry entry = new(true, true);
            SetStandardFields(entry, imported.Name.Title, imported.Username, imported.Password, imported.Url, imported.Notes);

            foreach (FieldWrite field in imported.Fields)
            {
                entry.Strings.Set(field.Name, new ProtectedString(field.Protect ?? true, field.Value!));
            }

            foreach (string tag in imported.Tags)
            {
                AddTagTo(entry, tag);
            }

            foreach (ExportAttachment attachment in imported.Attachments)
            {
                entry.Binaries.Set(attachment.Name, new ProtectedBinary(true, attachment.Bytes));
            }

            if (imported.Created is { } created)
            {
                entry.CreationTime = created.UtcDateTime;
            }

            foreach (ExportVersion version in imported.History.OrderBy(version => version.Modified))
            {
                PwEntry revision = new(false, false) { Uuid = entry.Uuid, CreationTime = entry.CreationTime };
                SetStandardFields(revision, imported.Name.Title, version.Username, version.Password, version.Url, version.Notes);
                revision.LastModificationTime = version.Modified.UtcDateTime;
                revision.LastAccessTime = version.Modified.UtcDateTime;
                entry.History.Add(revision);
            }

            if (imported.Modified is { } modified)
            {
                entry.LastModificationTime = modified.UtcDateTime;
                entry.LastAccessTime = modified.UtcDateTime;

                if (entry.History.UCount > 0)
                {
                    StampAfter(entry, entry.History.Select(revision => revision.LastModificationTime));
                }
            }
            else
            {
                MarkEdited(entry);
            }

            built.Add((imported.Name.GroupPath, entry));
        }

        PwGroup? bin = Bin();
        Dictionary<string, PwGroup> groups = new(StringComparer.Ordinal);
        foreach (var (groupPath, entry) in built)
        {
            if (!groups.TryGetValue(groupPath, out PwGroup? group))
            {
                group = EnsureImportGroup(groupPath, bin);
                groups.Add(groupPath, group);
            }

            group.AddEntry(entry, true);
        }

        _database.Modified = true;
        return [.. entries.Select(imported => imported.Name)];

        static void SetStandardFields(PwEntry entry, string title, string username, string password, string url, string notes)
        {
            SetField(entry, PwDefs.TitleField, title);
            SetField(entry, PwDefs.UserNameField, username);
            SetField(entry, PwDefs.PasswordField, password);
            SetField(entry, PwDefs.UrlField, url);
            SetField(entry, PwDefs.NotesField, notes);
        }
    }

    /// <summary>A live group of this vault by UUID, which an import may copy from.</summary>
    private PwGroup ImportableGroup(string uuidHex)
    {
        PwGroup? bin = Bin();
        PwGroup? group = string.Equals(_database.RootGroup.Uuid.ToHexString(), uuidHex, StringComparison.Ordinal)
            ? _database.RootGroup
            : _database.RootGroup.FindGroup(new PwUuid(Convert.FromHexString(uuidHex)), true);

        return group is null || (bin is not null && (ReferenceEquals(group, bin) || group.IsContainedIn(bin)))
            ? throw new VaultException("A group chosen for import is not in that file.")
            : group;
    }

    private static void WithoutGroup(PwGroup group, PwUuid uuid)
    {
        if (uuid.IsZero)
        {
            return;
        }

        foreach (PwGroup child in group.Groups.CloneShallowToList())
        {
            if (child.Uuid.Equals(uuid))
            {
                group.Groups.Remove(child);
            }
            else
            {
                WithoutGroup(child, uuid);
            }
        }
    }

    /// <summary>New UUIDs for a copied tree, its entries and their history, and no record of where it was.</summary>
    /// <param name="group">The copy.</param>
    /// <param name="icons">Collects every custom icon the copy names.</param>
    /// <param name="renewed">Collects each entry's old UUID against its new one, both as hex.</param>
    private static void Renew(PwGroup group, HashSet<PwUuid> icons, Dictionary<string, string> renewed)
    {
        group.Uuid = new PwUuid(true);
        group.PreviousParentGroup = PwUuid.Zero;
        Note(group.CustomIconUuid);

        foreach (PwEntry entry in group.Entries)
        {
            PwUuid fresh = new(true);
            renewed[entry.Uuid.ToHexString()] = fresh.ToHexString();
            entry.SetUuid(fresh, true);
            entry.PreviousParentGroup = PwUuid.Zero;
            Note(entry.CustomIconUuid);

            foreach (PwEntry revision in entry.History)
            {
                revision.PreviousParentGroup = PwUuid.Zero;
                Note(revision.CustomIconUuid);
            }
        }

        foreach (PwGroup child in group.Groups)
        {
            Renew(child, icons, renewed);
        }

        void Note(PwUuid icon)
        {
            if (!icon.IsZero)
            {
                icons.Add(icon);
            }
        }
    }

    /// <summary>The length of a field reference by UUID: <c>{REF:P@I:</c>, 32 hex digits and <c>}</c>.</summary>
    private const int _uuidReferenceLength = 42;

    private static readonly SearchValues<byte> _hexDigits = SearchValues.Create("0123456789ABCDEFabcdef"u8);

    /// <summary>
    /// Points every <c>{REF:&lt;field&gt;@I:&lt;uuid&gt;}</c> in a copy, history included, at the copy
    /// of the entry it named. A reference to an entry that was not copied is left as it is.
    /// </summary>
    /// <remarks>
    /// Values are rewritten as UTF-8 bytes of the same length and keep their protection, so no
    /// protected value becomes a string.
    /// </remarks>
    private static void Repoint(PwGroup copy, Dictionary<string, string> renewed)
    {
        foreach (PwEntry entry in copy.GetEntries(true))
        {
            RepointEntry(entry);
            foreach (PwEntry revision in entry.History)
            {
                RepointEntry(revision);
            }
        }

        void RepointEntry(PwEntry entry)
        {
            foreach (var (field, value) in entry.Strings.ToList())
            {
                byte[] utf8 = value.ReadUtf8();
                try
                {
                    if (RepointBytes(utf8))
                    {
                        entry.Strings.Set(field, new ProtectedString(value.IsProtected, utf8));
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(utf8);
                }
            }
        }

        bool RepointBytes(byte[] utf8)
        {
            var changed = false;
            for (var i = 0; i + _uuidReferenceLength <= utf8.Length; i++)
            {
                int brace = utf8.AsSpan(i, utf8.Length - _uuidReferenceLength + 1 - i).IndexOf((byte)'{');
                if (brace < 0)
                {
                    break;
                }

                i += brace;
                Span<byte> candidate = utf8.AsSpan(i, _uuidReferenceLength);
                Span<byte> hex = candidate[9..41];

                if (!Ascii.EqualsIgnoreCase(candidate[..5], "{REF:"u8)
                    || "TUPANItupani"u8.IndexOf(candidate[5]) < 0
                    || !Ascii.EqualsIgnoreCase(candidate[6..9], "@I:"u8)
                    || candidate[41] != (byte)'}'
                    || hex.ContainsAnyExcept(_hexDigits)
                    || !renewed.TryGetValue(Encoding.ASCII.GetString(hex), out var fresh))
                {
                    continue;
                }

                Encoding.ASCII.GetBytes(fresh, hex);
                changed = true;
                i += _uuidReferenceLength - 1;
            }

            return changed;
        }
    }

    /// <summary>Moves a copy's entries and groups into an existing group, joining groups of one name.</summary>
    private void Merge(PwGroup copy, PwGroup into)
    {
        PwGroup? bin = Bin();

        foreach (PwEntry entry in copy.Entries.CloneShallowToList())
        {
            into.AddEntry(entry, true);
        }

        foreach (PwGroup child in copy.Groups.CloneShallowToList())
        {
            PwGroup? same = null;
            foreach (PwGroup candidate in into.Groups)
            {
                if (!ReferenceEquals(candidate, bin) && string.Equals(candidate.Name, child.Name, StringComparison.Ordinal))
                {
                    same = candidate;
                    break;
                }
            }

            if (same is null)
            {
                into.AddGroup(child, true);
            }
            else
            {
                Merge(child, same);
            }
        }
    }

    /// <summary><see cref="EnsureGroup"/> that never walks into the recycle bin, whatever it is called.</summary>
    private PwGroup EnsureImportGroup(string groupPath, PwGroup? bin)
    {
        PwGroup current = _database.RootGroup;

        foreach (string segment in groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            PwGroup? next = null;
            foreach (PwGroup child in current.Groups)
            {
                if (!ReferenceEquals(child, bin) && string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                next = new PwGroup(true, true, segment, PwIcon.Folder);
                current.AddGroup(next, true);
            }

            current = next;
        }

        return current;
    }

    /// <summary>What an entry holds beyond <see cref="VaultEntry"/>. A test seam for what an import keeps.</summary>
    internal EntryFacts? FactsUnchecked(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        PwEntry entry = found.Entry;
        return new EntryFacts(
            entry.Uuid.ToHexString(),
            found.Group.Uuid.ToHexString(),
            entry.Strings.ToDictionary(pair => pair.Key, pair => pair.Value.ReadString(), StringComparer.Ordinal),
            entry.Binaries.ToDictionary(pair => pair.Key, pair => pair.Value.ReadData(), StringComparer.Ordinal),
            TagsOf(entry),
            entry.CustomIconUuid.IsZero ? null : entry.CustomIconUuid.ToHexString(),
            _database.GetCustomIconIndex(entry.CustomIconUuid) >= 0,
            entry.CreationTime,
            found.Group.CreationTime,
            entry.Expires ? entry.ExpiryTime : null,
            [.. entry.History.Select(revision => revision.Uuid.ToHexString())],
            [.. entry.History.Select(revision => ReadField(revision, PwDefs.PasswordField))],
            [.. entry.History.Select(IReadOnlyDictionary<string, byte[]> (revision) =>
                revision.Binaries.ToDictionary(pair => pair.Key, pair => pair.Value.ReadData(), StringComparer.Ordinal))]);
    }

    /// <summary>
    /// Writes a vault the way another KeePass application might: a chosen KDF and cipher, custom
    /// fields including KeePassXC's <c>otp</c>, an attachment, a tag, a custom icon, history, a
    /// recycle bin and a <c>.keypaste</c> group. A test seam; keypaste writes none of these itself.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="utf8Password">Its password; the caller zeroes it.</param>
    /// <param name="keyfilePath">Its keyfile, or null.</param>
    /// <param name="kdf"><c>Argon2d</c>, <c>Argon2id</c> or <c>AES-KDF</c>.</param>
    /// <param name="cipher"><c>AES-256</c> or <c>ChaCha20</c>.</param>
    internal static void WriteForeignUnchecked(string path, byte[] utf8Password, string? keyfilePath, string kdf, string cipher)
    {
        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), BuildKey(utf8Password, keyfilePath));
            database.DataCipherUuid = cipher == "ChaCha20" ? _chaCha20Uuid : StandardAesEngine.AesUuid;

            if (kdf == "AES-KDF")
            {
                database.KdfParameters = new AesKdf().GetDefaultParameters();
                database.KdfParameters.SetUInt64(AesKdf.ParamRounds, 1000);
            }
            else
            {
                Argon2Kdf argon = new(kdf == "Argon2id" ? Argon2Type.ID : Argon2Type.D);
                KdfParameters parameters = argon.GetDefaultParameters();
                argon.Randomize(parameters);
                parameters.SetUInt64(Argon2Kdf.ParamIterations, 1);
                parameters.SetUInt64(Argon2Kdf.ParamMemory, 1024 * 1024);
                database.KdfParameters = parameters;
            }

            PwCustomIcon icon = new(new PwUuid(true), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            database.CustomIcons.Add(icon);

            PwGroup root = database.RootGroup;
            root.AddEntry(Foreign("Loose", "loose-pw"), true);

            PwGroup banking = new(true, true, "Banking", PwIcon.Folder) { CreationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
            root.AddGroup(banking, true);

            PwEntry checking = Foreign("Checking", "v1");
            banking.AddEntry(checking, true);
            checking.CreateBackup(database);
            checking.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "v2"));
            checking.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "holder"));
            checking.Strings.Set("PIN", new ProtectedString(true, "4321"));
            checking.Strings.Set("otp", new ProtectedString(true, "otpauth://totp/Bank?secret=JBSWY3DPEHPK3PXP"));
            checking.Binaries.Set("statement.txt", new ProtectedBinary(false, Encoding.UTF8.GetBytes("statement bytes")));
            checking.Tags.Add("finance");
            checking.CustomIconUuid = icon.Uuid;
            checking.Expires = true;
            checking.ExpiryTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            checking.CreationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            PwGroup cards = new(true, true, "Cards", PwIcon.Folder);
            banking.AddGroup(cards, true);
            cards.AddEntry(Foreign("Visa", "visa-pw"), true);

            PwGroup reserved = new(true, true, ReservedGroups.Root, PwIcon.Folder);
            root.AddGroup(reserved, true);
            PwGroup tokens = new(true, true, "tokens", PwIcon.Folder);
            reserved.AddGroup(tokens, true);
            tokens.AddEntry(Foreign("planted", "planted-pw"), true);

            PwGroup bin = new(true, true, RecycleBinName, PwIcon.TrashBin);
            root.AddGroup(bin, true);
            database.RecycleBinUuid = bin.Uuid;
            database.RecycleBinEnabled = true;
            bin.AddEntry(Foreign("Deleted", "deleted-pw"), true);

            database.Save(null);
        }
        finally
        {
            database.Close();
        }

        static PwEntry Foreign(string title, string password)
        {
            PwEntry entry = new(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
            return entry;
        }
    }

    /// <summary>What <see cref="FactsUnchecked"/> reports.</summary>
    internal sealed record EntryFacts(
        string Uuid,
        string GroupUuid,
        IReadOnlyDictionary<string, string> Strings,
        IReadOnlyDictionary<string, byte[]> Attachments,
        IReadOnlyList<string> Tags,
        string? CustomIcon,
        bool CustomIconPresent,
        DateTime Created,
        DateTime GroupCreated,
        DateTime? Expires,
        IReadOnlyList<string> HistoryUuids,
        IReadOnlyList<string> HistoryPasswords,
        IReadOnlyList<IReadOnlyDictionary<string, byte[]>> HistoryAttachments);
}
