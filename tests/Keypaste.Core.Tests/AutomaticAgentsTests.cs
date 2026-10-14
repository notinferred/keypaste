using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// Who an owner says is answered without asking: each named agent Automatic somewhere now, with where,
/// judged as its requests are, beneath this computer's ceilings and against an end that has passed.
/// </summary>
public sealed class AutomaticAgentsTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheAutomaticAgents_AreNamedWithWhere_LeavingOutStarAndRecordsNotUnderstood()
    {
        var billing = AgentAccess.Defaults with { Projects = [Row("billing:dev", AgentMode.Automatic), Row("shop", AgentMode.Ask)] };

        List<AgentRecord> records =
        [
            new AgentRecord(AgentNames.Any, Understood(Automatic)),
            new AgentRecord("claude-code", Understood(billing)),
            new AgentRecord("codex", Understood(Automatic)),
            new AgentRecord("cursor", new AcafRead(Automatic, false, true)),
            new AgentRecord("quiet", Understood(AgentAccess.Defaults)),
        ];

        Assert.Equal(["claude-code (billing:dev)", "codex (by default)"], AutomaticAgents.Named(records, AccessCaps.Empty, _now));
    }

    /// <summary>An agent this computer holds back, one whose Automatic has ended and a protected row are named nowhere they no longer release.</summary>
    [Fact]
    public void WhatTheCeilingsHoldBack_AnEndThatHasPassed_AndAProtectedRow_AreNotNamed()
    {
        var ended = Automatic with { Limit = new AccessLimit { Until = _now.AddMinutes(-1) } };
        var lasting = Automatic with { Limit = new AccessLimit { Until = _now.AddHours(1), PerHour = 5 } };
        var rows = AgentAccess.Defaults with { Projects = [Row("billing:prod", AgentMode.Automatic), Row("shop", AgentMode.Automatic)] };
        Assert.True(AccessCaps.TryParse("[[client]]\nlabel = \"held\"\nmode = \"ask\"\n"u8, out var caps, out var problem), problem);

        List<AgentRecord> records =
        [
            new AgentRecord("ended", Understood(ended)),
            new AgentRecord("held", Understood(Automatic)),
            new AgentRecord("lasting", Understood(lasting)),
            new AgentRecord("rows", Understood(rows)),
        ];

        Assert.Equal(["lasting (by default)", "rows (shop)"], AutomaticAgents.Named(records, caps, _now));
    }
}
