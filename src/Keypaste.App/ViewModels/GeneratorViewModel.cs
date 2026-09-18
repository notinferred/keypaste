using System.Globalization;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The choice between a character password and a word-list passphrase, and what it costs.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the new-entry form and the new-variable form rather than written twice: the drift
/// worth preventing is two screens that disagree about what the generator does, and that lives in
/// the recipe, not in the layout (DECISIONS.md D-0238). Both views keep their own markup, because
/// <c>Controls/</c> holds no <c>.axaml</c> control and this feature is not the place to invent the
/// first one.
/// </para>
/// <para>
/// <b>It holds no secret.</b> A recipe is a length, a word count and a separator, so this survives
/// a lock with nothing to clear and nothing to refuse. That is also why <see cref="Strength"/> and
/// <see cref="Provenance"/> can be plain strings: they describe the list, not a value drawn
/// from it.
/// </para>
/// <para>
/// <b>Both lines are built from the core's figures</b> — <see cref="WordList.Count"/> and
/// <see cref="WordList.BitsPerWord"/>, which are derived from the vendored file — so the screen
/// cannot state a number the list contradicts. V.6 asks for the provenance, the size and the
/// entropy to be readable where somebody chooses a count, and <see cref="Strength"/> is
/// recomputed on every change to <see cref="WordCount"/> for exactly that reason.
/// </para>
/// </remarks>
internal sealed class GeneratorViewModel : ObservableObject
{
    private bool _useWords;
    private int _wordCount = PasswordGenerator.DefaultWords;
    private string _separator = PasswordGenerator.DefaultSeparator.ToString();

    /// <summary>Whether to draw words from the list rather than characters from an alphabet.</summary>
    internal bool UseWords
    {
        get => _useWords;
        set
        {
            if (Set(ref _useWords, value))
            {
                Raise(nameof(UseCharacters));
                Raise(nameof(Strength));
                Raise(nameof(Error));
            }
        }
    }

    /// <summary>The other half of the pair, so one binding can drive two radio buttons.</summary>
    internal bool UseCharacters
    {
        get => !_useWords;
        set => UseWords = !value;
    }

    /// <summary>How many words a passphrase gets.</summary>
    internal int WordCount
    {
        get => _wordCount;
        set
        {
            if (Set(ref _wordCount, value))
            {
                Raise(nameof(Strength));
                Raise(nameof(Error));
            }
        }
    }

    /// <summary>What goes between the words, as the box holds it.</summary>
    internal string Separator
    {
        get => _separator;
        set
        {
            if (Set(ref _separator, value ?? string.Empty))
            {
                Raise(nameof(Error));
            }
        }
    }

    /// <summary>The fewest words the generator will produce.</summary>
    internal static int MinimumWords => PasswordGenerator.MinimumWords;

    /// <summary>The most words the generator will produce.</summary>
    internal static int MaximumWords => PasswordGenerator.MaximumWords;

    /// <summary>Where the words come from, and how much each one is worth.</summary>
    internal static string Provenance => string.Format(
        CultureInfo.CurrentCulture,
        "{0} — {1:N0} words, {2:F1} bits each",
        WordList.Provenance,
        WordList.Count,
        WordList.BitsPerWord);

    /// <summary>What the current choice is worth, for somebody deciding on a count.</summary>
    internal string Strength => UseWords
        ? string.Format(
            CultureInfo.CurrentCulture,
            "{0} words — about {1:F0} bits",
            WordCount,
            new PassphraseRecipe
            {
                WordCount = WordCount,
                Separator = PasswordGenerator.DefaultSeparator,
            }.Bits)
        : string.Format(
            CultureInfo.CurrentCulture,
            "{0} characters — about {1:F0} bits",
            PasswordRecipe.Default.Length,
            PasswordRecipe.Default.Bits);

    /// <summary>Why the current choice cannot be generated, or null when it can.</summary>
    internal string? Error => Recipe is null && Refusal() is { Length: > 0 } refusal ? refusal : null;

    /// <summary>What to generate, or null when the choice on screen is not a recipe.</summary>
    /// <remarks>
    /// Null rather than a fallback: a count or a separator the core refuses must stop the form,
    /// not quietly become the default, or somebody who asked for four words gets six and is never
    /// told.
    /// </remarks>
    internal SecretRecipe? Recipe
    {
        get
        {
            if (!UseWords)
            {
                return SecretRecipe.For(PasswordRecipe.Default);
            }

            if (_separator.Length != 1)
            {
                return null;
            }

            var candidate = new PassphraseRecipe { WordCount = WordCount, Separator = _separator[0] };

            return candidate.TryValidate(out _) ? SecretRecipe.For(candidate) : null;
        }
    }

    private string Refusal()
    {
        if (!UseWords)
        {
            return string.Empty;
        }

        if (_separator.Length != 1)
        {
            return "Choose a single character to separate the words.";
        }

        return new PassphraseRecipe { WordCount = WordCount, Separator = _separator[0] }
            .TryValidate(out var error)
            ? string.Empty
            : error;
    }
}
