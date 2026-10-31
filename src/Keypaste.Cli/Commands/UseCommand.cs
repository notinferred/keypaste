using System.CommandLine;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste use [&lt;vault.kdbx&gt;]</c>: shows or changes the vault agents and the CLI use when no
/// <c>--vault</c> or <c>KEYPASTE_VAULT</c> names one (D-0389). It opens nothing and asks for no password.
/// </summary>
internal static class UseCommand
{
    private static readonly Argument<string?> _path = Syntax.Optional("vault.kdbx", "the vault; with none, prints the chosen one");

    internal static CliVerb Command { get; } = new(
        "use",
        "choose the vault used when no --vault is given\n\n"
            + $"agents and the CLI use it when neither --vault nor {VaultLocation.EnvironmentVariable} names one.",
        Execute)
    {
        _path,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        var home = context.Environment.Get(KeypasteHome.EnvironmentVariable);

        if (line.GetValue(_path) is not { } named)
        {
            if (ChosenVault.Read(home) is not { } chosen)
            {
                context.Stderr.WriteLine("keypaste use: no vault is chosen. Open one in the keypaste app or run `keypaste use <path>`.");
                return CliApp.ExitNotFound;
            }

            context.Stdout.WriteLine(chosen);
            return CliApp.ExitSuccess;
        }

        var path = Path.GetFullPath(named);

        if (!File.Exists(path))
        {
            context.Stderr.WriteLine($"keypaste use: no vault at '{path}'. Create it with `keypaste init {path}`.");
            return CliApp.ExitNotFound;
        }

        switch (ChosenVault.Choose(home, path, onlyIfNone: false))
        {
            case ChooseOutcome.Chosen:
                context.Stderr.WriteLine($"keypaste: agents and the CLI now use {path} when no --vault is given.");
                context.Stderr.WriteLine("keypaste: an AI tool's running bridge keeps the vault it started with until the tool restarts it.");
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
}
