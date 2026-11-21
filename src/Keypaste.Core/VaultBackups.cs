using System.Globalization;
using Keypaste.Core.Internal;
using Keypaste.Core.Ownership;
using Polly;

namespace Keypaste.Core;

/// <summary>One copy of a vault, taken before a save or a restore replaced it.</summary>
/// <param name="Path">Where the copy is.</param>
/// <param name="TakenAt">When it was taken, in UTC.</param>
/// <remarks>
/// <see cref="TakenAt"/> is read from the file name and never from its modification time, which a
/// copy does not carry across and which a restore or a folder sync rewrites.
/// </remarks>
public sealed record VaultBackup(string Path, DateTimeOffset TakenAt);

/// <summary>What <see cref="VaultBackups.Preserve"/> did.</summary>
public enum VaultBackupOutcome
{
    /// <summary>A copy was written.</summary>
    Written,

    /// <summary>
    /// Nothing was written, because a backup newer than <see cref="VaultBackups.Floor"/> is
    /// already there.
    /// </summary>
    SkippedRecent,
}

/// <summary>
/// Keeps the vault's previous encrypted bytes beside it, so losing the file is recoverable.
/// </summary>
/// <remarks>
/// <para>
/// Entry history and the recycle bin both live <em>inside</em> the vault, so neither survives the
/// file being truncated, overwritten by something else or deleted. Recovery distinguishes an
/// earlier entry value, a deleted entry, a damaged vault file and a lost unlock secret, each proved apart, and this is the third of them: a damaged vault file.
/// It is not the fourth — a backup opens with the credentials it was made under, and nothing here
/// recovers a lost unlock secret.
/// </para>
/// <para>
/// <b>A backup is the existing file's bytes, copied.</b> Nothing is decrypted, re-encrypted or
/// re-serialised: law 6 forbids touching the format, and a copy cannot get it wrong. It also means
/// a backup carries whatever reader floor its source carried — a vault that has recycled anything is
/// KDBX 4.1, and so is its backup.
/// </para>
/// <para>
/// <b>The vocabulary collides and the two meanings must not.</b> KeePassLib calls an entry's history
/// revisions "backups" — <c>CreateBackup</c>, <c>RestoreFromBackup</c>, <c>MaintainBackups</c>, which
/// has an evict-the-oldest rule of its own. That sense is confined to <c>KeePassInterop</c> and stays
/// there. Here "backup" means the whole file, because entry history is not a
/// whole-vault backup.
/// </para>
/// <para>
/// This lives in the core rather than in either front end because two implementations
/// of a rule that replaces a vault file are two chances to destroy one.
/// </para>
/// </remarks>
public static class VaultBackups
{
    /// <summary>How many of a vault's most recent backups are kept, besides the daily and weekly ones.</summary>
    public const int Retained = 5;

    /// <summary>For how many days, today included, the newest backup of each day is kept.</summary>
    internal const int DailyFor = 7;

    /// <summary>For how many weeks, this one included, the newest backup of each week is kept.</summary>
    internal const int WeeklyFor = 4;

    /// <summary>The suffix on a vault's backup directory.</summary>
    internal const string DirectorySuffix = ".backups";

    /// <summary>
    /// How close to the newest backup a save may come before it stops taking another.
    /// </summary>
    /// <remarks>
    /// The desktop holds one <see cref="Vault"/> across an unlock, so its per-unlock rule already
    /// collapses a session's edits into one backup. Each CLI command opens its own vault, though, so
    /// without a floor a six-command script would evict the whole retention window in seconds and
    /// leave five backups all from the same minute.
    /// </remarks>
    internal static readonly TimeSpan Floor = TimeSpan.FromMinutes(15);

    /// <summary>The stamp in a backup's name. Fixed width, so an ordinal sort is chronological.</summary>
    private const string _stampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>Where the backups of the vault at <paramref name="vaultPath"/> are kept: beside the file a save replaces, which is a link's target rather than the link.</summary>
    /// <param name="vaultPath">The vault itself, not its directory.</param>
    /// <remarks>
    /// Keyed on that file's <b>whole name</b>, extension included. Keying on the stem would give
    /// <c>personal.kdbx</c> and <c>personal.kdb</c> in one folder a single directory and identical
    /// backup names inside it, so retention would prune across both vaults.
    /// </remarks>
    public static string DirectoryFor(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        return Beside(FileOf(vaultPath));
    }

    /// <summary>The folders <see cref="List"/> reads that hold backups, <see cref="DirectoryFor"/> first and always.</summary>
    /// <remarks>The second, beside a link, holds copies taken before backups followed the link's target, which may open with earlier credentials.</remarks>
    public static IReadOnlyList<string> Directories(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        return [DirectoryFor(vaultPath), .. Folders(vaultPath).Skip(1).Where(folder => ListIn(folder.Directory, folder.NamedAfter).Count > 0).Select(folder => folder.Directory)];
    }

    /// <summary>Whether <paramref name="path"/> is the vault itself, one of its backup directories, or inside one.</summary>
    public static bool BelongsTo(string vaultPath, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (PathIdentity.SameFile(path, vaultPath))
        {
            return true;
        }

        var target = PathIdentity.Canonical(path);
        return Folders(vaultPath).Any(folder =>
        {
            var backups = PathIdentity.Canonical(folder.Directory);
            return string.Equals(target, backups, PathIdentity.Comparison)
                || target.StartsWith(backups + Path.DirectorySeparatorChar, PathIdentity.Comparison);
        });
    }

    /// <summary>The file a save replaces: a link's final target, or the path as named when it is no link or cannot be followed.</summary>
    private static string FileOf(string vaultPath)
    {
        var full = Path.GetFullPath(vaultPath);
        try
        {
            return VaultFiles.WriteTarget(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return full;
        }
    }

    private static string Beside(string file)
    {
        var directory = Path.GetDirectoryName(file)
            ?? throw new ArgumentException($"'{file}' has no directory to sit in.", nameof(file));

        return Path.Combine(directory, Path.GetFileName(file) + DirectorySuffix);
    }

    /// <summary>
    /// The folders holding the vault's backups, each with the file its backups are named after:
    /// <see cref="DirectoryFor"/>, and for a link also the folder beside the link, where backups went
    /// before they followed the link's target.
    /// </summary>
    /// <remarks>The older folder is read and pruned in place, never moved: it may be on another volume, and its names carry the link's name.</remarks>
    private static IEnumerable<(string Directory, string NamedAfter)> Folders(string vaultPath)
    {
        var file = FileOf(vaultPath);
        yield return (Beside(file), file);

        var named = Path.GetFullPath(vaultPath);
        if (!string.Equals(named, file, StringComparison.Ordinal))
        {
            yield return (Beside(named), named);
        }
    }

    /// <summary>Moves a conflicted copy already merged in with the vault's backups, under its own name, so no rotation removes it.</summary>
    /// <param name="vaultPath">The vault.</param>
    /// <param name="copyPath">The copy.</param>
    /// <returns>Where it is now.</returns>
    public static string Archive(string vaultPath, string copyPath)
    {
        ArgumentNullException.ThrowIfNull(vaultPath);
        ArgumentNullException.ThrowIfNull(copyPath);

        var folder = DirectoryFor(vaultPath);
        VaultFiles.CreateDirectory(folder, ownerOnly: true);
        var name = Path.GetFileNameWithoutExtension(copyPath);
        var target = Path.Combine(folder, Path.GetFileName(copyPath));

        for (var counter = 2; File.Exists(target); counter++)
        {
            target = Path.Combine(folder, $"{name}.{counter.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(copyPath)}");
        }

        // A power cut before the folder is flushed leaves the copy where it was, which the next look finds again.
        _ = VaultFiles.Move(copyPath, target, overwrite: false);
        VaultFiles.RestrictToOwner(target);

        return target;
    }

    /// <summary>The backups of the vault at <paramref name="vaultPath"/>, newest first.</summary>
    /// <remarks>
    /// Only files this type named are listed. Anything else in the directory — a copy somebody made
    /// themselves, a note, a half-written temporary — is left alone and never counted against
    /// retention, because pruning a file we did not write is not ours to do.
    /// </remarks>
    public static IReadOnlyList<VaultBackup> List(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var found = new List<(VaultBackup Backup, int Counter)>();
        foreach (var (directory, namedAfter) in Folders(vaultPath))
        {
            found.AddRange(ListIn(directory, namedAfter));
        }

        // The counter is part of the order and not a tiebreak to skip. Two backups sharing a second
        // differ only by it, and an ordinal sort of the whole name gets them backwards: the second
        // one is the one carrying "-2", and '-' sorts before the '.' that begins the first one's
        // extension, so the older name would come out on top. That is reachable — SaveOverwriting
        // takes a backup whatever else this unlock has already done.
        found.Sort(static (left, right) =>
        {
            var byTime = right.Backup.TakenAt.CompareTo(left.Backup.TakenAt);
            return byTime != 0 ? byTime : right.Counter.CompareTo(left.Counter);
        });

        return [.. found.Select(static parsed => parsed.Backup)];
    }

    private static List<(VaultBackup Backup, int Counter)> ListIn(string directory, string namedAfter)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var prefix = Path.GetFileNameWithoutExtension(namedAfter) + ".";
        var extension = Path.GetExtension(namedAfter);

        string[] names;
        try
        {
            names = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<(VaultBackup Backup, int Counter)>();
        foreach (var name in names)
        {
            if (Read(Path.GetFileName(name), prefix, extension) is { } parsed)
            {
                found.Add((new VaultBackup(name, parsed.TakenAt), parsed.Counter));
            }
        }

        return found;
    }

    /// <summary>
    /// Copies the vault at <paramref name="vaultPath"/> beside itself, then drops the copies
    /// <see cref="Keeps"/> does not keep.
    /// </summary>
    /// <param name="vaultPath">An existing vault, about to be replaced.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <param name="applyFloor">
    /// Whether <see cref="Floor"/> may suppress this backup. False on the overwriting save path: see
    /// <see cref="Vault.SaveOverwriting"/>, which discards somebody else's write, and the vault it
    /// replaces is exactly the copy somebody needs when that choice turns out to be the wrong one.
    /// </param>
    /// <param name="createdDirectory">Whether this call is what created the backup directory.</param>
    /// <param name="kept">The copy written, or <see langword="null"/> when the floor suppressed it.</param>
    /// <exception cref="VaultBackupException">
    /// The copy could not be written. <b>Nothing was pruned and nothing was replaced</b>; the caller
    /// must abandon the save.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The order is load-bearing. The new copy is complete and named before anything is pruned, so a
    /// failure here can never be paid for out of the last good backup. A partly written copy never
    /// holds a final name, because it is written under a reserved temporary one and renamed within
    /// the same directory.
    /// </para>
    /// <para>
    /// Called with the save gate held, so no other save in this process can replace the vault between
    /// the copy and the write it is a backup of.
    /// </para>
    /// </remarks>
    internal static VaultBackupOutcome Preserve(
        string vaultPath, DateTimeOffset now, bool applyFloor, out bool createdDirectory, out VaultBackup? kept)
    {
        createdDirectory = false;
        kept = null;

        var directory = DirectoryFor(vaultPath);

        Sweep(directory);

        if (applyFloor && List(vaultPath) is [{ TakenAt: var newest }, ..]
            && newest <= now && now - newest < Floor)
        {
            // Strictly `newest <= now` first. A stamp in the future — a clock moved back, or a vault
            // folder copied from a machine ahead of this one — makes now - newest negative, which is
            // less than any floor, and a signed comparison alone would then skip every backup for as
            // long as that file sits there. Ahead of us is due, not recent.
            return VaultBackupOutcome.SkippedRecent;
        }

        kept = Keep(vaultPath, now, out createdDirectory);

        Prune(vaultPath, now);

        return VaultBackupOutcome.Written;
    }

    /// <summary>Copies the vault into its backup directory under a complete name, and prunes nothing.</summary>
    /// <remarks>
    /// The half a save and a restore share. Pruning is the save's alone: a restore that pruned could
    /// drop the very backup it is restoring, so <see cref="Prune"/> is reachable only from
    /// <see cref="Preserve"/>.
    /// </remarks>
    private static VaultBackup Keep(string vaultPath, DateTimeOffset now, out bool createdDirectory)
    {
        var file = FileOf(vaultPath);
        var directory = Beside(file);

        createdDirectory = Ensure(directory);

        var reserved = Path.Combine(
            directory,
            $".{Path.GetFileName(file)}.keypaste-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");

        try
        {
            VaultFiles.Copy(vaultPath, reserved);

            var kept = Name(directory, file, now);
            // A backup lost to a power cut leaves the vault as it is; the save that follows reports its own folder.
            _ = VaultFiles.Move(reserved, kept, overwrite: false);

            var utc = now.ToUniversalTime();
            return new VaultBackup(kept, utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            VaultFiles.Discard(reserved);

            throw new VaultBackupException(
                $"Could not back '{vaultPath}' up to '{directory}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Opens one of the vault's backups to establish that it can be restored, and changes nothing.
    /// </summary>
    /// <param name="vaultPath">The vault the backup belongs to, which need not exist or be readable.</param>
    /// <param name="backup">One of the copies <see cref="List"/> names for that vault.</param>
    /// <param name="password">The master password the backup was made under.</param>
    /// <param name="keyfilePath">
    /// The keyfile the backup was made under, or <see langword="null"/>. A copy is the vault's own
    /// bytes, so it opens under exactly the factors the vault had when it was taken — which is also
    /// why a later access change does not reach the copies already on disk.
    /// </param>
    /// <param name="hardwareKey">The hardware key the backup was made under, or <see langword="null"/>.</param>
    /// <returns>What the backup holds, bound to the bytes that were opened.</returns>
    /// <exception cref="VaultRestoreException">
    /// <see cref="List"/> does not name the file, it has no KDBX header, or it changed while it was
    /// being opened. The password was not used in the first two cases.
    /// </exception>
    /// <exception cref="InvalidMasterPasswordException">
    /// The password does not open the backup, or its body is damaged. One answer on purpose: nothing
    /// finer can be said of a file that did not decrypt.
    /// </exception>
    /// <exception cref="HardwareKeys.HardwareKeyException">The hardware key did not answer.</exception>
    public static VaultBackupSummary Inspect(
        string vaultPath,
        VaultBackup backup,
        ReadOnlySpan<char> password,
        string? keyfilePath = null,
        HardwareKeys.HardwareKey? hardwareKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);
        ArgumentNullException.ThrowIfNull(backup);

        vaultPath = Path.GetFullPath(vaultPath);
        var listed = Listed(vaultPath, backup.Path);

        var digest = SourceSnapshot.Digest(listed.Path)
            ?? throw new VaultRestoreException($"The backup '{listed.Path}' could not be read.");

        int entries, groups, projects;
        long? saveCount;
        try
        {
            using var vault = Vault.OpenReadOnly(listed.Path, password, keyfilePath, hardwareKey);
            saveCount = vault.SaveCount;
            entries = vault.ReadEntries(includeReserved: true).Count;
            groups = vault.ReadGroupPaths(includeReserved: true).Count;
            projects = ProjectCatalog.Read(vault).Projects.Count;
        }
        catch (VaultException ex) when (ex is not UnreadableKeyfileException and not HardwareKeys.HardwareKeyException)
        {
            throw new InvalidMasterPasswordException(
                "That password does not open this backup, or the backup is damaged.", ex);
        }

        if (SourceSnapshot.Digest(listed.Path) is not { } after
            || !CryptographicOperations.FixedTimeEquals(digest, after))
        {
            throw new VaultRestoreException($"The backup '{listed.Path}' changed while it was being checked.");
        }

        return new VaultBackupSummary(vaultPath, listed, entries, groups, projects, Facts(vaultPath), digest) { SaveCount = saveCount };
    }

    /// <summary>Puts a validated backup in the vault's place, byte for byte, under this process's claim on the vault.</summary>
    /// <param name="claim">This process's claim on the vault, so no other keypaste process holds it while it is replaced.</param>
    /// <param name="validated">What <see cref="Inspect"/> returned for the backup.</param>
    /// <returns>Which copy was restored, and what became of the file it replaced.</returns>
    /// <exception cref="ArgumentException"><paramref name="claim"/> names another vault or was released.</exception>
    /// <exception cref="VaultRestoreException">The backup is no longer the one validated, or the vault could not be replaced.</exception>
    /// <exception cref="VaultBackupException">The vault being replaced could not be kept, so it was not replaced.</exception>
    /// <exception cref="VaultChangedOnDiskException">The vault changed during the restore, so it was not replaced.</exception>
    /// <remarks>
    /// <para>
    /// <b>Any exception means the vault was not replaced.</b> The rename is the last act and nothing
    /// follows it that can fail.
    /// </para>
    /// <para>
    /// The restored vault is the backup's bytes, so it opens with the password the backup was made
    /// under. Nothing is re-serialised, for the reason a backup is a copy in the first place.
    /// </para>
    /// <para>
    /// The file being replaced is kept as an ordinary backup, exempt from <see cref="Floor"/>, unless
    /// a listed backup already holds exactly its bytes. <b>Nothing is pruned</b>: the next save does
    /// that, and until then a restore can only add to what is recoverable.
    /// </para>
    /// <para>
    /// Staged beside the vault, or beside the file its link points to, rather than in the backup directory. A rename within one directory is
    /// on one volume by construction; a backup directory that is a junction to another volume would
    /// turn the rename into a copy over the live vault, which a kill could leave half written.
    /// </para>
    /// </remarks>
    public static VaultRestoreReport Restore(VaultClaim claim, VaultBackupSummary validated) => Restore(claim, validated, null, null);

    /// <param name="claim">This process's claim on the vault.</param>
    /// <param name="validated">What <see cref="Inspect"/> returned for the backup.</param>
    /// <param name="beforeReplacing">
    /// Called with the staged file's path after the vault has been kept and before it is replaced, so
    /// a test can act at the one instant between the two.
    /// </param>
    /// <param name="waitBetweenAttempts">Called instead of sleeping between rename attempts.</param>
    internal static VaultRestoreReport Restore(
        VaultClaim claim, VaultBackupSummary validated, Action<string>? beforeReplacing, Action<int>? waitBetweenAttempts)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(validated);
        claim.CheckHolds(validated.VaultPath, nameof(claim));

        var restored = KeePassInterop.WhileNoSaveReplaces(() => Replace(validated, beforeReplacing, waitBetweenAttempts));
        SeenSaveCount.Remember(claim.Home, claim.Vault, validated.SaveCount);
        return restored;
    }

    private static VaultRestoreReport Replace(
        VaultBackupSummary validated, Action<string>? beforeReplacing, Action<int>? waitBetweenAttempts)
    {
        var vaultPath = validated.VaultPath;
        var target = WriteTarget(vaultPath);

        SweepStaged(target);

        var backup = Listed(vaultPath, validated.Backup.Path);
        var bytes = ReadValidated(backup, validated.Digest);
        var staged = Stage(target, bytes);

        try
        {
            var live = ObserveReadable(vaultPath, waitBetweenAttempts);
            VaultBackup? preserved = null;
            VaultBackup? alreadyKeptAs = null;

            if (live.Digest is { } liveDigest)
            {
                alreadyKeptAs = HeldBy(vaultPath, liveDigest);
                if (alreadyKeptAs is null)
                {
                    Sweep(DirectoryFor(vaultPath));
                    preserved = Keep(vaultPath, DateTimeOffset.UtcNow, out _);
                }
            }

            beforeReplacing?.Invoke(staged);

            var flushed = MoveIntoPlace(staged, target, live, waitBetweenAttempts);

            return new VaultRestoreReport(backup, preserved, alreadyKeptAs) { Unflushed = !flushed };
        }
        catch
        {
            VaultFiles.Discard(staged);
            throw;
        }
    }

    /// <summary>The file the restore replaces, which is a link's target rather than the link.</summary>
    private static string WriteTarget(string vaultPath)
    {
        try
        {
            return VaultFiles.WriteTarget(vaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VaultRestoreException($"Could not find the file '{vaultPath}' links to: {ex.Message}", ex);
        }
    }

    /// <summary>The listed backup at <paramref name="backupPath"/>, which must carry a KDBX header.</summary>
    private static VaultBackup Listed(string vaultPath, string backupPath)
    {
        var listed = List(vaultPath)
            .FirstOrDefault(candidate => string.Equals(candidate.Path, backupPath, StringComparison.Ordinal))
            ?? throw new VaultRestoreException($"'{backupPath}' is not a backup of '{vaultPath}'.");

        return KdbxHeader.IsVaultFile(listed.Path)
            ? listed
            : throw new VaultRestoreException($"The backup '{listed.Path}' is not a KeePass database.");
    }

    /// <summary>The backup's bytes, read once, which must be the bytes <see cref="Inspect"/> opened.</summary>
    private static byte[] ReadValidated(VaultBackup backup, byte[] digest)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(backup.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VaultRestoreException($"The backup '{backup.Path}' could not be read: {ex.Message}", ex);
        }

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), digest)
            ? bytes
            : throw new VaultRestoreException(
                $"The backup '{backup.Path}' changed since it was checked, so nothing was restored.");
    }

    /// <summary>Writes the bytes to a new owner-only file beside the vault.</summary>
    private static string Stage(string vaultPath, byte[] bytes)
    {
        var staged = Path.Combine(
            Path.GetDirectoryName(vaultPath)!,
            $".{Path.GetFileName(vaultPath)}.keypaste-restore-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");

        try
        {
            VaultFiles.WriteNew(staged, bytes);
            return staged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VaultRestoreException($"Could not stage the backup beside '{vaultPath}': {ex.Message}", ex);
        }
    }

    /// <summary>Whether a file is at the vault's path, and what it holds; no digest when it cannot be read.</summary>
    private static (bool Exists, byte[]? Digest) Observe(string vaultPath) =>
        File.Exists(vaultPath) ? (true, SourceSnapshot.Digest(vaultPath)) : (false, null);

    /// <summary>The vault as it is before anything is done to it, waiting out a file that cannot be read.</summary>
    /// <remarks>
    /// A vault another process is in the middle of saving refuses a read for a moment, and a file that
    /// cannot be read is not a change. One that stays unreadable cannot be kept, so it is not
    /// replaced.
    /// </remarks>
    private static (bool Exists, byte[]? Digest) ObserveReadable(string vaultPath, Action<int>? waitBetweenAttempts)
    {
        var observed = Retrying(outcome => outcome.Result is (true, null), waitBetweenAttempts).Execute(() => Observe(vaultPath));

        return observed is (true, null)
            ? throw new VaultRestoreException($"'{vaultPath}' could not be read, so it was not replaced.")
            : observed;
    }

    /// <summary>A save's retries: <see cref="KeePassInterop.SaveAttempts"/> attempts, sleeping as a save does unless a test waits instead.</summary>
    private static ResiliencePipeline Retrying(Func<Outcome<object>, bool> again, Action<int>? waitBetweenAttempts) =>
        Retries.OnThisThread(KeePassInterop.SaveAttempts, again, waitBetweenAttempts ?? KeePassInterop.SleepAfter);

    /// <summary>The listed backup already holding exactly these bytes, if one does.</summary>
    private static VaultBackup? HeldBy(string vaultPath, byte[] liveDigest)
    {
        foreach (var backup in List(vaultPath))
        {
            if (SourceSnapshot.Digest(backup.Path) is { } digest
                && CryptographicOperations.FixedTimeEquals(digest, liveDigest))
            {
                return backup;
            }
        }

        return null;
    }

    /// <summary>Renames the staged file onto the vault, which must still be what was observed, and says whether the rename is on disk.</summary>
    /// <remarks>
    /// The vault is looked at again before every attempt, the way a save does: a wait is where
    /// another writer lands, and the bytes replaced must be the bytes kept. A vault that appears where
    /// there was none is a change too.
    /// </remarks>
    private static bool MoveIntoPlace(
        string staged, string vaultPath, (bool Exists, byte[]? Digest) observed, Action<int>? waitBetweenAttempts)
    {
        Exception? refusal = null;
        var flushed = false;

        if (!Retrying(outcome => outcome is { Exception: null, Result: false } && (refusal is null || MayClear(refusal)), waitBetweenAttempts)
            .Execute(() => TryMove(staged, vaultPath, observed, out refusal, out flushed)))
        {
            throw new VaultRestoreException(
                $"Could not replace '{vaultPath}': {refusal?.Message ?? "it could not be read"}", refusal!);
        }

        return flushed;
    }

    /// <summary>One rename; false with the reason it was refused, or with none while the vault cannot be read.</summary>
    private static bool TryMove(
        string staged, string vaultPath, (bool Exists, byte[]? Digest) observed, out Exception? refusal, out bool flushed)
    {
        refusal = null;
        flushed = false;
        var now = Observe(vaultPath);

        if (now is (true, null))
        {
            return false;
        }

        if (now.Exists != observed.Exists
            || (now.Digest is { } digest && !CryptographicOperations.FixedTimeEquals(digest, observed.Digest!)))
        {
            throw new VaultChangedOnDiskException(
                "The vault file changed while the backup was being restored, so it was not replaced.");
        }

        try
        {
            flushed = VaultFiles.Move(staged, vaultPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            refusal = ex;
            return false;
        }
    }

    /// <summary>Whether waiting could change the answer: a held name can come free, a missing one cannot.</summary>
    private static bool MayClear(Exception ex) => ex
        is UnauthorizedAccessException
        or (IOException and not FileNotFoundException and not DirectoryNotFoundException and not PathTooLongException);

    /// <summary>Removes staged files a killed restore left beside the vault.</summary>
    /// <remarks>
    /// Only from <see cref="Restore(VaultClaim, VaultBackupSummary)"/>, with the gate held. A save sweeping these would delete one
    /// between its close and its rename.
    /// </remarks>
    private static void SweepStaged(string vaultPath)
    {
        string[] stale;
        try
        {
            stale = Directory.GetFiles(
                Path.GetDirectoryName(vaultPath)!, $".{Path.GetFileName(vaultPath)}.keypaste-restore-*.tmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var path in stale)
        {
            VaultFiles.Discard(path);
        }
    }

    /// <summary>What is at the vault's path now, or null when nothing readable is.</summary>
    private static VaultFileFacts? Facts(string vaultPath)
    {
        try
        {
            var file = new FileInfo(vaultPath);
            return file.Exists ? new VaultFileFacts(vaultPath, file.Length, file.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>An unused name for a backup taken at <paramref name="now"/>.</summary>
    /// <remarks>
    /// The source's own extension, never a restated <c>.kdbx</c>: the bytes were copied rather than
    /// written, so nothing here knows the format well enough to name it, and <see cref="List"/> matches a
    /// copy to its vault by that extension. A second within the same second gets a counter — one backup per
    /// unlock makes that nearly unreachable, but a name collision must not be an exception.
    /// </remarks>
    private static string Name(string directory, string vaultPath, DateTimeOffset now)
    {
        var stem = Path.GetFileNameWithoutExtension(vaultPath);
        var extension = Path.GetExtension(vaultPath);
        var stamp = now.ToUniversalTime().ToString(_stampFormat, CultureInfo.InvariantCulture);

        var candidate = Path.Combine(directory, $"{stem}.{stamp}{extension}");
        for (var counter = 2; File.Exists(candidate); counter++)
        {
            candidate = Path.Combine(directory, Invariant($"{stem}.{stamp}-{counter}{extension}"));
        }

        return candidate;
    }

    /// <summary>
    /// Reads the stamp and the same-second counter back out of a backup's name, or null when the
    /// name is not one this type wrote.
    /// </summary>
    private static (DateTimeOffset TakenAt, int Counter)? Read(string name, string prefix, string extension)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(extension, StringComparison.Ordinal)
            || name.Length <= prefix.Length + extension.Length)
        {
            return null;
        }

        var middle = name[prefix.Length..^extension.Length];
        var counter = 1;

        if (middle.IndexOf('-', StringComparison.Ordinal) is var dash and >= 0)
        {
            if (!int.TryParse(
                middle[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out counter))
            {
                return null;
            }

            middle = middle[..dash];
        }

        return DateTimeOffset.TryParseExact(
            middle, _stampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var takenAt)
            ? (takenAt, counter)
            : null;
    }

    /// <summary>Creates the backup directory if it is absent, and says whether it did.</summary>
    private static bool Ensure(string directory)
    {
        if (Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            VaultFiles.CreateDirectory(directory, ownerOnly: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VaultBackupException(
                $"Could not create the backup directory '{directory}': {ex.Message}", ex);
        }
    }

    /// <summary>Drops every backup <see cref="Keeps"/> does not keep.</summary>
    /// <remarks>
    /// Runs only after a new backup is complete and named. Dropping one is deleting a directory
    /// entry and nothing more: the bytes stay on the storage until it reuses them, which SECURITY.md
    /// says of every deletion keypaste performs. A failure to prune is not a failure to back up, so
    /// it is swallowed — the copy the caller asked for is already there.
    /// </remarks>
    private static void Prune(string vaultPath, DateTimeOffset now)
    {
        var backups = List(vaultPath);
        var kept = Keeps(backups, now);

        foreach (var backup in backups.Where(backup => !kept.Contains(backup)))
        {
            try
            {
                VaultFiles.Delete(backup.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The <see cref="Retained"/> newest backups, the newest of each of the last <see cref="DailyFor"/>
    /// UTC days and the newest of each of the last <see cref="WeeklyFor"/> weeks, Monday to Sunday.
    /// </summary>
    /// <param name="newestFirst">The backups, as <see cref="List"/> orders them.</param>
    /// <param name="now">The current time.</param>
    /// <remarks>Calendar days and weeks, so a day's or a week's newest backup stays its newest once it ends and is never pruned before it ages out.</remarks>
    internal static IReadOnlySet<VaultBackup> Keeps(IReadOnlyList<VaultBackup> newestFirst, DateTimeOffset now)
    {
        var today = DayOf(now);
        HashSet<VaultBackup> kept = [.. newestFirst.Take(Retained)];
        HashSet<int> days = [];
        HashSet<int> weeks = [];

        foreach (var backup in newestFirst)
        {
            var day = DayOf(backup.TakenAt);

            if (day > today)
            {
                continue;
            }

            if (today - day < DailyFor && days.Add(day))
            {
                kept.Add(backup);
            }

            if ((today / 7) - (day / 7) < WeeklyFor && weeks.Add(day / 7))
            {
                kept.Add(backup);
            }
        }

        return kept;
    }

    /// <summary>The UTC day, counted from 1 January of year 1, a Monday.</summary>
    private static int DayOf(DateTimeOffset time) => DateOnly.FromDateTime(time.UtcDateTime).DayNumber;

    /// <summary>Removes temporaries a killed process left in the backup directory.</summary>
    /// <remarks>
    /// Opened at one exact path with <see cref="FileShare.None"/> and
    /// <see cref="FileOptions.DeleteOnClose"/> rather than deleted by name, which is the save's
    /// discipline: a temporary another process is still writing refuses us, and a refusal is where
    /// this stops. Best effort throughout — failing to tidy up must never fail a backup.
    /// </remarks>
    private static void Sweep(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        string[] stale;
        try
        {
            stale = Directory.GetFiles(directory, ".*.keypaste-*.tmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var path in stale)
        {
            VaultFiles.Discard(path);
        }
    }
}

/// <summary>What one save did about the bytes it replaced.</summary>
/// <param name="Directory">Where the backups of this vault are kept.</param>
/// <param name="Outcome">Whether a copy was written or the floor suppressed it.</param>
/// <param name="CreatedDirectory">Whether this save is what created <paramref name="Directory"/>.</param>
/// <param name="Retained">How many of the most recent backups are kept, besides one a day for a week and one a week for four weeks.</param>
/// <remarks>
/// Carries no entry name, count or content fingerprint, which is the standard THREATS.md T-24 already
/// holds <c>recent.toml</c> to, and no part of an unlock secret.
/// </remarks>
public sealed record VaultBackupReport(
    string Directory, VaultBackupOutcome Outcome, bool CreatedDirectory, int Retained);

/// <summary>What is at a vault's path, without opening it.</summary>
/// <param name="Path">The file.</param>
/// <param name="Length">Its size in bytes.</param>
/// <param name="ModifiedAt">When it was last written, in UTC.</param>
public sealed record VaultFileFacts(string Path, long Length, DateTimeOffset ModifiedAt);

/// <summary>What <see cref="VaultBackups.Inspect"/> found in a backup, and the only thing a restore accepts.</summary>
/// <remarks>
/// Only <see cref="VaultBackups.Inspect"/> can make one, and the digest it carries is not public, so
/// a restore cannot be aimed at bytes nobody opened or at another vault. A class rather than a record
/// so the digest stays out of <c>ToString</c>, which T-24 holds <see cref="VaultBackupReport"/> to as
/// well. The counts are of live items: the recycle bin is left out, as it is from every read.
/// </remarks>
public sealed class VaultBackupSummary
{
    internal VaultBackupSummary(
        string vaultPath, VaultBackup backup, int entries, int groups, int envProjects,
        VaultFileFacts? replaces, byte[] digest)
    {
        VaultPath = vaultPath;
        Backup = backup;
        Entries = entries;
        Groups = groups;
        EnvProjects = envProjects;
        Replaces = replaces;
        Digest = digest;
    }

    /// <summary>The vault a restore would replace.</summary>
    public string VaultPath { get; }

    /// <summary>The backup that was opened.</summary>
    public VaultBackup Backup { get; }

    /// <summary>How many entries it holds.</summary>
    public int Entries { get; }

    /// <summary>How many groups it holds.</summary>
    public int Groups { get; }

    /// <summary>How many projects its entries' tags make (<see cref="ProjectCatalog"/>).</summary>
    public int EnvProjects { get; }

    /// <summary>The file a restore would replace, or null when none is there.</summary>
    public VaultFileFacts? Replaces { get; }

    internal byte[] Digest { get; }

    /// <summary>The backup's save count, which a restore has this home remember so the unlock after it warns of no older copy.</summary>
    internal long? SaveCount { get; init; }
}

/// <summary>What a restore did.</summary>
/// <param name="Restored">The backup now in the vault's place.</param>
/// <param name="Preserved">The copy made of the file that was replaced, when one was made.</param>
/// <param name="AlreadyKeptAs">
/// The backup that already held the replaced file's exact bytes, which is why no copy was made. Both
/// are null when there was no file to replace.
/// </param>
public sealed record VaultRestoreReport(VaultBackup Restored, VaultBackup? Preserved, VaultBackup? AlreadyKeptAs)
{
    /// <summary>Whether the system would not flush the vault's folder after the restore's rename (<see cref="Vault.UnflushedText"/>).</summary>
    public bool Unflushed { get; init; }
}
