extern alias KeePass;

using System.ComponentModel;
using System.Globalization;
using KeePass::KeePassLib;
using KeePass::KeePassLib.Serialization;

namespace Keypaste.Core.Internal;

internal sealed partial class KeePassInterop
{
    /// <summary>The custom-data item KeePassXC rewrites on every save, so a new value of it is no change to its owner.</summary>
    private const string _keePassXcStamp = "_LAST_MODIFIED";

    // The root group's custom data counting keypaste's saves of the vault, which KeePassXC keeps, so an older copy put back reads lower (T-26).
    // Not the database's: KeePassLib dates each of its items, which raises the file to KDBX 4.1.
    private const string _saveCountKey = "keypaste.save-count";

    /// <summary>What the next save counts up from: the file's count as last read or written, or after a merge the larger of both copies'; null when no keypaste save has counted it.</summary>
    internal long? SaveCount => _saveCount;

    /// <summary>Whether the last save's rename reached the disk with its folder; false when the system would not flush the folder.</summary>
    internal bool LastSaveFlushedItsFolder { get; private set; } = true;

    private static long? SaveCountOf(PwDatabase database) =>
        long.TryParse(database.RootGroup.CustomData.Get(_saveCountKey), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;

    /// <summary>Changes the bytes a save produced, so a test can stand in for a writer that made a bad file.</summary>
    internal Func<byte[], byte[]>? AfterSerializing { get; set; }

    /// <summary>Called with the file a save writes, after its bytes and before its flush, so a test can fail the write as a full disk does.</summary>
    internal Action<string>? WhileWriting { get; set; }

    /// <summary>The vault as KDBX bytes, proven to open again, under the new key for a key change, before they can replace the file.</summary>
    /// <remarks>A hardware key is asked once: the bytes open on the answer it gave the save, which <see cref="HardwareKey"/> keeps for the same challenge.</remarks>
    /// <exception cref="VaultException">The bytes did not open, or must not replace the file.</exception>
    private byte[] Produce(KeyChange? keyChange)
    {
        _database.RootGroup.CustomData.Set(_saveCountKey, ((_saveCount ?? 0) + 1).ToString(CultureInfo.InvariantCulture));
        var bytes = Serialize();

        if (AfterSerializing is { } change)
        {
            bytes = change(bytes);
        }

        PwDatabase check = new();

        try
        {
            try
            {
                check.MasterKey = keyChange is null ? _database.MasterKey : Reread(keyChange.Verify);
                new KdbxFile(check).Load(new MemoryStream(bytes, writable: false), KeePass::KeePassLib.Serialization.KdbxFormat.Default, null);
            }
            catch (Exception ex)
            {
                throw new VaultException(
                    keyChange is null
                        ? "keypaste could not read back what it was about to save. Nothing was replaced."
                        : "The new credentials did not open what was about to be saved. Nothing was replaced.",
                    ex);
            }

            RefuseUnfaithful(check);
            return bytes;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
        finally
        {
            check.Close();
        }
    }

    private static HashSet<PwUuid> EntryIds(PwDatabase database) => [.. database.RootGroup.GetEntries(true).Select(entry => entry.Uuid)];

    /// <summary>Refuses a read-back that drops an entry of the file it replaces without recording its deletion, or that is not the open vault.</summary>
    /// <remarks>A delete for good and a merge record the deletion in the file's DeletedObjects; a move to the recycle bin keeps the entry.</remarks>
    /// <exception cref="VaultException">The bytes must not replace the file.</exception>
    private void RefuseUnfaithful(PwDatabase written)
    {
        HashSet<PwUuid> kept = EntryIds(written);
        HashSet<PwUuid> deleted = [.. written.DeletedObjects.Select(tombstone => tombstone.Uuid)];
        var lost = _entriesOnDisk.Count(id => !kept.Contains(id) && !deleted.Contains(id));

        if (lost > 0)
        {
            throw new VaultException(Invariant(
                $"keypaste did not save: the new file would lose {lost} item(s) nobody deleted. Nothing was replaced."));
        }

        var key = RandomNumberGenerator.GetBytes(32);

        try
        {
            using ContentDigests digests = new(key);

            if (!Fingerprint(_database, digests).SequenceEqual(Fingerprint(written, digests), StringComparer.Ordinal))
            {
                throw new VaultException("keypaste did not save: what it was about to write is not the open vault. Nothing was replaced.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Each entry's UUID and keyed content digest, in order.</summary>
    private static List<string> Fingerprint(PwDatabase database, ContentDigests digests) =>
        [.. database.RootGroup.GetEntries(true).Select(entry => entry.Uuid.ToHexString() + Convert.ToHexString(digests.Of(entry))).Order(StringComparer.Ordinal)];

    /// <summary>The vault as KDBX bytes, written to memory, where closing KeePassLib's streams cannot fail.</summary>
    /// <remarks>
    /// Never through <c>PwDatabase.Save</c>: KeePassLib writes a vault's last blocks while it closes its
    /// streams and discards what that write throws, so a disk that filled during the save was committed
    /// over the vault as a truncated file. Its file lock is off in keypaste and its hash fields
    /// have no reader here.
    /// </remarks>
    private byte[] Serialize()
    {
        MemoryStream buffer = new();

        try
        {
            // KdbxFile.Save closes the stream it is given; the buffer survives the close.
            new KdbxFile(_database).Save(buffer, null, KeePass::KeePassLib.Serialization.KdbxFormat.Default, null);
            return buffer.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
            buffer.Dispose();
        }
    }

    /// <summary>Replaces the vault, or the file its link points to, with <paramref name="bytes"/>, flushed to disk before the rename that commits them.</summary>
    /// <remarks>
    /// <paramref name="hasChangedOnDisk"/> is asked last, after the flush and immediately before the rename, and
    /// a change it reports discards the written file and leaves the vault as the other program saved it. A vault
    /// with no file yet is created by <see cref="WriteFirstFile"/>, whose rename refuses a file that arrived meanwhile.
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">Something else wrote to the vault; nothing was replaced.</exception>
    private void Replace(byte[] bytes, Func<bool>? hasChangedOnDisk)
    {
        // Resolved on every save, since a link can be pointed elsewhere while the vault is open.
        var target = VaultFiles.WriteTarget(_database.IOConnectionInfo.Path);

        if (!File.Exists(target))
        {
            LastSaveFlushedItsFolder = WriteFirstFile(target, bytes);
            return;
        }

        using (FileTransactionEx transaction = new(IOConnectionInfo.FromPath(target), _database.UseFileTransactions))
        {
            var written = (FileStream)transaction.OpenWrite();
            var writtenTo = written.Name;
            var inPlace = string.Equals(writtenTo, Path.GetFullPath(target), StringComparison.Ordinal);

            try
            {
                using (written)
                {
                    written.Write(bytes);
                    VaultFiles.KeepMode(written, target);
                    WhileWriting?.Invoke(writtenTo);
                    VaultFiles.Flush(written);
                }
            }
            catch when (!inPlace)
            {
                // KeePassLib removes only a Transactional NTFS temporary, and a half-written one on a full disk holds space the next attempt needs.
                VaultFiles.Discard(writtenTo);
                throw;
            }

            if (!inPlace && hasChangedOnDisk is not null && hasChangedOnDisk())
            {
                VaultFiles.Discard(writtenTo);
                throw new VaultChangedOnDiskException();
            }

            transaction.CommitWrite();
        }

        LastSaveFlushedItsFolder = VaultFiles.SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
    }

    /// <summary>Writes a vault's first file beside its path, flushed, and renames it into place, so a crash leaves no partial vault there.</summary>
    /// <remarks>
    /// Not through KeePassLib, which writes a file that does not exist yet straight to its name because a temporary in
    /// another folder would give it that folder's permissions; this one is in the vault's own folder. One a crash left is
    /// removed first, unless another handle holds it.
    /// </remarks>
    /// <returns>Whether the rename reached the disk with its folder.</returns>
    /// <exception cref="VaultChangedOnDiskException">A file arrived at the path first; it was not replaced.</exception>
    private bool WriteFirstFile(string target, byte[] bytes)
    {
        var temporary = target + StrandedTemporarySuffix;
        VaultFiles.Discard(temporary);
        VaultFiles.WriteNew(temporary, bytes, WhileWriting);

        try
        {
            return VaultFiles.Move(temporary, target, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VaultFiles.Discard(temporary);

            if (File.Exists(target))
            {
                throw new VaultChangedOnDiskException();
            }

            throw;
        }
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
    /// Asked as each attempt after a wait begins and again immediately before each rename, and the save
    /// is abandoned if it answers true. Null for a caller that has already been told to overwrite
    /// whatever is there.
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
    /// operations from outside it — was refuted, and F.6 owns the diagnosis.
    /// </para>
    /// <para>
    /// <b>Waiting is why the vault is re-read between attempts.</b> The thing most likely to be
    /// holding the vault's name is another process saving this same vault, so a retry that simply
    /// outlasts it writes a stale copy over the save it was waiting for — and that revert leaves no
    /// history item, because the entry it discarded never existed in this process's tree. That is
    /// the loss <see cref="VaultChangedOnDiskException"/> exists to refuse, and until that was fixed the
    /// only check for it ran once, before the first attempt.
    /// </para>
    /// <para>
    /// <b>The bytes are produced once, before the gate, and the disk is asked again before each rename.</b>
    /// Key derivation and encryption are nearly all of a save, so they run before the first attempt,
    /// hold no gate, and every retry writes the same bytes. An attempt re-reads the file as it begins
    /// and asks again after its flush, immediately before the rename, so another program's save that
    /// lands while keypaste derives keys or writes is refused rather than overwritten. What remains is
    /// a writer that commits between that last read and the rename, a few milliseconds: a detector,
    /// not a lock, since KDBX defines no file lock and KeePassXC takes none.
    /// </para>
    /// <para>
    /// Retrying is otherwise safe because the commit is one replacing move: Transactional NTFS where
    /// it works, and a single rename over the vault everywhere else (<c>KEYPASTE_ATOMIC_REPLACE</c>). A
    /// failed commit leaves the original file untouched, so a second attempt starts from the same place
    /// as the first, and a save that never succeeds reports exactly what it reported before.
    /// </para>
    /// <para>
    /// <b>The retry schedule bounds the sleeps and nothing else.</b> A caller waits for the
    /// changed-on-disk checks, producing the bytes, the gate, every attempt's work and the sleeps; <see cref="SaveTiming"/>
    /// keeps each separately, because <c>SaveAttempts × SaveRetryDelayMilliseconds</c> describes only
    /// the last of them and a claim about elapsed time needs all of them (F.10a).
    /// </para>
    /// </remarks>
    /// <exception cref="VaultChangedOnDiskException">
    /// Something else wrote to the vault after its caller last checked it. Nothing was replaced.
    /// </exception>
    /// <returns>The SHA-256 of the bytes that replaced the vault.</returns>
    /// <param name="clock">Receives this save's timing; the caller publishes it.</param>
    /// <param name="attempts">
    /// How many times to try. Defaults to the shipped budget; V-F.6 pins it to 1 so the retry
    /// cannot absorb the contention the fix is supposed to remove.
    /// </param>
    /// <param name="duringAttempt">Called inside each attempt before it writes; null outside a test.</param>
    /// <param name="beforeReplacing">
    /// Called once, on the attempt that is about to replace an existing vault, after the re-read has
    /// passed and while the gate is held. Preserves the bytes being replaced; a throw from it
    /// abandons the save. Null when the caller has already taken its backup, or has none to take.
    /// </param>
    /// <param name="keyChange">A key change this save commits, verifying the bytes before they replace the vault; null for an ordinary save.</param>
    /// <param name="cancellationToken">
    /// Fails an attempt still waiting for the gate, or about to wait for it, with
    /// <see cref="OperationCanceledException"/>, so a save queued behind another writes nothing once
    /// its caller has given up on it; an attempt that holds the gate finishes.
    /// </param>
    internal byte[] Save(
        Func<bool>? hasChangedOnDisk,
        Action<int>? waitBetweenAttempts,
        SaveClock clock,
        int attempts = SaveAttempts,
        Action<int>? duringAttempt = null,
        Action<string>? beforeReplacing = null,
        KeyChange? keyChange = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_readOnly)
        {
            throw new InvalidOperationException("A vault opened to import from is never written.");
        }

        clock.Redirect(ProcessTemporaryDirectory.EnsureRedirected);

        byte[] bytes;
        try
        {
            bytes = clock.Produce(() => Produce(keyChange));
        }
        catch (Exception ex) when (ex is not VaultException)
        {
            throw CouldNotSave(ex);
        }

        try
        {
            var wait = waitBetweenAttempts ?? SleepAfter;
            var attempt = 0;
            Retries.OnThisThread(attempts, outcome => outcome is { Exception: null, Result: false }, retry => clock.Wait(() => wait(retry)))
                .Execute(() => TryAttempt(bytes, hasChangedOnDisk, clock, ++attempt, attempts, duringAttempt, ref beforeReplacing, cancellationToken));

            _entriesOnDisk = EntryIds(_database);
            _saveCount = SaveCountOf(_database);
            keyChange?.Committed = true;
            _database.Modified = false;
            return clock.Stamp(() => SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    // The cause is named, not merely kept as an inner exception nobody prints: the disk is full, the file is open in KeePassXC, permissions changed.
    private VaultException CouldNotSave(Exception ex) =>
        new($"Could not save '{_database.IOConnectionInfo.Path}': {ex.Message}", ex);

    /// <summary>Sleeps after a refused save attempt, <see cref="SaveRetryDelayMilliseconds"/> times its number.</summary>
    internal static void SleepAfter(int attempt) => Thread.Sleep(SaveRetryDelayMilliseconds * attempt);

    /// <summary>One attempt, gated if it can transact; false if it was refused and may be retried.</summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is released before the sleep that follows a refusal</b>, so another save in this
    /// process can finish while this one waits (F.12). That makes the gate wait a wait like the sleep:
    /// another save of this vault can commit during either, so the re-read follows the gate, and a
    /// save that used to queue behind that commit and overwrite it is refused instead.
    /// </para>
    /// <para>
    /// <b>A stranded temporary is swept before the gate is released, on every way out.</b> Another
    /// save's fallback move replaces <c>&lt;vault&gt;.tmp</c> and holds no handle between its two
    /// hops, so a sweep outside the gate could delete that save's next bytes mid-move.
    /// </para>
    /// </remarks>
    private bool TryAttempt(
        byte[] bytes,
        Func<bool>? hasChangedOnDisk,
        SaveClock clock,
        int attempt,
        int attempts,
        Action<int>? duringAttempt,
        ref Action<string>? beforeReplacing,
        CancellationToken cancellationToken)
    {
        clock.BeginAttempt();
        var gated = EnterGateIfTransacting(clock, cancellationToken);
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

            if (gated)
            {
                // By name, before KeePassLib opens it with truncation: a crash can leave it a hard link to the vault.
                VaultFiles.Unlink(VaultFiles.WriteTarget(_database.IOConnectionInfo.Path) + StrandedTemporarySuffix);
            }

            clock.Attempt(() =>
            {
                duringAttempt?.Invoke(attempt);
                Replace(bytes, hasChangedOnDisk);
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
            throw CouldNotSave(ex);
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
    /// exactly as two processes used to, and on every platform they would share
    /// <c>&lt;vault&gt;.tmp</c> beside one vault. TMP cannot be made per-thread, so those attempts are
    /// serialised instead.
    /// </para>
    /// <para>
    /// <b>Only an attempt over an existing file is gated.</b> KeePassLib transacts only when the
    /// vault file exists, so a first save, whose temporary beside the vault the vault's claim keeps to one
    /// writer (<see cref="WriteFirstFile"/>), or one whose directory is gone never uses the private directory,
    /// and queueing it behind other saves' key derivation was F.10's overrun. Gating an
    /// attempt that then does not transact is harmless. The residual is a vault file created
    /// between this check and KeePassLib's own, whose attempt transacts ungated
    /// (<c>TheVendoredSaveTransactsOnlyOverAnExistingFile</c> pins the rule this restates).
    /// </para>
    /// </remarks>
    private bool EnterGateIfTransacting(SaveClock clock, CancellationToken cancellationToken)
    {
        if (!File.Exists(_database.IOConnectionInfo.Path))
        {
            return false;
        }

        clock.WaitForGate(Volatile.Read(ref _gateHolder), () => _saveGate.Wait(cancellationToken));
        Volatile.Write(ref _gateHolder, clock.Operation);
        return true;
    }

    /// <summary>Serialises transacted saves in this process. See <see cref="EnterGateIfTransacting"/>.</summary>
    private static readonly SemaphoreSlim _saveGate = new(1, 1);

    /// <summary>The <see cref="SaveClock.Operation"/> holding <see cref="_saveGate"/>; 0 when free.</summary>
    private static long _gateHolder;

    /// <summary>What <see cref="_gateHolder"/> reads while a restore, which has no save clock, holds the gate.</summary>
    internal const long HeldByAFileOperation = -1;

    /// <summary>Runs <paramref name="act"/> while no save in this process can replace a vault.</summary>
    /// <remarks>
    /// Taken whether or not the vault exists: gating something that does not transact is harmless,
    /// and asking first would be a check a file appearing could outrun. Held across the caller's own
    /// retry sleeps, unlike F.12's saves, because nothing inside derives a key and releasing it would
    /// separate keeping a vault from replacing it.
    /// </remarks>
    internal static T WhileNoSaveReplaces<T>(Func<T> act)
    {
        _saveGate.Wait();
        Volatile.Write(ref _gateHolder, HeldByAFileOperation);

        try
        {
            return act();
        }
        finally
        {
            Volatile.Write(ref _gateHolder, 0);
            _saveGate.Release();
        }
    }

    /// <summary>ERROR_TRANSACTIONAL_CONFLICT.</summary>
    /// <remarks>
    /// Observed on Windows 10 Pro 19045 by F.7's probe, on a non-transacted move onto the
    /// destination name — which is the second hop of the fallback in
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
    /// Every file and access failure but a full disk or quota is retried, since a scanner holding the
    /// file raises the same types as a bad path or a wrong permission, which take the whole budget to
    /// be reported. A full disk fails on the first attempt: waiting does not free space.
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
        IOException full when IsFullDisk(full) => false,
        IOException or UnauthorizedAccessException => true,
        Win32Exception win32 => IsATransientMoveCode(win32.NativeErrorCode),
        _ => false,
    };

    /// <summary>Whether the disk or the person's quota is full: ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL on Windows, ENOSPC or EDQUOT elsewhere.</summary>
    /// <remarks>.NET carries a Windows error as its HRESULT and a Unix one as the bare errno, and EDQUOT is 122 on Linux and 69 on macOS.</remarks>
    internal static bool IsFullDisk(IOException ex) =>
        OperatingSystem.IsWindows()
            ? ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027)
            : ex.HResult == 28 || ex.HResult == (OperatingSystem.IsLinux() ? 122 : 69);

    /// <summary>Whether a native move failure is one another attempt might get past.</summary>
    /// <remarks>
    /// Each code here is a recorded observation, named with the platform whose probe produced it.
    /// <b>A code seen only in a CI failure gets its own observation rather than joining this
    /// list</b>: the retry budget grew once on a number nobody had reproduced, and that is still
    /// carrying it.
    /// </remarks>
    private static bool IsATransientMoveCode(int code) => code switch
    {
        // Windows 10 Pro 19045.
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

    /// <summary>Removes the file the fallback move stranded beside the vault, or beside the file its link points to.</summary>
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
    /// and the vault already deleted, which is accepted rather than solved.
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
            VaultFiles.Discard(VaultFiles.WriteTarget(_database.IOConnectionInfo.Path) + StrandedTemporarySuffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
