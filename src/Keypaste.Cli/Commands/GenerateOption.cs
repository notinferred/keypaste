using System.Globalization;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>
/// The <c>--generate</c> flags, shared by the two verbs that store a secret.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no <c>keypaste gen</c> verb, and that is a decision rather than an omission.</b>
/// <c>keypaste get</c> requires <c>--show</c> before it will put a password on stdout, because
/// stdout is shell history, scrollback and CI logs. A verb whose entire output is a fresh secret on
/// stdout inverts the rule the rest of this CLI is built around. Generating where the value is
/// already being stored needs no such exception: the password goes into the vault and the only
/// thing printed is how long it was.
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
    ];

    /// <summary>What to add to a verb's usage line.</summary>
    internal const string Usage = "[--generate] [--length N] [--no-symbols] [--no-lookalikes]";

    /// <summary>Reads the generator flags.</summary>
    /// <param name="line">The parsed command line.</param>
    /// <param name="recipe">What to generate, or null when <c>--generate</c> was not given.</param>
    /// <param name="error">A message naming the problem, or empty.</param>
    /// <returns><see langword="false"/> on a usage mistake.</returns>
    /// <remarks>
    /// A shaping flag without <c>--generate</c> is an error rather than a no-op. Somebody typing
    /// <c>--length 32</c> and getting a prompt has been ignored, not helped.
    /// </remarks>
    internal static bool TryRead(CommandLine line, out PasswordRecipe? recipe, out string error)
    {
        var lengthText = line.Value("length");
        var shaped = lengthText is not null
            || line.HasFlag("no-symbols")
            || line.HasFlag("no-lookalikes");

        if (!line.HasFlag("generate"))
        {
            recipe = null;

            if (shaped)
            {
                error = "--length, --no-symbols and --no-lookalikes only mean something with --generate";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var length = PasswordGenerator.DefaultLength;
        if (lengthText is not null
            && !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length))
        {
            recipe = null;
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
            recipe = null;
            return false;
        }

        recipe = candidate;
        return true;
    }

    /// <summary>
    /// Generates a password into a buffer the caller owns and disposes.
    /// </summary>
    /// <param name="recipe">What to generate.</param>
    /// <returns>The buffer holding it.</returns>
    /// <remarks>
    /// Handed back in a <see cref="SecretBuffer"/> rather than a string so the caller's
    /// <c>using</c> zeroes it, exactly as it would a value read from the prompt. The one
    /// unavoidable <see cref="string"/> is made at the call site, where it is visible.
    /// </remarks>
    internal static SecretBuffer Generate(PasswordRecipe recipe)
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
}
