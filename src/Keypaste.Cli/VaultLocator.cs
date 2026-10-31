using System.CommandLine;
using Keypaste.Cli.Prompting;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;

namespace Keypaste.Cli;

/// <summary>
/// Binds the CLI's argument parser and environment seam to <see cref="VaultLocation"/>.
/// </summary>
/// <remarks>
/// The rule itself — no default path, <c>--vault</c> first, an empty variable counting as unset —
/// lives in the core, because the MCP bridge and <c>keypaste log</c> have to answer the same
/// question the same way (docs/PRODUCT.md law 4.3). What is left here is the binding, which is genuinely
/// CLI-shaped: <see cref="ParseResult"/> and <see cref="IEnvironmentProbe"/> mean nothing to a
/// server that has neither.
/// </remarks>
internal static class VaultLocator
{
    /// <summary>The environment variable consulted when <c>--vault</c> is absent.</summary>
    internal const string EnvironmentVariable = VaultLocation.EnvironmentVariable;

    /// <summary>Resolves the vault path from the command line, the environment and the chosen vault.</summary>
    /// <returns><see langword="false"/> with <paramref name="error"/> set when no path is available.</returns>
    internal static bool TryResolve(
        ParseResult line,
        IEnvironmentProbe environment,
        out string path,
        out string error) =>
        VaultLocation.TryResolve(
            line.GetValue(CliOptions.Vault),
            environment.Get(EnvironmentVariable),
            ChosenVault.Read(environment.Get(KeypasteHome.EnvironmentVariable)),
            out path,
            out error);

    /// <summary>Whether <c>--vault</c> or <c>KEYPASTE_VAULT</c> names a vault, as opposed to the chosen one.</summary>
    internal static bool IsNamed(ParseResult line, IEnvironmentProbe environment) =>
        !string.IsNullOrEmpty(line.GetValue(CliOptions.Vault)) || !string.IsNullOrEmpty(environment.Get(EnvironmentVariable));

    /// <summary>The environment variable consulted when <c>--keyfile</c> is absent.</summary>
    internal const string KeyfileEnvironmentVariable = VaultLocation.KeyfileEnvironmentVariable;

    /// <summary>Resolves the keyfile path, when the command line or the environment names one.</summary>
    /// <returns><see langword="false"/>, with <paramref name="path"/> null, when neither does.</returns>
    internal static bool TryResolveKeyfile(
        ParseResult line,
        IEnvironmentProbe environment,
        out string? path) =>
        VaultLocation.TryResolveKeyfile(
            line.GetValue(CliOptions.Keyfile),
            environment.Get(KeyfileEnvironmentVariable),
            out path);
}
