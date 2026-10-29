using Keypaste.Core.Internal;
using Keypaste.Core.Totp;

namespace Keypaste.Core.Import;

/// <summary>The items of another password manager's export, read and held in memory, and how they are copied into a vault.</summary>
/// <remarks>
/// <para>
/// <see cref="Plan"/> says where every item lands and names everything that does not, and
/// <see cref="Commit"/> copies what the plan says, all of it or none: a plan that no longer matches the vault is
/// refused, and an item that cannot be added or a save that fails takes everything back out of the open vault.
/// </para>
/// <para>
/// Items land under one group, named after the file unless the person names another, each in its folders
/// below it. A name keypaste would not write is made one it writes, and the plan says why.
/// </para>
/// </remarks>
public sealed class ExportSource : IDisposable
{
    private const string _untitled = "untitled";

    /// <summary>Keepass2Android's name for an item's further web addresses, numbered from 1, which KeePassXC reads in any field whose name starts <c>KP2A_URL</c>.</summary>
    private const string _moreUrlsField = "KP2A_URL_";

    /// <summary>The tag KeePassXC gives an item it adds a passkey to.</summary>
    private const string _passkeyTag = "Passkey";

    /// <summary>Why an attachment over D-0533's cap does not land, which a reader that does not unpack one also says.</summary>
    internal static readonly string AttachmentTooLarge = $"it is larger than {EntryAttachment.MaximumSize / (1024 * 1024)} MiB, the most keypaste attaches";

    private readonly ExportContents _contents;
    private bool _disposed;

    internal ExportSource(string fileName, string format, ExportContents contents)
    {
        FileName = fileName;
        Format = format;
        _contents = contents;
    }

    /// <summary>The export's file name.</summary>
    public string FileName { get; }

    /// <summary>Its format, as its reader names it.</summary>
    public string Format { get; }

    /// <summary>Its items, in its order.</summary>
    public IReadOnlyList<ExportItem> Items => _contents.Items;

    /// <summary>The records it holds that its reader read as no item.</summary>
    public IReadOnlyList<ExportOmission> Unread => _contents.Unread;

    /// <summary>Where every item lands and what does not, against the vault as it is now. Writes nothing.</summary>
    /// <param name="target">The vault imported into.</param>
    /// <param name="into">The group everything lands under; null for the file's name, as <see cref="ImportSource.DefaultPlan"/> names it.</param>
    /// <param name="decisions">What to do with each item, by index, whose name is taken; an item not named here is kept beside the other.</param>
    /// <returns>The plan.</returns>
    public ExportPlan Plan(Vault target, string? into, IReadOnlyDictionary<int, CollisionDecision>? decisions = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        var groupPaths = target.ReadGroupPaths(includeReserved: true);
        var groupCounts = groupPaths.CountBy(path => path, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var intoGroup = into ?? ImportSource.DefaultInto(FileName, groupPaths.ToHashSet(StringComparer.Ordinal));
        var held = target.Search(string.Empty, includeReserved: true).Select(match => Joined(match.Name.GroupPath, match.Name.Title)).ToHashSet(StringComparer.Ordinal);
        var landed = new HashSet<string>(StringComparer.Ordinal);
        var groups = new SortedSet<string>(StringComparer.Ordinal);
        var projects = new SortedSet<string>(StringComparer.Ordinal);
        List<ExportRow> rows = [];
        List<ExportLoss> losses = [.. Unread.Select(unread => new ExportLoss(-1, string.Empty, unread.Part, unread.Reason))];

        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            var label = string.Join("/", item.Folder.Append(item.Title));
            List<string> notes = [];
            var group = Group(intoGroup, item.Folder, notes);
            var title = Landable(item.Title, "item title", notes);
            var path = Joined(group, title);
            var collision = held.Contains(path) ? ExportCollision.VaultItem
                : landed.Contains(path) ? ExportCollision.ExportItem
                : ExportCollision.None;
            var decision = collision == ExportCollision.None ? CollisionDecision.KeepBoth : decisions?.GetValueOrDefault(index) ?? CollisionDecision.KeepBoth;

            if (collision != ExportCollision.None)
            {
                notes.Add(collision == ExportCollision.VaultItem ? $"'{title}' is already an item there" : $"'{title}' is another item of this export");

                if (decision == CollisionDecision.Skip)
                {
                    rows.Add(new ExportRow(index, label, null, collision, decision, string.Join("; ", notes)));
                    continue;
                }

                title = Free(group, title, held, landed);
            }

            landed.Add(Joined(group, title));
            groups.Add(group);
            rows.Add(new ExportRow(index, label, new EntryName(group, title), collision, decision, notes.Count == 0 ? null : string.Join("; ", notes)));

            var parts = Parts(item);
            losses.AddRange(parts.Losses.Select(loss => new ExportLoss(index, label, loss.Part, loss.Reason)));
            projects.UnionWith(parts.Tags.Select(ProjectTag.Read).Where(tag => tag.Kind == ProjectTagKind.Member).Select(tag => tag.Project).OfType<string>());
        }

        var refusal = ImportSource.RefuseDestination(intoGroup, groupCounts)
            ?? groups.Select(group => ImportSource.RefuseDestination(group, groupCounts)).FirstOrDefault(refused => refused is not null);

        return new ExportPlan(intoGroup, rows, losses, [.. projects], refusal);
    }

    /// <summary>Copies what <paramref name="plan"/> says into <paramref name="target"/> and saves it, all of it or none.</summary>
    /// <param name="target">The vault imported into.</param>
    /// <param name="plan">The plan, as the person saw it.</param>
    /// <param name="save">Writes the vault, as the caller saves; when it throws, everything this added is taken back out of the open vault.</param>
    /// <returns>What was copied.</returns>
    /// <remarks>
    /// Every landing item is added whole, with its fields, tags, attachments, earlier versions and times, in one change and
    /// with no history item of its own, under a later second than every version it carries (D-0387). The edit names each
    /// new item (D-0318), and a taken-back one names everything.
    /// </remarks>
    /// <exception cref="VaultException">The plan is refused or no longer matches the vault, or adding failed. Nothing was imported.</exception>
    /// <exception cref="Exception">Whatever <paramref name="save"/> threw, once everything was taken back.</exception>
    public ImportResult Commit(Vault target, ExportPlan plan, Action<Vault> save)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(save);

        if (plan.Refusal is { } refusal)
        {
            throw new VaultException($"Nothing was imported: {refusal}.");
        }

        if (!Plan(target, plan.Into, plan.Decisions).SameAs(plan))
        {
            throw new VaultException("Nothing was imported: the vault changed since the preview. Preview the import again.");
        }

        List<ImportedEntry> entries = [.. plan.Rows.Where(row => row.Lands is not null).Select(row => Entry(row.Lands!, Items[row.Index]))];

        if (entries.Count == 0)
        {
            return new ImportResult(0, 0, 0, VaultEdit.Of());
        }

        var checkpoint = target.Checkpoint();

        try
        {
            var groupsBefore = target.ReadGroupPaths(includeReserved: true).Count;
            var names = target.AddImported(entries);
            var groups = target.ReadGroupPaths(includeReserved: true).Count - groupsBefore;

            save(target);
            return new ImportResult(names.Count, groups, 0, VaultEdit.Of(names));
        }
        catch
        {
            TakeBack(target, checkpoint);
            throw;
        }
    }

    /// <summary>Zeroes every attachment's bytes; the items' other values are strings, which cannot be.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var attachment in Items.SelectMany(item => item.Attachments))
        {
            attachment.Zero();
        }
    }

    /// <summary>Puts the vault back as the checkpoint found it, unless a lock has already disposed it.</summary>
    internal static void TakeBack(Vault vault, VaultCheckpoint checkpoint)
    {
        try
        {
            vault.Restore(checkpoint);
        }
        catch (ObjectDisposedException)
        {
            // Locked meanwhile: the open vault, and what was added to it, is gone.
        }
    }

    private static ImportedEntry Entry(EntryName name, ExportItem item)
    {
        var parts = Parts(item);

        return new ImportedEntry(
            name,
            item.Username,
            item.Password,
            item.Urls.Count > 0 ? item.Urls[0] : string.Empty,
            item.Notes,
            parts.Fields,
            parts.Tags,
            parts.Attachments,
            item.History,
            item.Created,
            item.Modified);
    }

    /// <summary>What of an item lands beyond its standard fields, and what does not, with why.</summary>
    private static ItemParts Parts(ExportItem item)
    {
        List<(string Part, string Reason)> losses = [];
        List<FieldWrite> fields = [];
        HashSet<string> fieldNames = new(StringComparer.Ordinal);

        foreach (var field in item.Fields)
        {
            if (!FieldNameRules.IsWritable(field.Name, out var error))
            {
                losses.Add((Named("field", field.Name), error));
            }
            else if (!fieldNames.Add(field.Name))
            {
                losses.Add((Named("field", field.Name), "the item has another field of that name"));
            }
            else
            {
                fields.Add(new FieldWrite(field.Name, field.Value, field.Protect));
            }
        }

        List<string> tags = [];

        foreach (var tag in item.Tags.Distinct(StringComparer.Ordinal))
        {
            if (TagRules.IsValid(tag, out var error))
            {
                tags.Add(tag);
            }
            else
            {
                losses.Add((Named("tag", tag), error));
            }
        }

        List<ExportAttachment> attachments = [];
        HashSet<string> attachmentNames = new(StringComparer.Ordinal);

        foreach (var attachment in item.Attachments)
        {
            if (!VaultNameRules.IsValidAttachmentName(attachment.Name, out var error))
            {
                losses.Add((Named("attachment", attachment.Name), error));
            }
            else if (attachment.Size > EntryAttachment.MaximumSize)
            {
                losses.Add((Named("attachment", attachment.Name), AttachmentTooLarge));
            }
            else if (!attachmentNames.Add(attachment.Name))
            {
                losses.Add((Named("attachment", attachment.Name), "the item has another attachment of that name"));
            }
            else
            {
                attachments.Add(attachment);
            }
        }

        for (var more = 1; more < item.Urls.Count; more++)
        {
            fields.Add(new FieldWrite($"{_moreUrlsField}{more}", item.Urls[more], Protect: false));
        }

        if (!string.IsNullOrWhiteSpace(item.Totp))
        {
            using var readable = TotpSpec.Read(new Dictionary<string, string> { [TotpSpec.OtpField] = item.Totp }, out var refusal);

            if (readable is null)
            {
                losses.Add(("TOTP", refusal ?? $"The {TotpSpec.OtpField} field is not an otpauth://totp/ URI."));
            }
            else
            {
                fields.Add(new FieldWrite(TotpSpec.OtpField, item.Totp, Protect: true));
            }
        }

        if (item.Passkeys.Count > 0)
        {
            var passkey = item.Passkeys[0];

            // KeePassXC's own names, read by its browser integration and its passkey export, all protected (D-0369).
            fields.Add(new FieldWrite("KPEX_PASSKEY_CREDENTIAL_ID", passkey.CredentialId, Protect: true));
            fields.Add(new FieldWrite("KPEX_PASSKEY_PRIVATE_KEY_PEM", passkey.PrivateKeyPem, Protect: true));
            fields.Add(new FieldWrite("KPEX_PASSKEY_RELYING_PARTY", passkey.RelyingParty, Protect: true));
            fields.Add(new FieldWrite("KPEX_PASSKEY_USERNAME", passkey.Username, Protect: true));
            fields.Add(new FieldWrite("KPEX_PASSKEY_USER_HANDLE", passkey.UserHandle, Protect: true));

            if (!tags.Contains(_passkeyTag, StringComparer.Ordinal))
            {
                tags.Add(_passkeyTag);
            }
        }

        for (var more = 1; more < item.Passkeys.Count; more++)
        {
            losses.Add(($"passkey {more + 1}", "KeePassXC keeps one passkey per item, so only the item's first lands"));
        }

        losses.AddRange(item.Omitted.Select(omitted => (omitted.Part, omitted.Reason)));
        return new ItemParts(fields, tags, attachments, losses);
    }

    private static string Named(string kind, string name) => name.Length == 0 ? $"a {kind} with no name" : $"{kind} {name}";

    /// <summary>The group an item lands in: its folders below <paramref name="into"/>, each made a group name keypaste writes.</summary>
    private static string Group(string into, IReadOnlyList<string> folder, List<string> notes)
    {
        var path = into;

        foreach (var segment in folder)
        {
            var name = Landable(segment, "group name", notes);

            if (string.Equals(name, KeePassInterop.RecycleBinName, StringComparison.Ordinal))
            {
                name = $"{KeePassInterop.RecycleBinName} (imported)";
                notes.Add($"{KeePassInterop.RecycleBinName} is the recycle bin's name");
            }

            path = Joined(path, name);
        }

        return path;
    }

    /// <summary>The name itself when keypaste writes it, otherwise with each separator and control character made <c>-</c> and trimmed, or <c>untitled</c>.</summary>
    private static string Landable(string name, string noun, List<string> notes)
    {
        if (VaultNameRules.IsValidName(name, noun, out var error))
        {
            return name;
        }

        notes.Add(error);
        var made = new string([.. name.Select(c => c is '/' or '\\' || char.IsControl(c) ? '-' : c)]).Trim();

        return made.Length == 0 ? _untitled : made;
    }

    /// <summary>The first of <c>title (2)</c>, <c>title (3)</c> … that neither the vault nor this import holds in the group.</summary>
    private static string Free(string group, string title, HashSet<string> held, HashSet<string> landed)
    {
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{title} ({suffix})";
            var path = Joined(group, candidate);

            if (!held.Contains(path) && !landed.Contains(path))
            {
                return candidate;
            }
        }
    }

    /// <summary>A name joined to the group it is in, as a path addresses it, so a title holding <c>/</c> collides with what its path names.</summary>
    private static string Joined(string group, string name) => group.Length == 0 ? name : group + "/" + name;

    private sealed record ItemParts(
        IReadOnlyList<FieldWrite> Fields,
        IReadOnlyList<string> Tags,
        IReadOnlyList<ExportAttachment> Attachments,
        IReadOnlyList<(string Part, string Reason)> Losses);
}

/// <summary>One item an export import adds, built and checked before anything is written.</summary>
internal sealed record ImportedEntry(
    EntryName Name,
    string Username,
    string Password,
    string Url,
    string Notes,
    IReadOnlyList<FieldWrite> Fields,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ExportAttachment> Attachments,
    IReadOnlyList<ExportVersion> History,
    DateTimeOffset? Created,
    DateTimeOffset? Modified)
{
    /// <summary>The name only, so a log line or a debugger never shows a value.</summary>
    public override string ToString() => Name.ToString();
}
