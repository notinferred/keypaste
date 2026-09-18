using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Word-list passphrase generation.
/// </summary>
/// <remarks>
/// <para>
/// The claims worth testing are the ones a plausible wrong implementation would break. Counting
/// the words by splitting on the separator is the load-bearing one: it is how the entropy figure
/// is read back, and it fails against a generator that drew five words and a stray separator, or
/// one whose separator appears inside a word.
/// </para>
/// <para>
/// Like <see cref="PasswordGeneratorTests"/>, this re-derives its expectations from the vendored
/// file rather than from <see cref="WordList.Words"/>, so the test is an oracle rather than an
/// echo.
/// </para>
/// <para>
/// <b>Why V.6 adds no KeePassXC gate, which is a claim and so belongs somewhere checkable.</b> A
/// passphrase is stored as an ordinary KDBX Protected String through the same
/// <c>Vault.AddEntry</c> or <c>EnvStore.TrySet</c> then <c>Vault.Save</c> path the character
/// generator already uses, and the only characters it introduces are ASCII lowercase letters, the
/// hyphen four of the list's words are spelled with, and a separator — all of which are inside
/// <see cref="PasswordGenerator.Symbols"/> and already round-tripped by
/// <c>scripts/verify-keepassxc-writeback.sh</c>. The day a passphrase can hold a character that
/// gate has never written, that sentence stops being true and this suite is where it is written
/// down; <see cref="WordListTests.Every_word_is_three_to_nine_characters_of_lowercase_letters_and_hyphens"/>
/// is what would fail first.
/// </para>
/// </remarks>
public sealed class PassphraseGeneratorTests
{
    [Fact]
    public void Every_word_of_a_passphrase_comes_from_the_vendored_list()
    {
        var known = VendoredWordList.Words().ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < 200; i++)
        {
            foreach (var word in Words(PassphraseRecipe.Default))
            {
                Assert.Contains(word, known);
            }
        }
    }

    /// <summary>
    /// The list is drawn from, not sampled from a corner of.
    /// </summary>
    /// <remarks>
    /// The paired half of the test above, which a generator returning the same word every time
    /// would satisfy forever. Twenty thousand draws over 7,776 words leaves the expected number of
    /// distinct words near 7,200, so six thousand is a floor a working draw clears easily and a
    /// stuck or narrowed one cannot.
    /// </remarks>
    [Fact]
    public void Most_of_the_list_is_reachable_across_many_draws()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var recipe = PassphraseRecipe.Default with { WordCount = 20 };

        for (var i = 0; i < 1_000; i++)
        {
            foreach (var word in Words(recipe))
            {
                seen.Add(word);
            }
        }

        Assert.True(seen.Count > 6_000, $"only {seen.Count} distinct words came up in 20,000 draws");
    }

    [Theory]
    [InlineData(PasswordGenerator.MinimumWords, '.')]
    [InlineData(8, '_')]
    [InlineData(12, '+')]
    [InlineData(PasswordGenerator.MaximumWords, '~')]
    public void The_passphrase_has_the_requested_count_and_separator(int count, char separator)
    {
        var recipe = new PassphraseRecipe { WordCount = count, Separator = separator };

        var rendered = Render(recipe);

        Assert.Equal(count, rendered.Split(separator).Length);
        Assert.DoesNotContain(PasswordGenerator.DefaultSeparator, rendered.Replace(separator, 'x'));
    }

    /// <summary>
    /// A separator never lands inside a word, so the word count can be read back.
    /// </summary>
    /// <remarks>
    /// The count assertion above is only as good as this: five words and a trailing separator
    /// split into six pieces too. Every piece being a word from the list is what makes "six words"
    /// mean six words.
    /// </remarks>
    [Fact]
    public void Splitting_on_the_separator_yields_whole_words_and_no_empty_piece()
    {
        var known = VendoredWordList.Words().ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < 100; i++)
        {
            var pieces = Render(PassphraseRecipe.Default).Split(PasswordGenerator.DefaultSeparator);

            Assert.Equal(PasswordGenerator.DefaultWords, pieces.Length);

            foreach (var piece in pieces)
            {
                Assert.Contains(piece, known);
            }
        }
    }

    [Theory]
    [InlineData(PasswordGenerator.MinimumWords - 1)]
    [InlineData(PasswordGenerator.MaximumWords + 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_word_count_outside_the_bounds_is_refused_by_name(int count)
    {
        var recipe = PassphraseRecipe.Default with { WordCount = count };

        Assert.False(recipe.TryValidate(out var error));
        Assert.Contains(
            PasswordGenerator.MinimumWords.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            PasswordGenerator.MaximumWords.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error,
            StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Append(recipe, new SecretBuffer()));
    }

    /// <summary>
    /// A character the words themselves use cannot separate them.
    /// </summary>
    /// <remarks>
    /// A hyphen is the conventional diceware separator and this list makes it wrong: it holds
    /// <c>drop-down</c>, <c>felt-tip</c>, <c>t-shirt</c> and <c>yo-yo</c>, so a hyphen-joined
    /// passphrase does not split into the words it was counted in. The rule asks the list rather
    /// than a written-down set, so a re-vendored list narrows it by itself (D-0239).
    /// </remarks>
    [Theory]
    [InlineData('-')]
    [InlineData('a')]
    [InlineData('z')]
    public void A_separator_the_words_themselves_use_is_refused_by_name(char separator)
    {
        var recipe = PassphraseRecipe.Default with { Separator = separator };

        Assert.False(recipe.TryValidate(out var error));
        Assert.Contains(separator, error);
    }

    [Theory]
    [InlineData('\0')]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    public void An_invisible_separator_is_refused_by_name(char separator)
    {
        var recipe = PassphraseRecipe.Default with { Separator = separator };

        Assert.False(recipe.TryValidate(out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_default_constructed_recipe_is_not_a_recipe()
    {
        Assert.False(default(PassphraseRecipe).TryValidate(out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void The_default_recipe_is_six_words_joined_by_a_full_stop()
    {
        Assert.Equal(6, PassphraseRecipe.Default.WordCount);
        Assert.Equal('.', PassphraseRecipe.Default.Separator);
        Assert.True(PassphraseRecipe.Default.TryValidate(out _));
        Assert.False(WordList.Contains(PassphraseRecipe.Default.Separator));
    }

    /// <summary>
    /// The entropy figure is the word count times the list's bits, and nothing else.
    /// </summary>
    /// <remarks>
    /// D-0036 again: PRODUCT §1 and THREATS T-26 rest on six words being about 78 bits, so the
    /// arithmetic is asserted rather than described. The band is what fails if the minimum moves
    /// without the documents that quote it moving too.
    /// </remarks>
    [Fact]
    public void Six_words_is_about_seventy_eight_bits()
    {
        Assert.Equal(6 * WordList.BitsPerWord, PassphraseRecipe.Default.Bits);
        Assert.InRange(PassphraseRecipe.Default.Bits, 77.5, 77.6);
    }

    [Fact]
    public void No_two_passphrases_in_two_thousand_draws_are_the_same()
    {
        Assert.True(AllDistinct(() => Render(PassphraseRecipe.Default), 2_000));
    }

    /// <summary>
    /// The distinctness check would notice a generator that repeated itself.
    /// </summary>
    /// <remarks>
    /// The same helper over a generator that returns one value. Without this, the test above is
    /// satisfied by a <c>HashSet</c> assertion nobody has ever seen fail.
    /// </remarks>
    [Fact]
    public void The_distinctness_check_fails_a_generator_that_repeats()
    {
        Assert.False(AllDistinct(() => "always.the.same.six.word.answer", 2_000));
    }

    [Fact]
    public void The_generated_passphrase_reaches_the_buffer_and_the_buffer_is_zeroed()
    {
        using var buffer = new SecretBuffer();

        PasswordGenerator.Append(PassphraseRecipe.Default, buffer);

        Assert.Equal(PasswordGenerator.DefaultWords, new string(buffer.Value).Split('.').Length);

        buffer.Dispose();
        Assert.True(buffer.IsZeroed);
    }

    /// <summary>
    /// A recipe says which kind it is, which is how a surface can report what it stored.
    /// </summary>
    /// <remarks>
    /// The characters half is pinned as well as the words half: <c>keypaste add --generate</c>
    /// already prints "20-character password", and that line now comes from
    /// <see cref="SecretRecipe.Describe"/>.
    /// </remarks>
    [Fact]
    public void A_words_recipe_and_a_characters_recipe_describe_themselves_differently()
    {
        Assert.Equal(SecretRecipeKind.Words, SecretRecipe.For(PassphraseRecipe.Default).Kind);
        Assert.Equal("6-word passphrase", SecretRecipe.For(PassphraseRecipe.Default).Describe("password"));

        Assert.Equal(SecretRecipeKind.Characters, SecretRecipe.For(PasswordRecipe.Default).Kind);
        Assert.Equal("20-character password", SecretRecipe.For(PasswordRecipe.Default).Describe("password"));
        Assert.Equal(SecretRecipeKind.Characters, SecretRecipe.Default.Kind);
    }

    [Fact]
    public void Either_kind_of_recipe_generates_through_the_one_call()
    {
        using var words = new SecretBuffer();
        using var characters = new SecretBuffer();

        PasswordGenerator.Append(SecretRecipe.For(PassphraseRecipe.Default), words);
        PasswordGenerator.Append(SecretRecipe.For(PasswordRecipe.Default), characters);

        Assert.Equal(PasswordGenerator.DefaultWords, new string(words.Value).Split('.').Length);
        Assert.Equal(PasswordGenerator.DefaultLength, characters.Length);
    }

    [Fact]
    public void An_invalid_recipe_of_either_kind_is_refused_by_the_union()
    {
        Assert.False(SecretRecipe.For(PassphraseRecipe.Default with { WordCount = 1 }).TryValidate(out var words));
        Assert.NotEmpty(words);

        Assert.False(SecretRecipe.For(PasswordRecipe.Default with { Length = 1 }).TryValidate(out var characters));
        Assert.NotEmpty(characters);
    }

    private static bool AllDistinct(Func<string> draw, int draws)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < draws; i++)
        {
            if (!seen.Add(draw()))
            {
                return false;
            }
        }

        return true;
    }

    private static string Render(PassphraseRecipe recipe)
    {
        using var buffer = new SecretBuffer();
        PasswordGenerator.Append(recipe, buffer);
        return new string(buffer.Value);
    }

    private static string[] Words(PassphraseRecipe recipe) =>
        Render(recipe).Split(recipe.Separator);
}
