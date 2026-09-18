using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The generator's stated rule about the modulo operator, finally checkable.
/// </summary>
/// <remarks>
/// <para>
/// <c>PasswordGenerator</c> has said since 4.2 that uniformity comes from
/// <c>RandomNumberGenerator.GetItems</c> and that the <c>%</c> operator does not appear in the
/// file, citing docs/PRODUCT.md law 3.6. Nothing held it to that, and as written it was not even
/// checkable as text: <c>Symbols</c> is a string literal with a <c>%</c> in it, because a
/// per-cent sign is a character keypaste generates.
/// </para>
/// <para>
/// So the rule this asserts is the one that was meant: on the generation path, the only <c>%</c>
/// in code is inside that character set. Comment lines are skipped, because prose explaining the
/// rule necessarily names the operator — which is how the first draft of this test failed.
/// </para>
/// <para>
/// An index taken modulo a list length fails here the day it is written, which is the point:
/// V-V.6's distribution pair can only catch a bias that was shipped, and this catches the shape
/// of it in review.
/// </para>
/// </remarks>
public sealed class PassphraseSourceRulesTests
{
    private const string _symbols = "public const string Symbols";

    [Theory]
    [InlineData("PasswordGenerator.cs")]
    [InlineData("PassphraseRecipe.cs")]
    [InlineData("WordList.cs")]
    public void The_generation_path_carries_no_modulo_operator(string file)
    {
        var lines = File.ReadAllLines(SourcePath(file));

        // Positive control: a renamed or emptied file must fail this, not silently pass it.
        Assert.True(lines.Length > 40, $"{file} holds {lines.Length} lines; it was not read");

        var offending = lines
            .Where(line => line.Contains('%', StringComparison.Ordinal))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(line => !line.Contains(_symbols, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offending.Count == 0,
            $"{file} uses '%' outside the generated character set: " + string.Join(" | ", offending));
    }

    /// <summary>
    /// The exemption is a real line, not a spelling nothing matches.
    /// </summary>
    /// <remarks>
    /// Without this, renaming <c>Symbols</c> would turn the exemption into dead text and the test
    /// above into a stricter rule that happens to pass — or, if the rename went the other way, into
    /// a blanket exemption. Both are worth noticing.
    /// </remarks>
    [Fact]
    public void The_one_exempt_line_is_the_character_set_and_it_does_hold_a_per_cent_sign()
    {
        var exempt = File.ReadAllLines(SourcePath("PasswordGenerator.cs"))
            .Where(line => line.Contains(_symbols, StringComparison.Ordinal))
            .ToList();

        Assert.Single(exempt);
        Assert.Contains('%', exempt[0]);
        Assert.Contains('%', PasswordGenerator.Symbols);

        // And the scan is live: skipping comments must not have skipped the whole file.
        Assert.Contains(
            '%',
            File.ReadAllLines(SourcePath("PasswordGenerator.cs"))
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Aggregate(string.Empty, string.Concat));
    }

    private static string SourcePath(string file) =>
        Path.Combine(VendoredWordList.RepoRoot(), "src", "Keypaste.Core", file);
}
