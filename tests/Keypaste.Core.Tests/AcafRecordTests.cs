using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// ACAF v1: an agent's settings as one JSON record, read back exactly, a record keypaste does not wholly
/// understand never read as giving more than the defaults though what it says is read at its word, and a
/// change written into a record rewriting only what it names.
/// </summary>
public sealed class AcafRecordTests
{
    /// <summary>A record as a person or another writer might leave it, with a limit, a row, a value and a member keypaste does not understand.</summary>
    private const string _handEdited =
        """{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"automatic","limit":{"perHour":5,"perDay":50},"projects":[{"project":"billing","mode":"automatic","colour":"red"},{"project":"shop:Prod","mode":"ask"}],"later":{"x":[1,2]}}""";

    private static readonly DateTimeOffset _until = new(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public void TheDefaults_AreWrittenAsTheSpecificationShowsThem()
    {
        Assert.Equal(
            """{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"allow-for-a-while"}""",
            AcafRecord.Write(AgentAccess.Defaults));
    }

    [Fact]
    public void EverySetting_RoundTrips()
    {
        var access = new AgentAccess
        {
            See = See("tag:env:billing", "services/**"),
            Values = false,
            Run = true,
            Mode = AgentMode.Ask,
            Limit = new AccessLimit { PerHour = 20, Until = _until },
            Projects =
            [
                Row("billing", AgentMode.Automatic, new AccessLimit { PerHour = 5 }),
                Row("billing:staging", AgentMode.AllowForAWhile),
            ],
        };

        var text = AcafRecord.Write(access);
        var read = AcafRecord.Read(text);

        Assert.True(read.Understood);
        Assert.True(read.Rewritable);
        Assert.Equal(access, read.Access);
        Assert.Same(read.Access, read.Said);
        Assert.Contains("\"until\":\"2026-12-31T23:59:59Z\"", text, StringComparison.Ordinal);
        Assert.Contains("\"projects\":[{\"project\":\"billing\",\"mode\":\"automatic\",\"limit\":{\"perHour\":5}},{\"project\":\"billing:staging\",\"mode\":\"allow-for-a-while\"}]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberLeftOut_TakesTheDefaultsValue()
    {
        var read = AcafRecord.Read("""{"acaf":1,"mode":"automatic"}""");

        Assert.True(read.Understood);
        Assert.Equal(AgentAccess.Defaults with { Mode = AgentMode.Automatic }, read.Access);
    }

    [Fact]
    public void AnUnknownMember_IsNotUnderstood_ButIsKeptRewritable()
    {
        var read = AcafRecord.Read("""{"acaf":1,"mode":"automatic","colour":"red"}""");

        Assert.False(read.Understood);
        Assert.True(read.Rewritable);
        Assert.Equal(AgentMode.Automatic, read.Access.Mode);
    }

    /// <summary>A limit keypaste cannot read may have been all that bounded an Automatic default, so the default asks every time; the rows keep their own.</summary>
    [Theory]
    [InlineData("""{"perHour":5,"perDay":50}""")]
    [InlineData("""{"perHour":0}""")]
    [InlineData("""{"until":"2026-12-31"}""")]
    [InlineData("{}")]
    [InlineData("\"none\"")]
    public void ALimitNotUnderstood_AsksEveryTimeByDefault_AndKeepsTheRows(string limit)
    {
        var read = AcafRecord.Read($$"""{"acaf":1,"mode":"automatic","limit":{{limit}},"projects":[{"project":"billing","mode":"automatic"}]}""");

        Assert.False(read.Understood);
        Assert.True(read.Rewritable);
        Assert.Equal(AgentMode.Ask, read.Access.Mode);
        Assert.Equal(AccessLimit.None, read.Access.Limit);
        Assert.Equal([Row("billing", AgentMode.Automatic)], read.Access.Projects);
    }

    /// <summary>
    /// What a record says takes each part at its word, a value keypaste cannot read at its narrowest and a
    /// row it cannot place left out, while the record applies as though every part were at its narrowest.
    /// </summary>
    [Fact]
    public void WhatARecordSays_TakesEachPartAtItsWord()
    {
        var read = AcafRecord.Read(_handEdited);

        Assert.Equal((AgentMode.Ask, AccessLimit.None), (read.Access.Mode, read.Access.Limit));
        Assert.Empty(read.Access.Projects);
        Assert.Equal(Automatic with { Limit = new AccessLimit { PerHour = 5 }, Projects = [Row("billing", AgentMode.Automatic)] }, read.Said);
    }

    [Theory]
    [InlineData("""{"perHour":"lots","until":"2026-12-31"}""")]
    [InlineData("{}")]
    [InlineData("\"none\"")]
    public void ALimitKeypasteCannotRead_SaysTheLeastItCouldAllow(string limit)
    {
        var read = AcafRecord.Read($$"""{"acaf":1,"mode":"automatic","limit":{{limit}}}""");

        Assert.Equal(AgentMode.Automatic, read.Said.Mode);
        Assert.Equal(new AccessLimit { PerHour = 1, Until = DateTimeOffset.UnixEpoch }, read.Said.Limit);
    }

    [Fact]
    public void TwoRowsForOneScope_SayTheLeastOfThem_AndMoreRowsThanKeypasteKeeps_SayNone()
    {
        var twice = AcafRecord.Read(
            """{"acaf":1,"projects":[{"project":"billing","mode":"automatic","limit":{"perHour":9}},{"project":"billing","mode":"allow-for-a-while","limit":{"perHour":4,"until":"2026-12-31T23:59:59Z"}}]}""");
        var rows = Enumerable.Range(0, AgentAccess.MaximumProjects + 1).Select(i => $$"""{"project":"p{{i}}","mode":"automatic"}""");
        var tooMany = AcafRecord.Read($$"""{"acaf":1,"projects":[{{string.Join(',', rows)}}]}""");

        Assert.Equal([Row("billing", AgentMode.AllowForAWhile, new AccessLimit { PerHour = 4, Until = _until })], twice.Said.Projects);
        Assert.Empty(tooMany.Said.Projects);
        Assert.False(twice.Understood || tooMany.Understood);
    }

    [Theory]
    [InlineData("""{"acaf":2,"mode":"automatic"}""")]
    [InlineData("""{"acaf":"1","mode":"automatic"}""")]
    [InlineData("""{"acaf":1.5,"mode":"automatic"}""")]
    public void AnotherVersion_IsNotUnderstood_AndNeverRewritten(string text)
    {
        var read = AcafRecord.Read(text);

        Assert.False(read.Understood);
        Assert.False(read.Rewritable);
        Assert.Throws<ArgumentException>(() => AcafRecord.Write(AgentAccess.Defaults, [text]));
    }

    [Fact]
    public void ARecordWithNoVersion_IsNotUnderstood()
    {
        var read = AcafRecord.Read("""{"mode":"automatic"}""");

        Assert.False(read.Understood);
        Assert.True(read.Rewritable);
    }

    [Theory]
    [InlineData("""{"acaf":1,"mode":"sometimes"}""")]
    [InlineData("""{"acaf":1,"mode":2}""")]
    [InlineData("""{"acaf":1,"mode":"automatic","projects":[{"project":"billing","mode":"later"}]}""")]
    [InlineData("""{"acaf":1,"mode":"automatic","projects":[{"project":"billing:Prod","mode":"automatic"}]}""")]
    [InlineData("""{"acaf":1,"mode":"automatic","projects":[{"tag":"env:billing","mode":"automatic"}]}""")]
    [InlineData("""{"acaf":1,"mode":"automatic","projects":[{"project":"billing","mode":"automatic"},{"project":"billing","mode":"ask"}]}""")]
    [InlineData("""{"acaf":1,"mode":"automatic","projects":{"project":"billing"}}""")]
    public void AModeOrRowNotUnderstood_AsksForEverything(string text)
    {
        var read = AcafRecord.Read(text);

        Assert.False(read.Understood);
        Assert.Equal(AgentMode.Ask, read.Access.Mode);
        Assert.Empty(read.Access.Projects);
    }

    [Theory]
    [InlineData("""{"acaf":1,"values":"yes","run":"on"}""")]
    [InlineData("""{"acaf":1,"values":1,"run":null}""")]
    public void ValuesOrRunNotUnderstood_AreOff(string text)
    {
        var read = AcafRecord.Read(text);

        Assert.False(read.Understood);
        Assert.False(read.Access.Values);
        Assert.False(read.Access.Run);
    }

    [Theory]
    [InlineData("""{"acaf":1,"see":"tag:env:*"}""")]
    [InlineData("""{"acaf":1,"see":["tag:env:"]}""")]
    [InlineData("""{"acaf":1,"see":[1]}""")]
    [InlineData("""{"acaf":1,"see":["a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q"]}""")]
    public void WhatItSeesNotUnderstood_ReachesNothing(string text)
    {
        var read = AcafRecord.Read(text);

        Assert.False(read.Understood);
        Assert.Equal(ExposureReach.None, read.Access.See.Reach(new EntryName("a", "b"), ["env:billing"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"acaf":1,"acaf":1,"mode":"automatic"}""")]
    [InlineData("""{"acaf":1,"mode":"automatic",}""")]
    public void TextThatIsNoRecord_ReadsAsTheDefaults_NotUnderstood(string text)
    {
        var read = AcafRecord.Read(text);

        Assert.False(read.Understood);
        Assert.True(read.Rewritable);
        Assert.Equal(AgentAccess.Defaults, read.Access);
    }

    [Fact]
    public void WritingSettingsWhole_KeepsUnknownTopLevelMembers_TheFirstRecordsWhereTwoDisagree()
    {
        var text = AcafRecord.Write(
            Automatic,
            [
                """{"acaf":1,"mode":"ask","colour":"red","nested":{"a":[1,2]}}""",
                """{"acaf":1,"colour":"blue","shape":"round"}""",
                null,
                "not json",
            ]);

        Assert.Equal(
            """{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"automatic","colour":"red","nested":{"a":[1,2]},"shape":"round"}""",
            text);
        Assert.Equal(Automatic, AcafRecord.Read(text).Access);
    }

    [Fact]
    public void ALimitTime_IsKeptToTheSecondInUtc()
    {
        var limit = new AccessLimit { Until = new DateTimeOffset(2026, 12, 31, 23, 59, 59, 999, TimeSpan.FromHours(2)) };

        Assert.Equal(new DateTimeOffset(2026, 12, 31, 21, 59, 59, TimeSpan.Zero), limit.Until);
        Assert.Equal(TimeSpan.Zero, limit.Until!.Value.Offset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AccessLimit.MaximumPerHour + 1)]
    public void AnHourlyLimitOutOfRange_IsRefused(int perHour) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccessLimit { PerHour = perHour });

    [Fact]
    public void TwoRowsForOneScope_AreRefused() =>
        Assert.Throws<ArgumentException>(() => AgentAccess.Defaults with
        {
            Projects = [Row("billing", AgentMode.Ask), Row("billing", AgentMode.Automatic)],
        });

    [Fact]
    public void AScope_IsNamedAsAPersonNamesIt_AndAProjectNameMayHoldNoColon()
    {
        Assert.Equal("billing:staging", Scope("billing:staging").Name);
        Assert.Equal("billing", Scope("billing").Project);
        Assert.Null(Scope("billing").Environment);
        Assert.False(ProjectScope.TryParseName("a:b:c", out _, out _));
        Assert.False(ProjectScope.TryParseName("billing:Prod", out _, out _));
    }

    /// <summary>
    /// A change rewrites only what it names: a limit, a row, a member and a value keypaste does not
    /// understand stay as the record holds them, so another writer's settings survive it.
    /// </summary>
    [Fact]
    public void AChange_RewritesOnlyWhatItNames()
    {
        Assert.Equal(_handEdited.Replace("\"values\":true", "\"values\":false", StringComparison.Ordinal), Written(Edit(values: "no"), _handEdited));
        Assert.Equal(
            _handEdited.Replace("\"run\":false,\"mode\":\"automatic\"", "\"run\":false,\"mode\":\"ask\"", StringComparison.Ordinal),
            Written(Edit(mode: "ask"), _handEdited));
        Assert.Equal(
            _handEdited.Replace("\"mode\":\"automatic\",\"colour\"", "\"mode\":\"ask\",\"colour\"", StringComparison.Ordinal),
            Written(Edit(project: "billing", mode: "ask"), _handEdited));
        Assert.Equal(
            _handEdited.Replace("{\"perHour\":5,\"perDay\":50}", "{\"perHour\":9,\"perDay\":50}", StringComparison.Ordinal),
            Written(Edit(perHour: "9"), _handEdited));
        Assert.Equal(
            _handEdited.Replace("{\"perHour\":5,\"perDay\":50}", "{\"perDay\":50}", StringComparison.Ordinal),
            Written(Edit(perHour: AccessEdit.None), _handEdited));
    }

    [Fact]
    public void ANewRow_FollowsTheRows_WithTheModeThatAppliesThereNow_AndALimitOnlyWhereNamed()
    {
        const string record = """{"acaf":1,"mode":"ask","projects":[{"project":"billing","mode":"automatic"}],"colour":"red"}""";

        Assert.Equal(
            """{"acaf":1,"mode":"ask","projects":[{"project":"billing","mode":"automatic"},{"project":"billing:qa","mode":"automatic","limit":{"perHour":3}}],"colour":"red"}""",
            Written(Edit(project: "billing:qa", perHour: "3"), record));
        Assert.Equal(
            """{"acaf":1,"mode":"ask","projects":[{"project":"billing","mode":"automatic"},{"project":"shop","mode":"allow-for-a-while"}],"colour":"red"}""",
            Written(Edit(project: "shop", mode: "allow-for-a-while"), record));
        Assert.Equal(
            """{"acaf":1,"mode":"ask","projects":[{"project":"shop","mode":"ask","limit":{"until":"2026-12-31T23:59:59Z"}}]}""",
            Written(Edit(project: "shop", until: "2026-12-31T23:59:59Z"), """{"acaf":1,"mode":"ask"}"""));
    }

    [Fact]
    public void AMemberARecordLacks_IsAddedInTheOrderOfTheFormat_AndARecordWithNoVersionGainsIt()
    {
        Assert.Equal(
            """{"acaf":1,"run":true,"mode":"automatic","limit":{"perHour":5},"later":1}""",
            Written(Edit(run: "on", perHour: "5"), """{"acaf":1,"mode":"automatic","later":1}"""));
        Assert.Equal("""{"acaf":1,"values":false,"mode":"ask"}""", Written(Edit(values: "no"), """{"mode":"ask"}"""));
    }

    [Fact]
    public void AResetOfARow_RemovesItAlone_AndTwoRowsForOneScopeBecomeOne()
    {
        const string twice = """{"acaf":1,"projects":[{"project":"billing","mode":"ask","colour":"red"},{"project":"shop","mode":"ask"},{"project":"billing","mode":"automatic"}]}""";

        Assert.Equal("""{"acaf":1,"projects":[{"project":"shop","mode":"ask"}]}""", Written(Edit(project: "billing", reset: true), twice));
        Assert.Equal("""{"acaf":1}""", Written(Edit(project: "shop", reset: true), """{"acaf":1,"projects":[{"project":"shop","mode":"ask"}]}"""));
        Assert.Equal(
            """{"acaf":1,"projects":[{"project":"billing","mode":"automatic","colour":"red"},{"project":"shop","mode":"ask"}]}""",
            Written(Edit(project: "billing", mode: "automatic"), twice));
    }

    [Fact]
    public void AReset_RewritesEveryMemberThisVersionKnows_AndKeepsTheRest() =>
        Assert.Equal(
            """{"acaf":1,"see":["tag:env:*"],"values":true,"run":false,"mode":"ask","later":{"x":[1,2]}}""",
            Written(Edit(reset: true, mode: "ask"), _handEdited));

    [Fact]
    public void AChangeThatAltersNothing_LeavesTheRecordsOwnText()
    {
        const string spaced = """{ "acaf": 1, "mode": "ask", "later": true }""";

        Assert.Same(spaced, Written(Edit(mode: "ask"), spaced));
    }

    [Fact]
    public void ARowCannotBePlaced_InProjectsThatAreNoList_WhichEveryOtherChangeKeeps()
    {
        const string record = """{"acaf":1,"mode":"automatic","projects":{"project":"billing"}}""";

        Assert.Equal(AgentAccessWrite.ProjectsUnreadable, AcafRecord.WriteChange(Edit(project: "billing", mode: "ask"), [record], out var written));
        Assert.Empty(written);
        Assert.Same(record, Written(Edit(project: "billing", reset: true), record));
        Assert.Equal("""{"acaf":1,"mode":"ask","projects":{"project":"billing"}}""", Written(Edit(mode: "ask"), record));
    }

    /// <summary>
    /// A row is not added to a full list, nor changed in a list longer than keypaste keeps, which says no
    /// rows at all, so a change to one could not be judged; a reset still removes one.
    /// </summary>
    [Fact]
    public void ARow_IsNotAddedToAFullList_NorChangedInALongerOne()
    {
        var rows = Enumerable.Range(0, AgentAccess.MaximumProjects + 1).Select(i => $$"""{"project":"p{{i}}","mode":"ask"}""").ToList();
        var full = $$"""{"acaf":1,"projects":[{{string.Join(',', rows.Take(AgentAccess.MaximumProjects))}}]}""";
        var longer = $$"""{"acaf":1,"projects":[{{string.Join(',', rows)}}]}""";

        Assert.Equal(AgentAccessWrite.TooManyProjects, AcafRecord.WriteChange(Edit(project: "billing", mode: "ask"), [full], out _));
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(Edit(project: "p3", mode: "automatic"), [full], out _));
        Assert.Equal(AgentAccessWrite.ProjectsOverLimit, AcafRecord.WriteChange(Edit(project: "p3", mode: "ask"), [longer], out _));
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(Edit(project: "p3", reset: true), [longer], out _));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData(null)]
    public void ARecordThatIsNoObject_IsReplacedByTheChangeMadeToTheDefaults(string? record) =>
        Assert.Equal(AcafRecord.Write(Asks), Written(Edit(mode: "ask"), record));

    [Fact]
    public void AnotherRecordsUnknownMembers_AreAdded_AndItsOtherSettingsAreNot() =>
        Assert.Equal(
            """{"acaf":1,"mode":"ask","colour":"red","shape":"round"}""",
            Written(Edit(mode: "ask"), """{"acaf":1,"mode":"automatic","colour":"red"}""", """{"acaf":1,"run":true,"colour":"blue","shape":"round"}""", null));

    [Fact]
    public void ARecordOfAnotherVersion_IsNeverChanged() =>
        Assert.Throws<ArgumentException>(() => AcafRecord.WriteChange(Edit(mode: "ask"), ["""{"acaf":2}"""], out _));

    /// <summary>
    /// Written into a record keypaste understands, a change reads back as the same change made to the
    /// settings the record held, a changed row keeping its place in the record.
    /// </summary>
    [Fact]
    public void AChangeWrittenIntoARecord_ReadsAsTheChangeMadeToItsSettings()
    {
        var access = new AgentAccess
        {
            See = See("tag:env:billing"),
            Run = true,
            Mode = AgentMode.Ask,
            Limit = new AccessLimit { PerHour = 4 },
            Projects = [Row("billing", AgentMode.Automatic, new AccessLimit { Until = _until }), Row("shop:qa", AgentMode.AllowForAWhile)],
        };

        AccessEdit[] changes =
        [
            Edit(mode: "automatic", until: "2027-01-01T00:00:00Z"),
            Edit(perHour: AccessEdit.None),
            Edit(see: ["**"], values: "no", run: "off"),
            Edit(project: "billing", perHour: "7", until: AccessEdit.None),
            Edit(project: "billing:dev", perHour: "3"),
            Edit(project: "shop", mode: "ask"),
            Edit(project: "shop:qa", reset: true),
            Edit(project: "nobody", reset: true),
            Edit(reset: true),
            Edit(reset: true, run: "on", perHour: "2"),
        ];

        foreach (var edit in changes)
        {
            var read = AcafRecord.Read(Written(edit, AcafRecord.Write(access)));

            Assert.True(read.Understood, edit.ToString());
            Assert.Equal(ByScope(edit.Apply(access)), ByScope(read.Access));
        }

        static AgentAccess ByScope(AgentAccess settings) =>
            settings with { Projects = [.. settings.Projects.OrderBy(row => row.Scope.Name, StringComparer.Ordinal)] };
    }

    private static AccessEdit Edit(
        string? project = null,
        string? mode = null,
        IReadOnlyList<string>? see = null,
        string? values = null,
        string? run = null,
        string? perHour = null,
        string? until = null,
        bool reset = false)
    {
        Assert.True(AccessEdit.TryCreate(project, mode, see, values, run, perHour, until, reset, out var edit, out var error), error);
        return edit;
    }

    private static string Written(AccessEdit edit, params string?[] previous)
    {
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(edit, previous, out var written));
        return written;
    }
}
