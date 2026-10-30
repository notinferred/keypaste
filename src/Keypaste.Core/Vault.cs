using Keypaste.Core.AutoType;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Import;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Internal;
using Keypaste.Core.Recommendations;
using Keypaste.Core.Totp;

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
    private readonly Lock _state = new();
    private byte[]? _stamp;
    private DateTimeOffset? _savedAt;
    private bool _pending;
    private long _changes;
    private bool _writing;
    private bool _disposed;
    private bool _backedUp;
    private bool _rekeyed;
    private VaultBackup? _lastKept;

    /// <summary>The key this unlock's base digests content with, zeroed on disposal (D-0599).</summary>
    private readonly byte[] _baseKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>What this vault keeps of the file as it last read or wrote it, for a merge's lineage (D-0538, D-0599).</summary>
    private KeePassInterop.MergeBase _base;

    private Vault(KeePassInterop interop, string path, byte[]? stamp)
    {
        _interop = interop;
        Path = path;
        _stamp = stamp;
        _savedAt = _stamp is null ? null : WrittenAt(path);
        _base = interop.TakeBase(_baseKey);
    }

    /// <summary>The path of the file backing this vault.</summary>
    public string Path { get; }

    /// <summary>Whether saves write through a temporary file. A test seam; nothing else reads it.</summary>
    internal bool UsesFileTransactions => _interop.UsesFileTransactions;

    /// <summary>KeePassLib's unsaved-changes flag. A test seam; keypaste does not maintain it.</summary>
    internal bool Modified
    {
        get => _interop.Modified;
        set => _interop.Modified = value;
    }

    /// <summary>
    /// What the last save that took a backup did, or null while no save on this vault has replaced
    /// anything. A later save suppressed by the floor or by this unlock's own backup leaves it as
    /// it was, because what it reports — where the copies are kept — has not changed.
    /// </summary>
    /// <remarks>
    /// Exists so one caller can say, once, that keypaste has started keeping copies beside somebody's
    /// vault. A directory of encrypted vaults appearing next to their file without a word is the
    /// discovery docs/PRODUCT.md §6.1 calls a risk to trust.
    /// </remarks>
    public VaultBackupReport? LastBackup { get; private set; }

    /// <summary>Raised after a change to this open vault, naming the entries it touched.</summary>
    /// <remarks>
    /// Raised on the thread that made the change, before the method making it returns and so before
    /// any save of it, so whatever was released from those entries is withdrawn first (D-0318).
    /// </remarks>
    public event EventHandler<VaultEdit>? Edited;

    /// <summary>Raised after a save has written the file, outside the state lock.</summary>
    public event EventHandler? Saved;

    /// <summary>Whether the file holds what this vault holds, and when it was last written.</summary>
    /// <returns>
    /// <see cref="VaultSaveStatus.ChangedOnDisk"/> when something else wrote it, whether or not a change
    /// made here is waiting too; <see cref="VaultSaveStatus.Unsaved"/> while a change is not saved, a
    /// save is writing or nothing was ever read from the file; <see cref="VaultSaveStatus.Unreadable"/>
    /// when the file cannot be read; otherwise <see cref="VaultSaveStatus.Saved"/>.
    /// </returns>
    /// <remarks>Hashes the whole file, as <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> does, so a caller polls it sparingly.</remarks>
    public VaultSaveState SaveState()
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var status = Compare() switch
            {
                SavedRead.Current => VaultSaveStatus.Saved,
                SavedRead.Unsaved => VaultSaveStatus.Unsaved,
                SavedRead.ChangedOnDisk => VaultSaveStatus.ChangedOnDisk,
                _ => VaultSaveStatus.Unreadable,
            };

            return new VaultSaveState(status, _savedAt, Unwritten: status == VaultSaveStatus.ChangedOnDisk && _pending);
        }
    }

    /// <summary>Whether this vault holds what its file holds. Called under <see cref="_state"/>.</summary>
    /// <remarks>
    /// The file is compared while a change is pending too, so another program's save is reported over
    /// a change of this vault's that no save has written, or one was refused for (D-0412). Only while
    /// this vault is writing is a different file its own save in progress.
    /// </remarks>
    private SavedRead Compare()
    {
        if (_writing || _stamp is not { } stamp)
        {
            return SavedRead.Unsaved;
        }

        if (SourceSnapshot.Digest(Path) is not { } current)
        {
            return SavedRead.Unreadable;
        }

        if (!CryptographicOperations.FixedTimeEquals(stamp, current))
        {
            return SavedRead.ChangedOnDisk;
        }

        return _pending ? SavedRead.Unsaved : SavedRead.Current;
    }

    /// <summary>Opens what the file holds now with the factors this vault was opened with.</summary>
    /// <returns>A new vault over <see cref="Path"/>. This one is left as it was, for the caller to dispose.</returns>
    /// <exception cref="InvalidMasterPasswordException">Those factors no longer open the file: another program changed them.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer.</exception>
    /// <exception cref="VaultException">The file could not be read or opened, or this vault's access was changed.</exception>
    /// <remarks>
    /// For a person reloading after another program saved, as KeePassXC reloads (D-0412). The key is
    /// the one this vault already holds, so no password is asked again; a hardware key is, because
    /// its answer depends on the file. The digest is taken before the file is read, so a write landing
    /// between the two leaves the new vault changed on disk rather than stamped over what it missed.
    /// </remarks>
    public Vault Reload()
    {
        KeePassInterop.KeyCopy key;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfRekeyed();
            key = _interop.CopyKey();
        }

        var stamp = SourceSnapshot.Digest(Path) ?? throw new VaultException($"Could not read '{Path}'.");
        return new Vault(KeePassInterop.Reopen(Path, key), Path, stamp);
    }

    /// <summary>Merges this open copy into <paramref name="file"/>, a fresh open of the file another program saved, as D-0537 to D-0541 say, leaving this copy as it was.</summary>
    /// <param name="file">
    /// The file's copy, opened with this vault's key, which becomes the merged copy: unsaved when the merge took anything from this one,
    /// and saved under the stale-write check against the file it was opened from.
    /// </param>
    /// <param name="report">What the merge did, by name and never by value, when it merged.</param>
    /// <returns>Whether it merged, or why not; a refusal changes neither copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is this vault.</exception>
    /// <remarks>Lineage comes from the base this vault took of its file when it opened it and at each save since (D-0538).</remarks>
    internal VaultMergeOutcome MergeInto(Vault file, out VaultMergeReport? report)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (ReferenceEquals(file, this))
        {
            throw new ArgumentException("A vault merges into another open of its file.", nameof(file));
        }

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfRekeyed();

            lock (file._state)
            {
                ObjectDisposedException.ThrowIf(file._disposed, file);
                file.ThrowIfRekeyed();

                var outcome = file._interop.MergeFrom(_interop, _base, out report);

                if (report is { TookFromOpenCopy: true })
                {
                    file._pending = true;
                    file._changes++;
                }

                return outcome;
            }
        }
    }

    /// <summary>How many times one detection of another program's save starts its merge again for a save that landed before the merged copy's.</summary>
    internal const int MergeAttempts = 3;

    /// <summary>Merges another program's save of this vault's file with this open copy and saves the result, as D-0543 says, leaving this copy as it was.</summary>
    /// <returns>
    /// The merged copy and what the merge did, saved over the file unless it took nothing from this copy; or why there is none,
    /// the file and this copy then as they were.
    /// </returns>
    /// <exception cref="VaultException">This vault has never been saved, or its access was changed, so it is opened again instead.</exception>
    /// <remarks>
    /// <para>
    /// The caller holds its edit gate across this call, so no edit of this copy is left out of the merge, and puts the merged copy
    /// in this one's place in the same session. The file's bytes are read once per attempt, digested and parsed with this unlock's
    /// key, a hardware key asked again since its answer depends on the file, so the copy merged is the one the save's stale-write
    /// check holds the file to (D-0119).
    /// </para>
    /// <para>
    /// The merged copy's save keeps the file it replaces whatever the backup floor says. A save landing before it discards the
    /// merged copy and starts again from the file's new bytes, at most <see cref="MergeAttempts"/> times.
    /// </para>
    /// </remarks>
    public VaultMergeSave MergeAnotherSave() => MergeAnotherSave(waitBetweenAttempts: null);

    /// <summary><see cref="MergeAnotherSave()"/>, with <see cref="SaveWaiting"/>'s seams on each merged copy's save.</summary>
    /// <param name="waitBetweenAttempts">Called instead of sleeping between a merged copy's save attempts.</param>
    /// <param name="duringAttempt">Called inside each of a merged copy's save attempts, before any work.</param>
    internal VaultMergeSave MergeAnotherSave(Action<int>? waitBetweenAttempts, Action<int>? duringAttempt = null)
    {
        KeePassInterop.KeyCopy key;
        byte[] stamp;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfRekeyed();
            stamp = _stamp ?? throw new VaultException("This vault has not been saved yet, so there is no file to merge.");
            key = _interop.CopyKey();
        }

        for (var attempt = 1; attempt <= MergeAttempts; attempt++)
        {
            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.Unreadable, $"Could not read '{Path}': {ex.Message}");
            }

            var digest = SHA256.HashData(bytes);

            if (CryptographicOperations.FixedTimeEquals(digest, stamp))
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.Unchanged, "The vault file holds what keypaste last read or wrote.");
            }

            // From the bytes, since a parsed copy keeps no format version (D-0541).
            if (KdbxHeader.Of(bytes) is not { } header)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.Unreadable, $"'{Path}' is not a KeePass database.");
            }

            if (header.FormatMajorVersion < 4)
            {
                return new VaultMergeSave(
                    VaultMergeSaveOutcome.BelowKdbx4,
                    $"'{Path}' was saved as KDBX {header.FormatMajorVersion}.{header.FormatMinorVersion}, which keypaste does not merge; save it as KDBX 4 in KeePassXC.");
            }

            Vault file;

            try
            {
                // Disposed below unless it leaves as the result's copy, a lifetime CA2000 cannot follow.
#pragma warning disable CA2000
                file = new Vault(KeePassInterop.Reopen(Path, bytes, key), Path, digest);
#pragma warning restore CA2000
            }
            catch (InvalidMasterPasswordException ex)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.KeyRefused, ex.Message);
            }
            catch (HardwareKeyException ex)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.HardwareKeyUnanswered, ex.Message);
            }
            catch (VaultException ex)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.Unreadable, ex.Message);
            }

            var handedOver = false;

            try
            {
                var merged = MergeInto(file, out var report);

                if (merged != VaultMergeOutcome.Merged)
                {
                    return merged == VaultMergeOutcome.AnotherVault
                        ? new VaultMergeSave(VaultMergeSaveOutcome.AnotherVault, $"'{Path}' now holds another vault, so keypaste did not merge it.")
                        : new VaultMergeSave(VaultMergeSaveOutcome.DuplicateUuids, "A copy of the vault holds two objects under one UUID, so keypaste did not merge it.");
                }

                if (!report!.TookFromOpenCopy)
                {
                    handedOver = true;
                    return new VaultMergeSave(VaultMergeSaveOutcome.Updated, string.Empty) { Copy = file, Report = report };
                }

                file.Commit(
                    file.HasFileChangedSinceOpen, waitBetweenAttempts, KeePassInterop.SaveAttempts, duringAttempt, backUp: true, applyFloor: false);

                handedOver = true;
                return new VaultMergeSave(VaultMergeSaveOutcome.Saved, string.Empty) { Copy = file, Report = report, Kept = file._lastKept };
            }
            catch (VaultChangedOnDiskException)
            {
                // Another save landed after the bytes were read: merge again with what it wrote.
            }
            catch (HardwareKeyException ex)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.HardwareKeyUnanswered, ex.Message);
            }
            catch (VaultException ex)
            {
                return new VaultMergeSave(VaultMergeSaveOutcome.SaveFailed, ex.Message);
            }
            finally
            {
                if (!handedOver)
                {
                    file.Dispose();
                }
            }
        }

        return new VaultMergeSave(
            VaultMergeSaveOutcome.ChangedAgain,
            $"Another program saved '{Path}' again each time keypaste merged it, so keypaste waits for its next save.");
    }

    /// <summary>When the entry with this name was created and last modified, and when it expires.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its times, or null when no entry answers to that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public EntryTimes? ReadTimes(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.ReadTimes(name);
    }

    /// <summary>The KDBX UUID of the entry called <paramref name="name"/>, as hex, or
    /// <see langword="null"/> if none has that name.</summary>
    /// <remarks>For showing where an entry sits in the file, as KeePassXC knows it. keypaste
    /// addresses entries by name, never by this.</remarks>
    public string? EntryUuid(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.EntryUuid(name);
    }

    /// <summary>The name a live item has now, found by the KDBX UUID a check such as Recommendations' carries.</summary>
    /// <param name="entryUuid">The UUID as hex, as <see cref="EntryUuid"/> writes it.</param>
    /// <returns>The item's name, which a rename since the check changed; null when no item outside the recycle bin and keypaste's own groups has that UUID.</returns>
    /// <remarks>So a finding opens the item it named; every operation still addresses the item by this name.</remarks>
    public EntryName? EntryNameOf(string entryUuid)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entryUuid);

        return _interop.EntryNameOf(entryUuid);
    }

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
    internal void SetRecyclesDeletedEntries(bool recycles) =>
        Change(() => _interop.SetRecyclesDeletedEntries(recycles));

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
            stamp: null);
    }

    /// <summary>Creates a new vault protected by a password and a keyfile.</summary>
    /// <remarks>
    /// <para>
    /// <b>Internal on purpose.</b> This applies none of the rules about which keyfiles keypaste will
    /// attach. The public ways to make such a vault are <see cref="VaultCreation"/>, which applies them
    /// before calling this, and <see cref="ChangeAccess(VaultAccessChange, ReadOnlySpan{char}, ReadOnlySpan{char})"/>.
    /// </para>
    /// <para>
    /// It is also for fixtures. A test that needs a keyfile-protected vault must build it
    /// through the writer under test rather than beside it, for the reason
    /// <c>make-compat-fixture.sh</c> drives the shipped binary (D-0012). The compatibility gate has
    /// the stronger version of the same fixture: there, KeePassXC makes them.
    /// </para>
    /// </remarks>
    internal static Vault CreateWith(
        string path, ReadOnlySpan<char> masterPassword, string? keyfilePath, HardwareKey? hardwareKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Create(path, utf8, keyfilePath, hardwareKey)),
            path,
            stamp: null);
    }

    /// <summary>Opens an existing vault.</summary>
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword) =>
        Open(path, masterPassword, keyfilePath: null);

    /// <summary>Opens an existing vault, optionally protected by a keyfile as well.</summary>
    /// <param name="path">The vault file.</param>
    /// <param name="masterPassword">
    /// The master password. Empty is a passwordless vault when <paramref name="keyfilePath"/> is
    /// given, and a wrong password when it is not; see <c>KeePassInterop.BuildKey</c>.
    /// </param>
    /// <param name="keyfilePath">
    /// The keyfile, or <see langword="null"/> for a vault that has none. Inspect it with
    /// <see cref="VaultKeyfile.Inspect"/> first if the caller wants to say why a file is unusable;
    /// an unreadable one reaching here is an <see cref="InvalidMasterPasswordException"/> like any
    /// other factor that does not open the vault.
    /// </param>
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath) =>
        Open(path, masterPassword, keyfilePath, hardwareKey: null);

    /// <summary>Opens an existing vault that may also need a hardware key.</summary>
    /// <param name="path">The vault file.</param>
    /// <param name="masterPassword">
    /// The master password. Empty is no password when a keyfile or hardware key is given, as it is
    /// in KeePassXC.
    /// </param>
    /// <param name="keyfilePath">The keyfile, or <see langword="null"/> for a vault that has none.</param>
    /// <param name="hardwareKey">
    /// The hardware key's slot, or <see langword="null"/> for a vault that needs none. It is asked
    /// now, and again on every save of the returned vault, which keeps using it; the caller disposes
    /// it after the vault.
    /// </param>
    /// <exception cref="InvalidMasterPasswordException">The factors do not open the vault, which is also what a wrong hardware key or slot gives.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer, so nothing was opened.</exception>
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath, HardwareKey? hardwareKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (keyfilePath is not null && VaultKeyfile.Inspect(keyfilePath).Outcome == KeyfileOutcome.XmlUnreadable)
        {
            throw new UnreadableKeyfileException(keyfilePath);
        }

        // Digested before the key derivation, as Reload does, so a save landing while the key is derived
        // leaves this vault changed on disk rather than stamped with what it never read.
        var stamp = SourceSnapshot.Digest(path);
        var interop = WithUtf8Password(masterPassword, utf8 => KeePassInterop.Open(path, utf8, keyfilePath, hardwareKey));
        return new Vault(interop, path, stamp ?? SourceSnapshot.Digest(path));
    }

    /// <summary>Opens an existing vault with the parts of the key another open of it exported, as the desktop's quick unlock does (D-0550).</summary>
    /// <param name="path">The vault file.</param>
    /// <param name="parts">The parts, which stay the caller's; no keyfile is read.</param>
    /// <param name="hardwareKey">
    /// The hardware key in the slot <paramref name="parts"/> names, asked now and on every save of the returned vault,
    /// or <see langword="null"/> when they name none.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="hardwareKey"/> is not in the slot <paramref name="parts"/> names.</exception>
    /// <exception cref="InvalidMasterPasswordException">The parts no longer open the vault: its key was changed.</exception>
    /// <exception cref="HardwareKeyException">The hardware key did not answer, so nothing was opened.</exception>
    public static Vault Open(string path, VaultKeyParts parts, HardwareKey? hardwareKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(parts);

        if (hardwareKey?.Slot != parts.HardwareKeySlot)
        {
            throw new ArgumentException("The hardware key is not in the slot the key's parts name.", nameof(hardwareKey));
        }

        var stamp = SourceSnapshot.Digest(path);
        var interop = KeePassInterop.Open(path, parts, hardwareKey);
        return new Vault(interop, path, stamp ?? SourceSnapshot.Digest(path));
    }

    /// <summary>The parts this vault's key is made of, for <see cref="Open(string, VaultKeyParts, HardwareKey?)"/>.</summary>
    /// <returns>A copy, which the caller disposes, zeroing it.</returns>
    /// <exception cref="VaultException">This vault's access was changed, so its key is no longer the one it opened with.</exception>
    public VaultKeyParts ExportKeyParts()
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfRekeyed();
            return _interop.ExportKeyParts();
        }
    }

    /// <summary>Adds an entry, creating any groups <see cref="VaultEntry.GroupPath"/> names that do
    /// not exist yet. Call <see cref="Save"/> to persist it.</summary>
    /// <remarks>
    /// Checks no name, so it writes what KeePassXC could, keypaste's own records included. A person's new
    /// entry goes through <see cref="CreateEntry(VaultEntry, IReadOnlyList{FieldWrite}, IReadOnlyList{string})"/>
    /// or <see cref="CreateEntryAtPath"/>, which refuse what <see cref="IsCreatable"/> refuses.
    /// </remarks>
    public void AddEntry(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        Change(() => _interop.AddEntry(entry), VaultEdit.Of(EntryName.Of(entry)));
    }

    /// <summary>Whether keypaste creates an entry with this name for a person: a title <see cref="VaultNameRules"/> accepts, outside keypaste's own groups.</summary>
    /// <param name="name">The new entry's name.</param>
    /// <param name="error">Why it is refused, as a sentence, or empty.</param>
    /// <returns><see langword="true"/> when the name may be created.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static bool IsCreatable(EntryName name, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!VaultNameRules.IsValidTitle(name.Title, out error))
        {
            error = Refused(error).Message;
            return false;
        }

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            error = "keypaste keeps that group for itself. Choose another.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates an entry where a typed path puts it, creating any group the path names that does not
    /// exist yet, with no custom field or tag. Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="entry">The entry's standard fields and the group it goes in.</param>
    /// <remarks>
    /// The name is refused as <see cref="IsCreatable"/> refuses it, and so is a name an entry in that group
    /// already has. A path another entry already answers to is allowed, as KeePassXC allows it, and from
    /// then on <see cref="Find(string)"/> refuses that path until one is renamed.
    /// </remarks>
    /// <exception cref="VaultException">The name is refused, or an entry already has it. Nothing is changed.</exception>
    public void CreateEntryAtPath(VaultEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        var name = EntryName.Of(entry);

        if (!IsCreatable(name, out string error))
        {
            throw new VaultException(error);
        }

        Change(
            () =>
            {
                if (_interop.FindEntry(name) is not null)
                {
                    throw new VaultException($"'{entry.Title}' is already in that group. Choose another title.");
                }

                _interop.AddEntry(entry);
            },
            VaultEdit.Of(name));
    }

    /// <summary>
    /// Creates an entry in an existing group with its custom fields and tags, in one change and with
    /// no history item (D-0379). Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="entry">The entry's standard fields and the group it goes in, which must exist.</param>
    /// <param name="fields">Its custom fields, each named once and holding a value; a field is protected unless its write says otherwise.</param>
    /// <param name="tags">Its tags, each named once.</param>
    /// <remarks>
    /// Every check runs before anything is written: the name by <see cref="IsCreatable"/>, the field
    /// names by <see cref="FieldNameRules"/> and the tags by <see cref="TagRules"/>. The entry's own name
    /// may not be taken in its group, and neither may the path it joins to.
    /// </remarks>
    /// <exception cref="VaultException">Any of those is refused, or the group does not exist or names more than one. Nothing is changed.</exception>
    public void CreateEntry(VaultEntry entry, IReadOnlyList<FieldWrite> fields, IReadOnlyList<string> tags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(tags);

        if (!IsCreatable(EntryName.Of(entry), out string error))
        {
            throw new VaultException(error);
        }

        CheckNew(fields, tags);
        Change(() => _interop.CreateEntry(entry, fields, tags), VaultEdit.Of(EntryName.Of(entry)));
    }

    /// <summary>
    /// Creates a project environment's home entry, <c>env/&lt;project&gt;/.env</c> or
    /// <c>.env.&lt;environment&gt;</c>, tagged into the environment and holding its first fields, in one
    /// change with no history item and creating its group when missing (D-0413). Call
    /// <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="name">The home entry (<see cref="EnvStore.HomeEntry"/>).</param>
    /// <param name="tag">The environment's member tag.</param>
    /// <param name="fields">Its fields, each named once and holding a value.</param>
    /// <exception cref="VaultException">A name or the tag is refused, or the entry's name is taken. Nothing is changed.</exception>
    internal void CreateHomeEntry(EntryName name, string tag, IReadOnlyList<FieldWrite> fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(fields);

        CheckNew(fields, [tag]);
        Change(
            () => _interop.CreateEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title }, fields, [tag], createGroup: true),
            VaultEdit.Of(name));
    }

    /// <summary>Refuses a new entry's fields and tags before anything is written.</summary>
    private static void CheckNew(IReadOnlyList<FieldWrite> fields, IReadOnlyList<string> tags)
    {
        string error;
        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (FieldWrite write in fields)
        {
            if (!FieldNameRules.IsWritable(write.Name, out error))
            {
                throw Refused(error);
            }

            if (!named.Add(write.Name))
            {
                throw new VaultException($"'{write.Name}' is named twice.");
            }

            if (write.Value is null)
            {
                throw new VaultException($"'{write.Name}' has no value.");
            }
        }

        HashSet<string> tagged = new(StringComparer.Ordinal);

        foreach (string tag in tags)
        {
            if (!TagRules.IsValid(tag, out error))
            {
                throw Refused(error);
            }

            if (!tagged.Add(tag))
            {
                throw new VaultException($"The tag '{tag}' is named twice.");
            }
        }
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

        return Change(() => _interop.UpdateEntry(entry) > 0, updated => updated ? VaultEdit.Of(EntryName.Of(entry)) : null);
    }

    /// <summary>The custom fields of the one entry with this name, by name and protection, never by value.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its custom fields in ordinal order of name, or null when no entry answers to that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public IReadOnlyList<EntryField>? Fields(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.ReadFields(name);
    }

    /// <summary>One custom field's value.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="field">The field's name, matched exactly.</param>
    /// <returns>The value, or null when there is no such entry or no such field.</returns>
    /// <remarks>Any custom field is readable, including one keypaste will not write (<see cref="EntryField.IsReadOnly"/>).</remarks>
    /// <exception cref="ArgumentException"><paramref name="field"/> is a standard field, which <see cref="Find(EntryName)"/> reads.</exception>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public string? ReadField(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        if (FieldNameRules.IsStandard(field))
        {
            throw new ArgumentException($"'{field}' is a standard field, not a custom one.", nameof(field));
        }

        return _interop.ReadCustomField(name, field);
    }

    /// <summary>The item's TOTP, read in KeePassXC's order from its <c>TOTP Settings</c> and <c>TOTP Seed</c>, its <c>otp</c> field or KeePass 2's <c>TimeOtp-*</c> fields (D-0477).</summary>
    /// <param name="name">The entry.</param>
    /// <param name="refusal">Why the item's TOTP cannot be computed, naming a field and never a value; null otherwise.</param>
    /// <returns>
    /// The spec, which the caller disposes; null when there is no such entry, it has no TOTP, or
    /// <paramref name="refusal"/> says why it cannot be computed.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public TotpSpec? ReadTotp(EntryName name, out string? refusal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        refusal = null;

        return _interop.ReadCustomFields(name, TotpSpec.IsSourceField) is { } fields
            ? TotpSpec.Read(fields, out refusal)
            : null;
    }

    /// <summary>
    /// What an Auto-Type of one item into a window with this title would type, resolved whole from the vault as it is
    /// now: the sequence KeePass's and KeePassXC's settings choose for the title, every field it names filled in as
    /// D-0465 fills a value, or why it types nothing (D-0604 to D-0607).
    /// </summary>
    /// <param name="name">The item.</param>
    /// <param name="windowTitle">The title of the window it would type into, as the platform reports it.</param>
    /// <param name="clock">The clock, for whether the item has expired.</param>
    /// <returns>
    /// The plan, which the caller disposes when typing ends; null when no item answers to that name or the item is
    /// in one of keypaste's own groups, which Auto-Type never offers. A recycled item answers to no name.
    /// </returns>
    /// <remarks>Nothing is typed here; only the person, in the unlocked app, types a plan (D-0547).</remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public AutoTypePlan? PlanAutoType(EntryName name, string windowTitle, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(windowTitle);
        ArgumentNullException.ThrowIfNull(clock);

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (ReservedGroups.IsReserved(name.GroupPath) || _interop.ReadAutoType(name) is not { } settings)
            {
                return null;
            }

            var item = new AutoTypeItem(
                field => _interop.HasField(name, field),
                field => _interop.ReadStoredField(name, field),
                value => Filled(value, () => _interop.ReadPlaceholderFields(name))!,
                (out string? refusal) =>
                {
                    refusal = null;
                    return _interop.ReadCustomFields(name, TotpSpec.IsSourceField) is { } fields ? TotpSpec.Read(fields, out refusal) : null;
                });

            return AutoTypePlan.Resolve(settings, windowTitle, clock.GetUtcNow(), item, WindowPattern.Timeout);
        }
    }

    /// <summary>Whether the item may have a TOTP: a field one is read from holds anything, judged by length so no value is read.</summary>
    /// <param name="name">The entry.</param>
    /// <returns><see langword="true"/> when it may, though <see cref="ReadTotp"/> may still refuse it; <see langword="false"/> when no entry answers to that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public bool MayHoldTotp(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.MayHoldTotp(name);
    }

    /// <summary>Fills in the KeePass placeholders a value of one item holds from that item's own fields, as this vault holds it now, saved or not (D-0465).</summary>
    /// <param name="name">The item the value belongs to.</param>
    /// <param name="value">The value as the vault holds it.</param>
    /// <returns>
    /// The value as it leaves keypaste, or why it cannot leave; null when it holds a placeholder and no item
    /// answers to that name. A value holding none comes back as it is, any escape made braces, and nothing is read.
    /// </returns>
    /// <remarks>What is shown or edited stays the stored text; only what leaves is filled in.</remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public PlaceholderFill? FillPlaceholders(EntryName name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return Filled(value, () => _interop.ReadPlaceholderFields(name));
        }
    }

    /// <summary><see cref="FillPlaceholders(EntryName, string)"/> for the password of one of the item's revisions, from that revision's own fields.</summary>
    /// <param name="name">The item the revision belongs to.</param>
    /// <param name="revision">The revision's position in <see cref="ReadHistory"/>'s order.</param>
    /// <param name="password">The revision's password, as a reading that named this position gave it.</param>
    /// <returns>
    /// The password as it leaves keypaste, or why it cannot leave; null when the item has no revision at that
    /// position or the one there no longer holds that password, judged under the lock its fields are read under,
    /// so an edit saved since the reading cannot fill it in from another revision's fields.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public PlaceholderFill? FillPlaceholders(EntryName name, int revision, string password)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(password);

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _interop.RevisionHoldsPassword(name, revision, password)
                ? Filled(password, () => _interop.ReadPlaceholderFields(name, revision))
                : null;
        }
    }

    /// <summary>Sets one or several custom fields of one entry as a single edit. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="writes">The fields to write, each named once.</param>
    /// <returns><see langword="true"/> if the entry was found and written.</returns>
    /// <remarks>
    /// The whole set makes one history revision, as KeePassXC's own edit does, and the edit names the
    /// entry (D-0318). A new field is protected unless its write says otherwise; an existing one keeps
    /// its flag unless its write names one. Standard fields and the entry's other data are untouched.
    /// </remarks>
    /// <exception cref="VaultException">
    /// A name is not writable (<see cref="FieldNameRules.IsWritable"/>), two writes share a name, a
    /// write keeps the value of a field the entry does not have, or more than one entry answers to
    /// that name. Nothing is changed.
    /// </exception>
    public bool SetFields(EntryName name, IReadOnlyList<FieldWrite> writes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(writes);

        if (writes.Count == 0)
        {
            throw new VaultException("No field was named.");
        }

        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (FieldWrite write in writes)
        {
            if (!FieldNameRules.IsWritable(write.Name, out string error))
            {
                throw Refused(error);
            }

            if (!named.Add(write.Name))
            {
                throw new VaultException($"'{write.Name}' is named twice.");
            }
        }

        return Change(() => _interop.SetFields(name, writes) > 0, written => written ? VaultEdit.Of(name) : null);
    }

    /// <summary>Adds one custom field the entry does not have yet, as <see cref="SetFields"/> writes one. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="write">The new field.</param>
    /// <returns><see langword="true"/> if the entry was found and written.</returns>
    /// <remarks>Adding refuses a name the entry already has, where <see cref="SetFields"/> replaces its value: they are different acts a person asks for.</remarks>
    /// <exception cref="VaultException">The entry already has that field, or <see cref="SetFields"/> refuses the write. Nothing is changed.</exception>
    public bool AddField(EntryName name, FieldWrite write)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(write);

        if (Fields(name) is { } current && current.Any(field => string.Equals(field.Name, write.Name, StringComparison.Ordinal)))
        {
            throw new VaultException($"This item already has {EntryNameSanitizer.Sanitize(write.Name).Text}. Replace its value instead.");
        }

        return SetFields(name, [write]);
    }

    /// <summary>Removes one custom field as an edit with one history revision. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="field">The field's name, matched exactly.</param>
    /// <returns><see langword="true"/> if the field was there and was removed.</returns>
    /// <exception cref="VaultException">The name is not writable, or more than one entry answers to that name. Nothing is changed.</exception>
    public bool RemoveField(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        if (!FieldNameRules.IsWritable(field, out string error))
        {
            throw Refused(error);
        }

        return Change(() => _interop.RemoveField(name, field) > 0, removed => removed ? VaultEdit.Of(name) : null);
    }

    /// <summary>Gives one custom field another name as an edit with one history revision. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="field">The field's name, matched exactly.</param>
    /// <param name="newName">What to call it.</param>
    /// <returns><see langword="true"/> if the field was there and was renamed.</returns>
    /// <remarks>
    /// The value and its protection go with it, and the old name stays in the revision. Both names are
    /// refused as <see cref="FieldNameRules.IsWritable"/> refuses them, and so is a name a TOTP is read
    /// from (D-0477), since either rename would make or unmake the item's TOTP.
    /// </remarks>
    /// <exception cref="VaultException">
    /// A name is refused, the two are the same, another field of the entry has the new name in any case,
    /// or more than one entry answers to that name. Nothing is changed.
    /// </exception>
    public bool RenameField(EntryName name, string field, string newName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(newName);

        string[] names = [field, newName];

        foreach (string named in names)
        {
            if (!FieldNameRules.IsWritable(named, out string error))
            {
                throw Refused(error);
            }

            if (TotpSpec.IsSourceField(named))
            {
                throw new VaultException($"{EntryNameSanitizer.Sanitize(named).Text} is a field an item's TOTP is read from, and keypaste does not rename one.");
            }
        }

        if (string.Equals(field, newName, StringComparison.Ordinal))
        {
            throw new VaultException("The field is already called that.");
        }

        return Change(() => _interop.RenameField(name, field, newName) > 0, renamed => renamed ? VaultEdit.Of(name) : null);
    }

    /// <summary>The item's attachments by name and size, never by content.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its attachments in ordinal order of name, or null when no entry answers to that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public IReadOnlyList<EntryAttachment>? Attachments(EntryName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _interop.ReadAttachments(name);
        }
    }

    /// <summary>Attaches content under a name the item has no attachment by, as an edit with one history revision. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="attachment">The new attachment's name, which <see cref="VaultNameRules.IsValidAttachmentName"/> must accept.</param>
    /// <param name="content">What it holds, read from its position to its end, at most <see cref="EntryAttachment.MaximumSize"/> bytes.</param>
    /// <param name="keptInHistory">Whether the item's history keeps the item as it was, false when the vault's history limits drop that revision (D-0533).</param>
    /// <returns><see langword="true"/> if the entry was found and the attachment added.</returns>
    /// <remarks>
    /// The content is read into one buffer that is zeroed once KeePassLib holds its own copy, encrypted in memory, and reaches
    /// disk only inside the vault (D-0533). The edit names the entry (D-0318); its other attachments are kept as they are.
    /// </remarks>
    /// <exception cref="VaultException">
    /// The name is refused, the item already has an attachment by that name, the content is larger than the cap, or more
    /// than one entry answers to that name. Nothing is changed.
    /// </exception>
    /// <exception cref="IOException">The content could not be read. Nothing is changed.</exception>
    public bool AddAttachment(EntryName name, string attachment, Stream content, out bool keptInHistory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(content);

        if (!VaultNameRules.IsValidAttachmentName(attachment, out string error))
        {
            throw Refused(error);
        }

        return Attach(name, attachment, content, replacing: false, out keptInHistory);
    }

    /// <summary>Replaces what one attachment holds, keeping its name, as <see cref="AddAttachment"/> adds one. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="attachment">The attachment's name, matched exactly.</param>
    /// <param name="content">What it holds now, read from its position to its end, at most <see cref="EntryAttachment.MaximumSize"/> bytes.</param>
    /// <param name="keptInHistory">Whether the item's history keeps the item as it was, what the attachment held included.</param>
    /// <returns><see langword="true"/> if the entry was found and had that attachment.</returns>
    /// <exception cref="VaultException">The content is larger than the cap, or more than one entry answers to that name. Nothing is changed.</exception>
    /// <exception cref="IOException">The content could not be read. Nothing is changed.</exception>
    public bool ReplaceAttachment(EntryName name, string attachment, Stream content, out bool keptInHistory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(content);

        return Attach(name, attachment, content, replacing: true, out keptInHistory);
    }

    /// <summary>Removes one attachment as an edit with one history revision. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="attachment">The attachment's name, matched exactly.</param>
    /// <param name="keptInHistory">Whether the item's history keeps the item as it was, the attachment included.</param>
    /// <returns><see langword="true"/> if the entry was found and had that attachment.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name. Nothing is changed.</exception>
    public bool RemoveAttachment(EntryName name, string attachment, out bool keptInHistory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attachment);

        bool kept = false;
        bool removed = Change(() => _interop.RemoveAttachment(name, attachment, out kept) > 0, done => done ? VaultEdit.Of(name) : null);
        keptInHistory = kept;
        return removed;
    }

    /// <summary>Writes one attachment's bytes once to a new file where the person chose, readable by its owner alone outside Windows (D-0534).</summary>
    /// <param name="name">The entry.</param>
    /// <param name="attachment">The attachment's name, matched exactly.</param>
    /// <param name="destination">Where it goes. Its directory must exist, and nothing may be at the path.</param>
    /// <param name="failure">What the filesystem said, when the outcome is <see cref="AttachmentSaveOutcome.Failed"/>; empty otherwise.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// PRODUCT §3.4's exception for an attachment the person asks keypaste to save (D-0532), written as
    /// <see cref="VaultKeyfile.Create"/> writes a key file: staged beside the path and moved into place without replacing
    /// anything, so a file that arrives meanwhile is kept and the stage is deleted. The bytes are this vault's as it holds
    /// them, saved or not, and the copy they are read into is zeroed.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public AttachmentSaveOutcome SaveAttachment(EntryName name, string attachment, string destination, out string failure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        failure = string.Empty;
        var full = System.IO.Path.GetFullPath(destination);

        if (File.Exists(full) || Directory.Exists(full))
        {
            return AttachmentSaveOutcome.PathAlreadyExists;
        }

        byte[]? content;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            content = _interop.ReadAttachment(name, attachment);
        }

        if (content is null)
        {
            return AttachmentSaveOutcome.NotFound;
        }

        try
        {
            using var staged = AtomicFile.Stage(full, content, AtomicFile.OwnerOnly);
            staged.CommitWithoutReplacing();
            return AttachmentSaveOutcome.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(full) || Directory.Exists(full))
            {
                return AttachmentSaveOutcome.PathAlreadyExists;
            }

            failure = ex.Message;
            return AttachmentSaveOutcome.Failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    /// <summary>The names of the item's other fields whose value holds a <c>{S:…}</c> naming this one, which a rename would leave naming nothing.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="field">The field's name, matched exactly.</param>
    /// <returns>The fields' names in ordinal order, or null when no entry answers to that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<string>? FieldsNaming(EntryName name, string field)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        return _interop.FieldsNaming(name, field);
    }

    /// <summary>
    /// For each variable of the item, or of one of its revisions, whose value holds a KeePass placeholder, what fills in
    /// from the item when it leaves or the first placeholder that refuses it, by keyword and never by value (D-0465).
    /// </summary>
    /// <param name="name">The entry.</param>
    /// <param name="revision">A position in <see cref="ReadHistory"/>'s order to judge that revision's variables, or null for the item's own.</param>
    /// <returns>Each such variable in ordinal order of name, or null when there is no such entry or revision.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal IReadOnlyList<KeyValuePair<string, PlantedPlaceholders>>? PlaceholdersHeld(EntryName name, int? revision = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.ReadPlantedPlaceholders(name, revision, EnvConvention.IsEnvNamedField);
    }

    /// <summary>Moves keys found in notes into protected fields. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="check">The check that found them, while it is still undisposed.</param>
    /// <param name="findings">The findings to move, from <paramref name="check"/>.</param>
    /// <returns>What was moved, or why nothing was.</returns>
    /// <remarks>
    /// <para>
    /// Each entry changes as one edit with one history revision (D-0373): every value becomes a
    /// protected field named by its key, and exactly its line leaves the notes. A field that already
    /// holds the same value keeps its flag and is not written, and its line still leaves. The
    /// previous notes stay in the entry's history.
    /// </para>
    /// <para>
    /// The selection is all or nothing. It is refused, with nothing changed, when an entry has gone,
    /// its notes changed since the check, a field holds a different value, or its notes set a field
    /// twice.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A finding came from another check.</exception>
    /// <exception cref="VaultException">More than one entry answers to an entry's name. Nothing is changed.</exception>
    public NoteKeyMove MoveNoteKeys(NoteKeyCheck check, IReadOnlyList<NoteKeyFinding> findings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(findings);

        if (findings.Any(finding => !ReferenceEquals(finding.Check, check)))
        {
            throw new ArgumentException("Every finding must come from the check given.", nameof(findings));
        }

        return Change(
            () =>
            {
                List<NoteKeyRefusal> refusals = [];
                List<(EntryName Entry, List<FieldWrite> Writes, string Notes)> planned = [];

                foreach (var entry in findings.GroupBy(finding => finding.Entry))
                {
                    if (PlanNoteMove(check, entry.Key, [.. entry], refusals) is { } plan)
                    {
                        planned.Add((entry.Key, plan.Writes, plan.Notes));
                    }
                }

                if (refusals.Count > 0 || planned.Count == 0)
                {
                    return new NoteKeyMove(false, [], refusals);
                }

                foreach (var (entry, writes, notes) in planned)
                {
                    _interop.MoveToFields(entry, writes, notes);
                }

                return new NoteKeyMove(true, [.. planned.Select(plan => plan.Entry)], []);
            },
            move => move.Moved ? VaultEdit.Of(move.Entries) : null);
    }

    /// <summary>The one entry's own tags, never its group's.</summary>
    /// <param name="name">The entry.</param>
    /// <returns>Its tags as KeePass normalizes them, or null when no entry answers to that name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public IReadOnlyList<string>? Tags(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        return _interop.ReadTags(name);
    }

    /// <summary>Every entry outside the recycle bin that carries a tag of its own, with those tags.</summary>
    /// <returns>Each such entry depth-first; two entries sharing a name are both listed.</returns>
    public IReadOnlyList<EntryTags> ReadTags()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.ReadTags();
    }

    /// <summary>Adds one tag to an entry as an edit with one history revision. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="tag">The tag, matched ordinally, so <c>Prod</c> and <c>prod</c> are two tags.</param>
    /// <returns><see langword="true"/> if the entry was found and did not already carry the tag.</returns>
    /// <exception cref="VaultException">The tag is refused (<see cref="TagRules.IsValid"/>), or more than one entry answers to that name. Nothing is changed.</exception>
    public bool AddTag(EntryName name, string tag)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tag);

        if (!TagRules.IsValid(tag, out string error))
        {
            throw Refused(error);
        }

        return Change(() => _interop.AddTag(name, tag) > 0, added => added ? VaultEdit.Of(name) : null);
    }

    /// <summary>Removes tags from an entry as one edit with one history revision. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="tags">The tags, each matched ordinally; one the entry does not carry is skipped.</param>
    /// <returns><see langword="true"/> if the entry was found and carried at least one of them.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name. Nothing is changed.</exception>
    public bool RemoveTags(EntryName name, IReadOnlyList<string> tags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tags);

        return Change(() => _interop.RemoveTags(name, tags) > 0, removed => removed ? VaultEdit.Of(name) : null);
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
    /// its last change. The entry becomes the revision whole, its tags included, so a front end asks
    /// first what <see cref="ProjectTagChange.Restoring"/> says whenever the revision's project tags differ (D-0510).
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

        return Change(() => _interop.RestoreRevision(name, index) > 0, restored => restored ? VaultEdit.Of(name) : null);
    }

    /// <summary>Every entry in the vault outside keypaste's own groups, depth-first from the root group.</summary>
    /// <param name="includeReserved">Whether entries in <see cref="ReservedGroups"/> are read too, for keypaste's own record stores.</param>
    public IReadOnlyList<VaultEntry> ReadEntries(bool includeReserved = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entries = _interop.ReadEntries();

        return includeReserved ? entries : [.. entries.Where(entry => !ReservedGroups.IsReserved(entry.GroupPath))];
    }

    /// <summary>Every entry, keypaste's own records included, but only while this vault holds exactly what its file holds.</summary>
    /// <param name="entries">The entries when the answer is <see cref="SavedRead.Current"/>, otherwise null.</param>
    /// <returns>Whether the vault matches its file, and if not, why.</returns>
    /// <remarks>
    /// <para>
    /// What another process is served from (D-0317). A change made here and not yet saved, a write in
    /// progress, and a file somebody else has written since this vault last read or wrote it are each
    /// refused rather than answered from memory. The check and the read happen under the lock every
    /// change takes, so neither can see a change half made.
    /// </para>
    /// <para>
    /// The comparison is with the file's whole digest, for the reason <see cref="SourceSnapshot.Digest"/>
    /// gives. A file that cannot be read is <see cref="SavedRead.Unreadable"/>, not current.
    /// </para>
    /// </remarks>
    public SavedRead ReadSaved(out IReadOnlyList<VaultEntry>? entries) => ReadSaved(out entries, out _);

    /// <summary><see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/>, with every group path read from the same state.</summary>
    /// <param name="entries">The entries when the answer is <see cref="SavedRead.Current"/>, otherwise null.</param>
    /// <param name="groupPaths">Every group path, keypaste's own included, under the same condition.</param>
    /// <returns>Whether the vault matches its file, and if not, why.</returns>
    public SavedRead ReadSaved(out IReadOnlyList<VaultEntry>? entries, out IReadOnlyList<string>? groupPaths)
    {
        entries = null;
        groupPaths = null;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            entries = _interop.ReadEntries();
            groupPaths = _interop.ReadGroupPaths();
            return SavedRead.Current;
        }
    }

    /// <summary>Every live entry as a listing names it, but only while this vault holds exactly what its file holds; no value is read.</summary>
    /// <param name="listing">The entries when the answer is <see cref="SavedRead.Current"/>, otherwise null.</param>
    /// <returns>Whether the vault matches its file, and if not, why, as <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> judges it.</returns>
    public SavedRead ReadSavedListing(out IReadOnlyList<ListedEntry>? listing)
    {
        listing = null;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            listing = _interop.ReadListing();
            return SavedRead.Current;
        }
    }

    /// <summary>One custom field's value, but only while this vault holds exactly what its file holds.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="field">The custom field's name, matched exactly.</param>
    /// <param name="value">The value when the answer is <see cref="SavedRead.Current"/> and the field exists, otherwise null.</param>
    /// <returns>Whether the vault matches its file, and if not, why, as <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> judges it.</returns>
    /// <exception cref="ArgumentException"><paramref name="field"/> is a standard field.</exception>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public SavedRead ReadSavedField(EntryName name, string field, out string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(field);

        value = null;

        if (FieldNameRules.IsStandard(field))
        {
            throw new ArgumentException($"'{field}' is a standard field, not a custom one.", nameof(field));
        }

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            value = _interop.ReadCustomField(name, field);
            return SavedRead.Current;
        }
    }

    /// <summary><see cref="ReadTotp"/>, but only while this vault holds exactly what its file holds.</summary>
    /// <param name="name">The entry.</param>
    /// <param name="spec">
    /// When the answer is <see cref="SavedRead.Current"/>, the item's TOTP, which the caller disposes; null when there is no
    /// such entry, it has none, or <paramref name="refusal"/> says why it cannot be computed, and whenever the answer is not.
    /// </param>
    /// <param name="refusal">Why the item's TOTP cannot be computed, naming a field and never a value; null otherwise.</param>
    /// <returns>Whether the vault matches its file, and if not, why, as <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> judges it.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal SavedRead ReadSavedTotp(EntryName name, out TotpSpec? spec, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(name);

        spec = null;
        refusal = null;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            spec = _interop.ReadCustomFields(name, TotpSpec.IsSourceField) is { } fields
                ? TotpSpec.Read(fields, out refusal)
                : null;
            return SavedRead.Current;
        }
    }

    /// <summary><see cref="FillPlaceholders(EntryName, string)"/>, but only while this vault holds exactly what its file holds.</summary>
    /// <param name="name">The item the value belongs to.</param>
    /// <param name="value">The value as the vault holds it.</param>
    /// <param name="fill">
    /// When the answer is <see cref="SavedRead.Current"/>, the value as it leaves keypaste or why it cannot leave,
    /// or null when it holds a placeholder and no item answers to that name; otherwise null.
    /// </param>
    /// <returns>Whether the vault matches its file, and if not, why, as <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> judges it.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal SavedRead ReadSavedFill(EntryName name, string value, out PlaceholderFill? fill)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        fill = null;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            fill = Filled(value, () => _interop.ReadPlaceholderFields(name));
            return SavedRead.Current;
        }
    }

    private static PlaceholderFill? Filled(string value, Func<PlaceholderFields?> read)
    {
        if (KeePassPlaceholders.HoldsPlaceholder(value))
        {
            return read() is { } fields ? KeePassPlaceholders.Fill(value, () => fields) : null;
        }

        // At most an escape is left to fill in, and an escape reads no field, so the item is not read.
        return KeePassPlaceholders.Fill(value, static () => throw new System.Diagnostics.UnreachableException());
    }

    /// <summary>Each entry directly in one of keypaste's own groups, with one of its string fields, but only while this vault holds exactly what its file holds.</summary>
    /// <param name="groupPath">The reserved group, matched exactly.</param>
    /// <param name="field">The field read from each entry.</param>
    /// <param name="records">The entries in traversal order when the answer is <see cref="SavedRead.Current"/>, otherwise null.</param>
    /// <returns>Whether the vault matches its file, and if not, why, as <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> judges it.</returns>
    /// <exception cref="ArgumentException"><paramref name="groupPath"/> is not reserved.</exception>
    internal SavedRead ReadSavedRecords(string groupPath, string field, out IReadOnlyList<ReservedRecord>? records)
    {
        Reserved(groupPath);
        ArgumentNullException.ThrowIfNull(field);

        records = null;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            records = _interop.ReadRecords(groupPath, field);
            return SavedRead.Current;
        }
    }

    /// <summary>Each entry directly in one of keypaste's own groups, with one of its string fields, as this vault holds it now, saved or not.</summary>
    /// <param name="groupPath">The reserved group, matched exactly.</param>
    /// <param name="field">The field read from each entry.</param>
    /// <returns>The entries in traversal order.</returns>
    /// <exception cref="ArgumentException"><paramref name="groupPath"/> is not reserved.</exception>
    internal IReadOnlyList<ReservedRecord> ReadRecords(string groupPath, string field)
    {
        Reserved(groupPath);
        ArgumentNullException.ThrowIfNull(field);

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _interop.ReadRecords(groupPath, field);
        }
    }

    /// <summary>
    /// Sets one unprotected string field of the record with this name in one of keypaste's own groups:
    /// of the entry with the UUID its title derives, and of every other entry there with the title.
    /// Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="name">The record's entry, in a reserved group.</param>
    /// <param name="idOf">The UUID of the entry for a title.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">What it holds.</param>
    /// <returns>Whether anything changed.</returns>
    /// <remarks>
    /// A new record is created with the title's UUID and no history item; an existing one is edited in
    /// place, keeping its history, and is moved back into the group from wherever it is.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not in a reserved group.</exception>
    internal bool WriteRecord(EntryName name, Func<string, Guid> idOf, string field, string value) =>
        WriteRecord(name, idOf, field, value, out _);

    /// <summary>Sets a record's field as <see cref="WriteRecord(EntryName, Func{string, Guid}, string, string)"/> does, and says how to take the write back.</summary>
    /// <param name="name">The record's entry, in a reserved group.</param>
    /// <param name="idOf">The UUID of the entry for a title.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">What it holds.</param>
    /// <param name="undo">
    /// When anything changed, what puts the vault back as it was before the write, for a caller whose save
    /// then failed, so that no later save writes what that caller said was not made; otherwise null.
    /// </param>
    /// <returns>Whether anything changed.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not in a reserved group.</exception>
    internal bool WriteRecord(EntryName name, Func<string, Guid> idOf, string field, string value, out Action? undo)
    {
        ArgumentNullException.ThrowIfNull(name);
        Reserved(name.GroupPath);
        ArgumentNullException.ThrowIfNull(idOf);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        ObjectDisposedException.ThrowIf(_disposed, this);

        KeePassInterop.RecordSnapshot before;
        bool wasPending;
        bool written;
        long changes;

        // Change<T>'s steps, with what it changes taken first under the same lock.
        lock (_state)
        {
            before = _interop.Snapshot(name, idOf);
            wasPending = _pending;
            written = _interop.WriteRecord(name, idOf, field, value);

            if (written)
            {
                _pending = true;
                _changes++;
            }

            changes = _changes;
        }

        if (!written)
        {
            undo = null;
            return false;
        }

        Edited?.Invoke(this, VaultEdit.Of(name));
        undo = () => TakeBack(name, before, wasPending, changes);
        return true;
    }

    /// <summary>Everything an edit can change, as it is now, so a caller whose save of the edit failed can <see cref="Restore"/> it.</summary>
    /// <returns>The checkpoint, which only this vault can restore.</returns>
    public VaultCheckpoint Checkpoint()
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return new VaultCheckpoint(this, _interop.Checkpoint(), _pending, _savedAt);
        }
    }

    /// <summary>
    /// Puts the vault back as <paramref name="checkpoint"/> found it, so no later save writes an edit whose
    /// own save failed, and says the vault matches its file again when it did then and nothing was saved since.
    /// </summary>
    /// <param name="checkpoint">What <see cref="Checkpoint"/> took before the edit.</param>
    /// <exception cref="ArgumentException">The checkpoint is another vault's.</exception>
    /// <remarks>Everything changed since the checkpoint goes, so it is taken and restored around one edit and its save.</remarks>
    public void Restore(VaultCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!ReferenceEquals(checkpoint.Vault, this))
        {
            throw new ArgumentException("the checkpoint is another vault's", nameof(checkpoint));
        }

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _interop.Restore(checkpoint.Taken);
            _pending = checkpoint.WasPending || !Nullable.Equals(_savedAt, checkpoint.SavedAt);
            _changes++;
        }

        Edited?.Invoke(this, VaultEdit.Everything);
    }

    /// <summary>Puts back what one record write changed, and says the vault matches its file again when nothing else changed or was saved since.</summary>
    private void TakeBack(EntryName name, KeePassInterop.RecordSnapshot before, bool wasPending, long changes)
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _interop.Restore(before);

            // Another change, or a save that wrote this one, since: the vault no longer matches its file.
            _pending = _changes == changes ? wasPending : true;
            _changes++;
        }

        Edited?.Invoke(this, VaultEdit.Of(name));
    }

    private static void Reserved(string groupPath)
    {
        ArgumentNullException.ThrowIfNull(groupPath);

        if (!ReservedGroups.IsReserved(groupPath))
        {
            throw new ArgumentException($"'{groupPath}' is not one of keypaste's own groups.", nameof(groupPath));
        }
    }

    /// <summary><see cref="ReadSaved(out IReadOnlyList{VaultEntry}?, out IReadOnlyList{string}?)"/>, with each entry's tags and a tagged entry's variable fields.</summary>
    /// <param name="snapshot">What projects are resolved from when the answer is <see cref="SavedRead.Current"/>, otherwise null.</param>
    /// <returns>Whether the vault matches its file, and if not, why.</returns>
    internal SavedRead ReadSavedEnv(out EnvSnapshot? snapshot)
    {
        snapshot = null;

        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var read = Compare();
            if (read != SavedRead.Current)
            {
                return read;
            }

            snapshot = new EnvSnapshot(_interop.ReadEnvEntries(), _interop.ReadGroupPaths());
            return SavedRead.Current;
        }
    }

    /// <summary>What projects are resolved from, as this vault holds it now, saved or not.</summary>
    internal EnvSnapshot ReadEnvSnapshot()
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return new EnvSnapshot(_interop.ReadEnvEntries(), _interop.ReadGroupPaths());
        }
    }

    /// <summary>
    /// Every entry whose title, group path, username, URL or one of whose own tags contains <paramref name="query"/>.
    /// </summary>
    /// <param name="query">
    /// What to look for, compared case-insensitively as a substring. An empty or whitespace query
    /// matches every entry and reports <see cref="MatchedFields.None"/> for each.
    /// </param>
    /// <returns>Each entry found, with every field the query was in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>No value is returned, and the fields holding one are never read.</b> A result carries an
    /// <see cref="EntryName"/>, a <see cref="MatchedFields"/> and the tags matched, none of which is a
    /// value. The password and the notes are not compared at all: notes routinely hold recovery
    /// codes, connection strings and second passwords, so matching them would make a query a way to
    /// confirm a secret without ever opening the entry that holds it (docs/STEPS.md V.5b).
    /// </para>
    /// <para>
    /// Here rather than in a front end because it is a rule about what may be read out of a vault,
    /// which docs/PRODUCT.md law 4.2 puts in the core. A screen that did its own matching would have
    /// to hold every username it compared, and the desktop's hygiene gate exists to stop exactly
    /// that.
    /// </para>
    /// <para>
    /// A recycled entry is not found: the bin is skipped by the same traversal that skips it for
    /// <see cref="ReadEntries"/> (D-0248).
    /// </para>
    /// </remarks>
    /// <param name="includeReserved">Whether entries in <see cref="ReservedGroups"/> are found too.</param>
    public IReadOnlyList<EntryMatch> Search(string query, bool includeReserved = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(query);

        var matches = _interop.Search(query.Trim());

        return includeReserved ? matches : [.. matches.Where(match => !ReservedGroups.IsReserved(match.Name.GroupPath))];
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
    /// A path is what a person types and what an agent names, so it stays addressable, but it is
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

    /// <summary>Every group path in the vault except the root and keypaste's own groups, slash-separated.</summary>
    /// <param name="includeReserved">Whether <see cref="ReservedGroups"/> are read too.</param>
    /// <remarks>
    /// Groups holding no entries appear here and nowhere in <see cref="ReadEntries"/>, so a listing
    /// that wants to match KeePassXC's view of the same file needs both.
    /// </remarks>
    public IReadOnlyList<string> ReadGroupPaths(bool includeReserved = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var groups = _interop.ReadGroupPaths();

        return includeReserved ? groups : [.. groups.Where(group => !ReservedGroups.IsReserved(group))];
    }

    /// <summary>Whether a master password is one of the factors this vault opens with.</summary>
    public bool HasPassword
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _interop.KeyHasPassword;
        }
    }

    /// <summary>The keyfile this vault opens with, or <see langword="null"/> when it has none.</summary>
    public string? KeyfilePath
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _interop.KeyfilePath;
        }
    }

    /// <summary>The hardware key this vault opens with and asks on every save, or <see langword="null"/> when it has none.</summary>
    public HardwareKey? HardwareKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _interop.HardwareKey;
        }
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
    public DeletionOutcome RemoveEntry(EntryName name) => RemoveEntry(name, out _);

    /// <summary>Deletes the one entry with this name, and says what is now in the bin. Call
    /// <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry to delete.</param>
    /// <param name="recycled">
    /// The identity <see cref="RestoreRecycled"/> takes to put this entry back, when the outcome
    /// is <see cref="DeletionOutcome.Recycled"/>; otherwise the default.
    /// </param>
    /// <returns>
    /// What happened to the entry. Deleting nothing is not an error here; the caller decides
    /// whether it is one.
    /// </returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// For a caller that offers to undo the delete it just made. The identity comes from here
    /// rather than from a reading of the bin taken afterwards, because which row a delete produced
    /// is the vault's answer and not something a screen should work out (docs/PRODUCT.md §4.2).
    /// The path overload has no counterpart: nothing addressing an entry by path offers an undo.
    /// </remarks>
    public DeletionOutcome RemoveEntry(EntryName name, out RecycledEntryId recycled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        var id = default(RecycledEntryId);
        var outcome = Change(
            () => _interop.RemoveEntry(name, out id),
            removed => removed == DeletionOutcome.NothingMatched ? null : VaultEdit.Of(name));
        recycled = id;
        return outcome;
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
            ? RemoveEntry(EntryName.Of(found), out _)
            : DeletionOutcome.NothingMatched;
    }

    /// <summary>Renames the one entry with this name. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="name">The entry to rename.</param>
    /// <param name="title">What to call it.</param>
    /// <param name="renamed">
    /// The name the entry now answers to, or <see langword="null"/> when nothing was written.
    /// </param>
    /// <returns>What happened, including every reason it did not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="title"/> is null.</exception>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// <para>
    /// The entry is mutated in place, so its UUID, its timestamps, its attachments, its custom
    /// string fields and its whole history survive. A rename takes no history revision of its own:
    /// it overwrites no value, and KeePass evicts the oldest revision when the list fills.
    /// </para>
    /// <para>
    /// A recycled entry is not here to rename. Putting one back is
    /// <see cref="RestoreRecycled"/>; reorganizing the bin is not something keypaste does.
    /// </para>
    /// </remarks>
    public OrganizeOutcome RenameEntry(EntryName name, string title, out EntryName? renamed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(title);

        EntryName? result = null;
        var outcome = Change(
            () => _interop.RenameEntry(name, title, out result),
            _ => result is null ? null : VaultEdit.Of(name, result));
        renamed = result;
        return outcome;
    }

    /// <summary>Moves the one entry with this name into another group. Call <see cref="Save"/> to
    /// persist it.</summary>
    /// <param name="name">The entry to move.</param>
    /// <param name="destinationGroupPath">
    /// The group to move it into, slash-separated and excluding the root; an empty string is the
    /// root group. The group must already exist — nothing is created on the way.
    /// </param>
    /// <param name="moved">
    /// The name the entry now answers to, or <see langword="null"/> when nothing was written.
    /// </param>
    /// <returns>What happened, including every reason it did not.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="VaultException">More than one entry, or more than one group, answers.</exception>
    /// <remarks>
    /// The recycle bin is not a destination: a move is not a delete, and
    /// <see cref="RemoveEntry(EntryName)"/> is. Nothing records where the entry came from, so an
    /// organized vault stays KDBX 4.0 and only recycling raises it.
    /// </remarks>
    public OrganizeOutcome MoveEntry(EntryName name, string destinationGroupPath, out EntryName? moved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(destinationGroupPath);

        EntryName? result = null;
        var outcome = Change(
            () => _interop.MoveEntry(name, destinationGroupPath, out result),
            _ => result is null ? null : VaultEdit.Of(name, result));
        moved = result;
        return outcome;
    }

    /// <summary>
    /// Renames and moves the one entry with this name in a single write. Call <see cref="Save"/> to
    /// persist it.
    /// </summary>
    /// <param name="name">The entry to change.</param>
    /// <param name="target">
    /// What it should be called and where it should live. Either half may equal the entry's
    /// current one; the group must already exist, because nothing is created on the way.
    /// </param>
    /// <param name="result">
    /// The name the entry now answers to, or <see langword="null"/> when nothing was written.
    /// </param>
    /// <returns>
    /// <see cref="OrganizeOutcome.RenamedAndMoved"/>, <see cref="OrganizeOutcome.Renamed"/> or
    /// <see cref="OrganizeOutcome.Moved"/> for what actually changed,
    /// <see cref="OrganizeOutcome.DestinationUnchanged"/> when neither half did, and every reason
    /// it did not happen otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="VaultException">More than one entry, or more than one group, answers.</exception>
    /// <remarks>
    /// <para>
    /// <b>Prefer this to calling <see cref="RenameEntry"/> and then <see cref="MoveEntry"/>.</b>
    /// Those two are each this one with a half held constant, and running them in sequence is not
    /// the same thing: the first can succeed and the second be refused, leaving the open vault
    /// holding a change the caller was told did not happen, which the next unrelated
    /// <see cref="Save"/> then writes out. Every check here happens before the one mutation, so a
    /// refusal leaves the vault exactly as it found it, in memory as well as on disk (D-0272).
    /// </para>
    /// <para>
    /// The entry is mutated in place, so its UUID, its timestamps, its attachments, its custom
    /// string fields and its whole history survive, and nothing records where it came from, so an
    /// organized vault stays KDBX 4.0.
    /// </para>
    /// </remarks>
    public OrganizeOutcome Relocate(EntryName name, EntryName target, out EntryName? result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(target);

        EntryName? relocated = null;
        var outcome = Change(
            () => _interop.Relocate(name, target, out relocated),
            _ => relocated is null ? null : VaultEdit.Of(name, relocated));
        result = relocated;
        return outcome;
    }

    /// <summary>
    /// Copies the one entry with this name beside it as a new entry, in one change with no history item.
    /// Call <see cref="Save"/> to persist it.
    /// </summary>
    /// <param name="name">The entry to copy.</param>
    /// <param name="copy">The copy's name, or <see langword="null"/> when nothing was written.</param>
    /// <param name="droppedTags">The tags starting <c>env:</c> the copy did not take, or an empty list.</param>
    /// <returns>
    /// <see cref="OrganizeOutcome.Copied"/>; <see cref="OrganizeOutcome.NothingMatched"/> when no live entry
    /// outside keypaste's own groups answers to that name; <see cref="OrganizeOutcome.NameRefused"/> when the
    /// copy's title is one no vault could address.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    /// <remarks>
    /// The copy is titled <c>&lt;title&gt; (copy)</c>, or <c>(copy 2)</c> and onwards when that is taken
    /// (D-0091). It keeps every field, attachment and setting, with a new identity, no history and its times
    /// set to now, and it leaves out the entry's project tags, because a project refuses two entries giving
    /// one variable (D-0337); <paramref name="droppedTags"/> names them so the caller can say so.
    /// </remarks>
    public OrganizeOutcome CloneEntry(EntryName name, out EntryName? copy, out IReadOnlyList<string> droppedTags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);

        copy = null;
        droppedTags = [];

        if (ReservedGroups.IsReserved(name.GroupPath))
        {
            return OrganizeOutcome.NothingMatched;
        }

        EntryName? made = null;
        IReadOnlyList<string> dropped = [];
        var outcome = Change(
            () => _interop.CloneEntry(name, out made, out dropped),
            _ => made is null ? null : VaultEdit.Of(made));
        copy = made;
        droppedTags = dropped;
        return outcome;
    }

    /// <summary>Creates one empty group. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="parentGroupPath">
    /// The group to create it in, slash-separated and excluding the root; an empty string is the
    /// root group. It must already exist — a path that does not is refused rather than built.
    /// </param>
    /// <param name="name">What to call the new group. One name, never a path.</param>
    /// <param name="created">
    /// The path the new group has, or an empty string when nothing was written.
    /// </param>
    /// <returns>What happened, including every reason it did not.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="VaultException">More than one group answers to the parent path.</exception>
    /// <remarks>
    /// A new group holds nothing, so only <see cref="ReadGroupPaths"/> can see it:
    /// <see cref="ReadEntries"/> has nothing of it to report.
    /// </remarks>
    public GroupOutcome CreateGroup(string parentGroupPath, string name, out string created)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parentGroupPath);
        ArgumentNullException.ThrowIfNull(name);

        var path = string.Empty;
        var outcome = Change(
            () => _interop.CreateGroup(parentGroupPath, name, out path),
            made => made == GroupOutcome.Created ? VaultEdit.Of() : null);
        created = path;
        return outcome;
    }

    /// <summary>Renames one group, carrying everything under it. Call <see cref="Save"/> to
    /// persist it.</summary>
    /// <param name="groupPath">The group to rename, slash-separated and excluding the root.</param>
    /// <param name="name">What to call it. One name, never a path: this does not move the group.</param>
    /// <param name="renamedPath">
    /// The path the group now has, or an empty string when nothing was written.
    /// </param>
    /// <returns>What happened, including every reason it did not.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="VaultException">More than one group answers to that path.</exception>
    /// <remarks>
    /// <para>
    /// Every entry beneath the group is re-pathed by this, so every one of them is checked against
    /// the same rules a single rename or move is checked against, and a collision with any of them
    /// refuses the whole rename rather than half of it.
    /// </para>
    /// <para>
    /// The group is mutated in place and keeps its UUID, so a recycled entry that came from it can
    /// still find its way home: where an entry came from is a UUID and not a path.
    /// </para>
    /// </remarks>
    public GroupOutcome RenameGroup(string groupPath, string name, out string renamedPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(groupPath);
        ArgumentNullException.ThrowIfNull(name);

        var path = string.Empty;
        List<EntryName> beneath = [];
        var outcome = Change(
            () =>
            {
                beneath = Beneath(groupPath);
                return _interop.RenameGroup(groupPath, name, out path);
            },
            renamed => renamed == GroupOutcome.Renamed
                ? VaultEdit.Of([.. beneath, .. beneath.Select(entry => new EntryName(path + entry.GroupPath[groupPath.Length..], entry.Title))])
                : null);
        renamedPath = path;
        return outcome;
    }

    /// <summary>Moves one group, carrying everything under it, into another parent. Call <see cref="Save"/> to
    /// persist it.</summary>
    /// <param name="groupPath">The group to move, slash-separated and excluding the root.</param>
    /// <param name="destinationParentPath">
    /// The group to move it into, slash-separated and excluding the root; an empty string is the root group.
    /// It must already exist — nothing is created on the way.
    /// </param>
    /// <param name="movedPath">The path the group now has, or an empty string when nothing was written.</param>
    /// <returns>What happened, including every reason it did not.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="VaultException">More than one group answers to either path.</exception>
    /// <remarks>
    /// <para>
    /// Refused for the env root and keypaste's own groups, for a destination inside keypaste's own groups,
    /// for the group itself or a group inside it, and for a name the destination already holds. Every entry
    /// beneath is re-pathed, so each is checked as <see cref="RenameGroup"/> checks them, and one collision
    /// refuses the whole move.
    /// </para>
    /// <para>
    /// The group is mutated in place and keeps its UUID, and nothing records where it came from, so an
    /// organized vault stays KDBX 4.0 (D-0271).
    /// </para>
    /// </remarks>
    public GroupOutcome MoveGroup(string groupPath, string destinationParentPath, out string movedPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(groupPath);
        ArgumentNullException.ThrowIfNull(destinationParentPath);

        var path = string.Empty;
        List<EntryName> beneath = [];
        var outcome = Change(
            () =>
            {
                beneath = Beneath(groupPath);
                return _interop.MoveGroup(groupPath, destinationParentPath, out path);
            },
            moved => moved == GroupOutcome.Moved
                ? VaultEdit.Of([.. beneath, .. beneath.Select(entry => new EntryName(path + entry.GroupPath[groupPath.Length..], entry.Title))])
                : null);
        movedPath = path;
        return outcome;
    }

    /// <summary>Deletes one group with everything under it. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="groupPath">The group to delete, slash-separated and excluding the root.</param>
    /// <returns>
    /// <see cref="GroupOutcome.Recycled"/> or <see cref="GroupOutcome.DeletedPermanently"/> for what happened,
    /// and every reason it did not otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="groupPath"/> is null.</exception>
    /// <exception cref="VaultException">More than one group answers to that path.</exception>
    /// <remarks>
    /// <para>
    /// Where the vault has a recycle bin the whole group goes into it, as KeePassXC deletes one: every entry
    /// keeps its identity, fields and history, <see cref="ReadRecycled"/> lists each, and
    /// <see cref="RestoreRecycled"/> puts one back at the path it had, making again any group on the way the
    /// vault no longer has. Like recycling an entry, this writes the file as KDBX 4.1 (D-0247).
    /// </para>
    /// <para>
    /// Without a recycle bin only an empty group is deleted, for good; one holding an item or a group is
    /// <see cref="GroupOutcome.NotEmpty"/>. The env root, keypaste's own groups and a group holding the
    /// recycle bin are <see cref="GroupOutcome.NameReserved"/>, and the bin itself is not a group this
    /// reaches.
    /// </para>
    /// </remarks>
    public GroupOutcome DeleteGroup(string groupPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(groupPath);

        List<EntryName> beneath = [];
        return Change(
            () =>
            {
                beneath = Beneath(groupPath);
                return _interop.DeleteGroup(groupPath);
            },
            deleted => deleted is GroupOutcome.Recycled or GroupOutcome.DeletedPermanently ? VaultEdit.Of(beneath) : null);
    }

    /// <summary>Every live entry in the group at this path or under it, keypaste's own included.</summary>
    private List<EntryName> Beneath(string groupPath) =>
        [.. _interop.ReadEntries().Select(EntryName.Of).Where(entry => IsBeneath(entry.GroupPath, groupPath))];

    /// <summary>Copies groups of another vault into this one, all or none. Call <see cref="Save"/> to persist it.</summary>
    /// <param name="source">The vault copied from; <see cref="ImportSource.ApplyTo"/> has checked every destination.</param>
    /// <param name="pieces">What to copy, and where.</param>
    /// <returns>What was copied.</returns>
    internal ImportResult Import(ImportSource source, IReadOnlyList<ImportPiece> pieces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pieces);

        var groupsBefore = 0;
        VaultEdit edit = VaultEdit.Of();
        var copied = Change(
            () =>
            {
                groupsBefore = _interop.ReadGroupPaths().Count;
                return _interop.ImportFrom(source.Interop, pieces);
            },
            names => edit = VaultEdit.Of(names));

        var shared = _interop.Search(string.Empty)
            .CountBy(match => match.Name)
            .Where(pair => pair.Value > 1)
            .Select(pair => pair.Key)
            .ToHashSet();

        return new ImportResult(
            copied.Count, _interop.ReadGroupPaths().Count - groupsBefore, copied.Count(shared.Contains), edit);
    }

    /// <summary>Adds the items an export import built, each whole, in one change with no history item of their own. Call <see cref="Save"/> to persist them.</summary>
    /// <param name="entries">The items; <see cref="ExportSource.Plan"/> has checked every name.</param>
    /// <returns>The name of every item added.</returns>
    internal IReadOnlyList<EntryName> AddImported(IReadOnlyList<ImportedEntry> entries)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entries);

        return Change(() => _interop.AddImported(entries), names => VaultEdit.Of(names));
    }

    /// <summary>Sets an entry's expiry. A test seam; KeePassXC is what writes expiry.</summary>
    internal void SetExpiryUnchecked(EntryName name, DateTimeOffset? expires) =>
        Change(() => _interop.SetExpiryUnchecked(name, expires), VaultEdit.Of(name));

    /// <summary>Sets an entry's Auto-Type settings. A test seam; keypaste writes none (D-0605), and KeePass and KeePassXC are what write them.</summary>
    internal void SetAutoTypeUnchecked(EntryName name, bool enabled, bool obfuscated, string sequence, IReadOnlyList<(string Window, string Sequence)> associations) =>
        Change(() => _interop.SetAutoTypeUnchecked(name, enabled, obfuscated, sequence, associations), VaultEdit.Of(name));

    /// <summary>Sets a group's Auto-Type settings. A test seam; keypaste writes none (D-0605), and KeePass and KeePassXC are what write them.</summary>
    internal void SetGroupAutoTypeUnchecked(string groupPath, bool? enabled, string sequence) =>
        Change(() => _interop.SetGroupAutoTypeUnchecked(groupPath, enabled, sequence));

    /// <summary>Sets custom fields without <see cref="FieldNameRules"/>. A test seam; KeePassXC is what writes its own attributes, such as <c>otp</c>.</summary>
    internal void SetFieldsUnchecked(EntryName name, IReadOnlyList<FieldWrite> writes) =>
        Change(() => _interop.SetFields(name, writes), VaultEdit.Of(name));

    /// <summary>Adds a group without applying any of the rules. A test seam; nothing else uses it.</summary>
    /// <remarks>
    /// Two sibling groups of one name is a shape KDBX permits, KeePassXC makes and keypaste refuses
    /// to create, and keypaste still has to have an answer for a vault that holds one. Internal for
    /// the reason D-0255 gives about <see cref="SetRecyclesDeletedEntries"/>.
    /// </remarks>
    internal void AddGroupUnchecked(string parentGroupPath, string name) =>
        Change(() => _interop.AddGroupUnchecked(parentGroupPath, name));

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

        EntryName? restored = null;
        return Change(
            () => _interop.RestoreRecycled(id, out restored),
            _ => restored is null ? null : VaultEdit.Of(restored));
    }

    /// <summary>The name <see cref="RestoreRecycledRenamed"/> would put a recycled entry back under now.</summary>
    /// <param name="id">The identity from <see cref="ReadRecycled"/>.</param>
    /// <returns>
    /// The first free title of <c>&lt;title&gt; (restored)</c>, <c>(restored 2)</c> and onwards, in the group a
    /// restore puts the entry back in; null when no recycled entry has that identity, when it goes back into
    /// keypaste's own groups, or when its title cannot take the suffix.
    /// </returns>
    public EntryName? RenamedRestoreName(RecycledEntryId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _interop.RenamedRestoreName(id);
    }

    /// <summary>Puts a recycled entry back under a title of its own, for when another entry has taken its name.
    /// Call <see cref="Save"/> to persist it.</summary>
    /// <param name="id">The identity from <see cref="ReadRecycled"/>.</param>
    /// <param name="restored">The name the entry answers to now, or <see langword="null"/> when nothing changed.</param>
    /// <returns>
    /// <see cref="RestoreOutcome.Restored"/> or <see cref="RestoreOutcome.RestoredToRoot"/> as <see cref="RestoreRecycled"/>
    /// returns them; <see cref="RestoreOutcome.NothingMatched"/>; <see cref="RestoreOutcome.DestinationOccupied"/> when no
    /// title is offered, as <see cref="RenamedRestoreName"/> says. Nothing is changed unless this is a restore.
    /// </returns>
    /// <remarks>
    /// The entry goes back where <see cref="RestoreRecycled"/> puts it, with its identity, fields and history, titled
    /// <c>&lt;title&gt; (restored)</c>, or <c>(restored 2)</c> and onwards, the first that no other entry answers to by
    /// name or by path, as <see cref="CloneEntry"/> names a copy (D-0484). The title is written in the same change and
    /// takes no history item, as a rename takes none, so KeePassXC reads one edit of the title.
    /// </remarks>
    public RestoreOutcome RestoreRecycledRenamed(RecycledEntryId id, out EntryName? restored)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EntryName? result = null;
        var outcome = Change(
            () => _interop.RestoreRecycledRenamed(id, out result),
            _ => result is null ? null : VaultEdit.Of(result));
        restored = result;
        return outcome;
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

        return Change(() => _interop.PurgeRecycled(id), purged => purged ? VaultEdit.Of() : null);
    }

    /// <summary>Removes everything in the recycle bin for good. Call <see cref="Save"/> to persist
    /// it.</summary>
    /// <returns>The number of entries removed.</returns>
    public int EmptyRecycleBin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var changed = false;
        return Change(() => _interop.EmptyRecycleBin(out changed), _ => changed ? VaultEdit.Of() : null);
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
        ThrowIfRekeyed();

        // The check is handed to the retry loop, not just made before it. The name a save contends
        // for is most often held by another process saving this same vault, so a retry that waits
        // that out and then writes reverts it — see KeePassInterop.Save and D-0119.
        Commit(HasFileChangedSinceOpen, null, KeePassInterop.SaveAttempts, backUp: !_backedUp);
    }

    /// <summary>Writes the vault as <see cref="Save"/> does, unless <paramref name="cancellationToken"/> is
    /// cancelled while the save waits behind another save in this process.</summary>
    /// <param name="cancellationToken">Cancelled once the save must not begin writing, as when the unlock it was made under ends.</param>
    /// <exception cref="OperationCanceledException">Cancelled before the save began writing; nothing was written.</exception>
    /// <exception cref="VaultChangedOnDiskException">Something else wrote to <see cref="Path"/>.</exception>
    public void SaveUnlessCancelled(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfRekeyed();

        Commit(HasFileChangedSinceOpen, null, KeePassInterop.SaveAttempts, backUp: !_backedUp, cancellationToken: cancellationToken);
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
        ThrowIfRekeyed();

        // No check, at any point: this caller has already put the choice to a person and been told
        // to go ahead, so a change arriving mid-retry is one they have already accepted.
        //
        // backUp is unconditional here, and deliberately does not consult or set _backedUp. This
        // discards somebody else's write, and the vault it replaces is precisely the copy somebody
        // needs when that turns out to have been the wrong choice — the one case this feature cannot
        // afford to skip. A Save() later in the same unlock still gets its own per-unlock backup.
        Commit(null, null, KeePassInterop.SaveAttempts, backUp: true, applyFloor: false);
    }

    /// <summary>Changes what unlocks this vault: its master password, its keyfile, its hardware key, or several.</summary>
    /// <param name="change">What to change.</param>
    /// <param name="newPassword">The new master password; read only when <see cref="VaultAccessChange.SetPassword"/> is set.</param>
    /// <param name="confirmation">The same password, typed again.</param>
    /// <returns>What happened, and on success the kept copy of the file that was replaced.</returns>
    /// <exception cref="VaultChangedOnDiskException">Something else wrote to <see cref="Path"/>. Nothing was written.</exception>
    /// <exception cref="VaultAccessUnconfirmedException">The file was replaced and then did not open with the new credentials.</exception>
    /// <exception cref="VaultException">The backup, the check of the new bytes or the write failed; the vault is as it was.</exception>
    /// <remarks>
    /// <para>
    /// <b>A password is never taken away.</b> A vault that has one keeps one, and a vault a keyfile
    /// alone protects may change its keyfile but loses it only for a password. Only a keyfile that
    /// already exists is attached, and never one keyed by its hash (D-0287, D-0288). A hardware key
    /// is attached only while a key with that slot is connected, and is asked once, by the save.
    /// </para>
    /// <para>
    /// The save always keeps the file it replaces, like <see cref="SaveOverwriting"/>, but refuses a
    /// changed file like <see cref="Save"/> (D-0289). Once the vault has been replaced this object is
    /// sealed: every later write throws, and the vault is opened again with the new credentials.
    /// </para>
    /// </remarks>
    public VaultAccessResult ChangeAccess(
        VaultAccessChange change, ReadOnlySpan<char> newPassword, ReadOnlySpan<char> confirmation) =>
        ChangeAccess(change, newPassword, confirmation, duringAttempt: null);

    /// <summary>
    /// <see cref="ChangeAccess(VaultAccessChange, ReadOnlySpan{char}, ReadOnlySpan{char})"/>, with a
    /// hook inside each write attempt so a test can act between the backup and the write.
    /// </summary>
    internal VaultAccessResult ChangeAccess(
        VaultAccessChange change,
        ReadOnlySpan<char> newPassword,
        ReadOnlySpan<char> confirmation,
        Action<int>? duringAttempt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(change);
        ThrowIfRekeyed();

        var keyfilePath = change.Keyfile == AccessKeyfileChange.Attach && !string.IsNullOrEmpty(change.KeyfilePath)
            ? System.IO.Path.GetFullPath(change.KeyfilePath)
            : null;

        if (_stamp is null)
        {
            throw new VaultException("This vault has not been saved yet, so there is no file to change.");
        }

        if (RefuseAccessChange(change, keyfilePath, newPassword, confirmation) is { } refused)
        {
            return refused;
        }

        if (HasFileChangedSinceOpen())
        {
            throw new VaultChangedOnDiskException();
        }

        var keyfileAfter = change.Keyfile switch
        {
            AccessKeyfileChange.Attach => keyfilePath,
            AccessKeyfileChange.Remove => null,
            _ => _interop.KeyfilePath,
        };

        var hardwareKeyAfter = change.HardwareKeyChange switch
        {
            AccessHardwareKeyChange.Attach => change.HardwareKey,
            AccessHardwareKeyChange.Remove => null,
            _ => _interop.HardwareKey,
        };

        // Nothing released under the old factors is reused under the new ones (D-0318), and nothing
        // is read from this vault while its file is being replaced.
        bool wasPending;
        lock (_state)
        {
            wasPending = _pending;
            _pending = true;
            _changes++;
        }

        Edited?.Invoke(this, VaultEdit.Everything);

        var keepKeyfile = change.Keyfile == AccessKeyfileChange.Keep;
        var pending = change.SetPassword
            ? WithUtf8Password(newPassword, utf8 => _interop.ChangeKey(utf8, keyfileAfter, keepKeyfile, hardwareKeyAfter))
            : _interop.ChangeKey(null, keyfileAfter, keepKeyfile, hardwareKeyAfter);

        _lastKept = null;
        try
        {
            Commit(HasFileChangedSinceOpen, null, KeePassInterop.SaveAttempts, duringAttempt,
                backUp: true, applyFloor: false, keyChange: pending);
        }
        catch (VaultException ex) when (!pending.Committed && _lastKept is { } copy
            && ex is not VaultChangedOnDiskException and not VaultBackupException)
        {
            throw new VaultException($"{ex.Message} The vault is unchanged; a copy of it was kept at '{copy.Path}'.", ex);
        }
        finally
        {
            if (pending.Committed)
            {
                _rekeyed = true;
            }
            else
            {
                _interop.Revert(pending);

                lock (_state)
                {
                    _pending = wasPending;
                }
            }
        }

        var kept = _lastKept ?? throw new VaultException("The vault was changed without keeping the file it replaced.");

        try
        {
            _interop.ConfirmOnDisk(pending);
        }
        catch (VaultException ex)
        {
            throw new VaultAccessUnconfirmedException(kept, ex);
        }

        return new VaultAccessResult(VaultAccessOutcome.Changed, kept, default);
    }

    private VaultAccessResult? RefuseAccessChange(
        VaultAccessChange change, string? keyfilePath, ReadOnlySpan<char> newPassword, ReadOnlySpan<char> confirmation)
    {
        if (!change.SetPassword && change.Keyfile == AccessKeyfileChange.Keep && change.HardwareKeyChange == AccessHardwareKeyChange.Keep)
        {
            return Refused(VaultAccessOutcome.NothingToChange);
        }

        if (change.Keyfile == AccessKeyfileChange.Remove && _interop.KeyfilePath is null)
        {
            return Refused(VaultAccessOutcome.NoKeyfileToRemove);
        }

        if (change.HardwareKeyChange == AccessHardwareKeyChange.Remove && _interop.HardwareKey is null)
        {
            return Refused(VaultAccessOutcome.NoHardwareKeyToRemove);
        }

        var passwordAfter = change.SetPassword || _interop.KeyHasPassword;
        var keyfileAfter = change.Keyfile == AccessKeyfileChange.Attach
            || (change.Keyfile == AccessKeyfileChange.Keep && _interop.KeyfilePath is not null);

        if ((change.Keyfile == AccessKeyfileChange.Remove || change.HardwareKeyChange == AccessHardwareKeyChange.Remove)
            && !passwordAfter && !keyfileAfter)
        {
            return Refused(VaultAccessOutcome.WouldLeaveNoPassword);
        }

        if (change.Keyfile == AccessKeyfileChange.Attach && RefuseAttaching(Path, keyfilePath) is { } refusedKeyfile)
        {
            return refusedKeyfile;
        }

        if (change.HardwareKeyChange == AccessHardwareKeyChange.Attach)
        {
            var attached = change.HardwareKey
                ?? throw new ArgumentException("A hardware key to attach is required.", nameof(change));

            if (!attached.IsConnected())
            {
                return Refused(VaultAccessOutcome.HardwareKeyNotFound);
            }
        }

        if (change.SetPassword && newPassword.IsEmpty)
        {
            return Refused(VaultAccessOutcome.EmptyPassword);
        }

        if (change.SetPassword && !newPassword.SequenceEqual(confirmation))
        {
            return Refused(VaultAccessOutcome.PasswordsDoNotMatch);
        }

        return null;

        static VaultAccessResult Refused(VaultAccessOutcome outcome, KeyfileInspection keyfile = default) =>
            new(outcome, null, keyfile);
    }

    /// <summary>Why <paramref name="keyfilePath"/> may not become a factor of the vault at <paramref name="vaultPath"/>, or null when it may.</summary>
    /// <remarks>
    /// One rule for attaching a keyfile to an existing vault and for creating one with it: only a file
    /// that exists, is not the vault or one of its copies, and is not keyed by its hash (D-0287).
    /// </remarks>
    internal static VaultAccessResult? RefuseAttaching(string vaultPath, string? keyfilePath)
    {
        if (keyfilePath is null)
        {
            return Refused(VaultAccessOutcome.KeyfileUnusable, new KeyfileInspection(KeyfileOutcome.Missing, default));
        }

        if (VaultBackups.BelongsTo(vaultPath, keyfilePath))
        {
            return Refused(VaultAccessOutcome.KeyfileIsThisVault);
        }

        var inspection = VaultKeyfile.Inspect(keyfilePath);
        if (!inspection.Accepted)
        {
            return Refused(VaultAccessOutcome.KeyfileUnusable, inspection);
        }

        return inspection.IsFragile ? Refused(VaultAccessOutcome.KeyfileIsFragile, inspection) : null;

        static VaultAccessResult Refused(VaultAccessOutcome outcome, KeyfileInspection keyfile = default) =>
            new(outcome, null, keyfile);
    }

    private void ThrowIfRekeyed()
    {
        if (_rekeyed)
        {
            throw new VaultException(
                "This vault's access was changed; open it again with the new credentials before saving.");
        }
    }

    /// <summary>Writes an exact encrypted copy of the saved vault to a new file.</summary>
    /// <param name="destination">A path nothing occupies, outside this vault's backup directory.</param>
    /// <exception cref="VaultChangedOnDiskException">
    /// The file is no longer what this vault opened or last saved, so the copy would be somebody
    /// else's vault. Nothing was written.
    /// </exception>
    /// <exception cref="VaultException">The destination was refused or could not be written.</exception>
    /// <remarks>
    /// <para>
    /// The file's bytes and not a fresh serialisation, for <see cref="VaultBackups"/>' reason, so the
    /// copy opens with this vault's master password. Read once: the bytes compared with what was
    /// opened are the bytes written, and a second read would be a second chance to disagree.
    /// </para>
    /// <para>
    /// The refusals take no override. The backup directory is refused by where it is and not by what
    /// the file is called: a name <see cref="VaultBackups.List"/> recognises there would be pruned as
    /// a backup, and any other would sit among them looking like one.
    /// </para>
    /// </remarks>
    public void ExportTo(string destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfRekeyed();
        ArgumentException.ThrowIfNullOrEmpty(destination);

        if (_stamp is not { } stamp)
        {
            throw new VaultException("This vault has not been saved yet, so there is no file to copy.");
        }

        destination = System.IO.Path.GetFullPath(destination);
        RefuseAsExportDestination(destination);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VaultException($"Could not read '{Path}' to copy it: {ex.Message}", ex);
        }

        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), stamp))
        {
            throw new VaultChangedOnDiskException(
                "The vault file changed since it was opened, so the copy would not be the vault on screen.");
        }

        WriteNew(destination, bytes);
    }

    private void RefuseAsExportDestination(string destination)
    {
        if (PathIdentity.SameFile(destination, Path))
        {
            throw new VaultException("That is the vault itself. Nothing was written; name another file.");
        }

        // The directory's own name too: a file there would stop the directory ever being created, and
        // a save that cannot keep a backup does not happen.
        if (VaultBackups.BelongsTo(Path, destination))
        {
            throw new VaultException(
                "That is this vault's backup directory. Nothing was written; choose another folder.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new VaultException($"'{destination}' already exists. Nothing was written; name a new file.");
        }
    }

    private static void WriteNew(string destination, byte[] bytes)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var created = false;
        try
        {
            using var stream = new FileStream(destination, options);
            created = true;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (created)
            {
                try
                {
                    File.Delete(destination);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }

            throw new VaultException($"Could not write '{destination}': {ex.Message}", ex);
        }
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
                    $"'{entryPath}' names more than one item: a title containing a separator and " +
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
    /// <param name="afterReplacing">
    /// Called once the file is replaced and before this vault takes its new stamp, so a test can land
    /// another program's write in that gap.
    /// </param>
    internal void SaveWaiting(
        Action<int>? waitBetweenAttempts,
        int attempts = KeePassInterop.SaveAttempts,
        Action<int>? duringAttempt = null,
        Action? afterReplacing = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfRekeyed();

        Commit(HasFileChangedSinceOpen, waitBetweenAttempts, attempts, duringAttempt, backUp: !_backedUp, afterReplacing: afterReplacing);
    }

    private void Commit(
        Func<bool>? hasChangedOnDisk,
        Action<int>? waitBetweenAttempts,
        int attempts,
        Action<int>? duringAttempt = null,
        bool backUp = false,
        bool applyFloor = true,
        KeePassInterop.KeyChange? keyChange = null,
        Action? afterReplacing = null,
        CancellationToken cancellationToken = default)
    {
        var clock = new SaveClock();
        var succeeded = false;

        try
        {
            if (hasChangedOnDisk is not null && clock.Check(hasChangedOnDisk))
            {
                throw new VaultChangedOnDiskException();
            }

            lock (_state)
            {
                _writing = true;
            }

            _interop.Save(
                hasChangedOnDisk, waitBetweenAttempts, clock, attempts, duringAttempt,
                backUp ? path => PreserveBefore(path, applyFloor) : null,
                keyChange,
                cancellationToken);
            afterReplacing?.Invoke();
            var stamp = clock.Stamp(() => _interop.WrittenDigest ?? SourceSnapshot.Digest(Path));

            lock (_state)
            {
                _stamp = stamp;
                _savedAt = WrittenAt(Path);
                _base = _interop.TakeBase(_baseKey);
                _pending = false;
                _changes++;
                _writing = false;
            }

            succeeded = true;
        }
        finally
        {
            if (!succeeded)
            {
                lock (_state)
                {
                    _writing = false;
                }
            }

            clock.Publish(succeeded);
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private static DateTimeOffset? WrittenAt(string path)
    {
        try
        {
            return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps the bytes this save is about to replace. Called with the save gate held.</summary>
    /// <remarks>
    /// <paramref name="applyFloor"/> is false only on the overwriting and access-changing paths, and
    /// <see cref="_backedUp"/> is set only when the floor applies, so an overwriting save can neither
    /// be suppressed by an ordinary edit earlier in the unlock nor suppress one later in it.
    /// </remarks>
    private void PreserveBefore(string path, bool applyFloor)
    {
        var outcome = VaultBackups.Preserve(
            path, DateTimeOffset.UtcNow, applyFloor, out var createdDirectory, out _lastKept);

        if (applyFloor)
        {
            _backedUp = true;
        }

        LastBackup = new VaultBackupReport(
            VaultBackups.DirectoryFor(path), outcome, createdDirectory, VaultBackups.Retained);
    }

    /// <summary>Releases the vault's key material and decrypted contents.</summary>
    public void Dispose()
    {
        lock (_state)
        {
            if (_disposed)
            {
                return;
            }

            _interop.Dispose();
            CryptographicOperations.ZeroMemory(_baseKey);
            _disposed = true;
        }
    }

    /// <summary>Makes a change under the state lock and reports the entries it touched.</summary>
    /// <param name="apply">The change.</param>
    /// <param name="touched">What the outcome touched, or null when it changed nothing.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Until the next successful save the vault then holds something its file does not, and
    /// <see cref="ReadSaved(out IReadOnlyList{VaultEntry}?)"/> refuses it. <see cref="Edited"/> is raised outside the lock.
    /// </remarks>
    private T Change<T>(Func<T> apply, Func<T, VaultEdit?> touched)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        T outcome;
        VaultEdit? edit;

        lock (_state)
        {
            outcome = apply();
            edit = touched(outcome);

            if (edit is not null)
            {
                _pending = true;
                _changes++;
            }
        }

        if (edit is { Entries.Count: > 0 })
        {
            Edited?.Invoke(this, edit);
        }

        return outcome;
    }

    /// <summary><see cref="Change{T}(Func{T}, Func{T, VaultEdit?})"/> for a change that always changes something.</summary>
    private void Change(Action apply, VaultEdit? touched = null) =>
        Change(
            () =>
            {
                apply();
                return true;
            },
            _ => touched ?? VaultEdit.Of());

    /// <summary>Adds or replaces one attachment with what <paramref name="content"/> holds, under the cap, zeroing the buffer it was read into.</summary>
    private bool Attach(EntryName name, string attachment, Stream content, bool replacing, out bool keptInHistory)
    {
        if (content.CanSeek && content.Length - content.Position > EntryAttachment.MaximumSize)
        {
            throw TooLargeToAttach();
        }

        // One byte past the cap, so content larger than it fills the buffer and is refused rather than cut short.
        byte[] buffer = new byte[EntryAttachment.MaximumSize + 1];

        try
        {
            int length = content.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            if (length > EntryAttachment.MaximumSize)
            {
                throw TooLargeToAttach();
            }

            bool kept = false;
            bool attached = Change(
                () =>
                {
                    if (_interop.ReadAttachments(name) is not { } current)
                    {
                        return false;
                    }

                    bool present = current.Any(held => string.Equals(held.Name, attachment, StringComparison.Ordinal));

                    if (present && !replacing)
                    {
                        throw new VaultException($"This item already has an attachment called {EntryNameSanitizer.Sanitize(attachment).Text}. Replace it instead.");
                    }

                    if (!present && replacing)
                    {
                        return false;
                    }

                    return _interop.SetAttachment(name, attachment, buffer, length, out kept) > 0;
                },
                done => done ? VaultEdit.Of(name) : null);

            keptInHistory = kept;
            return attached;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>Content over <see cref="EntryAttachment.MaximumSize"/>, as a sentence.</summary>
    private static VaultException TooLargeToAttach() =>
        new($"That file is larger than {EntryAttachment.MaximumSize / (1024 * 1024)} MiB, the most keypaste attaches.");

    /// <summary>Every live entry's lost project memberships, for <see cref="LostProjectTagCheck"/>.</summary>
    internal IReadOnlyList<LostProjectTag> ReadLostProjectTags()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_state)
        {
            return _interop.ReadLostProjectTags();
        }
    }

    /// <summary>Every live entry with notes, for <see cref="NoteKeyCheck"/>.</summary>
    internal IReadOnlyList<EntryNotes> ReadNotes()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_state)
        {
            return _interop.ReadNotes();
        }
    }

    /// <summary>Every live entry's password rating and expiry outside keypaste's own groups, for <see cref="PasswordHealthCheck"/>.</summary>
    internal IReadOnlyList<EntryPasswordHealth> ReadPasswordHealth()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_state)
        {
            return _interop.ReadPasswordHealth();
        }
    }

    /// <summary>What one entry's move writes, or null with its refusals added.</summary>
    private (List<FieldWrite> Writes, string Notes)? PlanNoteMove(
        NoteKeyCheck check,
        EntryName name,
        IReadOnlyList<NoteKeyFinding> findings,
        List<NoteKeyRefusal> refusals)
    {
        if (_interop.ReadNotes(name) is not { } current || current.Uuid != findings[0].EntryUuid)
        {
            refusals.AddRange(findings.Select(finding => new NoteKeyRefusal(finding, NoteKeyRefusalReason.EntryGone)));
            return null;
        }

        var parsed = NoteKeyCheck.Parse(current.Notes);
        var repeated = NoteKeyCheck.Repeated(parsed);
        List<ParsedNoteKey> moving = [];
        List<FieldWrite> writes = [];
        var before = refusals.Count;

        foreach (var finding in findings)
        {
            var key = parsed.FirstOrDefault(key => key.Line == finding.Line && key.Field == finding.Field && key.Kind == finding.Kind);

            if (!check.Matches(finding, current.Notes) || key is null)
            {
                refusals.Add(new NoteKeyRefusal(finding, NoteKeyRefusalReason.NotesChanged));
                continue;
            }

            if (repeated.Contains(key.Field))
            {
                refusals.Add(new NoteKeyRefusal(finding, NoteKeyRefusalReason.KeyRepeated));
                continue;
            }

            var existing = _interop.ReadCustomField(name, key.Field);

            if (existing is not null && existing != key.Value)
            {
                refusals.Add(new NoteKeyRefusal(finding, NoteKeyRefusalReason.FieldHoldsOtherValue));
                continue;
            }

            if (existing is null)
            {
                writes.Add(new FieldWrite(key.Field, key.Value, Protect: true));
            }

            moving.Add(key);
        }

        return refusals.Count > before ? null : (writes, NoteKeyCheck.Without(current.Notes, moving));
    }

    /// <summary>A refused field name or tag, as a sentence.</summary>
    private static VaultException Refused(string error) =>
        new(string.Concat(char.ToUpperInvariant(error[0]).ToString(), error.AsSpan(1), "."));

    /// <summary>Whether a group path is <paramref name="groupPath"/> or lies inside it.</summary>
    private static bool IsBeneath(string candidate, string groupPath) =>
        string.Equals(candidate, groupPath, StringComparison.Ordinal)
        || candidate.StartsWith(groupPath + "/", StringComparison.Ordinal);

    /// <summary>Encodes the password to UTF-8, runs <paramref name="use"/>, and zeroes the buffer
    /// whether or not that succeeded.</summary>
    private static T WithUtf8Password<T>(
        ReadOnlySpan<char> masterPassword,
        Func<byte[], T> use)
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
