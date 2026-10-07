using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Says why a file is not written over a vault, by <see cref="VaultOverwriteRule"/>.</summary>
internal static class VaultOverwriteGuard
{
    /// <summary>True when <paramref name="targetPath"/> may be written; otherwise says why not, and --force never lifts it.</summary>
    /// <param name="verb">The command, as its messages name it.</param>
    /// <param name="vaultPath">The vault the command reads.</param>
    /// <param name="targetPath">The full path about to be written.</param>
    /// <param name="written">What would replace the vault, such as "a .env".</param>
    /// <param name="context">Where the refusal is written.</param>
    /// <param name="exit">The exit code on a refusal.</param>
    internal static bool TryRefuse(string verb, string vaultPath, string targetPath, string written, CliContext context, out int exit)
    {
        var overwrite = VaultOverwriteRule.Check(vaultPath, targetPath);

        exit = overwrite == VaultOverwrite.None
            ? CliApp.ExitSuccess
            : Refuse(verb, vaultPath, targetPath, overwrite, written, context);

        return overwrite == VaultOverwrite.None;
    }

    /// <summary>Says which vault <paramref name="targetPath"/> is, and that nothing was written.</summary>
    /// <param name="verb">The command, as its messages name it.</param>
    /// <param name="vaultPath">The vault the command reads.</param>
    /// <param name="targetPath">The full path that was not written.</param>
    /// <param name="overwrite">Which vault it is.</param>
    /// <param name="written">What would replace the vault, such as "a .env".</param>
    /// <param name="context">Where the refusal is written.</param>
    /// <returns>The exit code.</returns>
    internal static int Refuse(string verb, string vaultPath, string targetPath, VaultOverwrite overwrite, string written, CliContext context)
    {
        context.Stderr.WriteLine(overwrite switch
        {
            VaultOverwrite.TheVault => $"{verb}: '{targetPath}' is the vault this command reads from",
            VaultOverwrite.TheVaultElsewhere => $"{verb}: '{targetPath}' is the vault at '{vaultPath}'",
            _ => $"{verb}: '{targetPath}' is a KeePass vault",
        });
        context.Stderr.WriteLine($"Writing it would leave you with {written} and no vault.");
        context.Stderr.WriteLine("Nothing was written. --force does not lift this; name another file.");
        return CliApp.ExitUsageError;
    }
}
