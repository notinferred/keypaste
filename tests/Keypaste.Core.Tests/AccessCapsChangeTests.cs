using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// How a change moves this computer's ceilings: a narrowing lowers them so a restore cannot undo it, a
/// confirmed widening lifts them exactly as far as it goes, and with no owner only a lowering is made;
/// none of them ever lets another agent get more.
/// </summary>
public sealed class AccessCapsChangeTests
{
    private const string _agent = "claude-code";

    private static readonly DateTimeOffset _end = new(2026, 7, 26, 18, 0, 0, TimeSpan.Zero);

    private static AccessEdit Edit(string? project = null, string? mode = null, IReadOnlyList<string>? see = null, string? values = null, string? run = null, string? perHour = null, string? until = null)
    {
        Assert.True(AccessEdit.TryCreate(project, mode, see, values, run, perHour, until, false, out var edit, out var error), error);
        return edit;
    }

    private static CapsRow Own(AccessCaps caps, ProjectScope? scope = null) =>
        Assert.Single(caps.Rows, row => row.Label == _agent && row.Project == scope);

    [Fact]
    public void ANarrowing_LowersTheCeilingsForWhatItTakesAway()
    {
        var record = Automatic with { Run = true, See = See("**"), Projects = [Row("billing", AgentMode.Automatic)] };
        var current = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), AccessCaps.Empty);
        var proposed = record with { Mode = AgentMode.Ask, Run = false, Values = false, See = See("env/**"), Projects = [Row("billing", AgentMode.AllowForAWhile)] };
        var change = AccessChange.Classify(current, proposed);
        Assert.True(change.IsNarrowing);

        var lowered = AccessCaps.Empty.Narrowed(_agent, current, change);

        var row = Own(lowered);
        Assert.Equal((AgentMode.Ask, false, false), (row.Mode, row.Values, row.Run));
        Assert.Equal(["env/**"], row.See!.Globs);
        Assert.Equal(AgentMode.AllowForAWhile, Own(lowered, Scope("billing")).Mode);

        var held = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), lowered);
        Assert.Equal(AgentMode.AllowForAWhile, held.Item(["env:billing"], DateTimeOffset.UnixEpoch).Mode);
        Assert.Equal(AgentMode.Ask, held.Item([], DateTimeOffset.UnixEpoch).Mode);
        Assert.False(held.Values || held.Run);
    }

    /// <summary>
    /// Lowering never lifts a ceiling: one that already holds back part of what a narrowing sees is kept,
    /// as a narrowing of a record keypaste does not wholly understand can see more than this computer allows.
    /// </summary>
    [Fact]
    public void ASeeCeilingHoldingBackPartOfANarrowing_IsKept()
    {
        var caps = Caps(new CapsRow { Label = _agent, See = See("tag:env:*", "personal/**") });
        const string record = """{"acaf":1,"see":["**"],"later":true}""";
        var records = new AgentRecords(SavedRead.Current, [AcafRecord.Read(record)], []);
        var current = EffectiveAccess.Create(_agent, records, caps);
        Assert.Equal(AgentAccessWrite.Written, AcafRecord.WriteChange(Edit(see: ["tag:env:*", "shop/**"]), [record], out var written));
        var change = AccessChange.Classify(current, records, AcafRecord.Read(written));
        Assert.True(change.IsNarrowing);

        var lowered = caps.Narrowed(_agent, current, change).Narrowed(_agent, change.Said!, change);

        Assert.Same(Own(caps).See, Own(lowered).See);
    }

    /// <summary>An agent with no rows of its own is held to <c>*</c>'s, so its first own rows start from a copy of them.</summary>
    [Fact]
    public void AnAgentsFirstOwnRows_KeepWhatTheStarRowsHeldItTo()
    {
        var star = Caps(new CapsRow { Label = AgentNames.Any, Values = false });
        var record = AgentAccess.Defaults with { Run = true };
        var current = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), star);

        var lowered = star.Narrowed(_agent, current, AccessChange.Classify(current, record with { Run = false }));

        Assert.Equal((false, false), (Own(lowered).Values, Own(lowered).Run));
        Assert.Contains(lowered.Rows, row => row.Label == AgentNames.Any && row.Values == false);
    }

    [Fact]
    public void AConfirmedWidening_LiftsTheCeilings_AsFarAsItGoes_ForThatAgentOnly()
    {
        var caps = Caps(
            new CapsRow { Label = AgentNames.Any, Mode = AgentMode.Ask, Values = false },
            new CapsRow { Label = "cursor", Run = false });
        var current = EffectiveAccess.Create(_agent, AgentRecords.None, caps);
        var proposed = AgentAccess.Defaults with { Projects = [Row("billing:dev", AgentMode.Automatic)] };
        var change = AccessChange.Classify(current, proposed);
        Assert.False(change.IsNarrowing);

        var lifted = caps.Lifted(_agent, change);

        Assert.Null(Own(lifted).Values);
        Assert.Equal(AgentMode.AllowForAWhile, Own(lifted).Mode);
        Assert.Equal(AgentMode.Automatic, Own(lifted, Scope("billing:dev")).Mode);
        Assert.Contains(lifted.Rows, row => row.Label == AgentNames.Any && row.Mode == AgentMode.Ask && row.Values == false);
        Assert.Contains(lifted.Rows, row => row.Label == "cursor" && row.Run == false);

        var after = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(proposed), lifted);
        Assert.Equal(AgentMode.Automatic, after.Item(["env:billing"], DateTimeOffset.UnixEpoch).Mode);
        Assert.Equal(AgentMode.AllowForAWhile, after.Item(["env:billing:staging"], DateTimeOffset.UnixEpoch).Mode);
        Assert.True(after.Values);
    }

    /// <summary>An agent whose every ceiling is lifted still keeps a row, or it would fall back to <c>*</c>'s.</summary>
    [Fact]
    public void LiftingEveryCeiling_LeavesAnExplicitRow_WhileStarHasSome()
    {
        var caps = Caps(new CapsRow { Label = AgentNames.Any, Values = false });
        var current = EffectiveAccess.Create(_agent, AgentRecords.None, caps);

        var lifted = caps.Lifted(_agent, AccessChange.Classify(current, AgentAccess.Defaults));

        Assert.Equal(AgentMode.Automatic, Own(lifted).Mode);
        Assert.True(EffectiveAccess.Create(_agent, AgentRecords.None, lifted).Values);
    }

    [Fact]
    public void WithNoOwner_OnlyALoweringIsMade()
    {
        var caps = Caps(new CapsRow { Label = _agent, Mode = AgentMode.AllowForAWhile, See = See("env/**") });

        Assert.True(caps.TryLower(_agent, Edit(mode: "ask", values: "no", run: "off", see: ["env/billing/**"]), out var lowered));
        Assert.Equal((AgentMode.Ask, false, false), (Own(lowered).Mode, Own(lowered).Values, Own(lowered).Run));
        Assert.Equal(["env/billing/**"], Own(lowered).See!.Globs);

        Assert.False(caps.TryLower(_agent, Edit(mode: "automatic"), out _));
        Assert.False(caps.TryLower(_agent, Edit(values: "yes"), out _));
        Assert.False(caps.TryLower(_agent, Edit(run: "on"), out _));
        Assert.False(caps.TryLower(_agent, Edit(perHour: AccessEdit.None), out _));
        Assert.False(caps.TryLower(_agent, Edit(until: AccessEdit.None), out _));
        Assert.False(caps.TryLower(_agent, Edit(see: ["**"]), out _));
        Assert.False(Caps(new CapsRow { Label = _agent, Mode = AgentMode.Ask }).TryLower(_agent, Edit(mode: "allow-for-a-while"), out _));
    }

    /// <summary>
    /// A limit a narrowing adds on Automatic is kept as a ceiling on the row it names, so an agent's own end
    /// to Automatic holds though nothing writes it to the vault; one the record already has is not copied.
    /// </summary>
    [Fact]
    public void ANarrowingThatAddsALimit_KeepsItAsACeiling()
    {
        var record = Automatic with { See = See("**"), Projects = [Row("billing:dev", AgentMode.Automatic)] };
        var current = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), AccessCaps.Empty);
        var change = AccessChange.Classify(current, Edit(project: "billing:dev", mode: "automatic", until: UntilTime.Recorded(_end)).Apply(record));
        Assert.True(change.IsNarrowing);

        var lowered = AccessCaps.Empty.Narrowed(_agent, current, change);

        var row = Own(lowered, Scope("billing:dev"));
        Assert.Equal((AgentMode.Automatic, new AccessLimit { Until = _end }), (row.Mode, row.Limit));
        var held = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), lowered);
        Assert.Equal(AgentMode.Automatic, held.Item(["env:billing"], _end.AddMinutes(-1)).Mode);
        Assert.Equal(AgentMode.AllowForAWhile, held.Item(["env:billing"], _end).Mode);
        Assert.Equal(AgentMode.Automatic, held.Item([], _end).Mode);

        var limited = record with { Projects = [Row("billing:dev", AgentMode.Automatic, new AccessLimit { PerHour = 5 })] };
        var limitedNow = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(limited), AccessCaps.Empty);
        var valuesOff = AccessCaps.Empty.Narrowed(_agent, limitedNow, AccessChange.Classify(limitedNow, limited with { Values = false }));

        Assert.All(valuesOff.Rows, ceiling => Assert.Null(ceiling.Limit));
    }

    /// <summary>With no owner, a limit on Automatic is a lowering too: it tightens the row it names and each narrower row of the agent's.</summary>
    [Fact]
    public void WithNoOwner_ALimitIsALowering()
    {
        var caps = Caps(new CapsRow { Label = _agent, Project = Scope("billing:staging"), Mode = AgentMode.Automatic, Limit = new AccessLimit { PerHour = 9 } });

        Assert.True(caps.TryLower(_agent, Edit(project: "billing", perHour: "3"), out var lowered));

        Assert.Equal((AgentMode.Automatic, new AccessLimit { PerHour = 3 }), (Own(lowered, Scope("billing")).Mode, Own(lowered, Scope("billing")).Limit));
        Assert.Equal(new AccessLimit { PerHour = 3 }, Own(lowered, Scope("billing:staging")).Limit);

        Assert.True(lowered.TryLower(_agent, Edit(until: UntilTime.Recorded(_end)), out var ended));

        Assert.Equal(new AccessLimit { Until = _end }, Own(ended).Limit);
        Assert.Equal(new AccessLimit { PerHour = 3, Until = _end }, Own(ended, Scope("billing")).Limit);
    }

    /// <summary>
    /// A confirmed change whose limit allows more lifts this computer's limit for that environment alone,
    /// and a change that names one environment leaves what is held back elsewhere as it is.
    /// </summary>
    [Fact]
    public void AConfirmedLimit_LiftsTheCeilingsLimit_ForWhatItNamesAlone()
    {
        var caps = Caps(
            new CapsRow { Label = _agent, Project = Scope("billing:dev"), Mode = AgentMode.Automatic, Limit = new AccessLimit { PerHour = 3 } },
            new CapsRow { Label = _agent, Project = Scope("shop"), Mode = AgentMode.Automatic, Limit = new AccessLimit { Until = _end } });
        var record = Automatic with { Projects = [Row("billing:dev", AgentMode.Automatic), Row("shop", AgentMode.Automatic)] };
        var current = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), caps);
        var edit = Edit(project: "billing:dev", perHour: "20");

        var change = AccessChange.Classify(current, edit.Apply(record)).Without(AccessChange.Classify(current, record).Gains, edit.Names);

        Assert.Equal([new AccessGain(AccessDimension.Limit, Scope("billing:dev"))], change.Gains);

        var lifted = caps.Lifted(_agent, change);

        Assert.Null(Own(lifted, Scope("billing:dev")).Limit);
        Assert.Equal(new AccessLimit { Until = _end }, Own(lifted, Scope("shop")).Limit);
    }

    /// <summary>A change naming one thing leaves what this computer holds back elsewhere as it is: commands turned off after values were kept back is a narrowing.</summary>
    [Fact]
    public void AChangeNamingOneThing_IsNotARequestForWhatIsHeldBackElsewhere()
    {
        var caps = Caps(new CapsRow { Label = _agent, Values = false });
        var record = AgentAccess.Defaults with { Run = true };
        var current = EffectiveAccess.Create(_agent, AgentAccessBuilders.Own(record), caps);
        var heldBack = AccessChange.Classify(current, record).Gains;
        var runOff = Edit(run: "off");
        var valuesOn = Edit(values: "yes");

        Assert.True(AccessChange.Classify(current, runOff.Apply(record)).Without(heldBack, runOff.Names).IsNarrowing);
        Assert.Equal([new AccessGain(AccessDimension.Values, null)], AccessChange.Classify(current, valuesOn.Apply(record)).Without(heldBack, valuesOn.Names).Gains);
    }

    /// <summary>A lowered mode holds over the narrower rows of the agent that would otherwise beat it.</summary>
    [Fact]
    public void ALoweredMode_HoldsOverTheAgentsNarrowerRows()
    {
        var caps = Caps(
            new CapsRow { Label = _agent, Project = Scope("billing"), Mode = AgentMode.Automatic },
            new CapsRow { Label = _agent, Project = Scope("billing:staging"), Mode = AgentMode.Automatic },
            new CapsRow { Label = _agent, Project = Scope("shop"), Mode = AgentMode.Automatic });

        Assert.True(caps.TryLower(_agent, Edit(project: "billing", mode: "ask"), out var billing));
        Assert.Equal(AgentMode.Ask, Own(billing, Scope("billing")).Mode);
        Assert.Equal(AgentMode.Ask, Own(billing, Scope("billing:staging")).Mode);
        Assert.Equal(AgentMode.Automatic, Own(billing, Scope("shop")).Mode);

        Assert.True(caps.TryLower(_agent, Edit(mode: "allow-for-a-while"), out var agent));
        Assert.All(agent.Rows, row => Assert.Equal(AgentMode.AllowForAWhile, row.Mode));
    }
}
