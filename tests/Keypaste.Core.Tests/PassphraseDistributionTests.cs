using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The word draw is uniform, and the check that says so would catch the bias it is aimed at.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this exists for is modulo bias</b> — an index taken as
/// <c>random % list.Length</c>, which is the obvious way to write this and is wrong.
/// docs/PRODUCT.md law 3.6 is why the shipped draw is <c>RandomNumberGenerator.GetItems</c>
/// instead, and V-V.6 asks for a check that fails on the biased version and passes on the one
/// keypaste uses.
/// </para>
/// <para>
/// <b>Why the real 7,776-word list and not a small synthetic one.</b> Modulo bias shrinks as the
/// list shrinks against the random range: <c>byte % 5</c> over 256 values over-weights exactly one
/// index by 0.4 %, which no affordable sample size can see. A narrow list would let a biased
/// implementation pass, which is the one thing this test must not do.
/// </para>
/// <para>
/// <b>The arithmetic.</b> Two bytes give 65,536 values and
/// 65,536 = 8 × 7,776 + 3,328, so a modulo draw gives indices 0..3,327 nine preimages each and
/// the remaining 4,448 only eight. It therefore puts 3,328 × 9 / 65,536 = 0.45703 of its words
/// below the boundary where a uniform draw puts 3,328 / 7,776 = 0.42798.
/// </para>
/// <para>
/// At 60,000 sampled words the standard deviation of that share is
/// sqrt(0.428 × 0.572 / 60,000) = 0.00202. The band below is ±0.010, so it is 4.95σ wide for a
/// fair draw — a flake probability under one in a million, keeping to the "catch a broken draw,
/// not fail once a fortnight" standard <see cref="PasswordGeneratorTests"/> sets — while the
/// biased draw's share sits 14.4σ from the fair mean (0.02905 / 0.00202).
/// </para>
/// </remarks>
public sealed class PassphraseDistributionTests
{
    private const int _samples = 60_000;
    private const double _band = 0.010;

    /// <summary>
    /// Both halves measure the same statistic the same way.
    /// </summary>
    /// <remarks>
    /// If the fair and biased halves computed the share differently the pair would prove nothing
    /// about either. One helper, called twice.
    /// </remarks>
    [Fact]
    public void The_word_draw_is_not_modulo_biased()
    {
        var share = LowShare(Generated());

        Assert.InRange(share, FairShare() - _band, FairShare() + _band);
    }

    [Fact]
    public void The_same_check_fails_a_modulo_biased_draw()
    {
        var list = VendoredWordList.Words();

        var share = LowShare(Biased(list));

        Assert.False(
            share > FairShare() - _band && share < FairShare() + _band,
            $"the modulo draw landed at {share:F5}, inside the band around {FairShare():F5}; " +
            "the check above is not measuring what it claims to");
    }

    /// <summary>
    /// The boundary is derived, not remembered.
    /// </summary>
    /// <remarks>
    /// A re-vendored list of a different size moves the boundary, and a test still bucketing at
    /// 3,328 would be measuring nothing in particular. This is what makes that a failure instead.
    /// </remarks>
    [Fact]
    public void The_modulo_boundary_is_where_the_arithmetic_says_it_is()
    {
        Assert.Equal(3328, Boundary);
        Assert.Equal(WordList.Count, VendoredWordList.Words().Length);
        Assert.InRange(FairShare(), 0.427, 0.429);
    }

    /// <summary>How many indices a two-byte modulo draw over-weights.</summary>
    private static int Boundary => 65536 % WordList.Count;

    /// <summary>The share of uniformly drawn words that fall below <see cref="Boundary"/>.</summary>
    private static double FairShare() => (double)Boundary / WordList.Count;

    /// <summary>
    /// The share of <paramref name="drawn"/> whose index is below <see cref="Boundary"/>.
    /// </summary>
    private static double LowShare(IEnumerable<string> drawn)
    {
        var index = Index();
        var low = 0;
        var total = 0;

        foreach (var word in drawn)
        {
            if (index[word] < Boundary)
            {
                low++;
            }

            total++;
        }

        Assert.Equal(_samples, total);

        return (double)low / total;
    }

    /// <summary>Words from the shipped generator, read back out of its passphrases.</summary>
    private static IEnumerable<string> Generated()
    {
        for (var i = 0; i < _samples / PasswordGenerator.DefaultWords; i++)
        {
            using var buffer = new SecretBuffer();
            PasswordGenerator.Append(PassphraseRecipe.Default, buffer);

            foreach (var word in new string(buffer.Value).Split(PasswordGenerator.DefaultSeparator))
            {
                yield return word;
            }
        }
    }

    /// <summary>
    /// The generator law 3.6 forbids, written here so the check above can be shown to catch it.
    /// </summary>
    /// <remarks>
    /// This is the only <c>%</c> on the passphrase path anywhere in the repository, and
    /// <see cref="PassphraseSourceRulesTests"/> holds it to that.
    /// </remarks>
    private static IEnumerable<string> Biased(string[] list)
    {
        var two = new byte[2];

        for (var i = 0; i < _samples; i++)
        {
            RandomNumberGenerator.Fill(two);
            yield return list[BinaryPrimitives.ReadUInt16LittleEndian(two) % list.Length];
        }
    }

    private static Dictionary<string, int> Index()
    {
        var words = VendoredWordList.Words();
        var index = new Dictionary<string, int>(words.Length, StringComparer.Ordinal);

        for (var i = 0; i < words.Length; i++)
        {
            index[words[i]] = i;
        }

        return index;
    }
}
