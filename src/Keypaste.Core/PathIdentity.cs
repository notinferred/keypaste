namespace Keypaste.Core;

/// <summary>Whether two paths name the same file on this machine.</summary>
/// <remarks>
/// <para>
/// One rule, in the core, because more than one surface asks the question and the answers have to
/// agree: <c>env export</c> refuses to write plaintext over the vault it is reading from, the
/// desktop decides whether the vault you just picked is the one already remembered, and the recent
/// list decides whether it has seen a file before. D-0091 is what three private answers to one
/// question cost the last time.
/// </para>
/// <para>
/// This is a best-effort answer and says so out loud. It resolves what the managed API can resolve
/// — normalisation, symbolic links and junctions, including one in an ancestor directory — and
/// nothing else. It cannot see through a hard link, a bind mount, a <c>subst</c> drive, a volume
/// mounted twice, a UNC path against a mapped drive letter, a Windows 8.3 short name, or the
/// Unicode normalisation and non-ASCII case folding a filesystem does for itself. Reaching those
/// means asking the operating system for a file's identity, which is a P/Invoke on a path
/// docs/PRODUCT.md law 3.9 keeps free of them. A caller that must fail closed needs a second check
/// of its own; <see cref="KdbxHeader.IsVaultFile"/> is the one <c>env export</c> uses.
/// </para>
/// </remarks>
public static class PathIdentity
{
    /// <summary>
    /// Total resolution steps allowed for one path, which bounds link chains, link loops and depth
    /// together.
    /// </summary>
    private const int _budget = 256;

    /// <summary>How this platform compares two paths that are already in canonical form.</summary>
    /// <remarks>
    /// Case-insensitive on Windows and on macOS, where the default file systems are; case-sensitive
    /// on Linux, where it is not. A volume can always be mounted the other way — that is a stated
    /// residual, not something this can ask about without a syscall per comparison.
    /// </remarks>
    public static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The most resolved absolute form of a path this can reach without opening it.</summary>
    /// <param name="path">The path to canonicalise. Relative paths are resolved against the
    /// current directory, as <see cref="Path.GetFullPath(string)"/> does.</param>
    /// <returns>The canonical form, or the best form reached.</returns>
    /// <remarks>
    /// <para>
    /// <b>It never throws for a path it cannot inspect.</b> A destination on an unreadable share
    /// answers with the form reached so far, because refusing every export whose destination cannot
    /// be stat-ed would break ordinary use for a guard that has nothing to say about it.
    /// </para>
    /// <para>
    /// The destination of an export usually does not exist yet, and
    /// <see cref="File.ResolveLinkTarget(string, bool)"/> throws for a path that is not there and
    /// returns <see langword="null"/> for one that is not a link — so resolving the last component
    /// alone answers nothing. This resolves the deepest part of the path that does exist and then
    /// re-attaches the rest, which is what makes <c>link/.env</c>, where <c>link</c> is a symbolic
    /// link to the directory holding the vault, resolve to the same directory the vault is in.
    /// </para>
    /// </remarks>
    public static string Canonical(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return path;
        }

        var tail = new List<string>();
        var deepest = full;
        while (!Directory.Exists(deepest) && !File.Exists(deepest))
        {
            var parent = Path.GetDirectoryName(deepest);
            if (string.IsNullOrEmpty(parent) || tail.Count >= _budget)
            {
                return full;
            }

            tail.Add(Path.GetFileName(deepest));
            deepest = parent;
        }

        var budget = _budget;
        var resolved = Resolve(deepest, ref budget);
        for (var i = tail.Count - 1; i >= 0; i--)
        {
            resolved = Path.Combine(resolved, tail[i]);
        }

        return resolved;
    }

    /// <summary>Whether two paths name the same file, after links and this platform's case rules.</summary>
    /// <param name="left">One path.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when both canonicalise to one path.</returns>
    /// <remarks>
    /// <see langword="false"/> means "these did not turn out to be the same file", not "these are
    /// certainly different files" — the residuals in the type's own remarks are the difference.
    /// </remarks>
    public static bool SameFile(string left, string right)
    {
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);

        return string.Equals(Canonical(left), Canonical(right), Comparison);
    }

    /// <summary>Resolves one existing path, its ancestors first.</summary>
    /// <remarks>
    /// The parent is resolved before the component under it, which is the only way an ancestor that
    /// is itself a link gets followed: <c>returnFinalTarget</c> walks the chain of the last
    /// component and leaves everything above it alone. Driving the chain here rather than asking
    /// for the final target also keeps one algorithm across the three platforms, instead of two
    /// that agree until a case nobody tested.
    /// </remarks>
    private static string Resolve(string path, ref int budget)
    {
        if (budget-- <= 0)
        {
            // A loop, or a depth nothing legitimate reaches. Stop where we are: the caller gets a
            // path that is not the vault, which is the safe half of a wrong answer here.
            return path;
        }

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            return path;
        }

        var here = Path.Combine(Resolve(parent, ref budget), Path.GetFileName(path));
        var target = OneHop(here);
        return target is null ? here : Resolve(target, ref budget);
    }

    /// <summary>One link hop, or null when this is not a link and when it cannot be asked.</summary>
    private static string? OneHop(string path)
    {
        try
        {
            var target = Directory.Exists(path)
                ? Directory.ResolveLinkTarget(path, returnFinalTarget: false)
                : File.ResolveLinkTarget(path, returnFinalTarget: false);

            // Already absolute: the framework joins a relative target against the link's own
            // directory, which is the arithmetic easiest to get wrong by hand.
            return target?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
