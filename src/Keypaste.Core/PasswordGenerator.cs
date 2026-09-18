namespace Keypaste.Core;

/// <summary>Which kinds of character a generated password may contain.</summary>
[Flags]
public enum PasswordAlphabet
{
    /// <summary>Nothing, which is not a password anyone can generate.</summary>
    None = 0,

    /// <summary><c>a</c> to <c>z</c>.</summary>
    Lowercase = 1,

    /// <summary><c>A</c> to <c>Z</c>.</summary>
    Uppercase = 2,

    /// <summary><c>0</c> to <c>9</c>.</summary>
    Digits = 4,

    /// <summary>The punctuation in <see cref="PasswordGenerator.Symbols"/>.</summary>
    Symbols = 8,

    /// <summary>All four.</summary>
    Default = Lowercase | Uppercase | Digits | Symbols,
}

/// <summary>What to generate.</summary>
public readonly record struct PasswordRecipe
{
    /// <summary>How many characters.</summary>
    public int Length { get; init; }

    /// <summary>Which kinds of character are allowed.</summary>
    public PasswordAlphabet Alphabet { get; init; }

    /// <summary>Whether to leave out the characters that look alike.</summary>
    public bool ExcludeLookalikes { get; init; }

    /// <summary>Twenty characters from all four classes, keeping the lookalikes.</summary>
    /// <remarks>
    /// Lookalikes are kept by default because dropping them costs entropy to solve a problem —
    /// reading a password off one screen and typing it into another — that this product's copy
    /// button and <c>keypaste run</c> exist to remove. It is a flag for the times that fails.
    /// </remarks>
    public static PasswordRecipe Default { get; } = new()
    {
        Length = PasswordGenerator.DefaultLength,
        Alphabet = PasswordAlphabet.Default,
        ExcludeLookalikes = false,
    };

    /// <summary>How much entropy a password from this recipe carries.</summary>
    /// <remarks>
    /// Counted from the alphabet this recipe actually draws from, not from a number written down
    /// beside it, for the same reason <see cref="WordList.BitsPerWord"/> is derived: the default
    /// alphabet is 85 characters and 20 of them is about 128 bits (DECISIONS.md D-0036), and a
    /// widened symbol set has to move the figure rather than leave it stale. Zero for a recipe
    /// with no alphabet, which <see cref="TryValidate"/> refuses anyway.
    /// </remarks>
    public double Bits
    {
        get
        {
            var alphabet = Characters().Length;

            return alphabet == 0 ? 0 : Length * Math.Log2(alphabet);
        }
    }

    /// <summary>Whether this is a recipe that can be generated.</summary>
    /// <param name="error">A message naming the problem, or empty when the recipe is valid.</param>
    /// <returns><see langword="true"/> when the recipe is valid.</returns>
    public bool TryValidate(out string error)
    {
        if (Length < PasswordGenerator.MinimumLength || Length > PasswordGenerator.MaximumLength)
        {
            error = $"a generated password must be between {PasswordGenerator.MinimumLength} and {PasswordGenerator.MaximumLength} characters";
            return false;
        }

        if (Alphabet == PasswordAlphabet.None)
        {
            error = "a generated password needs at least one kind of character";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Every character this recipe may produce, in a stable order.</summary>
    internal string Characters()
    {
        var alphabet = new StringBuilder();

        foreach (var single in PasswordGenerator.Classes)
        {
            alphabet.Append(Characters(single));
        }

        return alphabet.ToString();
    }

    /// <summary>
    /// The characters of one class this recipe may produce, or empty when it is not selected.
    /// </summary>
    /// <param name="single">One of the four classes.</param>
    /// <returns>The characters of that class, after any lookalike exclusion.</returns>
    internal string Characters(PasswordAlphabet single)
    {
        if ((Alphabet & single) == 0)
        {
            return string.Empty;
        }

        var source = PasswordGenerator.SourceOf(single);

        if (!ExcludeLookalikes)
        {
            return source;
        }

        var kept = new StringBuilder(source.Length);

        foreach (var c in source)
        {
            if (!PasswordGenerator.Lookalikes.Contains(c, StringComparison.Ordinal))
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }
}

/// <summary>Generates passwords, and does not hand back a <see cref="string"/>.</summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no <c>string Generate()</c>.</b> A <see cref="string"/> cannot be
/// wiped, and a generator gets used behind a Regenerate button that people press until they like
/// what they see — five presses would leave five copies nothing can clear. <see cref="Fill"/> is
/// the primitive and <see cref="Append(PasswordRecipe, SecretBuffer)"/> is the convenience; a
/// caller that genuinely needs a
/// string writes <c>new string(buffer.Value)</c> on one visible line, which is what
/// <c>keypaste add</c> already does with the prompt's buffer.
/// </para>
/// <para>
/// <b>Uniformity comes from <see cref="RandomNumberGenerator"/> and not from arithmetic here.</b>
/// <c>GetItems</c> is documented as cryptographically strong and free of modulo bias. Writing the
/// rejection sampling by hand is exactly the sort of thing docs/PRODUCT.md law 3.6 says never to write, so
/// the <c>%</c> operator appears nowhere in this file outside <see cref="Symbols"/>, which is a
/// character it generates rather than an operation it performs, and this sentence is why.
/// </para>
/// </remarks>
public static class PasswordGenerator
{
    /// <summary>The shortest password this will generate.</summary>
    public const int MinimumLength = 8;

    /// <summary>The longest password this will generate.</summary>
    public const int MaximumLength = 256;

    /// <summary>How many characters a password has when nobody says otherwise.</summary>
    /// <remarks>
    /// Twenty characters over the 85-character default alphabet is log2(85) × 20, or about 128
    /// bits — a number a reader can check rather than take on trust (DECISIONS.md D-0036).
    /// </remarks>
    public const int DefaultLength = 20;

    /// <summary>The punctuation keypaste is willing to generate, and no other.</summary>
    /// <remarks>
    /// Space, <c>'</c>, <c>"</c>, backtick, <c>\</c>, <c>$</c>, <c>&amp;</c>, <c>&lt;</c>,
    /// <c>&gt;</c> and <c>|</c> are absent on purpose: they are the characters that break when a
    /// value is pasted into a shell, written into YAML, or put in a URL, and env values in
    /// particular travel through <c>keypaste env export --dotenv</c>, whose quoting already fights
    /// them. The cost is small and countable — the full alphabet is 85 characters rather than 95.
    /// </remarks>
    public const string Symbols = "!#%()*+,-./:;=?@[]^_{}~";

    /// <summary>The characters <see cref="PasswordRecipe.ExcludeLookalikes"/> removes.</summary>
    public const string Lookalikes = "Il1O0";

    /// <summary>How many draws missing a selected class are discarded before giving up.</summary>
    /// <remarks>
    /// A fail-closed backstop, not a tuning knob. At the default length the chance of even one
    /// retry is vanishing; a recipe that somehow needed a hundred is one nobody should get a
    /// password out of.
    /// </remarks>
    public const int MaximumAttempts = 100;

    /// <summary>The fewest words this will generate.</summary>
    /// <remarks>
    /// Six is the share passphrase docs/PRODUCT.md §1 ratifies and THREATS.md T-26 assumes, so the
    /// generator cannot hand out anything weaker than the figure the threat model already rests
    /// on. Six words over a 7,776-word list is log2(7776) × 6, about 78 bits — lower than the
    /// character default's 128, which is why the KDF matters and why every statement of the
    /// number says what it is (DECISIONS.md D-0236).
    /// </remarks>
    public const int MinimumWords = 6;

    /// <summary>How many words a passphrase has when nobody says otherwise.</summary>
    public const int DefaultWords = 6;

    /// <summary>The most words this will generate.</summary>
    /// <remarks>
    /// The list averages seven characters a word, so thirty-two words plus their separators is
    /// about 256 characters: <see cref="MaximumLength"/>, in the same units.
    /// </remarks>
    public const int MaximumWords = 32;

    /// <summary>What separates the words when nobody says otherwise.</summary>
    /// <remarks>
    /// Not a hyphen, which is the conventional choice: the word list holds <c>drop-down</c>,
    /// <c>felt-tip</c>, <c>t-shirt</c> and <c>yo-yo</c>, so a hyphen-separated passphrase cannot
    /// be split back into the words it was counted in (D-0239). A full stop appears in no word,
    /// and is already inside <see cref="Symbols"/>.
    /// </remarks>
    public const char DefaultSeparator = '.';

    /// <summary>The four classes, in the order they appear in a generated alphabet.</summary>
    internal static PasswordAlphabet[] Classes { get; } =
    [
        PasswordAlphabet.Lowercase,
        PasswordAlphabet.Uppercase,
        PasswordAlphabet.Digits,
        PasswordAlphabet.Symbols,
    ];

    /// <summary>Fills <paramref name="destination"/> with a password.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <param name="destination">Exactly <see cref="PasswordRecipe.Length"/> characters of space.</param>
    /// <exception cref="ArgumentException">The recipe is invalid, or the span is the wrong size.</exception>
    /// <exception cref="InvalidOperationException">No draw satisfied the recipe.</exception>
    /// <remarks>
    /// <para>
    /// <b>Every selected class appears, by discarding a draw that misses one and drawing again</b>
    /// rather than by overwriting positions or shuffling a seeded set. Both of those need an
    /// argument about the distribution they leave behind. This one does not: rejecting draws from a
    /// uniform distribution is uniform over what survives.
    /// </para>
    /// <para>
    /// <b>There is no passphrase <c>Fill</c>.</b> A word count and a separator do not determine a
    /// character count — the list's words run from three to nine letters — so a caller cannot size
    /// the span before the draw, and the exact-size contract above could not be honoured.
    /// <see cref="Append(PassphraseRecipe, SecretBuffer)"/> is the primitive on that side.
    /// </para>
    /// </remarks>
    public static void Fill(PasswordRecipe recipe, Span<char> destination)
    {
        if (!recipe.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(recipe));
        }

        if (destination.Length != recipe.Length)
        {
            throw new ArgumentException(
                $"expected {recipe.Length} characters of space, got {destination.Length}",
                nameof(destination));
        }

        var alphabet = recipe.Characters();

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            RandomNumberGenerator.GetItems<char>(alphabet, destination);

            if (HasEveryClass(recipe, destination))
            {
                return;
            }
        }

        destination.Clear();
        throw new InvalidOperationException(
            $"could not generate a password holding every requested kind of character in {MaximumAttempts} attempts");
    }

    /// <summary>Appends a freshly generated password to <paramref name="buffer"/>.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <param name="buffer">The buffer to append to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    /// <exception cref="InvalidOperationException">No draw satisfied the recipe.</exception>
    public static void Append(PasswordRecipe recipe, SecretBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var generated = new char[recipe.Length];

        try
        {
            Fill(recipe, generated);
            buffer.Append(generated);
        }
        finally
        {
            Array.Clear(generated);
        }
    }

    /// <summary>Appends a freshly generated passphrase to <paramref name="buffer"/>.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <param name="buffer">The buffer to append to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    /// <remarks>
    /// <para>
    /// <b>The draw is the one the character generator makes, with <c>T</c> as
    /// <see cref="string"/>.</b> <c>GetItems</c> is generic and documented as free of modulo bias,
    /// so law 3.6 is satisfied here for the same reason and by the same call: no index arithmetic
    /// of ours stands between the entropy source and the word.
    /// </para>
    /// <para>
    /// The words go straight into the caller's buffer with the separator between them, so the
    /// passphrase never exists as a <see cref="string"/> on the way. The drawn words themselves
    /// are references into <see cref="WordList"/> and are not copies to clear; what identifies the
    /// secret is which of them were chosen, and that array is released with the frame.
    /// </para>
    /// </remarks>
    public static void Append(PassphraseRecipe recipe, SecretBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (!recipe.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(recipe));
        }

        var drawn = new string[recipe.WordCount];
        RandomNumberGenerator.GetItems(WordList.Words, drawn.AsSpan());

        for (var i = 0; i < drawn.Length; i++)
        {
            if (i > 0)
            {
                buffer.Append(recipe.Separator);
            }

            buffer.Append(drawn[i]);
        }
    }

    /// <summary>Appends a freshly generated secret of either kind to <paramref name="buffer"/>.</summary>
    /// <param name="recipe">What to generate.</param>
    /// <param name="buffer">The buffer to append to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    public static void Append(SecretRecipe recipe, SecretBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        recipe.Append(buffer);
    }

    /// <summary>Every character of one class, before any exclusion.</summary>
    internal static string SourceOf(PasswordAlphabet single) => single switch
    {
        PasswordAlphabet.Lowercase => "abcdefghijklmnopqrstuvwxyz",
        PasswordAlphabet.Uppercase => "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        PasswordAlphabet.Digits => "0123456789",
        PasswordAlphabet.Symbols => Symbols,
        _ => string.Empty,
    };

    private static bool HasEveryClass(PasswordRecipe recipe, ReadOnlySpan<char> candidate)
    {
        foreach (var single in Classes)
        {
            var wanted = recipe.Characters(single);

            if (wanted.Length > 0 && candidate.IndexOfAny(wanted) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
