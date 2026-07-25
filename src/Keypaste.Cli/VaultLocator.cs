using Keypaste.Cli.Prompting;

namespace Keypaste.Cli;

/// <summary>Decides which vault file a command operates on.</summary>
/// <remarks>
/// There is deliberately <b>no default path</b>. A credential tool that silently picks a vault
/// when you forgot to say which one is a tool that eventually writes a secret into the wrong
/// file, or reports "not found" against a vault you have never seen. Being explicit costs one
/// flag and removes a whole class of confusion.
/// </remarks>
internal static class VaultLocator
{
    /// <summary>The environment variable consulted when <c>--vault</c> is absent.</summary>
    internal const string EnvironmentVariable = "KEYPASTE_VAULT";

    /// <summary>Resolves the vault path from the command line and the environment.</summary>
    /// <returns><see langword="false"/> with <paramref name="error"/> set when no path is available.</returns>
    internal static bool TryResolve(
        CommandLine line,
        IEnvironmentProbe environment,
        out string path,
        out string error)
    {
        path = string.Empty;
        error = string.Empty;

        var fromFlag = line.Value("vault");
        if (!string.IsNullOrEmpty(fromFlag))
        {
            path = Path.GetFullPath(fromFlag);
            return true;
        }

        // An empty variable is treated as unset: `KEYPASTE_VAULT= keypaste ls` should complain
        // that no vault was given, not that "" is missing.
        var fromEnvironment = environment.Get(EnvironmentVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            path = Path.GetFullPath(fromEnvironment);
            return true;
        }

        error = $"no vault given. Use --vault <path> or set {EnvironmentVariable}.";
        return false;
    }
}
