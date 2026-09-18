using System.Globalization;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste generate --words N</c>: prints a passphrase and stores nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one verb whose output is a fresh secret on stdout, and the exception is
/// argued rather than assumed (DECISIONS.md D-0237).</b> Everywhere else keypaste refuses to
/// print a secret without <c>--show</c>, because stdout is shell history, scrollback and CI logs.
/// A share passphrase (STEPS 5.4a) is the case that rule does not fit: it exists to travel to
/// another person by a second channel, and no vault holds it, so there is nothing for the
/// discipline to protect and no other way to obtain it.
/// </para>
/// <para>
/// <b>It generates passphrases only.</b> A character password on stdout has no such argument —
/// <c>add --generate</c> stores one and <c>get --show</c> prints one, both already — so
/// <c>--words</c> is required rather than defaulted. A bare <c>keypaste generate</c>, or a
/// truncated argument list, prints usage and exits 1: no typo can make this verb emit a secret.
/// </para>
/// <para>
/// <b>It opens no vault.</b> No <c>--vault</c>, no master-password prompt, no audit record, and
/// nothing read from disk: the word list is embedded. So it works before a vault exists, which is
/// what somebody choosing a passphrase for one needs.
/// </para>
/// </remarks>
internal static class GenerateCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("words", TakesValue: true),
        new("separator", TakesValue: true),
    ];

    /// <summary>Runs the verb.</summary>
    /// <param name="args">The whole command line, starting at the verb.</param>
    /// <param name="context">Where to write, and what to prompt with.</param>
    /// <returns>An exit code: success, or a usage error.</returns>
    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste generate: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteHelp(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine("keypaste generate: expected no arguments besides its options");
            return CliApp.ExitUsageError;
        }

        var wordsText = line.Value("words");

        if (wordsText is null)
        {
            context.Stderr.WriteLine("keypaste generate: --words <n> is required");
            WriteHelp(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (!int.TryParse(wordsText, NumberStyles.None, CultureInfo.InvariantCulture, out var words))
        {
            context.Stderr.WriteLine("keypaste generate: --words needs a whole number of words");
            return CliApp.ExitUsageError;
        }

        var separatorText = line.Value("separator");

        if (separatorText is { Length: not 1 })
        {
            context.Stderr.WriteLine("keypaste generate: --separator needs a single character");
            return CliApp.ExitUsageError;
        }

        var recipe = new PassphraseRecipe
        {
            WordCount = words,
            Separator = separatorText?[0] ?? PasswordGenerator.DefaultSeparator,
        };

        if (!recipe.TryValidate(out var refusal))
        {
            context.Stderr.WriteLine($"keypaste generate: {refusal}");
            return CliApp.ExitUsageError;
        }

        using var buffer = new SecretBuffer();
        PasswordGenerator.Append(recipe, buffer);

        // The passphrase, and nothing else, so `keypaste generate --words 6 > file` holds exactly
        // the passphrase. What it is made of goes to stderr, where a person reading the terminal
        // sees it and a redirect does not.
        context.Stdout.WriteLine(new string(buffer.Value));

        context.Stderr.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} words from the {1} of {2:N0}, about {3:F0} bits. Nothing was stored.",
            recipe.WordCount,
            WordList.Provenance,
            WordList.Count,
            recipe.Bits));

        return CliApp.ExitSuccess;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste generate --words <n> [--separator C]");
        writer.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "       {0} to {1} words from the {2} of {3:N0}, about {4:F1} bits each.",
            PasswordGenerator.MinimumWords,
            PasswordGenerator.MaximumWords,
            WordList.Provenance,
            WordList.Count,
            WordList.BitsPerWord));
        writer.WriteLine("       the passphrase goes to stdout and is not stored; to keep one, use");
        writer.WriteLine("       `keypaste add <entry> --generate --words <n>` instead.");
    }
}
