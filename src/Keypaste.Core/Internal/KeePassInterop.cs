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
    internal IReadOnlyList<VaultEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<VaultEntry> entries = [];
        Collect(_database.RootGroup, string.Empty, entries);
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
        CollectGroups(_database.RootGroup, string.Empty, paths);
        return paths;
    }

    /// <summary>Removes the one entry with this name.</summary>
    /// <returns>The number of entries removed: 0 if nothing matched, otherwise 1.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    internal int RemoveEntry(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Locate(name) is not { } found)
        {
            return 0;
        }

        // Removed outright rather than moved to the recycle bin: a vault the user asked to
        // delete from should not keep a readable copy of the secret (docs/PRODUCT.md law 3.4). This
        // takes the entry's history with it, which is the only way a value keypaste previously
        // wrote can be erased — see DECISIONS.md D-0014.
        found.Group.Entries.Remove(found.Entry);
        _database.DeletedObjects.Add(new PwDeletedObject(found.Entry.Uuid, DateTime.UtcNow));
        return 1;
    }

    /// <summary>
    /// Whether saves go through a temporary file. Exists so the regression test for the
    /// open-path defect can assert the mechanism directly; observing the effect is not practical,
    /// because an in-place save also leaves no debris behind.
    /// </summary>
    internal bool UsesFileTransactions => _database.UseFileTransactions;

    /// <summary>
    /// The number of history items an entry carries, or -1 if no entry has that name.
    /// </summary>
    /// <remarks>
    /// Exists so that "overwriting a value keeps the previous one" can be asserted rather than
    /// assumed. keypaste has no feature that reads history, so without this seam a change that
    /// silently stopped retaining it would pass every test while the documentation kept promising
    /// it (DECISIONS.md D-0014).
    /// </remarks>
    internal int CountHistoryItems(EntryName name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Locate(name) is { } found ? (int)found.Entry.History.UCount : -1;
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
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">
    /// Something else wrote to the vault while this save was waiting to retry. Nothing was written.
    /// </exception>
    /// <param name="attempts">
    /// How many times to try. Defaults to the shipped budget; V-F.6 pins it to 1 so the retry
    /// cannot absorb the contention the fix is supposed to remove.
    /// </param>
    internal void Save(
        Func<bool>? hasChangedOnDisk,
        Action<int>? waitBetweenAttempts,
        int attempts = SaveAttempts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ProcessTemporaryDirectory.EnsureRedirected();

        // One save at a time in this process. The private directory is the whole process's, so two
        // concurrent saves would name their temporaries in it together and collide exactly as two
        // processes used to (D-0122). TMP cannot be made per-thread, so the saves are made
        // one-at-a-time instead.
        _saveGate.Wait();

        // Set by whichever attempt is refused, and acted on once on the way out — including the way
        // out of a save that then succeeded. The fallback move strands its file on the attempt that
        // is refused, and no later attempt, successful or not, ever goes back for it.
        var strandedATemporary = false;

        try
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    _database.Save(null);
                    return;
                }
                catch (Exception ex) when (attempt < attempts && IsTransient(ex))
                {
                    strandedATemporary |= StrandsATemporary(ex);
                }
                catch (Exception ex) when (ex is not VaultException)
                {
                    strandedATemporary |= StrandsATemporary(ex);

                    // The cause is named, not merely kept as an inner exception nobody prints. A
                    // save can fail for reasons the user can act on — the disk is full, the file is
                    // open in KeePassXC, permissions changed — and "Could not save 'vault.kdbx'."
                    // on its own tells them none of them.
                    throw new VaultException(
                        $"Could not save '{_database.IOConnectionInfo.Path}': {ex.Message}", ex);
                }

                if (waitBetweenAttempts is null)
                {
                    Thread.Sleep(SaveRetryDelayMilliseconds * attempt);
                }
                else
                {
                    waitBetweenAttempts(attempt);
                }

                if (hasChangedOnDisk?.Invoke() == true)
                {
                    throw new VaultChangedOnDiskException();
                }
            }
        }
        finally
        {
            _saveGate.Release();

            if (strandedATemporary)
            {
                SweepStrandedTemporary();
            }
        }
    }

    /// <summary>Serialises saves in this process. See the comment where it is taken.</summary>
    private static readonly SemaphoreSlim _saveGate = new(1, 1);

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

    private static void Collect(PwGroup group, string groupPath, List<VaultEntry> entries)
    {
        foreach (PwEntry entry in group.Entries)
        {
            entries.Add(Read(entry, groupPath));
        }

        foreach (PwGroup child in group.Groups)
        {
            Collect(child, ChildPath(groupPath, child.Name), entries);
        }
    }

    private static void CollectGroups(PwGroup group, string groupPath, List<string> paths)
    {
        foreach (PwGroup child in group.Groups)
        {
            string childPath = ChildPath(groupPath, child.Name);
            paths.Add(childPath);
            CollectGroups(child, childPath, paths);
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
    /// Two entries answering to one name are refused rather than resolved to whichever came first,
    /// because there is no answer that is not a guess (docs/PRODUCT.md law 3.7). KDBX permits that
    /// within one group and KeePassXC will make it; <see cref="EntryHandle"/> is what keeps each of
    /// them individually addressable.
    /// </para>
    /// </remarks>
    private (PwGroup Group, PwEntry Entry)? Locate(EntryName name)
    {
        (PwGroup Group, PwEntry Entry)? found = null;
        var matches = 0;

        Search(_database.RootGroup, string.Empty);

        if (matches > 1)
        {
            string where = name.GroupPath.Length == 0 ? "the root group" : name.GroupPath;
            throw new VaultException(
                $"'{name.Title}' in '{where}' names {matches} entries. keypaste will not guess " +
                "which one you meant; rename one of them in KeePassXC.");
        }

        return found;

        void Search(PwGroup group, string groupPath)
        {
            if (string.Equals(groupPath, name.GroupPath, StringComparison.Ordinal))
            {
                foreach (PwEntry candidate in group.Entries)
                {
                    if (string.Equals(ReadField(candidate, PwDefs.TitleField), name.Title, StringComparison.Ordinal))
                    {
                        matches++;
                        found ??= (group, candidate);
                    }
                }
            }

            foreach (PwGroup child in group.Groups)
            {
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
