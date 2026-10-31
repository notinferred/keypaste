using System.CommandLine;
using System.Globalization;
using Keypaste.Core;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste generate --words N</c>: prints a passphrase and stores nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one verb whose output is a fresh secret on stdout, and the exception is
/// argued rather than assumed (DECISIONS.md D-0237).</b> Everywhere else keypaste refuses to
/// print a secret without <c>--reveal</c>, because stdout is shell history, scrollback and CI logs.
/// A share passphrase (STEPS 5.4a) is the case that rule does not fit: it exists to travel to
/// another person by a second channel, and no vault holds it, so there is nothing for the
/// discipline to protect and no other way to obtain it.
/// </para>
/// <para>
/// <b>It generates passphrases only.</b> A character password on stdout has no such argument —
/// <c>add --generate</c> stores one and <c>get --reveal</c> prints one, both already — so
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
    private static readonly Option<string> _words = Syntax.Value("--words", "n", "how many words; required");
    private static readonly Option<string> _separator = Syntax.Value("--separator", "C", $"between the words, {PasswordGenerator.DefaultSeparator} by default");

    internal static CliVerb Command { get; } = new(
        "generate",
        "print a passphrase (--words N); it is not stored\n\n"
            + string.Format(
                CultureInfo.InvariantCulture,
                "{0} to {1} words from the {2} of {3:N0}, about {4:F1} bits each. the passphrase goes to stdout and is not "
                    + "stored; to keep one, use `keypaste add <item> --generate --words <n>` instead.",
                PasswordGenerator.MinimumWords,
                PasswordGenerator.MaximumWords,
                WordList.Provenance,
                WordList.Count,
                WordList.BitsPerWord),
        Execute)
    {
        _words, _separator,
    };

    /// <summary>Runs the verb.</summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="context">Where to write, and what to prompt with.</param>
    /// <returns>An exit code: success, or a usage error.</returns>
    private static int Execute(ParseResult line, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        var wordsText = line.GetValue(_words);

        if (wordsText is null)
        {
            context.Stderr.WriteLine("keypaste generate: --words <n> is required");
            context.Stderr.WriteLine("see keypaste generate --help");
            return CliApp.ExitUsageError;
        }

        if (!int.TryParse(wordsText, NumberStyles.None, CultureInfo.InvariantCulture, out var words))
        {
            context.Stderr.WriteLine("keypaste generate: --words needs a whole number of words");
            return CliApp.ExitUsageError;
        }

        var separatorText = line.GetValue(_separator);

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
}
