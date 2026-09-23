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
    private bool _backedUp;
    private bool _rekeyed;
    private VaultBackup? _lastKept;

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
    internal static Vault CreateWith(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Create(path, utf8, keyfilePath)),
            path,
            stamp: false);
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
    public static Vault Open(string path, ReadOnlySpan<char> masterPassword, string? keyfilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (keyfilePath is not null && VaultKeyfile.Inspect(keyfilePath).Outcome == KeyfileOutcome.XmlUnreadable)
        {
            throw new UnreadableKeyfileException(keyfilePath);
        }

        return new Vault(
            WithUtf8Password(masterPassword, utf8 => KeePassInterop.Open(path, utf8, keyfilePath)),
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

    /// <summary>
    /// Every entry whose title, group path, username or URL contains <paramref name="query"/>.
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
    /// <see cref="EntryName"/> and a <see cref="MatchedFields"/>, both of which a listing already
    /// discloses. The password and the notes are not compared at all: notes routinely hold recovery
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
    public IReadOnlyList<EntryMatch> Search(string query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(query);

        return _interop.Search(query.Trim());
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

        return _interop.RemoveEntry(name, out recycled);
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
            ? _interop.RemoveEntry(EntryName.Of(found), out _)
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

        return _interop.RenameEntry(name, title, out renamed);
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

        return _interop.MoveEntry(name, destinationGroupPath, out moved);
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

        return _interop.Relocate(name, target, out result);
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

        return _interop.CreateGroup(parentGroupPath, name, out created);
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

        return _interop.RenameGroup(groupPath, name, out renamedPath);
    }

    /// <summary>Writes a protected custom string onto an entry. A test seam; nothing else uses it.</summary>
    /// <remarks>
    /// Internal for the reason <see cref="AddGroupUnchecked"/> is: it exists so a fixture can hold
    /// the custom fields KeePassXC writes, which keypaste preserves and <see cref="Search"/> must
    /// never read.
    /// </remarks>
    internal void AddProtectedFieldUnchecked(EntryName name, string field, string value) =>
        _interop.AddProtectedFieldUnchecked(name, field, value);

    /// <summary>Adds a group without applying any of the rules. A test seam; nothing else uses it.</summary>
    /// <remarks>
    /// Two sibling groups of one name is a shape KDBX permits, KeePassXC makes and keypaste refuses
    /// to create, and keypaste still has to have an answer for a vault that holds one. Internal for
    /// the reason D-0255 gives about <see cref="SetRecyclesDeletedEntries"/>.
    /// </remarks>
    internal void AddGroupUnchecked(string parentGroupPath, string name) =>
        _interop.AddGroupUnchecked(parentGroupPath, name);

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
        ThrowIfRekeyed();

        // The check is handed to the retry loop, not just made before it. The name a save contends
        // for is most often held by another process saving this same vault, so a retry that waits
        // that out and then writes reverts it — see KeePassInterop.Save and D-0119.
        Commit(HasFileChangedSinceOpen, null, KeePassInterop.SaveAttempts, backUp: !_backedUp);
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

    /// <summary>Changes what unlocks this vault: its master password, its keyfile, or both.</summary>
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
    /// already exists is attached, and never one keyed by its hash (D-0287, D-0288).
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

        var pending = change.SetPassword
            ? WithUtf8Password(newPassword, utf8 => _interop.ChangeKey(utf8, keyfileAfter, change.Keyfile == AccessKeyfileChange.Keep))
            : _interop.ChangeKey(null, keyfileAfter, change.Keyfile == AccessKeyfileChange.Keep);

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
        if (!change.SetPassword && change.Keyfile == AccessKeyfileChange.Keep)
        {
            return Refused(VaultAccessOutcome.NothingToChange);
        }

        if (change.Keyfile == AccessKeyfileChange.Remove && _interop.KeyfilePath is null)
        {
            return Refused(VaultAccessOutcome.NoKeyfileToRemove);
        }

        if (change.Keyfile == AccessKeyfileChange.Remove && !change.SetPassword && !_interop.KeyHasPassword)
        {
            return Refused(VaultAccessOutcome.WouldLeaveNoPassword);
        }

        if (change.Keyfile == AccessKeyfileChange.Attach && RefuseAttaching(Path, keyfilePath) is { } refusedKeyfile)
        {
            return refusedKeyfile;
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
        ThrowIfRekeyed();

        Commit(HasFileChangedSinceOpen, waitBetweenAttempts, attempts, duringAttempt, backUp: !_backedUp);
    }

    private void Commit(
        Func<bool>? hasChangedOnDisk,
        Action<int>? waitBetweenAttempts,
        int attempts,
        Action<int>? duringAttempt = null,
        bool backUp = false,
        bool applyFloor = true,
        KeePassInterop.KeyChange? keyChange = null)
    {
        var clock = new SaveClock();
        var succeeded = false;

        try
        {
            if (hasChangedOnDisk is not null && clock.Check(hasChangedOnDisk))
            {
                throw new VaultChangedOnDiskException();
            }

            _interop.Save(
                hasChangedOnDisk, waitBetweenAttempts, clock, attempts, duringAttempt,
                backUp ? path => PreserveBefore(path, applyFloor) : null,
                keyChange);
            _stamp = clock.Stamp(() => SourceSnapshot.Digest(Path));
            succeeded = true;
        }
        finally
        {
            clock.Publish(succeeded);
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
        if (_disposed)
        {
            return;
        }

        _interop.Dispose();
        _disposed = true;
    }

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
