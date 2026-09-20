using System.Globalization;
using System.Runtime.Versioning;
using Keypaste.Core.Internal;

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
/// file being truncated, overwritten by something else or deleted. docs/PRODUCT.md law 5.7 requires
/// the four recovery paths to be proved apart, and this is the third of them: a damaged vault file.
/// It is not the fourth — a backup opens with the credentials it was made under, and nothing here
/// recovers a lost unlock secret.
/// </para>
/// <para>
/// <b>A backup is the existing file's bytes, copied.</b> Nothing is decrypted, re-encrypted or
/// re-serialised: law 3.6 forbids touching the format, and a copy cannot get it wrong. It also means
/// a backup carries whatever reader floor its source carried — a vault that has recycled anything is
/// KDBX 4.1 (D-0247), and so is its backup.
/// </para>
/// <para>
/// <b>The vocabulary collides and the two meanings must not.</b> KeePassLib calls an entry's history
/// revisions "backups" — <c>CreateBackup</c>, <c>RestoreFromBackup</c>, <c>MaintainBackups</c>, which
/// has an evict-the-oldest rule of its own. That sense is confined to <c>KeePassInterop</c> and stays
/// there. Here "backup" means the whole file, which is what law 5.7 means by "entry history is not a
/// whole-vault backup".
/// </para>
/// <para>
/// This lives in the core rather than in either front end for D-0222's reason: two implementations
/// of a rule that replaces a vault file are two chances to destroy one.
/// </para>
/// </remarks>
public static class VaultBackups
{
    /// <summary>How many backups of one vault are kept.</summary>
    public const int Retained = 5;

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

    /// <summary>Where the backups of the vault at <paramref name="vaultPath"/> are kept.</summary>
    /// <param name="vaultPath">The vault itself, not its directory.</param>
    /// <remarks>
    /// Keyed on the vault's <b>whole file name</b>, extension included. Keying on the stem would give
    /// <c>personal.kdbx</c> and <c>personal.kdb</c> in one folder a single directory and identical
    /// backup names inside it, so retention would prune across both vaults.
    /// </remarks>
    public static string DirectoryFor(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(vaultPath))
            ?? throw new ArgumentException($"'{vaultPath}' has no directory to sit in.", nameof(vaultPath));

        return Path.Combine(directory, Path.GetFileName(vaultPath) + DirectorySuffix);
    }

    /// <summary>The backups of the vault at <paramref name="vaultPath"/>, newest first.</summary>
    /// <remarks>
    /// Only files this type named are listed. Anything else in the directory — a copy somebody made
    /// themselves, a note, a half-written temporary — is left alone and never counted against
    /// <see cref="Retained"/>, because pruning a file we did not write is not ours to do.
    /// </remarks>
    public static IReadOnlyList<VaultBackup> List(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var directory = DirectoryFor(vaultPath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var prefix = Path.GetFileNameWithoutExtension(vaultPath) + ".";
        var extension = Path.GetExtension(vaultPath);

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

    /// <summary>
    /// Copies the vault at <paramref name="vaultPath"/> beside itself, then drops the oldest copies
    /// beyond <see cref="Retained"/>.
    /// </summary>
    /// <param name="vaultPath">An existing vault, about to be replaced.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <param name="applyFloor">
    /// Whether <see cref="Floor"/> may suppress this backup. False on the overwriting save path: see
    /// <see cref="Vault.SaveOverwriting"/>, which discards somebody else's write, and the vault it
    /// replaces is exactly the copy somebody needs when that choice turns out to be the wrong one.
    /// </param>
    /// <param name="createdDirectory">Whether this call is what created the backup directory.</param>
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
        string vaultPath, DateTimeOffset now, bool applyFloor, out bool createdDirectory)
    {
        createdDirectory = false;

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

        Keep(vaultPath, now, out createdDirectory);

        Prune(vaultPath);

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
        var directory = DirectoryFor(vaultPath);

        createdDirectory = Ensure(directory);

        var reserved = Path.Combine(
            directory,
            $".{Path.GetFileName(vaultPath)}.keypaste-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");

        try
        {
            File.Copy(vaultPath, reserved, overwrite: false);
            RestrictToOwner(reserved);

            var kept = Name(directory, vaultPath, now);
            File.Move(reserved, kept);

            var utc = now.ToUniversalTime();
            return new VaultBackup(kept, utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Discard(reserved);

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
    /// <returns>What the backup holds, bound to the bytes that were opened.</returns>
    /// <exception cref="VaultRestoreException">
    /// <see cref="List"/> does not name the file, it has no KDBX header, or it changed while it was
    /// being opened. The password was not used in the first two cases.
    /// </exception>
    /// <exception cref="InvalidMasterPasswordException">
    /// The password does not open the backup, or its body is damaged. One answer on purpose: nothing
    /// finer can be said of a file that did not decrypt.
    /// </exception>
    public static VaultBackupSummary Inspect(string vaultPath, VaultBackup backup, ReadOnlySpan<char> password)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);
        ArgumentNullException.ThrowIfNull(backup);

        vaultPath = Path.GetFullPath(vaultPath);
        var listed = Listed(vaultPath, backup.Path);

        var digest = SourceSnapshot.Digest(listed.Path)
            ?? throw new VaultRestoreException($"The backup '{listed.Path}' could not be read.");

        int entries, groups, projects;
        try
        {
            using var vault = Vault.Open(listed.Path, password);
            entries = vault.ReadEntries().Count;
            groups = vault.ReadGroupPaths().Count;
            projects = new EnvStore(vault).Projects().Count;
        }
        catch (VaultException ex)
        {
            throw new InvalidMasterPasswordException(
                "That password does not open this backup, or the backup is damaged.", ex);
        }

        if (SourceSnapshot.Digest(listed.Path) is not { } after
            || !CryptographicOperations.FixedTimeEquals(digest, after))
        {
            throw new VaultRestoreException($"The backup '{listed.Path}' changed while it was being checked.");
        }

        return new VaultBackupSummary(vaultPath, listed, entries, groups, projects, Facts(vaultPath), digest);
    }

    /// <summary>Puts a validated backup in the vault's place, byte for byte.</summary>
    /// <param name="validated">What <see cref="Inspect"/> returned for the backup.</param>
    /// <returns>Which copy was restored, and what became of the file it replaced.</returns>
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
    /// Staged beside the vault rather than in the backup directory. A rename within one directory is
    /// on one volume by construction; a backup directory that is a junction to another volume would
    /// turn the rename into a copy over the live vault, which a kill could leave half written.
    /// </para>
    /// </remarks>
    public static VaultRestoreReport Restore(VaultBackupSummary validated) => Restore(validated, null, null);

    /// <param name="validated">What <see cref="Inspect"/> returned for the backup.</param>
    /// <param name="beforeReplacing">
    /// Called with the staged file's path after the vault has been kept and before it is replaced, so
    /// a test can act at the one instant between the two.
    /// </param>
    /// <param name="waitBetweenAttempts">Called instead of sleeping between rename attempts.</param>
    internal static VaultRestoreReport Restore(
        VaultBackupSummary validated, Action<string>? beforeReplacing, Action<int>? waitBetweenAttempts)
    {
        ArgumentNullException.ThrowIfNull(validated);

        return KeePassInterop.WhileNoSaveReplaces(() => Replace(validated, beforeReplacing, waitBetweenAttempts));
    }

    private static VaultRestoreReport Replace(
        VaultBackupSummary validated, Action<string>? beforeReplacing, Action<int>? waitBetweenAttempts)
    {
        var vaultPath = validated.VaultPath;

        SweepStaged(vaultPath);

        var backup = Listed(vaultPath, validated.Backup.Path);
        var bytes = ReadValidated(backup, validated.Digest);
        var staged = Stage(vaultPath, bytes);

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

            MoveIntoPlace(staged, vaultPath, live, waitBetweenAttempts);

            return new VaultRestoreReport(backup, preserved, alreadyKeptAs);
        }
        catch
        {
            Discard(staged);
            throw;
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
            : throw new VaultRestoreException($"The backup '{listed.Path}' is not a KDBX file.");
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

        try
        {
            using var stream = new FileStream(staged, options);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return staged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Discard(staged);

            throw new VaultRestoreException($"Could not stage the backup beside '{vaultPath}': {ex.Message}", ex);
        }
    }

    /// <summary>Whether a file is at the vault's path, and what it holds; no digest when it cannot be read.</summary>
    private static (bool Exists, byte[]? Digest) Observe(string vaultPath) =>
        File.Exists(vaultPath) ? (true, SourceSnapshot.Digest(vaultPath)) : (false, null);

    /// <summary>The vault as it is before anything is done to it, waiting out a file that cannot be read.</summary>
    /// <remarks>
    /// A vault another process is in the middle of saving refuses a read for a moment, and a file that
    /// cannot be read is not a change (D-0017). One that stays unreadable cannot be kept, so it is not
    /// replaced.
    /// </remarks>
    private static (bool Exists, byte[]? Digest) ObserveReadable(string vaultPath, Action<int>? waitBetweenAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            var observed = Observe(vaultPath);
            if (!observed.Exists || observed.Digest is not null)
            {
                return observed;
            }

            if (attempt >= KeePassInterop.SaveAttempts)
            {
                throw new VaultRestoreException($"'{vaultPath}' could not be read, so it was not replaced.");
            }

            Wait(attempt, waitBetweenAttempts);
        }
    }

    private static void Wait(int attempt, Action<int>? waitBetweenAttempts)
    {
        if (waitBetweenAttempts is not null)
        {
            waitBetweenAttempts(attempt);
        }
        else
        {
            Thread.Sleep(KeePassInterop.SaveRetryDelayMilliseconds * attempt);
        }
    }

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

    /// <summary>Renames the staged file onto the vault, which must still be what was observed.</summary>
    /// <remarks>
    /// The vault is looked at again before every attempt, the way D-0119 has a save do: a wait is where
    /// another writer lands, and the bytes replaced must be the bytes kept. A vault that appears where
    /// there was none is a change too.
    /// </remarks>
    private static void MoveIntoPlace(
        string staged, string vaultPath, (bool Exists, byte[]? Digest) observed, Action<int>? waitBetweenAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = Observe(vaultPath);
            var unreadable = now.Exists && now.Digest is null;

            if (!unreadable && (now.Exists != observed.Exists
                || (now.Digest is { } digest && !CryptographicOperations.FixedTimeEquals(digest, observed.Digest!))))
            {
                throw new VaultChangedOnDiskException(
                    "The vault file changed while the backup was being restored, so it was not replaced.");
            }

            Exception? refusal = null;

            if (!unreadable)
            {
                try
                {
                    File.Move(staged, vaultPath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    refusal = ex;
                }
            }

            if (attempt >= KeePassInterop.SaveAttempts || (refusal is not null && !MayClear(refusal)))
            {
                throw new VaultRestoreException(
                    $"Could not replace '{vaultPath}': {refusal?.Message ?? "it could not be read"}", refusal!);
            }

            Wait(attempt, waitBetweenAttempts);
        }
    }

    /// <summary>Whether waiting could change the answer: a held name can come free, a missing one cannot.</summary>
    private static bool MayClear(Exception ex) => ex
        is UnauthorizedAccessException
        or (IOException and not FileNotFoundException and not DirectoryNotFoundException and not PathTooLongException);

    /// <summary>Removes staged files a killed restore left beside the vault.</summary>
    /// <remarks>
    /// Only from <see cref="Restore(VaultBackupSummary)"/>, with the gate held. A save sweeping these would delete one
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
            Discard(path);
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
            candidate = Path.Combine(directory, $"{stem}.{stamp}-{counter}{extension}");
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

        if (middle.IndexOf('-') is var dash and >= 0)
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
            Directory.CreateDirectory(directory);
            RestrictDirectoryToOwner(directory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new VaultBackupException(
                $"Could not create the backup directory '{directory}': {ex.Message}", ex);
        }
    }

    /// <summary>Drops the oldest backups beyond <see cref="Retained"/>.</summary>
    /// <remarks>
    /// Runs only after a new backup is complete and named. Dropping one is deleting a directory
    /// entry and nothing more: the bytes stay on the storage until it reuses them, which SECURITY.md
    /// says of every deletion keypaste performs. A failure to prune is not a failure to back up, so
    /// it is swallowed — the copy the caller asked for is already there.
    /// </remarks>
    private static void Prune(string vaultPath)
    {
        var backups = List(vaultPath);

        for (var index = Retained; index < backups.Count; index++)
        {
            try
            {
                File.Delete(backups[index].Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Removes temporaries a killed process left in the backup directory.</summary>
    /// <remarks>
    /// Opened at one exact path with <see cref="FileShare.None"/> and
    /// <see cref="FileOptions.DeleteOnClose"/> rather than deleted by name, which is D-0120's
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
            Discard(path);
        }
    }

    /// <summary>Removes one temporary, if nobody else holds it.</summary>
    private static void Discard(string path)
    {
        try
        {
            using var _ = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Makes a backup readable by its owner alone, where the platform says so.</summary>
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Restrict(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Makes the backup directory reachable by its owner alone, where the platform says so.</summary>
    private static void RestrictDirectoryToOwner(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Restrict(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [UnsupportedOSPlatform("windows")]
    private static void Restrict(string path, UnixFileMode mode)
    {
        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy exists and is encrypted; a mode this filesystem will not hold does not make
            // it worthless. Windows inherits the profile's permissions, as recent.toml does (T-24).
        }
    }
}

/// <summary>What one save did about the bytes it replaced.</summary>
/// <param name="Directory">Where the backups of this vault are kept.</param>
/// <param name="Outcome">Whether a copy was written or the floor suppressed it.</param>
/// <param name="CreatedDirectory">Whether this save is what created <paramref name="Directory"/>.</param>
/// <param name="Retained">How many backups are kept.</param>
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
/// well. The counts are of live items: the recycle bin is left out, as it is from every read (D-0248).
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

    /// <summary>How many groups it holds, the <c>env</c> group and its projects among them.</summary>
    public int Groups { get; }

    /// <summary>How many env projects it holds.</summary>
    public int EnvProjects { get; }

    /// <summary>The file a restore would replace, or null when none is there.</summary>
    public VaultFileFacts? Replaces { get; }

    internal byte[] Digest { get; }
}

/// <summary>What a restore did.</summary>
/// <param name="Restored">The backup now in the vault's place.</param>
/// <param name="Preserved">The copy made of the file that was replaced, when one was made.</param>
/// <param name="AlreadyKeptAs">
/// The backup that already held the replaced file's exact bytes, which is why no copy was made. Both
/// are null when there was no file to replace.
/// </param>
public sealed record VaultRestoreReport(VaultBackup Restored, VaultBackup? Preserved, VaultBackup? AlreadyKeptAs);
