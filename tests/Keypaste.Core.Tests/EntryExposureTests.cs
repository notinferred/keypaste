using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The exposure set is the only thing standing between a connected agent and the inventory of a
/// personal vault (THREATS.md T-4), so it is tested for what it <em>refuses</em> at least as hard
/// as for what it allows.
/// </summary>
public sealed class EntryExposureTests
{
    private static EntryExposure Exposure(params string[] globs)
    {
        Assert.True(EntryExposure.TryCreate(globs, out var exposure, out var error), error);
        Assert.NotNull(exposure);
        return exposure;
    }

    private static EntryName Name(string groupPath, string title) => new(groupPath, title);

    /// <summary>
    /// Both halves in one test on purpose. "Allows the right thing" alone would pass an exposure
    /// that allows everything; "refuses the wrong thing" alone would pass one that allows nothing.
    /// </summary>
    [Fact]
    public void TheDefault_ReachesTheEnvTreeAndNothingElse()
    {
        var exposure = EntryExposure.Default;

        Assert.True(exposure.Allows(Name("env", "STRIPE_KEY")));
        Assert.True(exposure.Allows(Name("env/dev", "DATABASE_URL")));
        Assert.True(exposure.Allows(Name("env/dev/nested", "DEEP")));

        Assert.False(exposure.Allows(Name("personal", "bank")));
        Assert.False(exposure.Allows(Name("servers/production", "root")));
        Assert.False(exposure.Allows(Name(string.Empty, "loose-entry")));
    }

    /// <summary>
    /// A prefix must not be enough. If <c>env/**</c> matched <c>envelopes</c>, widening one subtree
    /// would quietly widen every group whose name starts with the same letters.
    /// </summary>
    [Theory]
    [InlineData("envx")]
    [InlineData("envelopes")]
    [InlineData("env-old")]
    [InlineData("myenv")]
    public void ASimilarlyNamedGroup_IsNotInTheEnvTree(string group) =>
        Assert.False(EntryExposure.Default.Allows(Name(group, "KEY")));

    /// <summary>
    /// The whole reason globs are matched against the group and the title separately rather than
    /// against the joined path. A title full of separators is still a title.
    /// </summary>
    [Fact]
    public void ATitleFullOfSlashes_CannotImpersonateAGroup()
    {
        var exposure = Exposure("env/prod/**");
        var traversal = Name("env/dev", "../../prod/ROOT_TOKEN");

        // Joined, this entry's path reads "env/dev/../../prod/ROOT_TOKEN".
        Assert.False(exposure.Allows(traversal));

        // ...while the entry that genuinely lives there is reachable.
        Assert.True(exposure.Allows(Name("env/prod", "ROOT_TOKEN")));
    }

    /// <summary>
    /// A single star stays inside one segment, so <c>env/*</c> is "the env group's own entries" and
    /// not "everything under env". Otherwise every narrow pattern would be a wide one.
    /// </summary>
    [Fact]
    public void ASingleStar_DoesNotCrossASeparator()
    {
        var direct = Exposure("env/*");

        Assert.True(direct.Allows(Name("env", "KEY")));
        Assert.False(direct.Allows(Name("env/dev", "KEY")));

        var oneLevel = Exposure("env/*/KEY");

        Assert.True(oneLevel.Allows(Name("env/dev", "KEY")));
        Assert.False(oneLevel.Allows(Name("env/dev/deeper", "KEY")));
        Assert.False(oneLevel.Allows(Name("env", "KEY")));
    }

    [Fact]
    public void ADoubleStar_MatchesTheGroupItselfAndEveryDepthBelow()
    {
        var exposure = Exposure("env/**");

        Assert.True(exposure.Allows(Name("env", "KEY")));
        Assert.True(exposure.Allows(Name("env/a", "KEY")));
        Assert.True(exposure.Allows(Name("env/a/b/c/d", "KEY")));
    }

    [Fact]
    public void APartialSegmentWildcard_MatchesWithinTheSegmentOnly()
    {
        var exposure = Exposure("env/dev*/DATABASE_URL");

        Assert.True(exposure.Allows(Name("env/dev", "DATABASE_URL")));
        Assert.True(exposure.Allows(Name("env/development", "DATABASE_URL")));
        Assert.False(exposure.Allows(Name("env/prod", "DATABASE_URL")));
        Assert.False(exposure.Allows(Name("env/dev", "OTHER")));
        Assert.False(exposure.Allows(Name("env/dev/inner", "DATABASE_URL")));
    }

    /// <summary>
    /// Case-insensitive matching is strictly wider matching, and widening is not something this
    /// type is allowed to do by accident.
    /// </summary>
    [Fact]
    public void MatchingIsCaseSensitive()
    {
        Assert.False(EntryExposure.Default.Allows(Name("ENV", "KEY")));
        Assert.False(EntryExposure.Default.Allows(Name("Env/dev", "KEY")));
        Assert.False(Exposure("env/dev/KEY").Allows(Name("env/dev", "key")));
    }

    /// <summary>
    /// The failure mode that would turn a misconfiguration into a full disclosure: "no patterns"
    /// must never collapse into "everything".
    /// </summary>
    [Fact]
    public void AnExposureWithNoGlobs_AllowsNothing()
    {
        var exposure = Exposure();

        Assert.False(exposure.Allows(Name("env", "KEY")));
        Assert.False(exposure.Allows(Name("env/dev", "KEY")));
        Assert.False(exposure.Allows(Name(string.Empty, "KEY")));
        Assert.Empty(exposure.Globs);
    }

    [Fact]
    public void EverythingCanBeExposedIfSomebodyReallyAsks()
    {
        var exposure = Exposure("**");

        Assert.True(exposure.Allows(Name("personal", "bank")));
        Assert.True(exposure.Allows(Name(string.Empty, "loose")));
        Assert.True(exposure.Allows(Name("a/b/c", "deep")));
    }

    [Fact]
    public void SeveralGlobsAreOredTogether_AndKeptInOrderForTheAuditLine()
    {
        var exposure = Exposure("env/**", "servers/staging/*");

        Assert.True(exposure.Allows(Name("env/dev", "KEY")));
        Assert.True(exposure.Allows(Name("servers/staging", "web")));
        Assert.False(exposure.Allows(Name("servers/production", "web")));

        Assert.Equal(["env/**", "servers/staging/*"], exposure.Globs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("env/\u0000dev")]
    [InlineData("env\\dev")]
    public void AMalformedGlob_IsRefusedRatherThanSkipped(string glob)
    {
        Assert.False(EntryExposure.TryCreate([glob], out var exposure, out var error));
        Assert.Null(exposure);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TooManyGlobs_AreRefused()
    {
        var globs = Enumerable.Range(0, EntryExposure.MaximumGlobs + 1)
            .Select(i => $"env/g{i}/**")
            .ToArray();

        Assert.False(EntryExposure.TryCreate(globs, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void AnOverlongGlob_IsRefused()
    {
        var glob = "env/" + new string('a', EntryExposure.MaximumGlobLength);

        Assert.False(EntryExposure.TryCreate([glob], out _, out var error));
        Assert.NotEmpty(error);
    }

    /// <summary>
    /// Matching happens on the raw name, so no change to the sanitizer can ever widen what is
    /// exposed. Here the title sanitizes to something that <em>would</em> match if the sanitized
    /// form were used, and it still does not.
    /// </summary>
    [Fact]
    public void MatchingUsesTheRawNameNotTheSanitizedOne()
    {
        var exposure = Exposure("env/dev/KEY");
        var name = Name("env/dev", "KEY\u200b");

        Assert.Equal("KEY", EntryNameSanitizer.Sanitize(name.Title).Text);
        Assert.False(exposure.Allows(name));
    }

    [Fact]
    public void TryCreate_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => EntryExposure.TryCreate(null!, out _, out _));
}
