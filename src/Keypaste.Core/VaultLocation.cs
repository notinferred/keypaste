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
/// of one rule is what CORE.md law 4.3 forbids. The CLI keeps a thin adapter that binds its own
/// argument parser and environment seam to this.
/// </para>
/// </remarks>
public static class VaultLocation
{
    /// <summary>The environment variable consulted when no path was given explicitly.</summary>
    public const string EnvironmentVariable = "KEYPASTE_VAULT";

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
}
