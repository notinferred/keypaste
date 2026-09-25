using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Refuses to write a file over the vault a command reads, or over any KeePass vault.</summary>
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
        exit = CliApp.ExitSuccess;

        if (PathIdentity.SameFile(vaultPath, targetPath))
        {
            context.Stderr.WriteLine(string.Equals(targetPath, vaultPath, StringComparison.Ordinal)
                ? $"{verb}: '{targetPath}' is the vault this command reads from"
                : $"{verb}: '{targetPath}' is the vault at '{vaultPath}'");
        }
        else if (File.Exists(targetPath) && KdbxHeader.IsVaultFile(targetPath))
        {
            context.Stderr.WriteLine($"{verb}: '{targetPath}' is a KeePass vault");
        }
        else
        {
            return true;
        }

        context.Stderr.WriteLine($"Writing it would leave you with {written} and no vault.");
        context.Stderr.WriteLine("Nothing was written. --force does not lift this; name another file.");
        exit = CliApp.ExitUsageError;
        return false;
    }
}
