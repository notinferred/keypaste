using System.ComponentModel;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace Keypaste.Core.Internal;

/// <summary>
/// The single point of contact between keypaste and KeePassLib.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only file in the repository permitted to reference KeePassLib.</b>
/// Everything above it speaks in <see cref="VaultEntry"/> and <see cref="VaultException"/>.
/// That rule is what keeps docs/PRODUCT.md law 4.3 honest — one core library, with the format
/// dependency behind a seam narrow enough that replacing it is a single-file change rather
/// than an archaeology project.
/// </para>
/// <para>
/// Vendored KeePassLib provenance and local modifications: <c>third_party/KeePassLib/UPSTREAM.md</c>.
/// </para>
/// </remarks>
internal sealed class KeePassInterop : IDisposable
{
    private readonly PwDatabase _database;
    private bool _disposed;

    // KdfPool fills its static list without synchronization; the runtime runs this once and holds concurrent first callers until it returns (F.11).
    static KeePassInterop() => _ = KdfPool.Engines.Count();

    private KeePassInterop(PwDatabase database)
    {
        _database = database;
    }

    /// <summary>Creates a new KDBX4 vault protected by the given UTF-8 master password.</summary>
    /// <remarks>The caller owns <paramref name="utf8Password"/> and is responsible for zeroing it.</remarks>
    internal static KeePassInterop Create(string path, byte[] utf8Password)
    {
        PwDatabase database = new();
        try
        {
            database.New(IOConnectionInfo.FromPath(path), BuildKey(utf8Password));
            ApplyKeypasteFormatSettings(database);
            ApplyWriteSafety(database);
        }
        catch
        {
            database.Close();
            throw;
        }

        return new KeePassInterop(database);
    }

    /// <summary>Opens an existing vault.</summary>
    /// <remarks>The caller owns <paramref name="utf8Password"/> and is responsible for zeroing it.</remarks>
    /// <exception cref="InvalidMasterPasswordException">The password does not open the vault.</exception>
    /// <exception cref="VaultException">The vault could not be read.</exception>
    internal static KeePassInterop Open(string path, byte[] utf8Password)
    {
        PwDatabase database = new();
        try
        {
            database.Open(IOConnectionInfo.FromPath(path), BuildKey(utf8Password), null);
        }
        catch (InvalidCompositeKeyException ex)
        {
            database.Close();
            throw new InvalidMasterPasswordException(
                "The master password is incorrect, or the vault is not a readable KDBX file.", ex);
        }
        catch (Exception ex)
        {
            database.Close();

            // KeePassLib reports an unreadable container, a failed HMAC, and a bad key through
            // several exception types. Anything that is not plainly an I/O problem is treated
            // as "this did not open" and nothing partial is handed back (docs/PRODUCT.md law 3.7).
            throw ex is IOException or UnauthorizedAccessException
                ? new VaultException($"Could not read '{path}'.", ex)
                : new VaultException($"'{path}' could not be opened as a KDBX vault.", ex);
        }

        ApplyWriteSafety(database);
        return new KeePassInterop(database);
    }

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

    /// <summary>Overwrites the fields of the one entry with this entry's name.</summary>
    /// <returns>The number of entries updated: 0 if nothing matched, otherwise 1.</returns>
    /// <remarks>
    /// The name identifies the entry, so this cannot rename one or move it; it changes field
    /// values in place.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int UpdateEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(EntryName.Of(entry)) is not { } found)
        {
            return 0;
        }

        PwEntry pwEntry = found.Entry;

        // Mutated rather than removed and re-added: re-adding mints a new UUID and discards
        // timestamps, attachments and custom string fields keypaste does not model (law 4.6).
        // CreateBackup trims the history list itself, so a separate MaintainBackups call is dead.
        pwEntry.CreateBackup(_database);

        SetField(pwEntry, PwDefs.TitleField, entry.Title);
        SetField(pwEntry, PwDefs.UserNameField, entry.Username);
        SetField(pwEntry, PwDefs.PasswordField, entry.Password);
        SetField(pwEntry, PwDefs.UrlField, entry.Url);
        SetField(pwEntry, PwDefs.NotesField, entry.Notes);

        pwEntry.Touch(true);
        return 1;
    }

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

    /// <summary>Returns the one entry with this name, or null when the vault holds none.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal VaultEntry? FindEntry(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found ? Read(found.Entry, name.GroupPath) : null;
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

    /// <summary>Whether a delete in this vault moves the entry to the recycle bin.</summary>
    /// <remarks>
    /// The vault's own setting, not keypaste's. KeePassXC writes it, a person can turn it off
    /// there, and a vault whose owner asked for no recycle bin does not get one because keypaste
    /// would prefer the safety. A caller that must word a confirmation before the act reads this;
    /// <see cref="RemoveEntry"/> reports what actually happened.
    /// </remarks>
    internal bool RecyclesDeletedEntries => _database.RecycleBinEnabled;

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

    /// <summary>Returns everything in the recycle bin, or an empty list when there is none.</summary>
    internal IReadOnlyList<RecycledEntry> ReadRecycled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Bin() is not { } bin)
        {
            return [];
        }

        List<RecycledEntry> rows = [];
        CollectRecycled(bin, rows);
        return rows;
    }

    /// <summary>Puts a recycled entry back where it was deleted from.</summary>
    /// <returns>What happened to the entry.</returns>
    internal RestoreOutcome RestoreRecycled(RecycledEntryId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Bin() is not { } bin || LocateRecycled(bin, id) is not { } found)
        {
            return RestoreOutcome.NothingMatched;
        }

        PwGroup? original = OriginalGroupOf(found.Entry);
        bool toRoot = original is null;
        PwGroup destination = original ?? _database.RootGroup;

        // Resolved before anything moves. A restore that recreated the one condition every
        // resolver in keypaste refuses — two entries answering to one name — would deny both of
        // them to MCP and take out the whole env project for `keypaste run` (D-0091).
        var restored = new EntryName(
            PathOf(destination) ?? string.Empty,
            ReadField(found.Entry, PwDefs.TitleField));

        if (Matches(restored).Count != 0)
        {
            return RestoreOutcome.DestinationOccupied;
        }

        found.Group.Entries.Remove(found.Entry);
        found.Entry.PreviousParentGroup = PwUuid.Zero;
        destination.AddEntry(found.Entry, true, true);

        return toRoot ? RestoreOutcome.RestoredToRoot : RestoreOutcome.Restored;
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
    /// <returns>The number of entries removed.</returns>
    internal int EmptyRecycleBin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Bin() is not { } bin)
        {
            return 0;
        }

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

        var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin)
        {
            EnableAutoType = false,
            EnableSearching = false,
        };

        _database.RootGroup.AddGroup(bin, true);
        _database.RecycleBinUuid = bin.Uuid;
        _database.RecycleBinChanged = DateTime.UtcNow;
        return bin;
    }

    private void CollectRecycled(PwGroup group, List<RecycledEntry> rows)
    {
        foreach (PwEntry entry in group.Entries)
        {
            rows.Add(new RecycledEntry(
                RecycledEntryId.FromUuidHex(entry.Uuid.ToHexString()),
                ReadField(entry, PwDefs.TitleField),
                OriginalGroupOf(entry) is { } original ? PathOf(original) : null,
                entry.LocationChanged));
        }

        foreach (PwGroup child in group.Groups)
        {
            CollectRecycled(child, rows);
        }
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

    /// <summary>The live group a recycled entry was deleted from, or null when there is none.</summary>
    /// <remarks>
    /// Null covers three cases a restore cannot tell apart and does not need to: the vault records
    /// no previous parent, the group it names is gone, and the group it names is itself recycled.
    /// </remarks>
    private PwGroup? OriginalGroupOf(PwEntry entry)
    {
        if (entry.PreviousParentGroup.IsZero)
        {
            return null;
        }

        PwGroup? original = _database.RootGroup.FindGroup(entry.PreviousParentGroup, true);
        if (original is null)
        {
            return null;
        }

        return Bin() is { } bin && (ReferenceEquals(original, bin) || original.IsContainedIn(bin))
            ? null
            : original;
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

    /// <summary>
    /// Whether saves go through a temporary file. Exists so the regression test for the
    /// open-path defect can assert the mechanism directly; observing the effect is not practical,
    /// because an in-place save also leaves no debris behind.
    /// </summary>
    internal bool UsesFileTransactions => _database.UseFileTransactions;

    /// <summary>Returns the history of the one entry with this name, newest first, or null when
    /// the vault holds no entry of that name.</summary>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<EntryRevision>? ReadHistory(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return null;
        }

        uint[] order = HistoryNewestFirst(found.Entry);
        var revisions = new EntryRevision[order.Length];

        for (var index = 0; index < order.Length; index++)
        {
            PwEntry revision = found.Entry.History.GetAt(order[index]);
            revisions[index] = new EntryRevision(
                index, revision.LastModificationTime, Read(revision, name.GroupPath));
        }

        return revisions;
    }

    /// <summary>Puts the revision at this position in <see cref="ReadHistory"/>'s order back as the
    /// entry's current values.</summary>
    /// <returns>The number of entries restored: 0 if nothing matched, otherwise 1.</returns>
    /// <remarks>
    /// The value being replaced becomes a history item rather than being lost (DECISIONS.md D-0014),
    /// which is what <c>RestoreFromBackup</c> does when it is given the database's history settings.
    /// The entry is mutated in place, so its UUID, attachments and custom fields survive exactly as
    /// they do through <see cref="UpdateEntry"/>.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The entry has no revision at that position. Nothing is changed.
    /// </exception>
    internal int RestoreRevision(EntryName name, int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        // Resolved before anything mutates: RestoreFromBackup takes the item at its raw position and
        // only then creates the backup that can evict it, so a position read later names another
        // revision. An index refused here has also written nothing.
        uint[] order = HistoryNewestFirst(found.Entry);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, order.Length);

        PwEntry pwEntry = found.Entry;
        pwEntry.RestoreFromBackup(order[index], _database);

        // Restoring assigns the old revision's timestamps back, so without this the entry would be
        // older than the value it just replaced: a merge would revert it and history eviction, which
        // drops the oldest, would take it first (DECISIONS.md D-0227).
        pwEntry.Touch(true);
        return 1;
    }

    /// <summary>The entry's KDBX UUID as hex, or null if no entry has that name.</summary>
    /// <remarks>
    /// A test seam. keypaste addresses an entry by name (DECISIONS.md D-0091), and a public UUID
    /// would be a second address form on the surface; V-V.2a still has to be able to say that a
    /// restore mutated the entry rather than replacing it.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal string? EntryUuid(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name)?.Entry.Uuid.ToHexString();
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

    /// <summary>Turns the recycle bin on or off. A test seam; KeePassXC owns this setting.</summary>
    internal void SetRecyclesDeletedEntries(bool recycles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _database.RecycleBinEnabled = recycles;
        _database.RecycleBinChanged = DateTime.UtcNow;
    }

    /// <summary>The raw history positions of one entry, newest first.</summary>
    /// <remarks>
    /// <para>
    /// The one ordering rule, so a read and a restore cannot reach different answers about the same
    /// index — the shape of the defect D-0091 found in entry identity.
    /// </para>
    /// <para>
    /// <b>The tiebreak is the rule, not a detail.</b> KDBX4 stores a timestamp to the second, so
    /// several revisions written inside one second come back from a reopened file with identical
    /// modification times; sorting is stable, so without the position they would be returned oldest
    /// first — the exact inversion. A backup is appended at the end of the list and read back in
    /// document order, so the later position is the later write.
    /// </para>
    /// </remarks>
    private static uint[] HistoryNewestFirst(PwEntry entry)
    {
        return Enumerable.Range(0, (int)entry.History.UCount)
            .OrderByDescending(position => entry.History.GetAt((uint)position).LastModificationTime)
            .ThenByDescending(position => position)
            .Select(position => (uint)position)
            .ToArray();
    }

    /// <summary>How many times a save is attempted before the failure is reported.</summary>
    /// <remarks>
    /// Eight attempts at a rising delay is about 2.2 seconds of contention absorbed. Four at 60ms
    /// bought 360ms, which run 34303291945 exhausted twice on a Windows runner.
    /// </remarks>
    internal const int SaveAttempts = 8;

    /// <summary>Base delay between save attempts. Multiplied by the attempt number.</summary>
    internal const int SaveRetryDelayMilliseconds = 80;

    /// <summary>
    /// What KeePassLib appends to the vault's own path for the temporary file it moves into place.
    /// </summary>
    /// <remarks>
    /// Restated because <c>FileTransactionEx.StrTempSuffix</c> is internal to the vendored
    /// assembly. <c>TheVendoredTemporarySuffixIsStillTheOneWeSweep</c> reads that constant by
    /// reflection and fails if the two ever disagree — on every platform, not only the one where
    /// the Windows regression for this can run.
    /// </remarks>
    internal const string StrandedTemporarySuffix = ".tmp";

    /// <summary>Writes the vault to its backing file.</summary>
    /// <param name="hasChangedOnDisk">
    /// Asked across every retry wait, and the save is abandoned if it answers true. Null for a
    /// caller that has already been told to overwrite whatever is there.
    /// </param>
    /// <param name="waitBetweenAttempts">
    /// Stands in for the wait, so a test can resolve a real conflict at the one moment that makes
    /// the outcome deterministic. Null outside a test.
    /// </param>
    /// <remarks>
    /// <para>
    /// Retried on a transient file error. Saving goes through a file transaction — write a
    /// temporary file, then replace the original — and on Windows that competes with every other
    /// process that watches the filesystem: Defender and the search indexer open a newly written
    /// file to scan it, which makes the replace fail for a few milliseconds at a time.
    /// </para>
    /// <para>
    /// <b>Concurrent saves contend with each other too, and harder — for a reason nobody has
    /// established.</b> KeePassLib's Windows path is Transactional NTFS with its temporary file in
    /// the one shared <c>%TEMP%</c>, and a save there fails "The function attempted to use a name
    /// that is reserved for use by another transaction". That refusal lands in two places: where
    /// the temporary file is opened, upstream of the fallback TxF has for the move, and on the
    /// fallback move itself, where the name refused is the vault's own. Observed on Windows CI in
    /// runs 34303291945 and 34403613553; not reproducible here at 32 overlapping savers, which is
    /// why the budget is set from what the runner needed. <b>Do not write down a mechanism this
    /// retry rests on:</b> the previous one — a directory enlisted in a transaction refusing
    /// operations from outside it — was refuted, and F.6 owns the diagnosis (D-0114).
    /// </para>
    /// <para>
    /// <b>Waiting is why the vault is re-read between attempts.</b> The thing most likely to be
    /// holding the vault's name is another process saving this same vault, so a retry that simply
    /// outlasts it writes a stale copy over the save it was waiting for — and that revert leaves no
    /// history item, because the entry it discarded never existed in this process's tree. That is
    /// the loss <see cref="VaultChangedOnDiskException"/> exists to refuse, and until D-0119 the
    /// only check for it ran once, before the first attempt.
    /// </para>
    /// <para>
    /// <b>What the re-read closes is the wait, and not the attempt.</b> An attempt is nearly all
    /// key derivation and encryption, and the move that can be refused is its last act, so a writer
    /// that commits between the re-read and that move is still reverted. The residual is therefore
    /// a writer whose hold outlasts one of these waits and commits during the attempt that follows
    /// it — accepted because a KeePassLib saver commits immediately after its own move, so its hold
    /// is brief (D-0119). A detector, not a lock.
    /// </para>
    /// <para>
    /// Retrying is otherwise safe precisely because the write is transactional. A failed commit
    /// leaves the original file untouched, so a second attempt starts from the same place as the
    /// first, and a save that never succeeds reports exactly what it reported before.
    /// </para>
    /// <para>
    /// <b>The retry schedule bounds the sleeps and nothing else.</b> A caller waits for the
    /// changed-on-disk checks, the gate, every attempt's work and the sleeps; <see cref="SaveTiming"/>
    /// keeps each separately, because <c>SaveAttempts × SaveRetryDelayMilliseconds</c> describes only
    /// the last of them and a claim about elapsed time needs all of them (F.10a).
    /// </para>
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">
    /// Something else wrote to the vault while this save was waiting to retry or for the gate. Nothing
    /// was written.
    /// </exception>
    /// <param name="clock">Receives this save's timing; the caller publishes it.</param>
    /// <param name="attempts">
    /// How many times to try. Defaults to the shipped budget; V-F.6 pins it to 1 so the retry
    /// cannot absorb the contention the fix is supposed to remove.
    /// </param>
    /// <param name="duringAttempt">Called inside each attempt before its work; null outside a test.</param>
    /// <param name="beforeReplacing">
    /// Called once, on the attempt that is about to replace an existing vault, after the re-read has
    /// passed and while the gate is held. Preserves the bytes being replaced; a throw from it
    /// abandons the save. Null when the caller has already taken its backup, or has none to take.
    /// </param>
    internal void Save(
        Func<bool>? hasChangedOnDisk,
        Action<int>? waitBetweenAttempts,
        SaveClock clock,
        int attempts = SaveAttempts,
        Action<int>? duringAttempt = null,
        Action<string>? beforeReplacing = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        clock.Redirect(ProcessTemporaryDirectory.EnsureRedirected);

        for (int attempt = 1;
            !TryAttempt(hasChangedOnDisk, clock, attempt, attempts, duringAttempt, ref beforeReplacing);
            attempt++)
        {
            var retry = attempt;
            clock.Wait(() =>
            {
                if (waitBetweenAttempts is null)
                {
                    Thread.Sleep(SaveRetryDelayMilliseconds * retry);
                }
                else
                {
                    waitBetweenAttempts(retry);
                }
            });
        }
    }

    /// <summary>One attempt, gated if it can transact; false if it was refused and may be retried.</summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is released before the sleep that follows a refusal</b>, so another save in this
    /// process can finish while this one waits (F.12). That makes the gate wait a wait like the sleep:
    /// another save of this vault can commit during either, so the re-read follows the gate, and a
    /// save that used to queue behind that commit and overwrite it is refused instead (D-0119).
    /// </para>
    /// <para>
    /// <b>A stranded temporary is swept before the gate is released, on every way out.</b> Another
    /// save's fallback move replaces <c>&lt;vault&gt;.tmp</c> and holds no handle between its two
    /// hops, so a sweep outside the gate could delete that save's next bytes mid-move.
    /// </para>
    /// </remarks>
    private bool TryAttempt(
        Func<bool>? hasChangedOnDisk,
        SaveClock clock,
        int attempt,
        int attempts,
        Action<int>? duringAttempt,
        ref Action<string>? beforeReplacing)
    {
        clock.BeginAttempt();
        var gated = EnterGateIfTransacting(clock);
        var strandedATemporary = false;

        try
        {
            if (hasChangedOnDisk is not null && (gated || attempt > 1) && clock.Reread(hasChangedOnDisk))
            {
                throw new VaultChangedOnDiskException();
            }

            if (gated && beforeReplacing is { } preserve)
            {
                // Here and nowhere else. The gate is held, so no other save in this process can
                // replace the vault between the copy and the write it is a backup of; the re-read
                // above has just established that the bytes on disk are the ones this vault was
                // opened from, so the copy is of a file keypaste is known to have read; and `gated`
                // is false exactly when the file does not exist, so a first creation has nothing to
                // preserve and never reaches this.
                //
                // Cleared before the call rather than after, so a backup is attempted once per save
                // however many attempts follow. A throw abandons the save with the vault untouched.
                beforeReplacing = null;
                preserve(_database.IOConnectionInfo.Path);
            }

            clock.Attempt(() =>
            {
                duringAttempt?.Invoke(attempt);
                _database.Save(null);
            });
            return true;
        }
        catch (Exception ex) when (attempt < attempts && IsTransient(ex))
        {
            strandedATemporary = StrandsATemporary(ex);
            return false;
        }
        catch (Exception ex) when (ex is not VaultException)
        {
            strandedATemporary = StrandsATemporary(ex);

            // The cause is named, not merely kept as an inner exception nobody prints. A save can
            // fail for reasons the user can act on — the disk is full, the file is open in
            // KeePassXC, permissions changed — and "Could not save 'vault.kdbx'." on its own tells
            // them none of them.
            throw new VaultException(
                $"Could not save '{_database.IOConnectionInfo.Path}': {ex.Message}", ex);
        }
        finally
        {
            if (strandedATemporary)
            {
                SweepStrandedTemporary();
            }

            if (gated)
            {
                clock.ReleaseGate();
                Volatile.Write(ref _gateHolder, 0);
                _saveGate.Release();
            }
        }
    }

    /// <summary>Takes the save gate before an attempt that can transact, and reports whether it did.</summary>
    /// <remarks>
    /// <para>
    /// <b>Transacted attempts go one at a time in this process.</b> The private directory is the whole
    /// process's, so two transacted attempts would name their temporaries in it together and collide
    /// exactly as two processes used to (D-0122), and on every platform they would share
    /// <c>&lt;vault&gt;.tmp</c> beside one vault. TMP cannot be made per-thread, so those attempts are
    /// serialised instead.
    /// </para>
    /// <para>
    /// <b>Only an attempt over an existing file is gated.</b> KeePassLib transacts only when the
    /// vault file exists, so a first save or one whose directory is gone never uses either name,
    /// and queueing it behind other saves' key derivation was F.10's overrun (D-0134). Gating an
    /// attempt that then does not transact is harmless. The residual is a vault file created
    /// between this check and KeePassLib's own, whose attempt transacts ungated
    /// (<c>TheVendoredSaveTransactsOnlyOverAnExistingFile</c> pins the rule this restates).
    /// </para>
    /// </remarks>
    private bool EnterGateIfTransacting(SaveClock clock)
    {
        if (!File.Exists(_database.IOConnectionInfo.Path))
        {
            return false;
        }

        clock.WaitForGate(Volatile.Read(ref _gateHolder), _saveGate.Wait);
        Volatile.Write(ref _gateHolder, clock.Operation);
        return true;
    }

    /// <summary>Serialises transacted saves in this process. See <see cref="EnterGateIfTransacting"/>.</summary>
    private static readonly SemaphoreSlim _saveGate = new(1, 1);

    /// <summary>The <see cref="SaveClock.Operation"/> holding <see cref="_saveGate"/>; 0 when free.</summary>
    private static long _gateHolder;

    /// <summary>ERROR_TRANSACTIONAL_CONFLICT.</summary>
    /// <remarks>
    /// Observed on Windows 10 Pro 19045 by <c>scripts/txf-probe.cs</c>, on its "a non-transacted
    /// move onto the destination name" line — which is the second hop of the fallback in
    /// <c>FileTransactionEx.TxfMove</c> exactly. Observed again on <c>windows-2025</c> in ci run
    /// 34602290950, where <c>VaultSaveUnderATransactedNameTests</c> asserted this number rather
    /// than skipping — so the refusal is not particular to the Windows 10 floor after all. Both
    /// are deliberate measurements; neither is a CI failure read backwards.
    /// </remarks>
    private const int _errorTransactionalConflict = 6800;

    /// <summary>
    /// Whether a failure is the kind that another attempt might get past.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately narrow. Anything else — a bad path, a full disk, a permission that is simply
    /// wrong — fails on the first attempt, because retrying it would only delay the message.
    /// </para>
    /// <para>
    /// <b>A native failure is admitted by its code, never by <see cref="Win32Exception"/> as a
    /// type.</b> The vendored fallback raises that one type for whatever the move failed with, so
    /// admitting the type would admit a full disk and a bad path from the very same <c>throw</c>,
    /// and both would then take the whole retry budget to say so.
    /// </para>
    /// </remarks>
    private static bool IsTransient(Exception ex) => ex switch
    {
        IOException or UnauthorizedAccessException => true,
        Win32Exception win32 => IsATransientMoveCode(win32.NativeErrorCode),
        _ => false,
    };

    /// <summary>Whether a native move failure is one another attempt might get past.</summary>
    /// <remarks>
    /// Each code here is a recorded observation, named with the platform whose probe produced it.
    /// <b>A code seen only in a CI failure gets its own observation rather than joining this
    /// list</b>: the retry budget grew once on a number nobody had reproduced, and D-0107 is still
    /// carrying it.
    /// </remarks>
    private static bool IsATransientMoveCode(int code) => code switch
    {
        // Windows 10 Pro 19045, scripts/txf-probe.cs. docs/STEPS.md F.7.
        _errorTransactionalConflict => true,
        _ => false,
    };

    /// <summary>
    /// Whether this failure is the one raised only after the fallback move has already put the
    /// vault's next bytes beside the vault.
    /// </summary>
    /// <remarks>
    /// The fallback moves its temporary file onto the vault's drive first and onto the vault
    /// second, so a refusal of the second hop leaves the first hop's file behind. A refusal of the
    /// first hop leaves nothing, and reports a different code.
    /// </remarks>
    private static bool StrandsATemporary(Exception ex) =>
        ex is Win32Exception win32 && IsATransientMoveCode(win32.NativeErrorCode);

    /// <summary>Removes the file the fallback move stranded beside the vault.</summary>
    /// <remarks>
    /// <para>
    /// <b>Opened rather than deleted by name, and only ever this one name.</b> Another writer can
    /// be on KeePassLib's non-transacted path — KeePass 2 with file transactions turned off is one
    /// — where this same name is the live write target, held open across the whole encryption and
    /// renamed onto the vault only after the vault itself has been deleted. Deleting it there
    /// destroys a vault. Asking for <see cref="FileShare.None"/> makes that writer's own handle
    /// refuse us, and a refusal is where this stops: a file somebody still has open is never
    /// touched.
    /// </para>
    /// <para>
    /// The window this does not close is that writer's close-then-rename, where the file is shut
    /// and the vault already deleted. D-0120 records it as accepted rather than solved.
    /// </para>
    /// <para>
    /// Best effort throughout. The caller is on its way to reporting why the save failed, and
    /// failing to tidy up must never replace that message.
    /// </para>
    /// </remarks>
    private void SweepStrandedTemporary()
    {
        try
        {
            using FileStream doomed = new(
                _database.IOConnectionInfo.Path + StrandedTemporarySuffix,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Close() releases the composite key and the decrypted entry data it holds.
        _database.Close();
        _disposed = true;
    }

    private static CompositeKey BuildKey(byte[] utf8Password)
    {
        CompositeKey key = new();

        // bRememberPassword: false — the key material is the SHA-256 of the password; there is
        // no reason to also retain the password itself for the lifetime of the database object.
        key.AddUserKey(new KcpPassword(utf8Password, false));
        return key;
    }

    private static void ApplyKeypasteFormatSettings(PwDatabase database)
    {
        // KDBX4 with Argon2d, at parameters keypaste states rather than inherits. See
        // KdbxFormat for why these are pinned here instead of taken from GetDefaultParameters.
        Argon2Kdf kdf = new(Argon2Type.D);
        KdfParameters parameters = kdf.GetDefaultParameters();
        kdf.Randomize(parameters);

        parameters.SetUInt64(Argon2Kdf.ParamIterations, KdbxFormat.Argon2Iterations);
        parameters.SetUInt64(Argon2Kdf.ParamMemory, KdbxFormat.Argon2Memory);
        parameters.SetUInt32(Argon2Kdf.ParamParallelism, KdbxFormat.Argon2Parallelism);
        parameters.SetUInt32(Argon2Kdf.ParamVersion, KdbxFormat.Argon2Version);

        database.KdfParameters = parameters;
    }

    /// <summary>
    /// Writes through a temporary file and moves it into place, so an interrupted save cannot
    /// truncate a vault that was previously readable.
    /// </summary>
    /// <remarks>
    /// This must be applied on the <see cref="Open"/> path as well as <see cref="Create"/>, and
    /// separately from <see cref="ApplyKeypasteFormatSettings"/>. KeePassLib defaults the flag to
    /// <see langword="false"/> and <c>PwDatabase.Close()</c> — which <c>Open()</c> calls first —
    /// resets it, so an open-modify-save cycle would otherwise write in place. The format settings
    /// cannot simply be re-applied on open instead: they re-randomise the KDF salt, which would
    /// rewrite the key derivation of an existing vault on every save.
    /// </remarks>
    private static void ApplyWriteSafety(PwDatabase database)
    {
        database.UseFileTransactions = true;
    }

    private static void SetField(PwEntry entry, string field, string value)
    {
        // Only the password is marked protected, matching KeePass's own default. The flag
        // controls in-memory protection and the KDBX inner-stream encryption of that field;
        // marking Title or URL protected would make the file open oddly in other KeePass
        // clients for no security gain (docs/PRODUCT.md law 4.6, compatibility is sacred).
        bool protect = string.Equals(field, PwDefs.PasswordField, StringComparison.Ordinal);
        entry.Strings.Set(field, new ProtectedString(protect, value));
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
        };
    }

    private static string ReadField(PwEntry entry, string field)
    {
        return entry.Strings.ReadSafe(field);
    }

    private static string ChildPath(string groupPath, string name)
    {
        return groupPath.Length == 0 ? name : groupPath + "/" + name;
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
    /// Two entries answering to one name are refused rather than resolved to whichever came first,
    /// because there is no answer that is not a guess (docs/PRODUCT.md law 3.7). KDBX permits that
    /// within one group and KeePassXC will make it; <see cref="EntryHandle"/> is what keeps each of
    /// them individually addressable.
    /// </para>
    /// </remarks>
    private (PwGroup Group, PwEntry Entry)? Locate(EntryName name)
    {
        List<(PwGroup Group, PwEntry Entry)> matches = Matches(name);

        if (matches.Count > 1)
        {
            string where = name.GroupPath.Length == 0 ? "the root group" : name.GroupPath;
            throw new VaultException(
                $"'{name.Title}' in '{where}' names {matches.Count} entries. keypaste will not guess " +
                "which one you meant; rename one of them in KeePassXC.");
        }

        return matches.Count == 0 ? null : matches[0];
    }

    /// <summary>Every live entry answering to this name, in traversal order.</summary>
    /// <remarks>
    /// Split out of <see cref="Locate"/> rather than written twice, for the reason that method's
    /// own documentation gives: two traversals that could disagree about what a name means is the
    /// shape of the defect D-0091 found. <see cref="RestoreRecycled"/> needs the count without the
    /// refusal, because it is asking whether putting an entry back would create the ambiguity
    /// rather than whether one is already there.
    /// </remarks>
    private List<(PwGroup Group, PwEntry Entry)> Matches(EntryName name)
    {
        List<(PwGroup Group, PwEntry Entry)> found = [];
        PwGroup? bin = Bin();

        Search(_database.RootGroup, string.Empty);
        return found;

        void Search(PwGroup group, string groupPath)
        {
            if (string.Equals(groupPath, name.GroupPath, StringComparison.Ordinal))
            {
                foreach (PwEntry candidate in group.Entries)
                {
                    if (string.Equals(ReadField(candidate, PwDefs.TitleField), name.Title, StringComparison.Ordinal))
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

                Search(child, ChildPath(groupPath, child.Name));
            }
        }
    }

    /// <summary>Resolves a group path, creating any segment that does not exist yet.</summary>
    /// <remarks>
    /// The first child of a matching name wins, which is right here and nowhere else: this answers
    /// "where should a new entry go", and a person holding two sibling groups of one name in
    /// KeePassXC gets the one KeePassXC also lists first. Finding an entry that already exists is
    /// <see cref="Locate"/>'s question, and that one is answered by traversal.
    /// </remarks>
    private PwGroup EnsureGroup(string groupPath)
    {
        PwGroup current = _database.RootGroup;
        if (groupPath.Length == 0)
        {
            return current;
        }

        foreach (string segment in groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            PwGroup? next = null;
            foreach (PwGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.Ordinal))
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
}
