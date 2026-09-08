namespace Keypaste.Core;

/// <summary>What became of a file a command offered to clean up after itself.</summary>
public enum SourceCleanup
{
    /// <summary>It was still the file that was read, and it is gone.</summary>
    Deleted,

    /// <summary>It holds something else now, or the path reaches somewhere else. Kept.</summary>
    Changed,

    /// <summary>It was not there to delete. Nothing was done.</summary>
    Missing,

    /// <summary>It could not be inspected, so it could not be established as unchanged. Kept.</summary>
    Unreadable,

    /// <summary>
    /// It is intact, but under a temporary name beside where it was: it could not be removed, or
    /// the original path was taken while it was held there.
    /// </summary>
    Stranded,
}

/// <summary>
/// The bytes a command read, and whether the file it read them from still holds them.
/// </summary>
/// <remarks>
/// <para>
/// <c>keypaste env pull</c> reads a <c>.env</c>, imports it, and then offers to delete it. Between
/// those two things sit a master-password prompt, a key derivation, an import confirmation and a
/// deletion confirmation — four windows in which a person can add a variable in another window, an
/// editor can save, or a <c>git checkout</c> can replace the file. Deleting whatever is at that
/// path afterwards loses bytes that were never imported, and there is nowhere to recover them
/// from: D-0015 already said this command's offer is a trap unless it is bound to what was read,
/// and it was bound only to the path (docs/STEPS.md F.1c).
/// </para>
/// <para>
/// Identity here is two rules, not one. <see cref="PathIdentity"/> answers whether the path still
/// reaches the same file — through symbolic links and junctions, including one in a directory above
/// it — and the digest answers whether that file still holds the same bytes. Neither is enough
/// alone: a file swapped for a different one of the same content passes the digest, and a file
/// rewritten in place passes the path rule. <c>PathIdentity</c>'s own remarks say a caller that
/// must fail closed needs a second check; this is <c>env pull</c>'s, in the role
/// <see cref="KdbxHeader.IsVaultFile"/> plays for <c>env export</c>.
/// </para>
/// </remarks>
public sealed class SourceSnapshot
{
    private readonly byte[] _digest;

    private SourceSnapshot(string path, byte[] digest)
    {
        Path = path;
        _digest = digest;
    }

    /// <summary>The file the bytes were read from, canonicalised when they were read.</summary>
    public string Path { get; }

    /// <summary>Records a file by the bytes a caller has already read from it.</summary>
    /// <param name="path">The path they were read from.</param>
    /// <param name="bytes">Exactly what was read, before any decoding.</param>
    /// <returns>The snapshot to check against later.</returns>
    /// <remarks>
    /// It takes the bytes rather than reading them again: a second read is a second chance to
    /// disagree with the one the caller actually acted on, which is the whole defect in miniature.
    /// </remarks>
    public static SourceSnapshot Take(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new SourceSnapshot(PathIdentity.Canonical(path), SHA256.HashData(bytes));
    }

    /// <summary>
    /// A digest of the file, or <see langword="null"/> when it could not be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole file rather than its modification time. Several filesystems round mtime to a
    /// second or two, so a rewrite inside the same second would be missed — and a rewrite inside
    /// the same second is the common case here, not the exotic one. A vault is kilobytes and a
    /// <c>.env</c> is capped at <see cref="DotEnv.MaximumBytes"/>; hashing either is not worth
    /// optimising around.
    /// </para>
    /// <para>
    /// <b>A file that cannot be read is not a change.</b> "This holds something else now" is a
    /// claim about a file that exists and can be inspected. A file that is missing, locked, or on a
    /// directory that momentarily went away is a different answer, and callers separate the two:
    /// <see cref="Vault.HasFileChangedSinceOpen"/> absorbs it as a transient write problem (D-0017)
    /// and <see cref="DeleteIfUnchanged"/> keeps the file.
    /// </para>
    /// </remarks>
    public static byte[]? Digest(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            return SHA256.HashData(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="path"/> still reaches the file this was taken from.</summary>
    /// <param name="path">The path to check, in any spelling.</param>
    /// <returns><see langword="true"/> when it is the same file holding the same bytes.</returns>
    public bool Matches(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return StillReaches(path)
            && Digest(path) is { } digest
            && CryptographicOperations.FixedTimeEquals(digest, _digest);
    }

    /// <summary>Whether the path resolves today to the file it resolved to when read.</summary>
    /// <remarks>
    /// Deliberately not <see cref="PathIdentity.SameFile"/>. That canonicalises both sides against
    /// the filesystem as it is now, so handing it the recorded path and the live one asks whether a
    /// path is itself and always says yes — a <c>.env</c> replaced by a symbolic link to somewhere
    /// else passes it, because both spellings then resolve to that somewhere else. The recorded
    /// form was frozen when the bytes were read and is compared, not re-resolved.
    /// </remarks>
    private bool StillReaches(string path) =>
        string.Equals(Path, PathIdentity.Canonical(path), PathIdentity.Comparison);

    /// <summary>Deletes the file, and only if it is still the one that was read.</summary>
    /// <param name="path">The path to clean up.</param>
    /// <param name="detail">
    /// Where an intact file was left when the result is <see cref="SourceCleanup.Stranded"/>, what
    /// the operating system said when it is <see cref="SourceCleanup.Unreadable"/>, and
    /// <see langword="null"/> otherwise.
    /// </param>
    /// <returns>What was done, and why when it was not a deletion.</returns>
    /// <remarks>
    /// <para>
    /// <b>It does not check and then delete.</b> Checking first leaves a window between the answer
    /// and the removal, and that window is not theoretical — it is where a save lands while the
    /// person is still lifting their finger off the <c>y</c>. So the file is renamed to a name
    /// beside itself first. A rename within one directory is atomic, and afterwards nothing else
    /// names that file, so the digest is taken and the deletion performed on something no other
    /// writer can reach. There is no seam here for a test to widen and none for a race to fit
    /// through, and <c>File.Delete</c> is still the real thing, which is what D-0015 asked for.
    /// </para>
    /// <para>
    /// If the bytes turn out to be someone else's, the file goes back. It never goes back
    /// <em>over</em> anything: a path that has been recreated in the meantime holds a file this
    /// method has no claim on, so the rename is attempted without overwriting and a refusal leaves
    /// the original beside it under the temporary name, reported rather than quietly dropped.
    /// Losing either file to tidy up after ourselves is the trade this whole type exists to refuse.
    /// </para>
    /// </remarks>
    public SourceCleanup DeleteIfUnchanged(string path, out string? detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        detail = null;

        if (!StillReaches(path))
        {
            return SourceCleanup.Changed;
        }

        if (!File.Exists(path))
        {
            return SourceCleanup.Missing;
        }

        if (Reserve(path) is not { } held)
        {
            detail = "no name could be reserved beside it";
            return SourceCleanup.Unreadable;
        }

        try
        {
            File.Move(path, held);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            detail = ex.Message;
            return File.Exists(path) ? SourceCleanup.Unreadable : SourceCleanup.Missing;
        }

        var digest = Digest(held);
        if (digest is null || !CryptographicOperations.FixedTimeEquals(digest, _digest))
        {
            return PutBack(held, path, out detail)
                ? digest is null ? SourceCleanup.Unreadable : SourceCleanup.Changed
                : SourceCleanup.Stranded;
        }

        try
        {
            File.Delete(held);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            detail = held;
            return SourceCleanup.Stranded;
        }

        return SourceCleanup.Deleted;
    }

    /// <summary>Returns the file to its own name, unless something is there now.</summary>
    private static bool PutBack(string held, string path, out string? detail)
    {
        detail = null;

        try
        {
            // Deliberately not overwriting: File.Move fails rather than replacing whatever
            // appeared at the original path, and that failure is the correct outcome.
            File.Move(held, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            detail = held;
            return false;
        }
    }

    /// <summary>An unused name beside the file, or null when it has no directory to sit in.</summary>
    private static string? Reserve(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var name = System.IO.Path.GetFileName(path);
        var tag = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        return System.IO.Path.Combine(directory, $".{name}.keypaste-{tag}.tmp");
    }
}
