using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Two profiles differ by a missing key, an unusable one or a reused value, never by value alone.</summary>
public sealed class EnvDiffTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-env-diff-").FullName;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly Vault _vault;

    public EnvDiffTests() => _vault = EnvMatrixTests.Seeded(_directory);

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_missing_key_is_named_both_ways()
    {
        var lines = Compare("staging", "prod");

        Assert.Contains(new EnvDiffLine("SENTRY_DSN", EnvDiffKind.Missing, "staging", null), lines);
        Assert.Contains(new EnvDiffLine("JWT_SIGNING_KEY", EnvDiffKind.Missing, "prod", null), lines);
    }

    [Fact]
    public void An_unusable_key_is_named_with_why()
    {
        Assert.Contains(
            new EnvDiffLine("JWT_SIGNING_KEY", EnvDiffKind.Unusable, "staging", "expired 2026-09-01 00:00:00Z"),
            Compare("dev", "staging"));
    }

    [Fact]
    public void A_reused_value_is_named_and_a_different_one_is_not()
    {
        Assert.Equal(
            [
                new EnvDiffLine("SENTRY_DSN", EnvDiffKind.Missing, "dev", null),
                new EnvDiffLine("STRIPE_KEY", EnvDiffKind.SameValue, "dev", "prod"),
            ],
            Compare("dev", "prod"));
    }

    [Fact]
    public void Two_profiles_with_the_same_usable_keys_have_no_difference()
    {
        new EnvStore(_vault).TrySet("acme-api", "qa", "DATABASE_URL", "qa-db", out _);
        new EnvStore(_vault).TrySet("acme-api", "qa2", "DATABASE_URL", "qa2-db", out _);

        Assert.Empty(Compare("qa", "qa2"));
        Assert.Throws<ArgumentException>(() => Compare("dev", "absent"));
    }

    private IReadOnlyList<EnvDiffLine> Compare(string a, string b) =>
        EnvDiff.Compare(EnvMatrix.Build(_vault, "acme-api", _clock), a, b);
}
