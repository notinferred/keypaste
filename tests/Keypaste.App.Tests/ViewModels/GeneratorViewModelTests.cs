using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The generator choice both new-secret forms share.
/// </summary>
/// <remarks>
/// V.6 asks that the list's provenance, its size and the entropy per word be readable where
/// somebody chooses a count. On a screen that means two lines beside the control, and it means
/// they move when the count moves — a figure that only updates on reopen is a figure somebody
/// chose against.
/// </remarks>
public sealed class GeneratorViewModelTests
{
    [Fact]
    public void The_default_choice_is_the_twenty_character_password_the_app_already_generated()
    {
        var generator = new GeneratorViewModel();

        Assert.False(generator.UseWords);
        Assert.True(generator.UseCharacters);
        Assert.Equal(SecretRecipeKind.Characters, generator.Recipe?.Kind);
        Assert.Equal("20-character password", generator.Recipe?.Describe("password"));
        Assert.Null(generator.Error);
    }

    [Fact]
    public void Choosing_words_gives_a_six_word_recipe()
    {
        var generator = new GeneratorViewModel { UseWords = true };

        Assert.Equal(6, generator.WordCount);
        Assert.Equal(SecretRecipeKind.Words, generator.Recipe?.Kind);
        Assert.Equal("6-word passphrase", generator.Recipe?.Describe("password"));
        Assert.Equal(PasswordGenerator.DefaultSeparator.ToString(), generator.Separator);
    }

    [Fact]
    public void The_radio_pair_is_one_choice_read_two_ways()
    {
        var generator = new GeneratorViewModel { UseCharacters = false };

        Assert.True(generator.UseWords);

        generator.UseCharacters = true;
        Assert.False(generator.UseWords);
    }

    [Fact]
    public void The_strength_line_names_the_count_and_the_bits_and_follows_the_count()
    {
        var generator = new GeneratorViewModel { UseWords = true };

        Assert.Contains("6 words", generator.Strength, StringComparison.Ordinal);
        Assert.Contains("78 bits", generator.Strength, StringComparison.Ordinal);

        generator.WordCount = 12;

        Assert.Contains("12 words", generator.Strength, StringComparison.Ordinal);
        Assert.Contains("155 bits", generator.Strength, StringComparison.Ordinal);
    }

    /// <summary>
    /// The strength line changes announce themselves, or the screen keeps a stale figure.
    /// </summary>
    /// <remarks>
    /// The assertion above passes against a property nothing raises: the test reads the getter
    /// directly, which a binding does not. This is the half that catches it.
    /// </remarks>
    [Fact]
    public void Changing_the_count_raises_the_strength_line()
    {
        var generator = new GeneratorViewModel { UseWords = true };
        var raised = new List<string?>();
        generator.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        generator.WordCount = 10;

        Assert.Contains(nameof(GeneratorViewModel.Strength), raised);
    }

    [Fact]
    public void The_provenance_line_names_the_list_and_its_size_and_its_bits_per_word()
    {
        Assert.Contains(WordList.Provenance, GeneratorViewModel.Provenance, StringComparison.Ordinal);
        Assert.Contains("7,776", GeneratorViewModel.Provenance, StringComparison.Ordinal);
        Assert.Contains("12.9 bits", GeneratorViewModel.Provenance, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bounds_the_control_offers_are_the_generator_s_own()
    {
        Assert.Equal(PasswordGenerator.MinimumWords, GeneratorViewModel.MinimumWords);
        Assert.Equal(PasswordGenerator.MaximumWords, GeneratorViewModel.MaximumWords);
    }

    /// <summary>
    /// A refused choice offers no recipe, rather than quietly falling back to the default.
    /// </summary>
    /// <remarks>
    /// Somebody who typed four words and got six would never be told. The form stops instead, and
    /// <see cref="GeneratorViewModel.Error"/> is what it says.
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(33)]
    public void A_count_outside_the_bounds_offers_no_recipe_and_says_why(int count)
    {
        var generator = new GeneratorViewModel { UseWords = true, WordCount = count };

        Assert.Null(generator.Recipe);
        Assert.NotNull(generator.Error);
        Assert.Contains("6", generator.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("ab")]
    public void A_separator_the_words_use_or_that_is_not_one_character_offers_no_recipe(string separator)
    {
        var generator = new GeneratorViewModel { UseWords = true, Separator = separator };

        Assert.Null(generator.Recipe);
        Assert.NotNull(generator.Error);
    }

    [Fact]
    public void A_refused_word_count_leaves_the_character_choice_generating()
    {
        var generator = new GeneratorViewModel { UseWords = true, WordCount = 2 };

        Assert.Null(generator.Recipe);

        generator.UseCharacters = true;

        Assert.NotNull(generator.Recipe);
        Assert.Null(generator.Error);
    }

    /// <summary>
    /// Every property answers, always, because none of them reaches the vault.
    /// </summary>
    /// <remarks>
    /// <c>SecretHygieneTests</c> requires that no view-model property throw after a lock — a
    /// silent throw is not an empty screen. This one holds no session at all, which is the reason
    /// it passes; asserting it is what would notice the day somebody gives it one.
    /// </remarks>
    [Fact]
    public void Every_property_answers_without_a_session()
    {
        var generator = new GeneratorViewModel { UseWords = true };

        foreach (var property in typeof(GeneratorViewModel)
            .GetProperties(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static))
        {
            property.GetValue(property.GetGetMethod(nonPublic: true)!.IsStatic ? null : generator);
        }
    }
}
