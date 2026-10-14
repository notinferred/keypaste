using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// What applies to one request: the least of the bridge's flags, the agent's records and this computer's
/// ceilings, a row beating the default, the strictest row across an item's projects, and Automatic never
/// for <c>*</c>, an unnamed bridge, a protected environment or a spent limit.
/// </summary>
public sealed class EffectiveAccessTests
{
    private const string _agent = "claude-code";

    private static readonly DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly EntryName _item = new("services", "Stripe");

    private static AgentMode Mode(EffectiveAccess access, params string[] tags) => access.Item(tags, _now).Mode;

    [Fact]
    public void AnAgentWithNoRecord_GetsTodaysBehaviour()
    {
        var access = EffectiveAccess.Create(_agent, AgentRecords.None, AccessCaps.Empty);

        Assert.True(access.Values);
        Assert.False(access.Run);
        Assert.False(access.MayRun(bridgeOffersRun: true));
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing"));
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access));
        Assert.Equal(ExposureReach.ProjectFields, access.Reach(See("**"), _item, ["env:billing"]));
        Assert.Equal(ExposureReach.None, access.Reach(See("**"), _item, []));
    }

    [Fact]
    public void ARecord_GivesWhatItSays_WithinTheBridgesFlags()
    {
        var access = EffectiveAccess.Create(_agent, Own(Automatic with { Run = true, See = See("services/**") }), AccessCaps.Empty);

        Assert.Equal(AgentMode.Automatic, Mode(access, "env:billing"));
        Assert.True(access.MayRun(bridgeOffersRun: true));
        Assert.False(access.MayRun(bridgeOffersRun: false));
        Assert.Equal(ExposureReach.Entry, access.Reach(See("**"), _item, []));
        Assert.Equal(ExposureReach.ProjectFields, access.Reach(EntryExposure.Default, _item, ["env:billing"]));
        Assert.Equal(ExposureReach.None, access.Reach(See("other/**"), _item, []));
    }

    [Fact]
    public void ARow_BeatsTheDefault_AndAnEnvironmentsRowBeatsItsProjects()
    {
        var access = EffectiveAccess.Create(
            _agent,
            Own(Asks with { Projects = [Row("billing", AgentMode.Automatic), Row("billing:staging", AgentMode.AllowForAWhile)] }),
            AccessCaps.Empty);

        Assert.Equal(AgentMode.Automatic, Mode(access, "env:billing"));
        Assert.Equal(AgentMode.Automatic, Mode(access, "env:billing:qa"));
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing:staging"));
        Assert.Equal(AgentMode.Ask, Mode(access, "env:shop"));
        Assert.Equal(AgentMode.Ask, Mode(access));
        Assert.Equal(AgentMode.Ask, Mode(access, "env:Billing", "team"));
    }

    [Fact]
    public void AnItemInSeveralProjects_TakesTheStrictest()
    {
        var access = EffectiveAccess.Create(_agent, Own(Automatic with { Projects = [Row("shop", AgentMode.Ask)] }), AccessCaps.Empty);

        Assert.Equal(AgentMode.Automatic, Mode(access, "env:billing"));
        Assert.Equal(AgentMode.Ask, Mode(access, "env:billing", "env:shop"));
        Assert.Equal(AgentMode.Ask, Mode(access, "env:shop", "env:billing"));
    }

    [Theory]
    [InlineData("env:billing:prod")]
    [InlineData("env:billing:production-eu")]
    [InlineData("env:billing:Prod")]
    public void AProtectedEnvironment_IsAlwaysAsked(string tag)
    {
        var access = EffectiveAccess.Create(
            _agent,
            Own(Automatic with { Projects = [Row("billing", AgentMode.Automatic), Row("billing:prod", AgentMode.Automatic)] }),
            AccessCaps.Empty);

        var item = access.Item([tag], _now);

        Assert.Equal(AgentMode.Ask, item.Mode);
        Assert.Empty(item.Limits);
        Assert.Equal(AgentMode.Ask, Mode(access, "env:billing", tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(AgentNames.Any)]
    [InlineData("a/b")]
    public void ABridgeWithNoName_GetsTheDefaults_WhateverTheRecordsSay(string? label)
    {
        var records = new AgentRecords(SavedRead.Current, [Understood(Automatic with { Run = true })], [Understood(Automatic with { Run = true })]);
        var access = EffectiveAccess.Create(label, records, AccessCaps.Empty);

        Assert.Null(access.Label);
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing"));
        Assert.False(access.Run);
        Assert.False(access.TryAutomatic(access.Item(["env:billing"], _now), new HourlyLimit(TimeProvider.System)));
    }

    [Fact]
    public void TheAnyRecord_OnlyNarrows_AgentsWithNoRecordOfTheirOwn()
    {
        var narrowed = Asks with { Values = false, See = See("tag:env:billing") };

        var unrecorded = EffectiveAccess.Create("cursor", Any(narrowed), AccessCaps.Empty);
        Assert.Equal(AgentMode.Ask, Mode(unrecorded, "env:billing"));
        Assert.False(unrecorded.Values);
        Assert.Equal(ExposureReach.None, unrecorded.Reach(See("**"), _item, ["env:shop"]));

        var recorded = EffectiveAccess.Create(_agent, new AgentRecords(SavedRead.Current, [Understood(Automatic)], [Understood(narrowed)]), AccessCaps.Empty);
        Assert.Equal(AgentMode.Automatic, Mode(recorded, "env:billing"));
        Assert.True(recorded.Values);

        var widening = EffectiveAccess.Create("cursor", Any(Automatic with { Run = true, See = See("**") }), AccessCaps.Empty);
        Assert.Equal(AgentMode.AllowForAWhile, Mode(widening, "env:billing"));
        Assert.False(widening.Run);
        Assert.Equal(ExposureReach.None, widening.Reach(See("**"), _item, []));
    }

    [Fact]
    public void ARecordNotWhollyUnderstood_AppliesOnlyBeneathTheDefaults()
    {
        var records = new AgentRecords(SavedRead.Current, [new AcafRead(Automatic with { Run = true }, false, true)], []);
        var access = EffectiveAccess.Create(_agent, records, AccessCaps.Empty);

        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing"));
        Assert.False(access.Run);

        var narrowing = EffectiveAccess.Create(_agent, new AgentRecords(SavedRead.Current, [new AcafRead(Asks with { Values = false }, false, false)], []), AccessCaps.Empty);

        Assert.Equal(AgentMode.Ask, Mode(narrowing, "env:billing"));
        Assert.False(narrowing.Values);
    }

    [Theory]
    [InlineData(SavedRead.Unsaved)]
    [InlineData(SavedRead.ChangedOnDisk)]
    [InlineData(SavedRead.Unreadable)]
    public void RecordsNotReadFromTheFile_ApplyOnlyBeneathTheDefaults(SavedRead state)
    {
        var access = EffectiveAccess.Create(_agent, new AgentRecords(state, [Understood(Automatic with { Run = true })], []), AccessCaps.Empty);

        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing"));
        Assert.False(access.Run);
    }

    [Fact]
    public void TwoRecordsForOneAgent_ApplyAsTheLeastOfThem()
    {
        var access = EffectiveAccess.Create(
            _agent,
            Own(Automatic with { Run = true, See = See("**") }, AgentAccess.Defaults with { Mode = AgentMode.Automatic, Values = false, Run = true }),
            AccessCaps.Empty);

        Assert.Equal(AgentMode.Automatic, Mode(access, "env:billing"));
        Assert.False(access.Values);
        Assert.True(access.Run);
        Assert.Equal(ExposureReach.None, access.Reach(See("**"), _item, []));
    }

    [Fact]
    public void ThisComputersCeilings_NarrowEveryDimension()
    {
        var caps = Caps(
            new CapsRow { Label = _agent, Mode = AgentMode.AllowForAWhile, Values = false, Run = false, See = See("tag:env:billing") },
            new CapsRow { Label = _agent, Project = Scope("billing:staging"), Mode = AgentMode.Ask });
        var access = EffectiveAccess.Create(_agent, Own(Automatic with { Run = true, See = See("**") }), caps);

        Assert.False(access.Values);
        Assert.False(access.MayRun(bridgeOffersRun: true));
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing"));
        Assert.Equal(AgentMode.Ask, Mode(access, "env:billing:staging"));
        Assert.Equal(ExposureReach.ProjectFields, access.Reach(See("**"), _item, ["env:billing"]));
        Assert.Equal(ExposureReach.None, access.Reach(See("**"), _item, ["env:shop"]));
    }

    [Fact]
    public void AnAutomaticTimeLimitThatHasPassed_AsksForAWhile()
    {
        var access = EffectiveAccess.Create(
            _agent,
            Own(Automatic with
            {
                Limit = new AccessLimit { Until = _now.AddHours(1) },
                Projects = [Row("billing", AgentMode.Automatic, new AccessLimit { Until = _now })],
            }),
            AccessCaps.Empty);

        Assert.Equal(AgentMode.Automatic, Mode(access, "env:shop"));
        Assert.Equal(AgentMode.AllowForAWhile, access.Item(["env:shop"], _now.AddHours(1)).Mode);
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing"));
        Assert.Equal(AgentMode.AllowForAWhile, Mode(access, "env:billing", "env:shop"));
    }

    [Fact]
    public void AnHourlyLimit_IsSpentByAnAutomaticRelease_AndThenAsks()
    {
        var clock = new ManualClock();
        var limits = new HourlyLimit(clock);
        var access = EffectiveAccess.Create(
            _agent,
            Own(Automatic with { Limit = new AccessLimit { PerHour = 2 }, Projects = [Row("billing", AgentMode.Automatic, new AccessLimit { PerHour = 1 })] }),
            AccessCaps.Empty);

        var shop = access.Item(["env:shop"], _now);
        var billing = access.Item(["env:billing"], _now);

        Assert.Equal(new RowLimit(string.Empty, 2), Assert.Single(shop.Limits));
        Assert.Equal(new RowLimit("billing", 1), Assert.Single(billing.Limits));

        Assert.True(access.TryAutomatic(billing, limits));
        Assert.False(access.TryAutomatic(billing, limits));
        Assert.True(access.TryAutomatic(shop, limits));
        Assert.True(access.TryAutomatic(shop, limits));
        Assert.False(access.TryAutomatic(shop, limits));

        clock.Advance(TimeSpan.FromHours(1));

        Assert.True(access.TryAutomatic(billing, limits));
    }

    [Fact]
    public void AnItemInTwoAutomaticProjects_SpendsBothLimits()
    {
        var access = EffectiveAccess.Create(
            _agent,
            Own(Automatic with { Projects = [Row("billing", AgentMode.Automatic, new AccessLimit { PerHour = 5 }), Row("shop", AgentMode.Automatic, new AccessLimit { PerHour = 1 })] }),
            AccessCaps.Empty);
        var limits = new HourlyLimit(new ManualClock());

        Assert.True(access.TryAutomatic(access.Item(["env:billing", "env:shop"], _now), limits));
        Assert.False(access.TryAutomatic(access.Item(["env:shop"], _now), limits));
        Assert.Equal(1, limits.Spent(_agent, "billing"));
        Assert.True(access.TryAutomatic(access.Item(["env:billing"], _now), limits));
    }

    [Fact]
    public void ARequestThatIsNotAutomatic_SpendsNothing()
    {
        var access = EffectiveAccess.Create(_agent, Own(AgentAccess.Defaults with { Limit = new AccessLimit { PerHour = 1 } }), AccessCaps.Empty);
        var limits = new HourlyLimit(new ManualClock());
        var item = access.Item(["env:billing"], _now);

        Assert.Empty(item.Limits);
        Assert.False(access.TryAutomatic(item, limits));
        Assert.Equal(0, limits.Spent(_agent, string.Empty));
    }
}
