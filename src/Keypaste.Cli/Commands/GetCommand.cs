using System.Globalization;
using Keypaste.Cli.Clipboard;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Retrieves a password, one custom field or the current TOTP code: <c>keypaste get &lt;entry&gt; [totp | --field &lt;name&gt;]</c>.</summary>
/// <remarks>
/// Without <c>--reveal</c> the secret goes to the clipboard and never to stdout. That is the whole
/// point of the verb: a password on stdout ends up in shell history, scrollback, and CI logs.
/// Either way the value leaves with its own item's placeholders filled in, or not at all (D-0465).
/// A TOTP code is computed from the opened vault at the call and its seed zeroed before it leaves;
/// the seed itself never does.
/// </remarks>
internal static class GetCommand
{
    /// <summary>How long the secret stays on the clipboard by default.</summary>
    /// <remarks>
    /// Read from the core rather than written here, so this and the desktop app's countdown are one
    /// number rather than two that agree today.
    /// </remarks>
    internal static readonly int DefaultTimeoutSeconds =
        (int)Core.Clipboard.ClipboardClear.DefaultWindow.TotalSeconds;

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("field", TakesValue: true),
        new("show", TakesValue: false),
        new("reveal", TakesValue: false),
        new("timeout", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste get: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.HasFlag("show"))
        {
            context.Stderr.WriteLine("keypaste get: --show is now --reveal");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste get <item> [totp | --field <name>] [--reveal] [--timeout <seconds>]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count is < 1 or > 2)
        {
            context.Stderr.WriteLine("keypaste get: expected exactly one item name");
            return CliApp.ExitUsageError;
        }

        var totp = line.Operands.Count == 2;
        if (totp && !string.Equals(line.Operands[1], CredentialFields.Totp, StringComparison.Ordinal))
        {
            context.Stderr.WriteLine($"keypaste get: after the item name only {CredentialFields.Totp} may follow; --field <name> reads a custom field");
            return CliApp.ExitUsageError;
        }

        var field = line.Value("field");
        if (totp && field is not null)
        {
            context.Stderr.WriteLine($"keypaste get: {CredentialFields.Totp} and --field cannot be used together");
            return CliApp.ExitUsageError;
        }

        if (field is { Length: 0 })
        {
            context.Stderr.WriteLine("keypaste get: the field name cannot be empty");
            return CliApp.ExitUsageError;
        }

        if (field is not null && FieldNameRules.IsStandard(field))
        {
            context.Stderr.WriteLine($"keypaste get: '{field}' is one of the item's standard fields; without --field, get reads the password");
            return CliApp.ExitUsageError;
        }

        var timeout = DefaultTimeoutSeconds;
        var timeoutText = line.Value("timeout");
        if (timeoutText is not null
            && (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out timeout)
                || timeout < 0))
        {
            context.Stderr.WriteLine("keypaste get: --timeout needs a whole number of seconds");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste get: {locateError}");
            return CliApp.ExitUsageError;
        }

        var entryPath = line.Operands[0];
        var reveal = line.HasFlag("reveal");

        return VaultSession.Open(path, line, context, vault =>
        {
            var entry = vault.Find(entryPath);
            if (entry is null)
            {
                context.Stderr.WriteLine($"keypaste get: no item '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            var name = EntryName.Of(entry);

            if (totp)
            {
                return GetTotp(context, vault, name, entryPath, reveal, timeout);
            }

            var stored = field is null ? entry.Password : vault.ReadField(name, field);
            if (stored is null)
            {
                context.Stderr.WriteLine($"keypaste get: '{entryPath}' has no field '{field}'");
                return CliApp.ExitNotFound;
            }

            var fill = vault.FillPlaceholders(name, stored);
            if (fill is null)
            {
                context.Stderr.WriteLine($"keypaste get: no item '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            if (fill.Value is not { } secret)
            {
                context.Stderr.WriteLine(
                    $"keypaste get: the {field ?? "password"} of '{entryPath}' {fill.Refusal}; keypaste show '{entryPath}' --reveal shows it as the vault holds it");
                return CliApp.ExitInternalError;
            }

            if (reveal)
            {
                context.Stdout.WriteLine(secret);
                return CliApp.ExitSuccess;
            }

            return Copy(context, secret, timeout);
        });
    }

    private static int GetTotp(CliContext context, Vault vault, EntryName name, string entryPath, bool reveal, int timeoutSeconds)
    {
        string code;

        using (var spec = vault.ReadTotp(name, out var refusal))
        {
            if (spec is null)
            {
                context.Stderr.WriteLine(refusal is null
                    ? $"keypaste get: '{entryPath}' has no TOTP"
                    : $"keypaste get: the TOTP of '{entryPath}' cannot be computed: {refusal}");
                return refusal is null ? CliApp.ExitNotFound : CliApp.ExitInternalError;
            }

            code = spec.Code(context.Clock).Value;
        }

        if (reveal)
        {
            context.Stdout.WriteLine(code);
            return CliApp.ExitSuccess;
        }

        return Copy(context, code, timeoutSeconds);
    }

    private static int Copy(CliContext context, string secret, int timeoutSeconds)
    {
        var status = context.Clipboard.TrySet(secret, out var error);
        if (status != ClipboardStatus.Ok)
        {
            // Never fall back to printing the secret. A user who wants it on stdout says so.
            context.Stderr.WriteLine(status == ClipboardStatus.NoDisplay
                ? $"keypaste get: {error}. Use --reveal to print it instead."
                : $"keypaste get: could not use the clipboard: {error}. Use --reveal instead.");
            return CliApp.ExitInternalError;
        }

        if (timeoutSeconds == 0)
        {
            context.Stderr.WriteLine("Copied to clipboard. It will not be cleared automatically.");
            return CliApp.ExitSuccess;
        }

        // The baseline is read straight after the copy, so whatever the platform's read-back
        // does to the bytes it does identically at both ends of the wait.
        context.Clipboard.TryReadHash(out var expected, out _);

        context.ClipboardClear.ClearAfter(
            context.Clipboard,
            expected,
            TimeSpan.FromSeconds(timeoutSeconds),
            context.Stderr);

        return CliApp.ExitSuccess;
    }
}
