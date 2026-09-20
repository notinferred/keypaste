using System.Globalization;
using System.Runtime.Versioning;

namespace Keypaste.Core;

/// <summary>One copy of a vault, taken before a save replaced it.</summary>
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
    /// <see cref="Vault.SaveOverwriting"/>, which is a restore replacing a live vault, and the vault
    /// it replaces is exactly the copy somebody needs when the restore turns out to be the wrong one.
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

        createdDirectory = Ensure(directory);

        var reserved = Path.Combine(
            directory,
            $".{Path.GetFileName(vaultPath)}.keypaste-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");

        try
        {
            File.Copy(vaultPath, reserved, overwrite: false);
            RestrictToOwner(reserved);
            File.Move(reserved, Name(directory, vaultPath, now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Discard(reserved);

            throw new VaultBackupException(
                $"Could not back '{vaultPath}' up to '{directory}': {ex.Message}", ex);
        }

        Prune(vaultPath);

        return VaultBackupOutcome.Written;
    }

    /// <summary>An unused name for a backup taken at <paramref name="now"/>.</summary>
    /// <remarks>
    /// The source's own extension, never a restated <c>.kdbx</c>: the bytes were copied rather than
    /// written, so nothing here knows the format well enough to name it, and V.4b's restore reads the
    /// extension back off the name. A second within the same second gets a counter — one backup per
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
