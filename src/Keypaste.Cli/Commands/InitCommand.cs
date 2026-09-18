using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Creates a new vault: <c>keypaste init &lt;vault.kdbx&gt;</c>.</summary>
internal static class InitCommand
{
    private static readonly OptionSpec[] _options = [new("vault", TakesValue: true)];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste init: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste init <vault.kdbx>");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 1)
        {
            context.Stderr.WriteLine("keypaste init: expected at most one vault path");
            return CliApp.ExitUsageError;
        }

        var positional = line.Operands.Count == 1 ? line.Operands[0] : null;
        var flag = line.Value("vault");

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
        else if (!VaultLocator.TryResolve(line, context.Environment, out path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste init: {locateError}");
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
