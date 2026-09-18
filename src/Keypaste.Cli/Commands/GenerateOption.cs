using System.Globalization;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>
/// The <c>--generate</c> flags, shared by the two verbs that store a secret.
/// </summary>
/// <remarks>
/// <para>
/// <b>These flags generate <i>into the vault</i>, and print only a count.</b>
/// <c>keypaste get</c> requires <c>--show</c> before it will put a password on stdout, because
/// stdout is shell history, scrollback and CI logs, and generating where the value is already
/// being stored needs no such exception: the password goes into the vault and the only thing
/// printed is what kind and how much of it there was.
/// </para>
/// <para>
/// <b>This file used to say there could be no <c>keypaste generate</c> verb at all, and V.6
/// overturned that for exactly one case (DECISIONS.md D-0237).</b> A passphrase for a share
/// (5.4a) exists to leave by a second channel, and no vault holds it, so there is no store for it
/// to go into and <c>get --show</c>'s discipline has nothing to protect. That verb generates
/// passphrases only; a character password on stdout still has no argument, because
/// <c>add --generate</c> already stores one and <c>get --show</c> already prints it.
/// </para>
/// <para>
/// One flag set, parsed once, so <c>add</c> and <c>env set</c> cannot drift into two spellings of
/// the same option.
/// </para>
/// </remarks>
internal static class GenerateOption
{
    /// <summary>What to add to a verb's own option list.</summary>
    internal static OptionSpec[] Specs { get; } =
    [
        new("generate", TakesValue: false),
        new("length", TakesValue: true),
        new("no-symbols", TakesValue: false),
        new("no-lookalikes", TakesValue: false),
        new("words", TakesValue: true),
        new("separator", TakesValue: true),
    ];

    /// <summary>What to add to a verb's usage line.</summary>
    internal const string Usage =
        "[--generate] [--length N | --words N] [--separator C] [--no-symbols] [--no-lookalikes]";

    /// <summary>Reads the generator flags.</summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="recipe">What to generate, or null when <c>--generate</c> was not given.</param>
    /// <param name="error">A message naming the problem, or empty.</param>
    /// <returns><see langword="false"/> on a usage mistake.</returns>
    /// <remarks>
    /// A shaping flag without <c>--generate</c> is an error rather than a no-op. Somebody typing
    /// <c>--length 32</c> and getting a prompt has been ignored, not helped. Mixing the character
    /// flags with <c>--words</c> is an error for the same reason: the two describe different kinds
    /// of secret, and silently honouring one of them is a guess.
    /// </remarks>
    internal static bool TryRead(CommandLine line, out SecretRecipe? recipe, out string error)
    {
        recipe = null;

        var lengthText = line.Value("length");
        var wordsText = line.Value("words");
        var separatorText = line.Value("separator");

        var shapedAsCharacters = lengthText is not null
            || line.HasFlag("no-symbols")
            || line.HasFlag("no-lookalikes");
        var shapedAsWords = wordsText is not null || separatorText is not null;

        if (!line.HasFlag("generate"))
        {
            if (shapedAsCharacters || shapedAsWords)
            {
                error = "--length, --words, --separator, --no-symbols and --no-lookalikes only mean something with --generate";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (shapedAsCharacters && wordsText is not null)
        {
            error = "--words and --length, --no-symbols and --no-lookalikes describe different kinds of secret; pick one";
            return false;
        }

        if (separatorText is not null && wordsText is null)
        {
            error = "--separator only means something with --words";
            return false;
        }

        return wordsText is null
            ? TryReadCharacters(line, lengthText, out recipe, out error)
            : TryReadWords(wordsText, separatorText, out recipe, out error);
    }

    /// <summary>
    /// Generates a secret into a buffer the caller owns and disposes.
    /// </summary>
    /// <param name="recipe">What to generate.</param>
    /// <returns>The buffer holding it.</returns>
    /// <remarks>
    /// Handed back in a <see cref="SecretBuffer"/> rather than a string so the caller's
    /// <c>using</c> zeroes it, exactly as it would a value read from the prompt. The one
    /// unavoidable <see cref="string"/> is made at the call site, where it is visible.
    /// </remarks>
    internal static SecretBuffer Generate(SecretRecipe recipe)
    {
        var buffer = new SecretBuffer();

        try
        {
            PasswordGenerator.Append(recipe, buffer);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static bool TryReadCharacters(
        CommandLine line,
        string? lengthText,
        out SecretRecipe? recipe,
        out string error)
    {
        recipe = null;

        var length = PasswordGenerator.DefaultLength;
        if (lengthText is not null
            && !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length))
        {
            error = "--length needs a whole number of characters";
            return false;
        }

        var alphabet = line.HasFlag("no-symbols")
            ? PasswordAlphabet.Lowercase | PasswordAlphabet.Uppercase | PasswordAlphabet.Digits
            : PasswordAlphabet.Default;

        var candidate = new PasswordRecipe
        {
            Length = length,
            Alphabet = alphabet,
            ExcludeLookalikes = line.HasFlag("no-lookalikes"),
        };

        if (!candidate.TryValidate(out error))
        {
            return false;
        }

        recipe = SecretRecipe.For(candidate);
        return true;
    }

    private static bool TryReadWords(
        string wordsText,
        string? separatorText,
        out SecretRecipe? recipe,
        out string error)
    {
        recipe = null;

        if (!int.TryParse(wordsText, NumberStyles.None, CultureInfo.InvariantCulture, out var words))
        {
            error = "--words needs a whole number of words";
            return false;
        }

        if (separatorText is { Length: not 1 })
        {
            error = "--separator needs a single character";
            return false;
        }

        var candidate = new PassphraseRecipe
        {
            WordCount = words,
            Separator = separatorText?[0] ?? PasswordGenerator.DefaultSeparator,
        };

        if (!candidate.TryValidate(out error))
        {
            return false;
        }

        recipe = SecretRecipe.For(candidate);
        return true;
    }
}
