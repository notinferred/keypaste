using System.CommandLine;
using Keypaste.Core;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Creates a new vault: <c>keypaste init &lt;vault.kdbx&gt;</c>.</summary>
internal static class InitCommand
{
    private static readonly Argument<string?> _path = Syntax.Optional("vault.kdbx", "where to create it, or --vault");

    internal static CliVerb Command { get; } = new("init", "create a new vault", Execute) { _path, CliOptions.Vault };

    private static int Execute(ParseResult line, CliContext context)
    {
        var positional = line.GetValue(_path);
        var flag = line.GetValue(CliOptions.Vault);

        if (positional is not null && flag is not null)
        {
            context.Stderr.WriteLine("keypaste init: give the vault path once, not both positionally and with --vault");
            return CliApp.ExitUsageError;
        }

        string path;
        if (positional is not null)
        {
            path = Path.GetFullPath(positional);
        }
        else if (!VaultLocator.TryResolve(line, context.Environment, out path, out _))
        {
            context.Stderr.WriteLine("keypaste init: give the path of the vault to create: keypaste init <path>");
            return CliApp.ExitUsageError;
        }

        // Asked before the prompt, not only inside VaultCreation, so nobody is made to type a master
        // password twice before being told the file was never going to be written.
        if (VaultCreation.Inspect(path) == VaultDestination.Occupied)
        {
            context.Stderr.WriteLine($"keypaste init: '{path}' already exists");
            return CliApp.ExitUsageError;
        }

        using var master = VaultSession.ReadNewMasterPassword(context);
        if (master is null)
        {
            return CliApp.ExitAuthFailed;
        }

        var outcome = VaultCreation.TryCreate(
            path,
            master.Password.Value,
            master.Confirmation.Value,
            out var created,
            out var failure);

        using (created)
        {
            switch (outcome)
            {
                case VaultCreationOutcome.Created:
                    break;

                case VaultCreationOutcome.PathAlreadyExists:
                    context.Stderr.WriteLine($"keypaste init: '{path}' already exists");
                    return CliApp.ExitUsageError;

                case VaultCreationOutcome.EmptyPassword:
                    context.Stderr.WriteLine("keypaste: the master password cannot be empty");
                    return CliApp.ExitAuthFailed;

                case VaultCreationOutcome.PasswordsDoNotMatch:
                    context.Stderr.WriteLine("keypaste: the passwords do not match");
                    return CliApp.ExitAuthFailed;

                default:
                    context.Stderr.WriteLine($"keypaste init: {failure}");
                    return CliApp.ExitInternalError;
            }
        }

        context.Stderr.WriteLine($"Created {path}");
        return CliApp.ExitSuccess;
    }
}
