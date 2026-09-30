using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste use [&lt;vault.kdbx&gt;]</c>: shows or changes the vault agents and the CLI use when no
/// <c>--vault</c> or <c>KEYPASTE_VAULT</c> names one (D-0389). It opens nothing and asks for no password.
/// </summary>
internal static class UseCommand
{
    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, [], out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste use: {error}");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        var home = context.Environment.Get(KeypasteHome.EnvironmentVariable);

        if (line.Operands.Count == 0)
        {
            if (ChosenVault.Read(home) is not { } chosen)
            {
                context.Stderr.WriteLine("keypaste use: no vault is chosen. Open one in the keypaste app or run `keypaste use <path>`.");
                return CliApp.ExitNotFound;
            }

            context.Stdout.WriteLine(chosen);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 1)
        {
            context.Stderr.WriteLine("keypaste use: expected one vault path");
            return CliApp.ExitUsageError;
        }

        var path = Path.GetFullPath(line.Operands[0]);

        if (!File.Exists(path))
        {
            context.Stderr.WriteLine($"keypaste use: no vault at '{path}'. Create it with `keypaste init {path}`.");
            return CliApp.ExitNotFound;
        }

        switch (ChosenVault.Choose(home, path, onlyIfNone: false))
        {
            case ChooseOutcome.Chosen:
                context.Stderr.WriteLine($"keypaste: agents and the CLI now use {path} when no --vault is given.");
                context.Stderr.WriteLine("keypaste: an MCP client's running bridge keeps the vault it started with until the client restarts it.");
                return CliApp.ExitSuccess;

            case ChooseOutcome.Unreadable:
                context.Stderr.WriteLine($"keypaste use: {KeypasteHome.SettingsPath(home)} could not be read, so it was left as it is. Fix or delete it, then try again.");
                return CliApp.ExitInternalError;

            case ChooseOutcome.Unrecordable:
                context.Stderr.WriteLine($"keypaste use: '{path}' holds a quote, a control character or a backslash, which app.toml cannot record. Move or rename the vault, or name it with --vault.");
                return CliApp.ExitUsageError;

            default:
                context.Stderr.WriteLine($"keypaste use: {KeypasteHome.SettingsPath(home)} could not be written.");
                return CliApp.ExitInternalError;
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste use [<vault.kdbx>]");
        writer.WriteLine();
        writer.WriteLine("Chooses the vault agents and the CLI use when neither --vault nor");
        writer.WriteLine($"{VaultLocation.EnvironmentVariable} names one. With no path, prints the chosen vault.");
    }
}
