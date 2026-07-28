using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The password generator.
/// </summary>
/// <remarks>
/// <para>
/// A generator is easy to test vacuously: assert the length, assert two calls differ, ship. Neither
/// notices the bug that matters, which is a draw that is not uniform — a seeded generator, an
/// off-by-one on the alphabet slice, or somebody replacing <c>GetItems</c> with an index and a
/// <c>%</c>. So <see cref="Every_character_of_a_short_alphabet_comes_up_about_as_often"/> exists,
/// and it is deliberately a wide band rather than a chi-square: it is here to catch a broken draw,
/// not to fail once a fortnight on a fair one.
/// </para>
/// <para>
/// The other claim worth stating is the negative one:
/// <see cref="Nothing_outside_the_requested_alphabet_appears"/> is what stops a recipe quietly
/// producing a character that breaks a shell, which is the whole reason
/// <see cref="PasswordGenerator.Symbols"/> is a shorter list than ASCII punctuation.
/// </para>
/// </remarks>
public sealed class PasswordGeneratorTests
{
    [Theory]
    [InlineData(PasswordGenerator.MinimumLength)]
    [InlineData(PasswordGenerator.DefaultLength)]
    [InlineData(PasswordGenerator.MaximumLength)]
    public void The_password_is_exactly_as_long_as_asked(int length)
    {
        var generated = Generate(PasswordRecipe.Default with { Length = length });

        Assert.Equal(length, generated.Length);
    }

    [Theory]
    [InlineData(PasswordAlphabet.Lowercase)]
    [InlineData(PasswordAlphabet.Digits)]
    [InlineData(PasswordAlphabet.Symbols)]
    [InlineData(PasswordAlphabet.Lowercase | PasswordAlphabet.Digits)]
    [InlineData(PasswordAlphabet.Default)]
    public void Nothing_outside_the_requested_alphabet_appears(PasswordAlphabet alphabet)
    {
        var recipe = PasswordRecipe.Default with { Alphabet = alphabet };
        var allowed = Allowed(alphabet, excludeLookalikes: false);

        for (var i = 0; i < 50; i++)
        {
            foreach (var c in Generate(recipe))
            {
                Assert.True(allowed.Contains(c, StringComparison.Ordinal), $"'{c}' is not in {alphabet}");
            }
        }
    }

    [Theory]
    [InlineData(PasswordAlphabet.Lowercase | PasswordAlphabet.Digits)]
    [InlineData(PasswordAlphabet.Uppercase | PasswordAlphabet.Symbols)]
    [InlineData(PasswordAlphabet.Default)]
    public void Every_requested_kind_of_character_is_present(PasswordAlphabet alphabet)
    {
        var recipe = PasswordRecipe.Default with { Alphabet = alphabet };

        // 500 rather than a handful: the discard-and-retry is what makes this true, and a
        // single-draw implementation passes a small sample most of the time.
        for (var i = 0; i < 500; i++)
        {
            var generated = Generate(recipe);

            foreach (var single in new[]
            {
                PasswordAlphabet.Lowercase,
                PasswordAlphabet.Uppercase,
                PasswordAlphabet.Digits,
                PasswordAlphabet.Symbols,
            })
            {
                if ((alphabet & single) == 0)
                {
                    continue;
                }

                Assert.Contains(generated, c => Allowed(single, excludeLookalikes: false).Contains(c, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Excluding_lookalikes_removes_those_five_characters_and_no_others()
    {
        var recipe = PasswordRecipe.Default with { ExcludeLookalikes = true, Length = 64 };
        var seen = new HashSet<char>();

        for (var i = 0; i < 200; i++)
        {
            foreach (var c in Generate(recipe))
            {
                seen.Add(c);
            }
        }

        foreach (var lookalike in PasswordGenerator.Lookalikes)
        {
            Assert.DoesNotContain(lookalike, seen);
        }

        // The paired half: everything else is still reachable. Without it, an implementation that
        // excluded the whole of uppercase would pass the assertion above.
        var expected = Allowed(PasswordAlphabet.Default, excludeLookalikes: true);
        foreach (var c in expected)
        {
            Assert.Contains(c, seen);
        }
    }

    /// <summary>
    /// A draw over four characters lands roughly evenly, which a biased one does not.
    /// </summary>
    [Fact]
    public void Every_character_of_a_short_alphabet_comes_up_about_as_often()
    {
        // Digits with lookalikes excluded leaves 2..9, eight characters. Narrower than the default
        // alphabet, so a bias shows up in a sample this size rather than hiding in the noise.
        var recipe = new PasswordRecipe
        {
            Length = 200,
            Alphabet = PasswordAlphabet.Digits,
            ExcludeLookalikes = true,
        };

        var counts = new Dictionary<char, int>();
        const int Draws = 100;

        for (var i = 0; i < Draws; i++)
        {
            foreach (var c in Generate(recipe))
            {
                counts[c] = counts.TryGetValue(c, out var seen) ? seen + 1 : 1;
            }
        }

        var total = Draws * recipe.Length;
        var expected = total / 8.0;

        Assert.Equal(8, counts.Count);

        foreach (var (character, count) in counts)
        {
            Assert.True(
                count > expected * 0.8 && count < expected * 1.2,
                $"'{character}' came up {count} times out of {total}, expected about {expected:F0}");
        }
    }

    [Fact]
    public void Two_passwords_are_not_the_same_password()
    {
        Assert.NotEqual(Generate(PasswordRecipe.Default), Generate(PasswordRecipe.Default));
    }

    [Theory]
    [InlineData(PasswordGenerator.MinimumLength - 1)]
    [InlineData(PasswordGenerator.MaximumLength + 1)]
    [InlineData(0)]
    public void A_length_outside_the_bounds_is_refused_by_name(int length)
    {
        var refused = (PasswordRecipe.Default with { Length = length }).TryValidate(out var error);

        Assert.False(refused);
        Assert.Contains(PasswordGenerator.MinimumLength.ToString(System.Globalization.CultureInfo.InvariantCulture), error, StringComparison.Ordinal);
        Assert.Contains(PasswordGenerator.MaximumLength.ToString(System.Globalization.CultureInfo.InvariantCulture), error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_alphabet_is_refused_by_name()
    {
        var recipe = PasswordRecipe.Default with { Alphabet = PasswordAlphabet.None };

        Assert.False(recipe.TryValidate(out var error));
        Assert.NotEmpty(error);
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Append(recipe, new SecretBuffer()));
    }

    [Fact]
    public void The_default_recipe_is_twenty_characters_of_everything()
    {
        Assert.Equal(20, PasswordRecipe.Default.Length);
        Assert.Equal(PasswordAlphabet.Default, PasswordRecipe.Default.Alphabet);
        Assert.False(PasswordRecipe.Default.ExcludeLookalikes);
        Assert.True(PasswordRecipe.Default.TryValidate(out _));
    }

    /// <summary>
    /// The default alphabet is the 85 characters the entropy claim in the remarks is counted from.
    /// </summary>
    /// <remarks>
    /// A documented "about 128 bits" is a claim, and D-0036 says a claim needs something that can
    /// hold it. Widening the symbol set later without revisiting that sentence fails here.
    /// </remarks>
    [Fact]
    public void The_default_alphabet_is_eighty_five_characters()
    {
        Assert.Equal(85, Allowed(PasswordAlphabet.Default, excludeLookalikes: false).Length);
    }

    [Fact]
    public void The_shell_hostile_punctuation_is_not_generated()
    {
        foreach (var c in " '\"`\\$&<>|")
        {
            Assert.DoesNotContain(c, PasswordGenerator.Symbols);
        }
    }

    [Fact]
    public void The_generated_password_reaches_the_buffer_and_the_scratch_space_is_zeroed()
    {
        using var buffer = new SecretBuffer();

        PasswordGenerator.Append(PasswordRecipe.Default, buffer);

        Assert.Equal(PasswordGenerator.DefaultLength, buffer.Length);

        buffer.Dispose();
        Assert.True(buffer.IsZeroed);
    }

    private static string Generate(PasswordRecipe recipe)
    {
        var destination = new char[recipe.Length];
        PasswordGenerator.Fill(recipe, destination);
        return new string(destination);
    }

    private static string Allowed(PasswordAlphabet alphabet, bool excludeLookalikes)
    {
        var everything = new StringBuilder();

        Append(PasswordAlphabet.Lowercase, "abcdefghijklmnopqrstuvwxyz");
        Append(PasswordAlphabet.Uppercase, "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        Append(PasswordAlphabet.Digits, "0123456789");
        Append(PasswordAlphabet.Symbols, PasswordGenerator.Symbols);

        return everything.ToString();

        void Append(PasswordAlphabet flag, string characters)
        {
            if ((alphabet & flag) == 0)
            {
                return;
            }

            foreach (var c in characters)
            {
                if (!excludeLookalikes || !PasswordGenerator.Lookalikes.Contains(c, StringComparison.Ordinal))
                {
                    everything.Append(c);
                }
            }
        }
    }
}
