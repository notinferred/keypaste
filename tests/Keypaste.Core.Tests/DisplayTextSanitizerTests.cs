using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The rule for text a person reads, and how it differs from the rule for names.
/// </summary>
/// <remarks>
/// Two rules in one repository is a cost, and <see cref="Both_rules_disagree_about_every_structural_character"/>
/// is what buys it: it drives the difference from both sides at once, so neither can be quietly
/// changed into the other. <see cref="EntryNameSanitizerTests"/> owns the name rule on its own terms.
/// </remarks>
public sealed class DisplayTextSanitizerTests
{
    private static readonly string _bidi = ((char)0x202E).ToString();
    private static readonly string _zwsp = ((char)0x200B).ToString();
    private static readonly string _lineSeparator = ((char)0x2028).ToString();
    private static readonly string _paragraphSeparator = ((char)0x2029).ToString();

    /// <summary>A character from the Unicode tag block, which is astral.</summary>
    private static readonly string _tag = char.ConvertFromUtf32(0xE0041);

    [Fact]
    public void A_url_survives_whole()
    {
        var result = DisplayTextSanitizer.Sanitize("https://example.test/path?a=b#c");

        Assert.Equal("https://example.test/path?a=b#c", result.Text);
        Assert.False(result.WasAltered);
    }

    [Fact]
    public void A_configuration_example_keeps_its_brackets_braces_and_pipes()
    {
        const string Raw = "[server]\n  host = {{HOST}}\n  filter = a|b\n  path = C:\\logs";

        Assert.Equal(Raw, DisplayTextSanitizer.Sanitize(Raw).Text);
    }

    [Fact]
    public void A_line_break_and_a_tab_survive()
    {
        Assert.Equal("one\n\ttwo", DisplayTextSanitizer.Sanitize("one\n\ttwo").Text);
    }

    [Fact]
    public void A_windows_line_ending_becomes_one_line_break()
    {
        var result = DisplayTextSanitizer.Sanitize("one\r\ntwo");

        Assert.Equal("one\ntwo", result.Text);
        Assert.True(result.WasAltered);
    }

    [Theory]
    [InlineData("\u0000")]
    [InlineData("\u0007")]
    [InlineData("\u001b")]
    public void An_ordinary_control_character_is_replaced(string control)
    {
        Assert.Equal("a b", DisplayTextSanitizer.Sanitize("a" + control + "b").Text);
    }

    [Fact]
    public void A_bidi_override_is_replaced()
    {
        Assert.Equal("a b", DisplayTextSanitizer.Sanitize("a" + _bidi + "b").Text);
    }

    [Fact]
    public void A_zero_width_space_is_replaced()
    {
        Assert.Equal("a b", DisplayTextSanitizer.Sanitize("a" + _zwsp + "b").Text);
    }

    /// <summary>
    /// The reason the scan walks runes. A loop over <c>char</c> sees two surrogates, neither of
    /// which is in the Format category, and lets the whole tag block through.
    /// </summary>
    [Fact]
    public void An_astral_tag_character_is_replaced()
    {
        Assert.Equal("a b", DisplayTextSanitizer.Sanitize("a" + _tag + "b").Text);
    }

    /// <summary>
    /// These two are line breaks in name only: they are not what an editor writes, and they move
    /// text about in ways a reader cannot see.
    /// </summary>
    [Fact]
    public void The_line_and_paragraph_separators_are_still_replaced()
    {
        Assert.Equal("a b c", DisplayTextSanitizer.Sanitize("a" + _lineSeparator + "b" + _paragraphSeparator + "c").Text);
    }

    [Fact]
    public void A_replacement_character_is_replaced()
    {
        Assert.Equal("a b", DisplayTextSanitizer.Sanitize("a\ufffdb").Text);
    }

    /// <summary>
    /// The argument <see cref="EntryNameSanitizer"/> makes, which carries over unchanged: deleting
    /// would put back together a word somebody split on purpose.
    /// </summary>
    [Fact]
    public void A_rejected_character_becomes_a_space_rather_than_disappearing()
    {
        Assert.NotEqual("ignore", DisplayTextSanitizer.Sanitize("ig\u0000nore").Text);
        Assert.Equal("ig nore", DisplayTextSanitizer.Sanitize("ig\u0000nore").Text);
    }

    [Fact]
    public void Consecutive_rejections_collapse_but_real_spaces_do_not()
    {
        Assert.Equal("a b", DisplayTextSanitizer.Sanitize("a" + _bidi + _zwsp + "b").Text);
        Assert.Equal("a  b", DisplayTextSanitizer.Sanitize("a  b").Text);
    }

    /// <summary>
    /// A rejected character sitting against a line break must not indent the line after it, which
    /// is what tracking "the last rune was a space" rather than "the last write was a substitution"
    /// would do to every note whose lines end in something unprintable.
    /// </summary>
    [Fact]
    public void A_rejection_beside_a_line_break_does_not_indent_the_next_line()
    {
        Assert.Equal("one \ntwo", DisplayTextSanitizer.Sanitize("one" + _zwsp + "\ntwo").Text);
    }

    [Fact]
    public void Nothing_becomes_nothing_rather_than_a_placeholder()
    {
        var result = DisplayTextSanitizer.Sanitize(string.Empty);

        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.WasAltered);
        Assert.Equal(EntryNameSanitizer.Placeholder, EntryNameSanitizer.Sanitize(string.Empty).Text);
    }

    [Fact]
    public void Text_longer_than_the_cap_is_cut_and_says_so()
    {
        var result = DisplayTextSanitizer.Sanitize(new string('a', 40), maximumLength: 8);

        Assert.Equal(8, result.Text.Length);
        Assert.True(result.WasAltered);
    }

    [Fact]
    public void A_notes_body_gets_the_larger_cap()
    {
        Assert.Equal(512, DisplayTextSanitizer.MaximumLength);
        Assert.Equal(8192, DisplayTextSanitizer.MaximumNotesLength);
    }

    /// <summary>
    /// The whole reason both rules exist, asserted from both sides so neither can drift into the
    /// other: every character the name rule calls structural is kept here, and removed there.
    /// </summary>
    [Theory]
    [InlineData('`')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('|')]
    [InlineData('\\')]
    [InlineData('/')]
    public void Both_rules_disagree_about_every_structural_character(char structural)
    {
        var raw = "a" + structural + "b";

        Assert.Equal(raw, DisplayTextSanitizer.Sanitize(raw).Text);
        Assert.Equal("a b", EntryNameSanitizer.Sanitize(raw).Text);
    }

    [Theory]
    [InlineData("\u0000")]
    [InlineData("\u001b")]
    public void Both_rules_agree_about_a_control_character(string control)
    {
        var raw = "a" + control + "b";

        Assert.Equal("a b", DisplayTextSanitizer.Sanitize(raw).Text);
        Assert.Equal("a b", EntryNameSanitizer.Sanitize(raw).Text);
    }

    [Fact]
    public void Null_and_a_non_positive_cap_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => DisplayTextSanitizer.Sanitize(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayTextSanitizer.Sanitize("a", 0));
    }
}
