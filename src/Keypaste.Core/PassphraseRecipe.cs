namespace Keypaste.Core;

/// <summary>What to generate: characters from an alphabet, or words from a list.</summary>
public enum SecretRecipeKind
{
    /// <summary>A <see cref="PasswordRecipe"/>: characters drawn from an alphabet.</summary>
    Characters,

    /// <summary>A <see cref="PassphraseRecipe"/>: words drawn from <see cref="WordList"/>.</summary>
    Words,
}

/// <summary>What passphrase to generate.</summary>
/// <remarks>
/// <para>
/// A second type rather than a kind field on <see cref="PasswordRecipe"/>. One type carrying
/// <c>Length</c>, <c>Alphabet</c>, <c>ExcludeLookalikes</c>, a word count and a separator admits
/// states with no meaning — words with an alphabet, characters with a word count — so its
/// validator would have to either ignore three fields or refuse
/// <c>PasswordRecipe.Default with { … }</c>, which is how every call site here builds a recipe
/// (DECISIONS.md D-0234).
/// </para>
/// <para>
/// <see cref="Separator"/> is a <see cref="char"/> and not a <see cref="string"/> so that a
/// default-constructed recipe is invalid rather than null: <c>default</c> carries <c>'\0'</c> and
/// no word count, and <see cref="TryValidate"/> refuses both.
/// </para>
/// </remarks>
public readonly record struct PassphraseRecipe
{
    /// <summary>How many words.</summary>
    public int WordCount { get; init; }

    /// <summary>The character written between the words.</summary>
    public char Separator { get; init; }

    /// <summary>Six words separated by a full stop.</summary>
    /// <remarks>
    /// Six because that is the share passphrase docs/PRODUCT.md §1 ratifies, so nothing can be
    /// generated weaker than what THREATS.md T-26 assumes. A full stop rather than a hyphen
    /// because the word list holds four hyphenated words, so a hyphen would make the passphrase
    /// unsplittable and its word count unreadable (D-0239).
    /// </remarks>
    public static PassphraseRecipe Default { get; } = new()
    {
        WordCount = PasswordGenerator.DefaultWords,
        Separator = PasswordGenerator.DefaultSeparator,
    };

    /// <summary>How much entropy a passphrase from this recipe carries.</summary>
    public double Bits => WordCount * WordList.BitsPerWord;

    /// <summary>Whether this is a recipe that can be generated.</summary>
    /// <param name="error">A message naming the problem, or empty when the recipe is valid.</param>
    /// <returns><see langword="true"/> when the recipe is valid.</returns>
    /// <remarks>
    /// The separator refusal asks <see cref="WordList.Contains"/> rather than comparing against a
    /// written-down set: a character the list itself uses cannot separate its words, and a list
    /// re-vendored with different punctuation has to narrow the rule by itself.
    /// </remarks>
    public bool TryValidate(out string error)
    {
        if (WordCount < PasswordGenerator.MinimumWords || WordCount > PasswordGenerator.MaximumWords)
        {
            error = $"a generated passphrase must be between {PasswordGenerator.MinimumWords} and {PasswordGenerator.MaximumWords} words";
            return false;
        }

        if (Separator == '\0' || char.IsControl(Separator) || char.IsWhiteSpace(Separator))
        {
            error = "a passphrase separator must be a visible character";
            return false;
        }

        if (WordList.Contains(Separator))
        {
            error = $"'{Separator}' appears inside the words themselves, so it cannot separate them";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

/// <summary>Either kind of recipe, and which kind it is.</summary>
/// <remarks>
/// <para>
/// This is what docs/STEPS.md V.6 means by the recipe recording which kind produced a value.
/// Nothing in keypaste persists a recipe — a <see cref="VaultEntry"/> has no field for one — so
/// the kind travels with the recipe to the surface that stores the value, which is what lets
/// <c>keypaste add</c> say it generated a six-word passphrase rather than a password.
/// </para>
/// <para>
/// The two payloads are private on purpose. Nothing needs them back: a caller either holds the
/// concrete recipe it built or wants <see cref="Describe"/>, and keeping them unreachable is what
/// makes "a words recipe carrying an alphabet" unrepresentable rather than merely unread.
/// </para>
/// </remarks>
public readonly record struct SecretRecipe
{
    private readonly PasswordRecipe _password;
    private readonly PassphraseRecipe _passphrase;

    private SecretRecipe(SecretRecipeKind kind, PasswordRecipe password, PassphraseRecipe passphrase)
    {
        Kind = kind;
        _password = password;
        _passphrase = passphrase;
    }

    /// <summary>Which kind of secret this recipe makes.</summary>
    public SecretRecipeKind Kind { get; }

    /// <summary>Twenty characters from all four classes.</summary>
    public static SecretRecipe Default { get; } = For(PasswordRecipe.Default);

    /// <summary>Wraps a character recipe.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <returns>The recipe, as either kind.</returns>
    /// <remarks>
    /// A method rather than an implicit conversion: CA2225 wants a named alternative anyway, and a
    /// silent conversion between two recipe kinds is exactly the confusion the two types exist to
    /// prevent.
    /// </remarks>
    public static SecretRecipe For(PasswordRecipe recipe) =>
        new(SecretRecipeKind.Characters, recipe, default);

    /// <summary>Wraps a passphrase recipe.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <returns>The recipe, as either kind.</returns>
    public static SecretRecipe For(PassphraseRecipe recipe) =>
        new(SecretRecipeKind.Words, default, recipe);

    /// <summary>Whether this is a recipe that can be generated.</summary>
    /// <param name="error">A message naming the problem, or empty when the recipe is valid.</param>
    /// <returns><see langword="true"/> when the recipe is valid.</returns>
    public bool TryValidate(out string error) => Kind switch
    {
        SecretRecipeKind.Words => _passphrase.TryValidate(out error),
        _ => _password.TryValidate(out error),
    };

    /// <summary>What this recipe makes, for a line a person reads.</summary>
    /// <param name="characterNoun">What a character secret is called here: a password, a value.</param>
    /// <returns>A phrase such as <c>20-character password</c> or <c>6-word passphrase</c>.</returns>
    /// <remarks>
    /// The noun is a parameter because the two verbs store different things: <c>keypaste add</c>
    /// stores a password and <c>keypaste env set</c> stores a variable's value, and calling an
    /// environment variable a password would be wrong. A passphrase is a passphrase either way.
    /// </remarks>
    public string Describe(string characterNoun) => Kind switch
    {
        SecretRecipeKind.Words => $"{_passphrase.WordCount}-word passphrase",
        _ => $"{_password.Length}-character {characterNoun}",
    };

    /// <summary>Appends a freshly generated secret of this kind to <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    internal void Append(SecretBuffer buffer)
    {
        switch (Kind)
        {
            case SecretRecipeKind.Words:
                PasswordGenerator.Append(_passphrase, buffer);
                break;
            default:
                PasswordGenerator.Append(_password, buffer);
                break;
        }
    }
}
