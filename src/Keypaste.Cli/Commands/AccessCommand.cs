using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Changes what unlocks a vault: <c>keypaste access [--password] [--new-keyfile F | --remove-keyfile]</c>.
/// </summary>
/// <remarks>
/// <c>--keyfile</c> and <c>KEYPASTE_KEYFILE</c> keep the meaning they have on every other verb, the
/// keyfile that opens the vault now. The new side is taken from flags alone, so a variable left set
/// in a shell can never become the key a vault is changed to (D-0290).
/// </remarks>
internal static class AccessCommand
{
    private const string _usage =
        "usage: keypaste access [--password] [--new-keyfile <path> | --remove-keyfile] [--vault V] [--keyfile K]";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("password", TakesValue: false),
        new("new-keyfile", TakesValue: true),
        new("remove-keyfile", TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste access: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine($"keypaste access: unexpected argument '{line.Operands[0]}'");
            return CliApp.ExitUsageError;
        }

        var newKeyfile = line.Value("new-keyfile");
        var removeKeyfile = line.HasFlag("remove-keyfile");
        var setPassword = line.HasFlag("password");

        if (newKeyfile is not null && removeKeyfile)
        {
            context.Stderr.WriteLine("keypaste access: give --new-keyfile or --remove-keyfile, not both");
            return CliApp.ExitUsageError;
        }

        if (!setPassword && newKeyfile is null && !removeKeyfile)
        {
            context.Stderr.WriteLine("keypaste access: nothing to change; give --password, --new-keyfile or --remove-keyfile");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste access: {locateError}");
            return CliApp.ExitUsageError;
        }

        var change = new VaultAccessChange(
            setPassword,
            newKeyfile is not null ? AccessKeyfileChange.Attach
                : removeKeyfile ? AccessKeyfileChange.Remove
                : AccessKeyfileChange.Keep,
            newKeyfile is null ? null : Path.GetFullPath(newKeyfile));

        if (change.KeyfilePath is { } attaching && RefuseBeforePrompting(attaching, path, context) is { } exit)
        {
            return exit;
        }

        return VaultSession.Open(path, line, context, vault => Change(vault, change, context), namesHardwareKeys: true);
    }

    /// <summary>The refusals that need no password, made before anybody is asked for one.</summary>
    private static int? RefuseBeforePrompting(string keyfile, string vaultPath, CliContext context)
    {
        if (VaultBackups.BelongsTo(vaultPath, keyfile))
        {
            context.Stderr.WriteLine($"keypaste access: {Words(VaultAccessOutcome.KeyfileIsThisVault, keyfile)}");
            return CliApp.ExitUsageError;
        }

        var inspection = VaultKeyfile.Inspect(keyfile);
        if (!inspection.Accepted)
        {
            context.Stderr.WriteLine($"keypaste access: {VaultSession.Refusal(inspection.Outcome, keyfile)}");
            return CliApp.ExitNotFound;
        }

        if (inspection.IsFragile)
        {
            context.Stderr.WriteLine($"keypaste access: {Words(VaultAccessOutcome.KeyfileIsFragile, keyfile)}");
            return CliApp.ExitUsageError;
        }

        return null;
    }

    private static int Change(Vault vault, VaultAccessChange change, CliContext context)
    {
        VaultAccessResult result;
        if (change.SetPassword)
        {
            using var next = VaultSession.ReadNewMasterPassword(context);
            if (next is null)
            {
                return CliApp.ExitAuthFailed;
            }

            result = vault.ChangeAccess(change, next.Password.Value, next.Confirmation.Value);
        }
        else
        {
            result = vault.ChangeAccess(change, [], []);
        }

        if (result.Outcome == VaultAccessOutcome.KeyfileUnusable)
        {
            context.Stderr.WriteLine($"keypaste access: {VaultSession.Refusal(result.Keyfile.Outcome, change.KeyfilePath!)}");
            return CliApp.ExitNotFound;
        }

        if (result.Outcome != VaultAccessOutcome.Changed)
        {
            context.Stderr.WriteLine($"keypaste access: {Words(result.Outcome, change.KeyfilePath)}");
            return CliApp.ExitUsageError;
        }

        Report(vault.Path, change, result.Kept!, context);
        return CliApp.ExitSuccess;
    }

    private static void Report(string vaultPath, VaultAccessChange change, VaultBackup kept, CliContext context)
    {
        var copies = VaultBackups.List(vaultPath).Count;
        var err = context.Stderr;

        err.WriteLine($"Changed what unlocks {vaultPath}.");
        err.WriteLine($"keypaste: the vault as it was is kept at '{kept.Path}'.");
        err.WriteLine(
            $"keypaste: copies made before this change may open with earlier credentials ({copies} in " +
            $"'{VaultBackups.DirectoryFor(vaultPath)}'). If an old password or keyfile was exposed, delete them.");
        err.WriteLine(
            $"keypaste: routine saves keep only the last {VaultBackups.Retained} copies, so these leave over " +
            "time, including the one this change just took.");

        if (change.Keyfile == AccessKeyfileChange.Attach)
        {
            err.WriteLine(
                $"keypaste: the vault now needs '{change.KeyfilePath}'. Losing that file locks the vault; " +
                "back it up somewhere other than beside the vault.");
        }

        err.WriteLine(
            "keypaste: a running `keypaste agent` still holds the earlier vault; restart it to use the new credentials.");
    }

    private static string Words(VaultAccessOutcome outcome, string? keyfile) => outcome switch
    {
        VaultAccessOutcome.NothingToChange => "nothing to change",
        VaultAccessOutcome.EmptyPassword => "the master password cannot be empty",
        VaultAccessOutcome.PasswordsDoNotMatch => "the passwords do not match",
        VaultAccessOutcome.WouldLeaveNoPassword =>
            "that keyfile is all that unlocks this vault; give --password to set one in its place",
        VaultAccessOutcome.NoKeyfileToRemove => "this vault has no keyfile to remove",
        VaultAccessOutcome.KeyfileIsFragile =>
            $"'{keyfile}' is not a keyfile, so the vault would be keyed to its exact contents; " +
            "attach a KeePass XML, 32-byte or 64-character hex keyfile",
        VaultAccessOutcome.KeyfileIsThisVault =>
            $"'{keyfile}' is this vault or one of its backups, not a keyfile",
        _ => "the change was refused",
    };

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(_usage);
        writer.WriteLine();
        writer.WriteLine("Changes what unlocks the vault. Asks for the current master password, then the new");
        writer.WriteLine("one twice when --password is given; piped, one line each, in that order.");
        writer.WriteLine();
        writer.WriteLine("  --password               set a new master password");
        writer.WriteLine("  --new-keyfile <path>     require this existing keyfile, adding or replacing one");
        writer.WriteLine("  --remove-keyfile         stop requiring the current keyfile");
        writer.WriteLine($"  --keyfile <path>         the keyfile that opens the vault now, or set {VaultLocator.KeyfileEnvironmentVariable}");
        writer.WriteLine();
        writer.WriteLine("keypaste never creates a keyfile and never takes a vault's password away. The file");
        writer.WriteLine("the change replaces is kept, and still opens with the old credentials.");
    }
}
