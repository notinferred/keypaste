using System.Globalization;
using Keypaste.App.Resources;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The choice between a character password and a word-list passphrase, and what it costs.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the new-entry form and the new-variable form rather than written twice: the drift
/// worth preventing is two screens that disagree about what the generator does, and that lives in
/// the recipe, not in the layout. Both views keep their own markup, because
/// <c>Controls/</c> holds no <c>.axaml</c> control and this feature is not the place to invent the
/// first one.
/// </para>
/// <para>
/// <b>It holds no secret.</b> A recipe is a length, a word count and a separator, so this survives
/// a lock with nothing to clear and nothing to refuse. That is also why <see cref="Strength"/> and
/// <see cref="StrengthTip"/> can be plain strings: they describe the recipe, not a value drawn
/// from it.
/// </para>
/// <para>
/// <b>The tooltip is built from the core's figures</b>, the recipe's bits and <see cref="WordList.Count"/>, so the screen cannot state a
/// number the list contradicts, and both are recomputed on every change to a choice.
/// </para>
/// </remarks>
internal sealed partial class GeneratorViewModel : ObservableObject
{
    private string _separator = PasswordGenerator.DefaultSeparator.ToString();

    internal GeneratorViewModel()
    {
    }

    /// <summary>Takes every choice <paramref name="other"/> holds, as Apply carries a generator's choices back to its form.</summary>
    internal void TakeChoicesFrom(GeneratorViewModel other)
    {
        ArgumentNullException.ThrowIfNull(other);

        UseWords = other.UseWords;
        Length = other.Length;
        Letters = other.Letters;
        Digits = other.Digits;
        Symbols = other.Symbols;
        LeaveOutLookalikes = other.LeaveOutLookalikes;
        WordCount = other.WordCount;
        Separator = other.Separator;
    }

    /// <summary>Whether to draw words from the list rather than characters from an alphabet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseCharacters), nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial bool UseWords { get; set; }

    /// <summary>The other half of the pair, so one binding can drive two radio buttons.</summary>
    internal bool UseCharacters
    {
        get => !UseWords;
        set => UseWords = !value;
    }

    /// <summary>How many characters a password gets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial int Length { get; set; } = PasswordGenerator.DefaultLength;

    /// <summary>Whether lowercase and uppercase letters may appear.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial bool Letters { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial bool Digits { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial bool Symbols { get; set; } = true;

    /// <summary>Whether to leave out the characters that look alike, such as 0 and O.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial bool LeaveOutLookalikes { get; set; }

    /// <summary>The fewest characters the generator will produce.</summary>
    internal static int MinimumLength => PasswordGenerator.MinimumLength;

    /// <summary>The most characters the generator will produce.</summary>
    internal static int MaximumLength => PasswordGenerator.MaximumLength;

    /// <summary>How many words a passphrase gets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength), nameof(StrengthTip), nameof(Error))]
    internal partial int WordCount { get; set; } = PasswordGenerator.DefaultWords;

    /// <summary>What goes between the words, as the box holds it.</summary>
    internal string Separator
    {
        get => _separator;
        set
        {
            if (SetProperty(ref _separator, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Error));
            }
        }
    }

    /// <summary>The fewest words the generator will produce.</summary>
    internal static int MinimumWords => PasswordGenerator.MinimumWords;

    /// <summary>The most words the generator will produce.</summary>
    internal static int MaximumWords => PasswordGenerator.MaximumWords;

    /// <summary>The bits a guesser faces, as the generator draws them; a strength word above, the figure in its tooltip.</summary>
    private double Bits => UseWords
        ? new PassphraseRecipe { WordCount = WordCount, Separator = PasswordGenerator.DefaultSeparator }.Bits
        : CharacterRecipe.Bits;

    /// <summary>How strong the current choice is, in one word.</summary>
    internal string Strength => Bits switch
    {
        >= StrongBits => Strings.StrengthStrong,
        >= FairBits => Strings.StrengthFair,
        _ => Strings.StrengthWeak,
    };

    /// <summary>The strength word's tooltip: its bits, and for words the list they come from.</summary>
    internal string StrengthTip => UseWords
        ? string.Create(CultureInfo.CurrentCulture, $"About {Bits:F0} bits, from {WordList.Provenance} of {WordList.Count:N0} words")
        : string.Create(CultureInfo.CurrentCulture, $"About {Bits:F0} bits");

    /// <summary>At least this many bits is strong, which the generator's six-word and 20-character defaults clear.</summary>
    internal const double StrongBits = 72;

    /// <summary>At least this many bits is fair, under it weak.</summary>
    internal const double FairBits = 50;

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
                return CharacterRecipe.TryValidate(out _) ? SecretRecipe.For(CharacterRecipe) : null;
            }

            if (_separator.Length != 1)
            {
                return null;
            }

            var candidate = new PassphraseRecipe { WordCount = WordCount, Separator = _separator[0] };

            return candidate.TryValidate(out _) ? SecretRecipe.For(candidate) : null;
        }
    }

    private PasswordRecipe CharacterRecipe => new()
    {
        Length = Length,
        Alphabet = (Letters ? PasswordAlphabet.Lowercase | PasswordAlphabet.Uppercase : PasswordAlphabet.None)
            | (Digits ? PasswordAlphabet.Digits : PasswordAlphabet.None)
            | (Symbols ? PasswordAlphabet.Symbols : PasswordAlphabet.None),
        ExcludeLookalikes = LeaveOutLookalikes,
    };

    private string Refusal()
    {
        if (!UseWords)
        {
            return CharacterRecipe.TryValidate(out var refusal) ? string.Empty : refusal;
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
