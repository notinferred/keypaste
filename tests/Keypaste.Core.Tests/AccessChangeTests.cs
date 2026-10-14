using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// A change applies at once only when it is provably no wider, in every dimension, than what applies
/// now, this computer's ceilings included; anything else is a widening the person confirms. A change
/// that leaves a record keypaste does not wholly understand gains only what it adds beyond what the
/// record says and what the agent would get beneath the defaults.
/// </summary>
public sealed class AccessChangeTests
{
    private const string _agent = "claude-code";

    private static readonly DateTimeOffset _until = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static EffectiveAccess Current(AgentAccess record, AccessCaps? caps = null) =>
        EffectiveAccess.Create(_agent, Own(record), caps ?? AccessCaps.Empty);

    private static IReadOnlyList<AccessGain> Gains(AgentAccess record, AgentAccess proposed, AccessCaps? caps = null) =>
        AccessChange.Classify(Current(record, caps), proposed).Gains;

    private static AccessEdit Edit(string? project = null, string? mode = null, string? values = null, string? run = null, string? perHour = null)
    {
        Assert.True(AccessEdit.TryCreate(project, mode, null, values, run, perHour, null, false, out var edit, out var error), error);
        return edit;
    }

    /// <summary>A change to a record's text, judged by what the record it is written into would let the agent do.</summary>
    private static AccessChange Judged(string record, AccessEdit edit, AccessCaps? caps = null)
    {
        var records = new AgentRecords(SavedRead.Current, [AcafRecord.Read(record)], []);

        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(edit, [record], out var written));
        return AccessChange.Classify(EffectiveAccess.Create(_agent, records, caps ?? AccessCaps.Empty), records, AcafRecord.Read(written));
    }

    [Fact]
    public void TheSameSettings_AreANarrowing()
    {
        var change = AccessChange.Classify(Current(Automatic), Automatic);

        Assert.True(change.IsNarrowing);
        Assert.Same(Automatic, change.Proposed);
    }

    [Fact]
    public void ValuesAndRun_WidenWhenTurnedOn_AndNarrowWhenTurnedOff()
    {
        var off = AgentAccess.Defaults with { Values = false };

        Assert.Equal([new AccessGain(AccessDimension.Values, null)], Gains(off, AgentAccess.Defaults));
        Assert.Equal([new AccessGain(AccessDimension.Run, null)], Gains(AgentAccess.Defaults, AgentAccess.Defaults with { Run = true }));
        Assert.Empty(Gains(AgentAccess.Defaults with { Run = true }, off));
    }

    [Theory]
    [InlineData("tag:env:billing", true)]
    [InlineData("tag:env:billing:staging", true)]
    [InlineData("tag:env:bill*", true)]
    [InlineData("**", false)]
    [InlineData("services/**", false)]
    public void WhatItSees_NarrowsOnlyUnderWhatItSeesNow(string glob, bool narrowing) =>
        Assert.Equal(narrowing, Gains(AgentAccess.Defaults, AgentAccess.Defaults with { See = See(glob) }).Count == 0);

    [Fact]
    public void AModeThatAsksLess_IsAWidening_InTheScopeWhereItAsksLess()
    {
        Assert.Equal([new AccessGain(AccessDimension.Mode, null)], Gains(AgentAccess.Defaults, Automatic));
        Assert.Equal(
            [new AccessGain(AccessDimension.Mode, Scope("billing"))],
            Gains(AgentAccess.Defaults, AgentAccess.Defaults.WithRow(Row("billing", AgentMode.Automatic))));

        var record = Asks.WithRow(Row("billing", AgentMode.AllowForAWhile));

        Assert.Equal(
            [new AccessGain(AccessDimension.Mode, Scope("billing:staging"))],
            Gains(record, record.WithRow(Row("billing:staging", AgentMode.Automatic))));
    }

    [Fact]
    public void RemovingARow_IsANarrowing_WhenTheDefaultAsksMore()
    {
        var record = Asks.WithRow(Row("billing", AgentMode.Automatic));

        Assert.Empty(Gains(record, record.WithoutRow(Scope("billing"))));
        Assert.Equal(
            [new AccessGain(AccessDimension.Mode, Scope("billing"))],
            Gains(Automatic.WithRow(Row("billing", AgentMode.Ask)), Automatic));
    }

    [Fact]
    public void AResetThatGivesMore_IsAWidening()
    {
        var narrowed = Asks with { Values = false, See = See("tag:env:billing") };
        var gains = Gains(narrowed, AgentAccess.Defaults);

        Assert.Contains(new AccessGain(AccessDimension.Values, null), gains);
        Assert.Contains(new AccessGain(AccessDimension.See, null), gains);
        Assert.Contains(new AccessGain(AccessDimension.Mode, null), gains);
    }

    [Fact]
    public void ThisComputersCeilings_CountAsWhatAppliesNow()
    {
        var caps = Caps(new CapsRow { Label = _agent, Mode = AgentMode.Ask, Values = false, See = See("tag:env:billing") });

        var gains = Gains(Automatic, Automatic, caps);

        Assert.Equal(
            [new AccessGain(AccessDimension.Values, null), new AccessGain(AccessDimension.See, null), new AccessGain(AccessDimension.Mode, null)],
            gains);
        Assert.Empty(Gains(Automatic, Asks with { Values = false, See = See("tag:env:billing:dev") }, caps));
    }

    [Fact]
    public void AProjectCeiling_IsComparedInItsOwnScope()
    {
        var caps = Caps(new CapsRow { Label = _agent, Project = Scope("billing:prod-eu"), Mode = AgentMode.Ask });

        Assert.Equal([new AccessGain(AccessDimension.Mode, Scope("billing:prod-eu"))], Gains(Automatic, Automatic, caps));
    }

    [Fact]
    public void ALimitOnAutomatic_NarrowsWhenAddedOrLowered_AndWidensOtherwise()
    {
        var limited = Automatic with { Limit = new AccessLimit { PerHour = 10, Until = _until } };

        Assert.Empty(Gains(Automatic, limited));
        Assert.Empty(Gains(limited, limited with { Limit = new AccessLimit { PerHour = 5, Until = _until.AddDays(-1) } }));
        Assert.Equal([new AccessGain(AccessDimension.Limit, null)], Gains(limited, limited with { Limit = new AccessLimit { PerHour = 11, Until = _until } }));
        Assert.Equal([new AccessGain(AccessDimension.Limit, null)], Gains(limited, limited with { Limit = new AccessLimit { PerHour = 10 } }));
        Assert.Equal([new AccessGain(AccessDimension.Limit, null)], Gains(limited, Automatic));
        Assert.Empty(Gains(limited, AgentAccess.Defaults));
    }

    [Fact]
    public void AnHourlyLimitMovedToAnotherRow_IsAWidening_SinceItsCountStartsAgain()
    {
        var limited = Automatic with { Limit = new AccessLimit { PerHour = 10 } };
        var moved = limited.WithRow(Row("billing", AgentMode.Automatic, new AccessLimit { PerHour = 1 }));

        Assert.Equal([new AccessGain(AccessDimension.Limit, Scope("billing"))], Gains(limited, moved));
    }

    /// <summary>
    /// While the record a change leaves still holds a part keypaste does not understand, it applies only
    /// beneath the defaults: what it already said is no gain, so a narrowing applies at once, and what the
    /// change adds to it is.
    /// </summary>
    [Fact]
    public void AChangeLeavingARecordNotUnderstood_GainsOnlyWhatItAdds()
    {
        const string record = """{"acaf":1,"see":["**"],"run":true,"projects":[{"project":"shop","mode":"automatic"}],"later":true}""";

        var valuesOff = Judged(record, Edit(values: "no"));

        Assert.True(valuesOff.IsNarrowing);
        Assert.NotNull(valuesOff.Said);
        Assert.Equal([new AccessGain(AccessDimension.Mode, Scope("billing"))], Judged(record, Edit(project: "billing", mode: "automatic")).Gains);
    }

    /// <summary>A change that leaves the record wholly understood gains everything the record then gives, what it said before included.</summary>
    [Fact]
    public void AChangeLeavingTheRecordUnderstood_GainsEverythingItThenGives()
    {
        var limitFixed = Judged("""{"acaf":1,"mode":"automatic","limit":{"perHour":"lots"}}""", Edit(perHour: "10"));

        Assert.Equal([new AccessGain(AccessDimension.Mode, null)], limitFixed.Gains);
        Assert.Null(limitFixed.Said);
    }

    /// <summary>A part keypaste cannot read stays as it is through a change, so a looser limit beside it is still a gain.</summary>
    [Fact]
    public void ALimitLoosenedBesideAPartKeypasteCannotRead_IsAGain()
    {
        const string record = """{"acaf":1,"mode":"automatic","limit":{"perHour":5,"burst":3}}""";

        Assert.Equal([new AccessGain(AccessDimension.Limit, null)], Judged(record, Edit(perHour: "10")).Gains);
        Assert.True(Judged(record, Edit(perHour: "2")).IsNarrowing);
    }

    /// <summary>A ceiling this computer holds is a gain to lift where the defaults give it, and none where they do not.</summary>
    [Fact]
    public void ACeilingLifted_IsAGain_OnlyWhereTheDefaultsGiveIt()
    {
        var caps = Caps(new CapsRow { Label = _agent, Values = false, Run = false });

        Assert.Equal(
            [new AccessGain(AccessDimension.Values, null)],
            Judged("""{"acaf":1,"values":true,"run":true,"later":true}""", Edit(values: "yes", run: "on"), caps).Gains);
    }

    [Fact]
    public void TheAnyName_GainsAgainstTheDefaults()
    {
        Assert.False(AccessChange.Classify(EffectiveAccess.Defaults, Automatic).IsNarrowing);
        Assert.True(AccessChange.Classify(EffectiveAccess.Defaults, Asks with { Values = false }).IsNarrowing);
    }

    [Theory]
    [InlineData("**", "tag:env:billing", true)]
    [InlineData("**", "env/**", true)]
    [InlineData("**", "a/b/c", true)]
    [InlineData("env/**", "env/billing/KEY", true)]
    [InlineData("env/**", "env/*/KEY", true)]
    [InlineData("env/**", "env/**", true)]
    [InlineData("env/**", "env", false)]
    [InlineData("env/**", "**", false)]
    [InlineData("env/**", "e*/x", false)]
    [InlineData("env/*/KEY", "env/billing/KEY", true)]
    [InlineData("env/*/KEY", "env/billing/OTHER", false)]
    [InlineData("env/billing/*", "env/billing/STRIPE_*", true)]
    [InlineData("env/billing/STRIPE_*", "env/billing/*", false)]
    [InlineData("env/billing/A*B", "env/billing/A*", false)]
    [InlineData("env/**/KEY", "env/**/KEY", true)]
    [InlineData("tag:env:*", "tag:env:billing:staging", true)]
    [InlineData("tag:env:billing", "tag:env:billing:prod", true)]
    [InlineData("tag:env:billing:staging", "tag:env:billing", false)]
    [InlineData("tag:env:*", "env/**", false)]
    [InlineData("tag:env:bill*", "tag:env:billing-*", true)]
    [InlineData("tag:env:billing-*", "tag:env:bill*", false)]
    public void APatternLiesUnderAnother_OnlyWhenThatCanBeShown(string outer, string inner, bool covered) =>
        Assert.Equal(covered, See(outer).Covers(See(inner)));
}
