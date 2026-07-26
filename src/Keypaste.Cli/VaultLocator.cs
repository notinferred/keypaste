using Keypaste.Cli.Prompting;
using Keypaste.Core;

namespace Keypaste.Cli;

/// <summary>
/// Binds the CLI's argument parser and environment seam to <see cref="VaultLocation"/>.
/// </summary>
/// <remarks>
/// The rule itself — no default path, <c>--vault</c> first, an empty variable counting as unset —
/// lives in the core, because the MCP bridge and <c>keypaste log</c> have to answer the same
/// question the same way (CORE.md law 4.3). What is left here is the binding, which is genuinely
/// CLI-shaped: <see cref="CommandLine"/> and <see cref="IEnvironmentProbe"/> mean nothing to a
/// server that has neither.
/// </remarks>
internal static class VaultLocator
{
    /// <summary>The environment variable consulted when <c>--vault</c> is absent.</summary>
    internal const string EnvironmentVariable = VaultLocation.EnvironmentVariable;

    /// <summary>Resolves the vault path from the command line and the environment.</summary>
    /// <returns><see langword="false"/> with <paramref name="error"/> set when no path is available.</returns>
    internal static bool TryResolve(
        CommandLine line,
        IEnvironmentProbe environment,
        out string path,
        out string error) =>
        VaultLocation.TryResolve(
            line.Value("vault"),
            environment.Get(EnvironmentVariable),
            out path,
            out error);
}
