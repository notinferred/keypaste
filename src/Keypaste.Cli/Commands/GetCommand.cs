using System.CommandLine;
using System.Globalization;
using Keypaste.Cli.Clipboard;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Retrieves a password, one custom field or the current TOTP code: <c>keypaste get &lt;entry&gt; [totp | --field &lt;name&gt;]</c>.</summary>
/// <remarks>
/// Without <c>--reveal</c> the secret goes to the clipboard and never to stdout. That is the whole
/// point of the verb: a password on stdout ends up in shell history, scrollback, and CI logs.
/// Either way the value leaves with its own item's placeholders filled in, or not at all.
/// A TOTP code is computed from the opened vault at the call and its seed zeroed before it leaves;
/// the seed itself never does.
/// </remarks>
internal static class GetCommand
{
    /// <summary>The longest <c>--timeout</c>: a timer waits at most 2^32 - 2 milliseconds, about 49.7 days.</summary>
    internal const int MaximumTimeoutSeconds = 4_294_967;

    /// <summary>How long the secret stays on the clipboard by default.</summary>
    /// <remarks>
    /// Read from the core rather than written here, so this and the desktop app's countdown are one
    /// number rather than two that agree today.
    /// </remarks>
    internal static readonly int DefaultTimeoutSeconds =
        (int)Core.Clipboard.ClipboardClear.DefaultWindow.TotalSeconds;

    private static readonly Argument<string> _item = Syntax.Operand("item", "the item's name");
    private static readonly Argument<string?> _totp = Syntax.Keyword(CredentialFields.Totp, "its current TOTP code instead");
    private static readonly Option<string> _field = Syntax.Value("--field", "name", "a custom field instead");
    private static readonly Option<bool> _reveal = Syntax.Flag("--reveal", "print it rather than copy it");
    private static readonly Option<string> _timeout = Syntax.Value("--timeout", "seconds", $"clear the clipboard after this long, {DefaultTimeoutSeconds} by default; 0 never");

    internal static CliVerb Command { get; } = new("get", "copy a secret to the clipboard, or print it with --reveal", Execute)
    {
        _item, _totp, _field, _reveal, _timeout, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        var word = line.GetValue(_totp);
        var totp = word is not null;
        if (totp && !string.Equals(word, CredentialFields.Totp, StringComparison.Ordinal))
        {
            context.Stderr.WriteLine($"keypaste get: after the item name only {CredentialFields.Totp} may follow; --field <name> reads a custom field");
            return CliApp.ExitUsageError;
        }

        var field = line.GetValue(_field);
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
        var timeoutText = line.GetValue(_timeout);
        if (timeoutText is not null
            && (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out timeout)
                || timeout < 0
                || timeout > MaximumTimeoutSeconds))
        {
            context.Stderr.WriteLine($"keypaste get: --timeout needs a whole number of seconds, at most {MaximumTimeoutSeconds}");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste get: {locateError}");
            return CliApp.ExitUsageError;
        }

        var entryPath = line.GetValue(_item)!;
        var reveal = line.GetValue(_reveal);

        // The vault is closed and the master password zeroed before the value is printed or the clipboard wait starts.
        return VaultSession.OpenThen<string>(
            path,
            line,
            context,
            vault => Read(context, vault, entryPath, field, totp),
            secret =>
            {
                if (reveal)
                {
                    context.Stdout.WriteLine(secret);
                    return CliApp.ExitSuccess;
                }

                return Copy(context, secret, timeout);
            });
    }

    private static (int Exit, string? Secret) Read(CliContext context, Vault vault, string entryPath, string? field, bool totp)
    {
        var entry = vault.Find(entryPath);
        if (entry is null)
        {
            context.Stderr.WriteLine($"keypaste get: no item '{entryPath}'");
            return (CliApp.ExitNotFound, null);
        }

        var name = EntryName.Of(entry);

        if (totp)
        {
            return ReadTotp(context, vault, name, entryPath);
        }

        var stored = field is null ? entry.Password : vault.ReadField(name, field);
        if (stored is null)
        {
            context.Stderr.WriteLine($"keypaste get: '{entryPath}' has no field '{field}'");
            return (CliApp.ExitNotFound, null);
        }

        var fill = vault.FillPlaceholders(name, stored);
        if (fill is null)
        {
            context.Stderr.WriteLine($"keypaste get: no item '{entryPath}'");
            return (CliApp.ExitNotFound, null);
        }

        if (fill.Value is not { } secret)
        {
            context.Stderr.WriteLine(
                $"keypaste get: the {field ?? "password"} of '{entryPath}' {fill.Refusal}; keypaste show '{entryPath}' --reveal shows it as the vault holds it");
            return (CliApp.ExitInternalError, null);
        }

        return (CliApp.ExitSuccess, secret);
    }

    private static (int Exit, string? Code) ReadTotp(CliContext context, Vault vault, EntryName name, string entryPath)
    {
        using var spec = vault.ReadTotp(name, out var refusal);
        if (spec is null)
        {
            context.Stderr.WriteLine(refusal is null
                ? $"keypaste get: '{entryPath}' has no TOTP"
                : $"keypaste get: the TOTP of '{entryPath}' cannot be computed: {refusal}");
            return (refusal is null ? CliApp.ExitNotFound : CliApp.ExitInternalError, null);
        }

        return (CliApp.ExitSuccess, spec.Code(context.Clock).Value);
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

        context.ClipboardClear.ClearAfter(
            context.Clipboard,
            ClipboardBaseline.Take(context.Clipboard),
            TimeSpan.FromSeconds(timeoutSeconds),
            context.Stderr);

        return CliApp.ExitSuccess;
    }
}
