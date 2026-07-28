using System.Globalization;
using Keypaste.Cli.Clipboard;

namespace Keypaste.Cli.Commands;

/// <summary>Retrieves a password: <c>keypaste get &lt;entry&gt;</c>.</summary>
/// <remarks>
/// Without <c>--show</c> the secret goes to the clipboard and never to stdout. That is the whole
/// point of the verb: a password on stdout ends up in shell history, scrollback, and CI logs.
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
        new("show", TakesValue: false),
        new("timeout", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste get: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste get <entry> [--show] [--timeout <seconds>]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste get: expected exactly one entry name");
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
        var show = line.HasFlag("show");

        return VaultSession.Open(path, context, vault =>
        {
            var entry = vault.Find(entryPath);
            if (entry is null)
            {
                context.Stderr.WriteLine($"keypaste get: no entry '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            if (show)
            {
                context.Stdout.WriteLine(entry.Password);
                return CliApp.ExitSuccess;
            }

            return Copy(context, entry.Password, timeout);
        });
    }

    private static int Copy(CliContext context, string password, int timeoutSeconds)
    {
        var status = context.Clipboard.TrySet(password, out var error);
        if (status != ClipboardStatus.Ok)
        {
            // Never fall back to printing the secret. A user who wants it on stdout says so.
            context.Stderr.WriteLine(status == ClipboardStatus.NoDisplay
                ? $"keypaste get: {error}. Use --show to print the password instead."
                : $"keypaste get: could not use the clipboard: {error}. Use --show instead.");
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
