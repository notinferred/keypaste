namespace Keypaste.Core;

/// <summary>Decides which vault file a command operates on.</summary>
/// <remarks>
/// <para>
/// There is deliberately <b>no default path</b>. A credential tool that silently picks a vault when
/// you forgot to say which one is a tool that eventually writes a secret into the wrong file, or
/// reports "not found" against a vault you have never seen. Being explicit costs one flag and
/// removes a whole class of confusion.
/// </para>
/// <para>
/// The rule lives in the core rather than in the CLI because "which vault are we talking about" is
/// a product rule, not a command-line one: the MCP bridge has to answer it identically, or Stage
/// 2.2 will hand an agent a credential out of one file while <c>keypaste ls</c> shows another, and
/// <c>keypaste log</c> will render history for a vault the user never opened. Two implementations
/// of one rule is what docs/PRODUCT.md law 4.3 forbids. The CLI keeps a thin adapter that binds its own
/// argument parser and environment seam to this.
/// </para>
/// </remarks>
public static class VaultLocation
{
    /// <summary>The environment variable consulted when no path was given explicitly.</summary>
    public const string EnvironmentVariable = "KEYPASTE_VAULT";

    /// <summary>The environment variable consulted when no keyfile was given explicitly.</summary>
    /// <remarks>
    /// It names a file, never a secret, but the file is the second factor — so THREATS records that
    /// this variable is readable by every process running as the same user and commonly outlives
    /// the session that set it, in a shell profile or a client configuration.
    /// </remarks>
    public const string KeyfileEnvironmentVariable = "KEYPASTE_KEYFILE";

    /// <summary>Resolves the vault path from an explicit value and the environment.</summary>
    /// <param name="fromFlag">The explicit path — <c>--vault</c> on either front end. May be null.</param>
    /// <param name="fromEnvironment">The value of <see cref="EnvironmentVariable"/>. May be null.</param>
    /// <param name="path">The absolute path, on success.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when no path is available.</returns>
    /// <remarks>
    /// An empty variable counts as unset: <c>KEYPASTE_VAULT= keypaste ls</c> should complain that no
    /// vault was given, not that <c>""</c> is missing.
    /// </remarks>
    public static bool TryResolve(
        string? fromFlag,
        string? fromEnvironment,
        out string path,
        out string error)
    {
        path = string.Empty;
        error = string.Empty;

        if (!string.IsNullOrEmpty(fromFlag))
        {
            path = Path.GetFullPath(fromFlag);
            return true;
        }

        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            path = Path.GetFullPath(fromEnvironment);
            return true;
        }

        error = $"no vault given. Use --vault <path> or set {EnvironmentVariable}.";
        return false;
    }

    /// <summary>Resolves the keyfile path, if one was given at all.</summary>
    /// <param name="fromFlag">The explicit path — <c>--keyfile</c>. May be null.</param>
    /// <param name="fromEnvironment">The value of <see cref="KeyfileEnvironmentVariable"/>. May be null.</param>
    /// <param name="path">The absolute path, or <see langword="null"/> when no keyfile was given.</param>
    /// <returns><see langword="true"/> when a keyfile was given.</returns>
    /// <remarks>
    /// <para>
    /// Unlike the vault, absent is a valid answer with a meaning of its own — most vaults have no
    /// keyfile — so this reports whether one was named rather than failing when none was. There is
    /// nothing to refuse here and so no error out-parameter: whether the file is usable is
    /// <see cref="VaultKeyfile.Inspect"/>'s question, and it needs the path first.
    /// </para>
    /// <para>
    /// Here beside the vault rule for the reason that rule gives: the answer has to be the same
    /// wherever it is asked (docs/PRODUCT.md law 4.3). An empty variable counts as unset, so
    /// <c>KEYPASTE_KEYFILE= keypaste ls</c> opens a vault that has no keyfile rather than failing
    /// on a keyfile called "".
    /// </para>
    /// </remarks>
    public static bool TryResolveKeyfile(string? fromFlag, string? fromEnvironment, out string? path)
    {
        string? chosen = !string.IsNullOrEmpty(fromFlag) ? fromFlag
            : !string.IsNullOrEmpty(fromEnvironment) ? fromEnvironment
            : null;

        path = chosen is null ? null : Path.GetFullPath(chosen);
        return path is not null;
    }
}
