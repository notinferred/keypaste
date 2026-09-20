using Keypaste.Core.Internal;

namespace Keypaste.Core;

/// <summary>A KDBX4 vault: create it, add entries, save it, reopen it.</summary>
/// <remarks>
/// Master passwords are spans rather than <c>SecureString</c>, which does not encrypt on Linux or
/// macOS. The span is copied into a UTF-8 buffer, used, and zeroed in a <c>finally</c>; the caller
/// owns the lifetime of whatever backs it.
/// </remarks>
public sealed class Vault : IDisposable
{
    private readonly KeePassInterop _interop;
    private byte[]? _stamp;
    private bool _disposed;

    private Vault(KeePassInterop interop, string path, bool stamp)
    {
        _interop = interop;
        Path = path;
        _stamp = stamp ? SourceSnapshot.Digest(path) : null;
    }

    /// <summary>The path of the file backing this vault.</summary>
    public string Path { get; }

    /// <summary>Whether saves write through a temporary file. A test seam; nothing else reads it.</summary>
    internal bool UsesFileTransactions => _interop.UsesFileTransactions;

    /// <summary>The KDBX UUID of the entry called <paramref name="name"/>, as hex, or
    /// <see langword="null"/> if none has that name. A test seam; keypaste addresses entries by
    /// name.</summary>
    internal string? EntryUuid(EntryName name) => _interop.EntryUuid(name);

    /// <summary>How many deleted-object tombstones the vault carries.</summary>
    /// <remarks>
    /// A test seam, for the reason <see cref="EntryUuid"/> gives. V-V.3a has to be able to say
    /// that recycling writes no tombstone and purging writes one, and no keypaste surface prints
    /// them. The compatibility gate asks KeePassXC the same question about the saved file.
    /// </remarks>
    internal int TombstoneCount => _interop.TombstoneCount;

    /// <summary>Turns this vault's recycle bin on or off.</summary>
    /// <remarks>
    /// A test seam, and the only writer of this setting in keypaste: KeePassXC owns it, and
    /// <see cref="RecyclesDeletedEntries"/> only reads it. What a vault whose owner turned the bin
    /// off does on a delete still has to be assertable without a KeePassXC installation.
    /// </remarks>
    internal void SetRecyclesDeletedEntries(bool recycles) => _interop.SetRecyclesDeletedEntries(recycles);

    /// <summary>Creates a new, empty vault protected by <paramref name="masterPassword"/>. Nothing
    /// is written to disk until <see cref="Save"/>.</summary>
    public static Vault Create(string path, ReadOnlySpan<char> masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // No stamp: there is no file yet, and a Create aimed at an occupied path is a caller
        // saying "make a new vault here" rather than a stale copy of one. `VaultCreation` is what
        // refuses to overwrite, for both front ends, and it does so before reaching this.
        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Create(path, utf8)),
            path,
            stamp: false);
    }

    /// <summary>Opens an existing vault.</summary>
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Open(path, utf8)),
            path,
            stamp: true);
    }

    /// <summary>Adds an entry, creating any groups <see cref="VaultEntry.GroupPath"/> names that do
    /// not exist yet. Call <see cref="Save"/> to persist it.</summary>
    public void AddEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        _interop.AddEntry(entry);
    }

    /// <summary>Overwrites the fields of the entry at <paramref name="entry"/>'s
    /// <see cref="VaultEntry.Path"/>. Call <see cref="Save"/> to persist it.</summary>
    /// <remarks>
    /// The name identifies the entry, so this cannot rename one, and the entry is edited in place
    /// rather than replaced — its UUID, timestamps, attachments and any custom KeePassXC fields
    /// survive. The previous values are kept as a KeePass history item, so overwriting a secret does
    /// not erase the old one (DECISIONS.md D-0014); only <see cref="RemoveEntry(EntryName)"/> does.
    /// </remarks>
    /// <returns><see langword="true"/> if an entry was updated.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public bool UpdateEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        return _interop.UpdateEntry(entry) > 0;
    }

    /// <summary>The earlier states KeePass history keeps for the one entry with this name, newest
    /// first.</summary>
    /// <remarks>
    /// <para>
    /// Ordered by each revision's modification time, and by its position in the file where a KDBX
    /// timestamp's one-second resolution makes two of them equal.
    /// </para>
    /// <para>
    /// <b>An empty list and <see langword="null"/> are different answers.</b> Empty means the entry
    /// is there and has never been changed; null means no entry answers to that name. A caller that
    /// collapses the two — <c>?.Count ?? 0</c> — tells somebody their history is empty when their
    /// entry is gone.
    /// </para>
    /// </remarks>
    /// <returns>The revisions, or <see langword="null"/> when the vault holds no such entry.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public IReadOnlyList<EntryRevision>? ReadHistory(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.ReadHistory(name);
    }

    /// <summary>Makes the revision at <paramref name="index"/> in <see cref="ReadHistory"/>'s order
    /// the entry's current values. Call <see cref="Save"/> to persist it.</summary>
    /// <remarks>
    /// The value it replaces becomes a history item rather than being lost (DECISIONS.md D-0014),
    /// and the entry keeps its UUID, so a restore is an edit like any other: one more revision, not
    /// a new entry. The entry's modification time becomes the moment of the restore (D-0227).
    /// <paramref name="index"/> is only meaningful against a reading of this same vault taken since
    /// its last change.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> if an entry was restored. Restoring nothing is not an error here; the
    /// caller decides whether it is one.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The entry exists and has no revision at <paramref name="index"/>. Nothing is changed.
    /// </exception>
    public bool RestoreRevision(EntryName name, int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.RestoreRevision(name, index) > 0;
    }

    /// <summary>Every entry in the vault, depth-first from the root group.</summary>
    public IReadOnlyList<VaultEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadEntries();
    }

    /// <summary>Finds the one entry with this group path and title, or <see langword="null"/>.</summary>
    /// <remarks>
    /// The unambiguous form, and the one every mutation goes through. <see cref="Find(string)"/> takes
    /// the two joined, and joining is lossy: a title <c>b/c</c> in group <c>a</c> and a title <c>c</c>
    /// in group <c>a/b</c> produce one string, so a caller holding both parts must not join them.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public VaultEntry? Find(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.FindEntry(name);
    }

    /// <summary>Finds the one entry at <paramref name="entryPath"/>, for example
    /// <c>servers/production</c>, or <see langword="null"/>.</summary>
    /// <remarks>
    /// A path is what a person types and what a policy file holds, so it stays addressable, but it is
    /// not an identity: one two entries answer to is refused rather than resolved to whichever the
    /// file lists first. <see cref="Find(EntryName)"/> tells them apart.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that path.</exception>
    public VaultEntry? Find(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryPath);

        return ResolveByPath(entryPath);
    }

    /// <summary>Every group path in the vault except the root, slash-separated.</summary>
    /// <remarks>
    /// Groups holding no entries appear here and nowhere in <see cref="ReadEntries"/>, so a listing
    /// that wants to match KeePassXC's view of the same file needs both.
    /// </remarks>
    public IReadOnlyList<string> ReadGroupPaths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadGroupPaths();
    }

    /// <summary>Whether deleting from this vault moves the entry to the recycle bin.</summary>
    /// <remarks>
    /// <para>
    /// The vault's own setting, which KeePassXC writes and a person can turn off there. keypaste
    /// honours it rather than overriding it: a vault whose owner asked for no recycle bin does not
    /// get one because keypaste would prefer the safety.
    /// </para>
    /// <para>
    /// Read this to word a confirmation truthfully before the act — "there is no undo" is right in
    /// one vault and wrong in another. <see cref="RemoveEntry(EntryName)"/> reports what actually
    /// happened, which is the answer that cannot go stale.
    /// </para>
    /// </remarks>
    public bool RecyclesDeletedEntries
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _interop.RecyclesDeletedEntries;
        }
    }

    /// <summary>Deletes the one entry with this name. Call <see cref="Save"/> to persist it.</summary>
    /// <returns>
    /// What happened to the entry. Deleting nothing is not an error here; the caller decides
    /// whether it is one.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// Where the vault has a recycle bin this is reversible: the entry keeps its identity, its
    /// fields and its history, and <see cref="RestoreRecycled"/> puts it back.
    /// <see cref="PurgeRecycled"/> and <see cref="EmptyRecycleBin"/> are the irreversible ones,
    /// and they are separate on purpose.
    /// </remarks>
    public DeletionOutcome RemoveEntry(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.RemoveEntry(name);
    }

    /// <summary>Deletes the entry at <paramref name="entryPath"/>. Call <see cref="Save"/> to
    /// persist it.</summary>
    /// <returns>
    /// What happened to the entry. Deleting nothing is not an error here; the caller decides
    /// whether it is one.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that path.</exception>
    public DeletionOutcome RemoveEntry(string entryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryPath);

        return ResolveByPath(entryPath) is { } found
            ? _interop.RemoveEntry(EntryName.Of(found))
            : DeletionOutcome.NothingMatched;
    }

    /// <summary>Everything in the recycle bin, or an empty list when there is nothing to recover.</summary>
    /// <remarks>
    /// The rows carry no field values — see <see cref="RecycledEntry"/>. A restored entry is read
    /// back through <see cref="Find(EntryName)"/> like any other.
    /// </remarks>
    public IReadOnlyList<RecycledEntry> ReadRecycled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadRecycled();
    }

    /// <summary>Puts a recycled entry back. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="id">The identity from <see cref="ReadRecycled"/>.</param>
    /// <returns>What happened to the entry. Nothing is changed unless this is a restore.</returns>
    public RestoreOutcome RestoreRecycled(RecycledEntryId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.RestoreRecycled(id);
    }

    /// <summary>Removes one recycled entry and its history for good. Call <see cref="Save"/> to
    /// persist it.</summary>
    /// <param name="id">The identity from <see cref="ReadRecycled"/>.</param>
    /// <returns><see langword="true"/> if an entry was removed.</returns>
    /// <remarks>
    /// Irreversible, and the only route to that: an entry has to be in the bin before this can
    /// reach it, so losing a value takes two deliberate acts rather than one.
    /// </remarks>
    public bool PurgeRecycled(RecycledEntryId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.PurgeRecycled(id);
    }

    /// <summary>Removes everything in the recycle bin for good. Call <see cref="Save"/> to persist
    /// it.</summary>
    /// <returns>The number of entries removed.</returns>
    public int EmptyRecycleBin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.EmptyRecycleBin();
    }

    /// <summary>Whether something else has written to <see cref="Path"/> since this vault read
    /// it.</summary>
    /// <remarks>
    /// A detector, not a lock: a write landing between this call and the one that follows it is still
    /// lost, and closing that window needs a file lock KDBX does not define.
    /// <see langword="false"/> for a vault from <see cref="Create"/> that has never been saved and for
    /// a file that could not be read at all — see <see cref="SourceSnapshot.Digest"/>, and D-0017 for
    /// the transient-failure absorption that depends on it.
    /// On Windows a file another process holds open for writing cannot be read, so a save racing a
    /// concurrent writer narrowly is not detected here. <b>The retry is not what saves that case —
    /// it used to be what lost it.</b> The replace fails, and a retry that merely outlasted the
    /// other writer would then revert it; what makes the race survivable is that
    /// <see cref="Internal.KeePassInterop.Save"/> asks this question again across every wait
    /// (D-0119), leaving only a writer whose hold outlasts a wait and commits inside the attempt
    /// that follows.
    /// </remarks>
    public bool HasFileChangedSinceOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _stamp is { } stamp
            && SourceSnapshot.Digest(Path) is { } current
            && !stamp.AsSpan().SequenceEqual(current);
    }

    /// <summary>Writes the vault to <see cref="Path"/>, encrypted, unless something else wrote
    /// there first.</summary>
    /// <remarks>
    /// Two saves of the same content differ byte for byte: the salt, the nonces and the derived key
    /// material are regenerated on every write.
    /// On <see cref="VaultChangedOnDiskException"/> nothing is written; a caller that has asked a
    /// person and been told to go ahead calls <see cref="SaveOverwriting"/>.
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">Something else wrote to <see cref="Path"/>.</exception>
    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The check is handed to the retry loop, not just made before it. The name a save contends
        // for is most often held by another process saving this same vault, so a retry that waits
        // that out and then writes reverts it — see KeePassInterop.Save and D-0119.
        Commit(HasFileChangedSinceOpen, null, KeePassInterop.SaveAttempts);
    }

    /// <summary>Writes the vault to <see cref="Path"/>, discarding whatever else was written
    /// there.</summary>
    /// <remarks>
    /// For a caller that has put the choice to a person and been told to proceed, and for nothing
    /// else: one reaching for this to avoid handling <see cref="VaultChangedOnDiskException"/> has
    /// turned an audible data loss back into a silent one.
    /// </remarks>
    public void SaveOverwriting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // No check, at any point: this caller has already put the choice to a person and been told
        // to go ahead, so a change arriving mid-retry is one they have already accepted.
        Commit(null, null, KeePassInterop.SaveAttempts);
    }

    /// <summary>
    /// The whole of what a typed path means, in one place. Reading and removing ask this same question
    /// so they cannot reach different answers about the same file — three resolvers that disagreed is
    /// the defect D-0091 found, and a fourth would be the same mistake again.
    /// </summary>
    private VaultEntry? ResolveByPath(string entryPath)
    {
        VaultEntry? found = null;

        foreach (VaultEntry entry in _interop.ReadEntries())
        {
            if (!string.Equals(entry.Path, entryPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                throw new VaultException(
                    $"'{entryPath}' names more than one entry: a title containing a separator and " +
                    "a group of that name produce the same path. Rename one of them in KeePassXC.");
            }

            found = entry;
        }

        return found;
    }

    /// <summary>
    /// Saves, resolving a conflict from inside the retry wait rather than on a timer.
    /// </summary>
    /// <remarks>
    /// Exists so the regression for docs/STEPS.md F.7 can hold the vault's name, and then release
    /// or commit at the one instant that makes the outcome deterministic: after an attempt has been
    /// refused and before the vault is re-read. A timer cannot do it — an attempt is nearly all key
    /// derivation and the move is its last act, so a commit landing mid-attempt lets that attempt
    /// win and the test flakes.
    /// </remarks>
    /// <param name="waitBetweenAttempts">
    /// Called instead of sleeping, so a test can act at the one deterministic instant.
    /// </param>
    /// <param name="attempts">
    /// The retry budget. Defaults to the shipped one; V-F.6 pins it to 1, so a green run proves the
    /// fix removed the contention rather than that the budget outlasted it. Reachable only through
    /// <c>InternalsVisibleTo</c> — it is not a knob a consumer or a command line can turn, because
    /// a smaller budget is strictly worse in production and a larger one hides what V-F.6 catches.
    /// </param>
    /// <param name="duringAttempt">
    /// Called inside each attempt, after the gate is taken and before any work, so a test can hold
    /// the gate the way a slow attempt does.
    /// </param>
    internal void SaveWaiting(
        Action<int>? waitBetweenAttempts,
        int attempts = KeePassInterop.SaveAttempts,
        Action<int>? duringAttempt = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Commit(HasFileChangedSinceOpen, waitBetweenAttempts, attempts, duringAttempt);
    }

    private void Commit(
        Func<bool>? hasChangedOnDisk,
        Action<int>? waitBetweenAttempts,
        int attempts,
        Action<int>? duringAttempt = null)
    {
        var clock = new SaveClock();
        var succeeded = false;

        try
        {
            if (hasChangedOnDisk is not null && clock.Check(hasChangedOnDisk))
            {
                throw new VaultChangedOnDiskException();
            }

            _interop.Save(hasChangedOnDisk, waitBetweenAttempts, clock, attempts, duringAttempt);
            _stamp = clock.Stamp(() => SourceSnapshot.Digest(Path));
            succeeded = true;
        }
        finally
        {
            clock.Publish(succeeded);
        }
    }

    /// <summary>Releases the vault's key material and decrypted contents.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _interop.Dispose();
        _disposed = true;
    }

    /// <summary>Encodes the password to UTF-8, runs <paramref name="use"/>, and zeroes the buffer
    /// whether or not that succeeded.</summary>
    private static KeePassInterop WithUtf8Password(
        ReadOnlySpan<char> masterPassword,
        Func<byte[], KeePassInterop> use)
    {
        byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(masterPassword)];
        try
        {
            Encoding.UTF8.GetBytes(masterPassword, utf8);
            return use(utf8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }
}
