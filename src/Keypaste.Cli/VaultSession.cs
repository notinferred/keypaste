using Keypaste.Cli.Prompting;
using Keypaste.Core;

namespace Keypaste.Cli;

/// <summary>
/// Prompts for the master password, opens a vault, and maps every failure to an exit code.
/// </summary>
/// <remarks>
/// One place, so no command invents its own wording or its own exit code for "wrong password".
/// The callback shape is also what keeps the <c>using var vault</c> inside a single method, so no
/// <see cref="Vault"/> can escape the method that created it — which is what satisfies CA2000
/// (an error in this repository) by construction rather than by suppression.
/// </remarks>
internal static class VaultSession
{
    /// <summary>Opens the vault at <paramref name="path"/> and runs <paramref name="body"/>.</summary>
    internal static int Open(string path, CliContext context, Func<Vault, int> body)
    {
        if (!File.Exists(path))
        {
            context.Stderr.WriteLine($"keypaste: no vault at '{path}'");
            return CliApp.ExitNotFound;
        }

        using var master = context.Prompt.ReadSecret("Master password: ");
        if (master is null)
        {
            context.Stderr.WriteLine("keypaste: no master password given");
            return CliApp.ExitAuthFailed;
        }

        try
        {
            using var vault = Vault.Open(path, master.Value);
            return body(vault);
        }
        catch (InvalidMasterPasswordException)
        {
            context.Stderr.WriteLine("keypaste: wrong master password");
            return CliApp.ExitAuthFailed;
        }
        catch (VaultException ex)
        {
            context.Stderr.WriteLine($"keypaste: {ex.Message}");
            return CliApp.ExitInternalError;
        }
    }

    /// <summary>
    /// Reads a master password twice and checks the two match.
    /// </summary>
    /// <remarks>
    /// Confirmation happens even when stdin is redirected: one code path means the compatibility
    /// gate exercises the branch a human takes, and it costs a script one extra line.
    /// </remarks>
    internal static SecretBuffer? ReadNewMasterPassword(CliContext context)
    {
        var first = context.Prompt.ReadSecret("New master password: ");
        if (first is null)
        {
            context.Stderr.WriteLine("keypaste: no master password given");
            return null;
        }

        if (first.Length == 0)
        {
            first.Dispose();
            context.Stderr.WriteLine("keypaste: the master password cannot be empty");
            return null;
        }

        using var second = context.Prompt.ReadSecret("Confirm master password: ");
        if (second is null || !first.ValueEquals(second))
        {
            first.Dispose();
            context.Stderr.WriteLine("keypaste: the passwords do not match");
            return null;
        }

        return first;
    }
}
