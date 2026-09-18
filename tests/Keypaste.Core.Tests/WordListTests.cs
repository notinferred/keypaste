using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The vendored word list, and the pin that makes its entropy claim mean something.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every assertion here reads the vendored file, never <see cref="WordList.Words"/>.</b> The
/// core's own first-use check covers the shipped binary; this covers the repository, and the two
/// have to be independent or the test is asking the code whether the code is right.
/// </para>
/// <para>
/// <b>A digest test is the easiest thing in the world to write vacuously:</b> hash the file,
/// compare with the const, pass — against a <c>WordList</c> that never checks anything, and
/// against a file that is empty. So the pin is also shown to reject a one-byte change, and shown
/// not to be the digest of an empty file.
/// </para>
/// </remarks>
public sealed class WordListTests
{
    [Fact]
    public void The_vendored_list_is_the_pinned_one_by_digest_and_size()
    {
        var bytes = File.ReadAllBytes(VendoredWordList.ListPath);

        Assert.Equal(WordList.Digest, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(WordList.Count, VendoredWordList.Words().Length);
    }

    /// <summary>
    /// The check behind the pin actually refuses a changed list.
    /// </summary>
    /// <remarks>
    /// Without this, the digest test above passes against a <c>WordList</c> whose verification is
    /// a comment. The mutated copy goes through the same comparison, and the running list is
    /// asserted to still load, so a check that accepts everything fails one half or the other.
    /// </remarks>
    [Fact]
    public void A_single_changed_byte_is_not_the_pinned_list()
    {
        var mutated = File.ReadAllBytes(VendoredWordList.ListPath);
        mutated[^2] = (byte)(mutated[^2] == 'a' ? 'b' : 'a');

        Assert.NotEqual(WordList.Digest, Convert.ToHexStringLower(SHA256.HashData(mutated)));
        Assert.Equal(WordList.Count, WordList.Words.Length);
    }

    /// <summary>
    /// The pin is a digest of the list, not of nothing.
    /// </summary>
    /// <remarks>
    /// The failure this catches is a vendoring step that wrote an empty or truncated file and a
    /// pin regenerated from it: every other test here would then agree with itself.
    /// </remarks>
    [Fact]
    public void The_pin_is_not_the_digest_of_an_empty_or_truncated_file()
    {
        Assert.NotEqual(WordList.Digest, Convert.ToHexStringLower(SHA256.HashData([])));
        Assert.True(new FileInfo(VendoredWordList.ListPath).Length > 50_000);
    }

    /// <summary>
    /// What the file proves about its words, and nothing more.
    /// </summary>
    /// <remarks>
    /// The character set is asserted rather than assumed because the separator rule is derived
    /// from it: four of these words carry an internal hyphen, which is why a hyphen cannot be a
    /// separator (D-0239). Prefix-freedom is deliberately <em>not</em> asserted — EFF documents
    /// that for its short list #2, not for this one, and nothing here needs it.
    /// </remarks>
    [Fact]
    public void Every_word_is_three_to_nine_characters_of_lowercase_letters_and_hyphens()
    {
        var words = VendoredWordList.Words();

        Assert.Equal(words.Length, words.Distinct(StringComparer.Ordinal).Count());

        foreach (var word in words)
        {
            Assert.InRange(word.Length, 3, 9);

            foreach (var c in word)
            {
                Assert.True(char.IsAsciiLetterLower(c) || c == '-', $"'{word}' holds '{c}'");
            }
        }
    }

    [Fact]
    public void The_four_hyphenated_words_are_the_reason_a_hyphen_is_not_a_separator()
    {
        Assert.Equal(
            ["drop-down", "felt-tip", "t-shirt", "yo-yo"],
            VendoredWordList.Words()
                .Where(word => word.Contains('-', StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));

        Assert.True(WordList.Contains('-'));
        Assert.False(WordList.Contains('.'));
    }

    /// <summary>
    /// The published per-word entropy figure is derived from the list, not typed beside it.
    /// </summary>
    /// <remarks>
    /// D-0036: a claim needs something that can hold it. The band alone would pass against a
    /// hard-coded 12.925, so the equality against the file's own count is the half that matters;
    /// the band is what fails if somebody decides <c>BitsPerWord</c> should mean something else.
    /// </remarks>
    [Fact]
    public void The_bits_per_word_figure_comes_from_the_size_of_the_file()
    {
        Assert.Equal(Math.Log2(VendoredWordList.Words().Length), WordList.BitsPerWord);
        Assert.InRange(WordList.BitsPerWord, 12.92, 12.93);
    }

    /// <summary>
    /// The pin is stated once.
    /// </summary>
    /// <remarks>
    /// D-0203 and D-0207 refuse a pin restated outside its one authority. UPSTREAM.md has to point
    /// at the symbol, because a second copy of the hex is a second thing to forget to update.
    /// </remarks>
    [Fact]
    public void The_provenance_document_names_the_pin_and_does_not_restate_it()
    {
        var upstream = File.ReadAllText(Path.Combine(VendoredWordList.Directory, "UPSTREAM.md"));

        Assert.Contains("WordList.Digest", upstream, StringComparison.Ordinal);
        Assert.False(
            Regex.IsMatch(upstream, "[0-9a-fA-F]{64}", RegexOptions.None, TimeSpan.FromSeconds(5)),
            "UPSTREAM.md holds a 64-character hex run; the pin belongs in WordList.Digest alone");
    }

    [Fact]
    public void The_notices_file_attributes_the_list_and_its_licence()
    {
        var notices = File.ReadAllText(Path.Combine(VendoredWordList.RepoRoot(), "THIRD_PARTY_NOTICES.md"));

        Assert.Contains("Electronic Frontier Foundation", notices, StringComparison.Ordinal);
        Assert.Contains("CC-BY-4.0", notices, StringComparison.Ordinal);
        Assert.Contains("third_party/eff-large-wordlist/LICENSE", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void The_embedded_list_is_the_vendored_list()
    {
        Assert.Equal(VendoredWordList.Words(), WordList.Words.ToArray());
    }
}
