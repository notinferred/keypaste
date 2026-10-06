using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
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

    private static EntryExposure EnvTree => Exposure("env/**");

    /// <summary>Whether a place pattern reaches the whole of an untagged entry.</summary>
    private static bool Allows(EntryExposure exposure, EntryName name) => exposure.Reach(name, []) == ExposureReach.Entry;

    /// <summary>
    /// Both halves in one test on purpose. "Allows the right thing" alone would pass an exposure
    /// that allows everything; "refuses the wrong thing" alone would pass one that allows nothing.
    /// </summary>
    [Fact]
    public void TheDefault_ReachesTheVariablesOfTaggedEntriesAndNothingByPlace()
    {
        var exposure = EntryExposure.Default;

        Assert.Equal(ExposureReach.ProjectFields, exposure.Reach(Name("services", "Stripe"), ["env:billing"]));
        Assert.Equal(ExposureReach.ProjectFields, exposure.Reach(Name(string.Empty, "Database"), ["team", "env:billing:prod"]));
        Assert.Equal(ExposureReach.ProjectFields, exposure.Reach(Name("env/billing", ".env"), ["env:billing"]));

        Assert.Equal(ExposureReach.None, exposure.Reach(Name("env", "STRIPE_KEY"), []));
        Assert.Equal(ExposureReach.None, exposure.Reach(Name("env/dev", "DATABASE_URL"), []));
        Assert.Equal(ExposureReach.None, exposure.Reach(Name("personal", "bank"), ["team"]));
        Assert.Equal(ExposureReach.None, exposure.Reach(Name("personal", "bank"), ["Env:billing", "env:billing:Prod:x"]));
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
        Assert.False(Allows(EnvTree, Name(group, "KEY")));

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
        Assert.False(Allows(exposure, traversal));

        // ...while the entry that genuinely lives there is reachable.
        Assert.True(Allows(exposure, Name("env/prod", "ROOT_TOKEN")));
    }

    /// <summary>
    /// A single star stays inside one segment, so <c>env/*</c> is "the env group's own entries" and
    /// not "everything under env". Otherwise every narrow pattern would be a wide one.
    /// </summary>
    [Fact]
    public void ASingleStar_DoesNotCrossASeparator()
    {
        var direct = Exposure("env/*");

        Assert.True(Allows(direct, Name("env", "KEY")));
        Assert.False(Allows(direct, Name("env/dev", "KEY")));

        var oneLevel = Exposure("env/*/KEY");

        Assert.True(Allows(oneLevel, Name("env/dev", "KEY")));
        Assert.False(Allows(oneLevel, Name("env/dev/deeper", "KEY")));
        Assert.False(Allows(oneLevel, Name("env", "KEY")));
    }

    [Fact]
    public void ADoubleStar_MatchesTheGroupItselfAndEveryDepthBelow()
    {
        var exposure = Exposure("env/**");

        Assert.True(Allows(exposure, Name("env", "KEY")));
        Assert.True(Allows(exposure, Name("env/a", "KEY")));
        Assert.True(Allows(exposure, Name("env/a/b/c/d", "KEY")));
    }

    [Fact]
    public void APartialSegmentWildcard_MatchesWithinTheSegmentOnly()
    {
        var exposure = Exposure("env/dev*/DATABASE_URL");

        Assert.True(Allows(exposure, Name("env/dev", "DATABASE_URL")));
        Assert.True(Allows(exposure, Name("env/development", "DATABASE_URL")));
        Assert.False(Allows(exposure, Name("env/prod", "DATABASE_URL")));
        Assert.False(Allows(exposure, Name("env/dev", "OTHER")));
        Assert.False(Allows(exposure, Name("env/dev/inner", "DATABASE_URL")));
    }

    /// <summary>
    /// Case-insensitive matching is strictly wider matching, and widening is not something this
    /// type is allowed to do by accident.
    /// </summary>
    [Fact]
    public void MatchingIsCaseSensitive()
    {
        Assert.False(Allows(EnvTree, Name("ENV", "KEY")));
        Assert.False(Allows(EnvTree, Name("Env/dev", "KEY")));
        Assert.False(Allows(Exposure("env/dev/KEY"), Name("env/dev", "key")));
    }

    /// <summary>
    /// The failure mode that would turn a misconfiguration into a full disclosure: "no patterns"
    /// must never collapse into "everything".
    /// </summary>
    [Fact]
    public void AnExposureWithNoGlobs_AllowsNothing()
    {
        var exposure = Exposure();

        Assert.False(Allows(exposure, Name("env", "KEY")));
        Assert.False(Allows(exposure, Name("env/dev", "KEY")));
        Assert.False(Allows(exposure, Name(string.Empty, "KEY")));
        Assert.Empty(exposure.Globs);
    }

    [Fact]
    public void EverythingCanBeExposedIfSomebodyReallyAsks()
    {
        var exposure = Exposure("**");

        Assert.True(Allows(exposure, Name("personal", "bank")));
        Assert.True(Allows(exposure, Name(string.Empty, "loose")));
        Assert.True(Allows(exposure, Name("a/b/c", "deep")));
    }

    [Fact]
    public void SeveralGlobsAreOredTogether_AndKeptInOrderForTheAuditLine()
    {
        var exposure = Exposure("env/**", "servers/staging/*");

        Assert.True(Allows(exposure, Name("env/dev", "KEY")));
        Assert.True(Allows(exposure, Name("servers/staging", "web")));
        Assert.False(Allows(exposure, Name("servers/production", "web")));

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
        Assert.False(Allows(exposure, name));
    }

    [Fact]
    public void TryCreate_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => EntryExposure.TryCreate(null!, out _, out _));

    /// <summary>keypaste's own records are never named or released, whatever a bridge or a rule was told.</summary>
    [Theory]
    [InlineData("**")]
    [InlineData("**/*")]
    [InlineData(".keypaste/**")]
    [InlineData(".keypaste/tokens/*")]
    [InlineData(".Keypaste/**")]
    [InlineData("*/*/*")]
    public void EveryGlob_IncludingDoubleStar_RefusesReservedEntries(string glob)
    {
        var exposure = Exposure(glob);

        Assert.False(Allows(exposure, Name(ReservedGroups.Tokens, "7d2e91c0")));
        Assert.False(Allows(exposure, Name(ReservedGroups.Shares, "link")));
        Assert.False(Allows(exposure, Name(".Keypaste/tokens", "7d2e91c0")));
        Assert.False(Allows(exposure, Name(ReservedGroups.Root, "loose")));
    }

    [Fact]
    public void ANameThatOnlyStartsLikeTheReservedGroup_IsNotReserved()
    {
        Assert.True(Allows(Exposure("**"), Name(".keypaste-notes", "x")));
        Assert.True(Allows(Exposure("**"), Name("env/.keypaste", "x")));
    }

    [Fact]
    public void TheListerNeverNamesReservedEntries()
    {
        var directory = Directory.CreateTempSubdirectory("keypaste-reserved-").FullName;

        try
        {
            using var vault = ReservedVault(directory);

            Assert.True(new VaultEntryNameLister(() => vault).TryList(Exposure("**"), out var names, out _));

            Assert.Contains(Name("env/dev", "KEY"), names.Select(entry => entry.Name));
            Assert.DoesNotContain(names, entry => ReservedGroups.IsReserved(entry.Name.GroupPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// D-0422 on a real vault: the lister names the fields that hold something and may be asked for,
    /// and the project tags, and a tag's reach drops the standard fields.
    /// </summary>
    [Fact]
    public void TheLister_NamesFieldsThatHoldSomethingAndProjectTags()
    {
        var directory = Directory.CreateTempSubdirectory("keypaste-listed-").FullName;

        try
        {
            var stripe = Name("services", "Stripe");

            using var vault = Vault.Create(Path.Combine(directory, "vault.kdbx"), EnvStoreTests.MasterPassword);
            vault.AddEntry(new VaultEntry { GroupPath = stripe.GroupPath, Title = stripe.Title, Password = "login-sentinel", Url = "https://stripe.example" });
            Assert.True(vault.SetFields(stripe, [new FieldWrite("STRIPE_SECRET_KEY", "key-sentinel"), new FieldWrite("Region", "eu")]));
            Assert.True(vault.AddTag(stripe, "env:billing"));
            Assert.True(vault.AddTag(stripe, "finance"));
            vault.Save();

            var lister = new VaultEntryNameLister(() => vault);
            Assert.True(lister.TryList(Exposure("services/**"), out var whole, out _));
            Assert.True(lister.TryList(EntryExposure.Default, out var tagged, out _));

            Assert.Equal(new ListedEntry(stripe, ["password", "url", "STRIPE_SECRET_KEY"], ["env:billing"]), Assert.Single(whole));
            Assert.Equal(new ListedEntry(stripe, ["STRIPE_SECRET_KEY"], ["env:billing"]), Assert.Single(tagged));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AHandleToAReservedEntry_IsOutOfScope()
    {
        var directory = Directory.CreateTempSubdirectory("keypaste-reserved-").FullName;

        try
        {
            using var vault = ReservedVault(directory);
            using var fixture = new ApproverFixture();
            fixture.Channel.Answer = ApprovalAnswer.Approved;

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => vault),
                new VaultEntryNameLister(() => vault),
                fixture.Gate,
                fixture.Grants,
                fixture.Policy);

            var reply = await handler.RequestAsync(
                new CredentialRequest
                {
                    Entry = EntryHandle.For(Name(ReservedGroups.Tokens, "7d2e91c0")),
                    Field = "password",
                    Reason = "read the verifier",
                    TtlSeconds = 60,
                    Exposure = ["**"],
                },
                "connection",
                TestContext.Current.CancellationToken);

            Assert.Equal(AuditDecision.Denied, reply.Decision);
            Assert.Equal(AuditMethod.OutOfScope, reply.Method);
            Assert.Null(reply.Value);
            Assert.Equal(0, fixture.Channel.Asked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// D-0422: a selector reads project membership as a tag says it, so <c>env:billing</c> is
    /// billing's <c>dev</c>, and an omitted environment in the selector means every one.
    /// </summary>
    [Fact]
    public void ATagSelector_MatchesProjectMembershipNotTheTagsText()
    {
        var billing = Exposure("tag:env:billing");
        var production = Exposure("tag:env:billing:prod");
        var development = Exposure("tag:env:billing:dev");
        var everyProduction = Exposure("tag:env:*:prod");
        var entry = Name("services", "Stripe");

        Assert.Equal(ExposureReach.ProjectFields, billing.Reach(entry, ["env:billing"]));
        Assert.Equal(ExposureReach.ProjectFields, billing.Reach(entry, ["env:billing:prod"]));
        Assert.Equal(ExposureReach.None, billing.Reach(entry, ["env:billing-eu"]));

        Assert.Equal(ExposureReach.ProjectFields, production.Reach(entry, ["env:billing:prod"]));
        Assert.Equal(ExposureReach.None, production.Reach(entry, ["env:billing"]));

        Assert.Equal(ExposureReach.ProjectFields, development.Reach(entry, ["env:billing"]));
        Assert.Equal(ExposureReach.ProjectFields, everyProduction.Reach(entry, ["env:web:prod"]));
        Assert.Equal(ExposureReach.None, everyProduction.Reach(entry, ["env:web:staging"]));
    }

    /// <summary>T-38: reach is per entry, so an entry in two environments is reached through either.</summary>
    [Fact]
    public void AnEntryInTwoEnvironments_IsReachedThroughEither()
    {
        Assert.Equal(
            ExposureReach.ProjectFields,
            Exposure("tag:env:billing:dev").Reach(Name("services", "Stripe"), ["env:billing:prod", "env:billing"]));
    }

    /// <summary>A place names the whole entry, so a place pattern wins over a selector on the same entry.</summary>
    [Fact]
    public void APlacePattern_ReachesTheWholeEntryEvenWhenASelectorAlsoMatches() =>
        Assert.Equal(
            ExposureReach.Entry,
            Exposure("tag:env:*", "services/**").Reach(Name("services", "Stripe"), ["env:billing"]));

    /// <summary>A tag never exposes a password: the reach through one covers only fields named like variables.</summary>
    [Fact]
    public void ATagReachesOnlyTheEntrysProjectVariables()
    {
        var exposure = EntryExposure.Default;
        var entry = Name("services", "Stripe");
        string[] tags = ["env:billing"];

        Assert.True(exposure.Permits(entry, tags, "STRIPE_SECRET_KEY"));

        foreach (var standard in CredentialFields.All)
        {
            Assert.False(exposure.Permits(entry, tags, standard));
        }

        Assert.False(exposure.Permits(entry, tags, "KP2A_URL_1"));
        Assert.True(Exposure("services/**").Permits(entry, [], "password"));
        Assert.False(exposure.Permits(Name(ReservedGroups.Tokens, "7d2e91c0"), tags, "STRIPE_SECRET_KEY"));
    }

    /// <summary>
    /// The bridge cannot read tags, so it forwards what a tag might reach and refuses only what the
    /// owner would refuse whatever the entry's tags.
    /// </summary>
    [Fact]
    public void TheBridgesCheck_RefusesOnlyWhatNoTagCouldReach()
    {
        var entry = Name("services", "Stripe");

        Assert.True(EntryExposure.Default.MayPermit(entry, "STRIPE_SECRET_KEY"));
        Assert.False(EntryExposure.Default.MayPermit(entry, "password"));
        Assert.False(EntryExposure.Default.MayPermit(Name(ReservedGroups.Tokens, "7d2e91c0"), "STRIPE_SECRET_KEY"));
        Assert.True(Exposure("services/**").MayPermit(entry, "password"));
        Assert.False(Exposure("api/**").MayPermit(entry, "STRIPE_SECRET_KEY"));
    }

    /// <summary>
    /// A selector that does not parse is refused, never read as a place: <c>TAG:env:x</c> taken as a
    /// title pattern would leave a different exposure in force than the one written.
    /// </summary>
    [Theory]
    [InlineData("tag:")]
    [InlineData("tag:env:")]
    [InlineData("tag:env::prod")]
    [InlineData("tag:env:billing:")]
    [InlineData("tag:env:a:b:c")]
    [InlineData("tag:team")]
    [InlineData("TAG:env:billing")]
    [InlineData("Tag:env:billing")]
    [InlineData("tag:ENV:billing")]
    public void AMalformedTagSelector_IsRefused(string selector)
    {
        Assert.False(EntryExposure.TryCreate([selector], out var exposure, out var error));
        Assert.Null(exposure);
        Assert.Contains("tag:env:<project>", error, StringComparison.Ordinal);
    }

    private static Vault ReservedVault(string directory)
    {
        var vault = Vault.Create(Path.Combine(directory, "vault.kdbx"), EnvStoreTests.MasterPassword);
        vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "KEY", Password = "v" });
        vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Tokens, Title = "7d2e91c0", Password = "verifier" });
        vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Shares, Title = "link", Password = "revoke" });
        vault.Save();
        return vault;
    }
}
