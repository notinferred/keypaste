using Keypaste.Core.Agents;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// A change to an agent's settings in the CLI's words: what they mean, what they refuse, that a change
/// touches only what it names, and that its end time is read and shown on this computer's clock and
/// crosses the pipe in UTC.
/// </summary>
public sealed class AccessEditTests
{
    private static readonly DateTimeOffset _now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public void AProjectRow_TakesItsModeAndLimit_AndLeavesTheRestAsItWas()
    {
        var edit = Edit(project: "billing:staging", mode: "automatic", perHour: "20", until: "2026-12-31T23:59:59Z");

        var applied = edit.Apply(AgentAccess.Defaults);

        var row = Assert.Single(applied.Projects);
        Assert.Equal(("billing:staging", AgentMode.Automatic, 20), (row.Scope.Name, row.Mode, row.Limit.PerHour));
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero), row.Limit.Until);
        Assert.Equal(AgentAccess.Defaults with { Projects = applied.Projects }, applied);
        Assert.Equal("project billing:staging, mode automatic, per-hour 20, until 2026-12-31T23:59:59Z", edit.ToString());
    }

    [Fact]
    public void TheWholeAgentsSettings_ApplyWithNoProject()
    {
        var applied = Edit(mode: "ask", see: ["env/**"], values: "no", run: "on", until: "2027-01-02T03:04:05Z").Apply(AgentAccess.Defaults);

        Assert.Equal(AgentMode.Ask, applied.Mode);
        Assert.Equal(["env/**"], applied.See.Globs);
        Assert.False(applied.Values);
        Assert.True(applied.Run);
        Assert.Equal(new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero), applied.Limit.Until);
        Assert.Empty(applied.Projects);
    }

    [Fact]
    public void AProjectWithNoMode_TakesTheModeThatAppliesThereNow()
    {
        var current = Automatic with { Projects = [Row("billing", AgentMode.Ask)] };

        var applied = Edit(project: "billing:staging", perHour: "5").Apply(current);

        Assert.Equal(AgentMode.Ask, applied.Projects.Single(row => row.Scope == Scope("billing:staging")).Mode);
        Assert.Equal(5, applied.Projects.Single(row => row.Scope == Scope("billing:staging")).Limit.PerHour);
    }

    [Fact]
    public void None_RemovesALimit_AndResetStartsFromTheDefaults()
    {
        var limited = Automatic with { Limit = new AccessLimit { PerHour = 3, Until = DateTimeOffset.UnixEpoch }, Run = true, Projects = [Row("billing", AgentMode.Ask)] };

        Assert.True(Edit(perHour: "none").Apply(limited).Limit is { PerHour: null, Until: not null });
        Assert.True(Edit(until: "none").Apply(limited).Limit is { PerHour: 3, Until: null });
        Assert.Equal(AgentAccess.Defaults, Edit(reset: true).Apply(limited));
        Assert.Equal(limited with { Projects = [] }, Edit(project: "billing", reset: true).Apply(limited));
        Assert.Equal(AgentAccess.Defaults with { Mode = AgentMode.Ask }, Edit(mode: "ask", reset: true).Apply(limited));
    }

    [Theory]
    [InlineData(null, "sometimes", null, null, null, null, false)]
    [InlineData(null, null, "maybe", null, null, null, false)]
    [InlineData(null, null, null, "yes", null, null, false)]
    [InlineData(null, null, null, null, "0", null, false)]
    [InlineData(null, null, null, null, "1001", null, false)]
    [InlineData(null, null, null, null, "+5", null, false)]
    [InlineData(null, null, null, null, null, "tomorrow", false)]
    [InlineData(null, null, null, null, null, "2026-12-31", false)]
    [InlineData(null, null, null, null, null, "2026-12-31T18:00:00+02:00", false)]
    [InlineData("billing:staging:extra", "ask", null, null, null, null, false)]
    [InlineData("billing", "ask", null, null, null, null, true)]
    [InlineData("billing", null, "no", null, null, null, false)]
    [InlineData(null, null, null, null, null, null, false)]
    public void WordsThatMakeNoChange_AreRefused(string? project, string? mode, string? values, string? run, string? perHour, string? until, bool reset)
    {
        Assert.False(AccessEdit.TryCreate(project, mode, null, values, run, perHour, until, reset, out var edit, out var error));
        Assert.Null(edit);
        Assert.NotEmpty(error);
    }

    private static ManualClock Clock(TimeZoneInfo zone) => new(_now) { Zone = zone };

    [Fact]
    public void ASeeWithAProject_OrABadSelector_IsRefused()
    {
        Assert.False(AccessEdit.TryCreate("billing", null, ["env/**"], null, null, null, null, false, out _, out _));
        Assert.False(AccessEdit.TryCreate(null, null, [new string('a', EntryExposure.MaximumGlobLength + 1)], null, null, null, null, false, out _, out _));
    }

    /// <summary>A person's or an agent's time is on this computer's clock unless it names its offset, and is sent in UTC, as the record holds it.</summary>
    [Theory]
    [InlineData("2026-12-31", "2026-12-31T21:59:59Z")]
    [InlineData(" 2026-12-31 ", "2026-12-31T21:59:59Z")]
    [InlineData("2026-12-31 18:00", "2026-12-31T16:00:00Z")]
    [InlineData("2026-12-31T18:00", "2026-12-31T16:00:00Z")]
    [InlineData("2026-12-31 9:05:30", "2026-12-31T07:05:30Z")]
    [InlineData("2026-12-31T18:00:00Z", "2026-12-31T18:00:00Z")]
    [InlineData("2026-12-31 18:00 -05:00", "2026-12-31T23:00:00Z")]
    [InlineData("2026-12-31T18:00+05:30", "2026-12-31T12:30:00Z")]
    public void ATimeAPersonGives_IsOnThisComputersClock_UnlessItNamesItsOffset_AndIsSentInUtc(string given, string sent)
    {
        Assert.True(UntilTime.TryRecord(given, Clock(TestZones.PlusTwo), out var recorded, out var error), error);
        Assert.Equal(sent, recorded);
        Assert.Equal(sent, Edit(until: recorded).ToString()[6..]);
    }

    /// <summary>East of UTC a date ends before midnight UTC, and west of it after, so Automatic ends when the person's own day does.</summary>
    [Fact]
    public void ADate_EndsAtTheCloseOfThatDayOnThisComputer_EastOrWestOfUtc()
    {
        Assert.True(UntilTime.TryRead("2026-12-31", TestZones.PlusTwo, _now, out var east, out _));
        Assert.True(UntilTime.TryRead("2026-12-31", TestZones.MinusFive, _now, out var west, out _));
        Assert.True(UntilTime.TryRead("2026-07-01", TestZones.Berlin, _now, out var summer, out _));

        Assert.Equal(new DateTimeOffset(2026, 12, 31, 21, 59, 59, TimeSpan.Zero), east);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 4, 59, 59, TimeSpan.Zero), west);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 21, 59, 59, TimeSpan.Zero), summer);
    }

    /// <summary>A time the clock shows twice as it goes back is the earlier, which ends Automatic sooner; one it skips is refused, saying why.</summary>
    [Fact]
    public void ATimeTheClockShowsTwice_IsTheEarlier_AndOneItSkips_IsRefused()
    {
        Assert.True(UntilTime.TryRead("2026-10-25 02:30", TestZones.Berlin, _now, out var twice, out _));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), twice);

        Assert.False(UntilTime.TryRead("2026-03-29 02:30", TestZones.Berlin, _now, out _, out var skipped));
        Assert.Equal("until 2026-03-29 02:30 never happens on this computer's clock, which skips it as it goes forward; give a time after the change", skipped);
    }

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("")]
    [InlineData("31/12/2026")]
    [InlineData("2026-12-31+02:00")]
    [InlineData("2026-12-31 24:00")]
    [InlineData("2026-12-31 18:00 +14:30")]
    [InlineData("2026-12-31 18:00 +2:00")]
    public void WordsThatAreNoTime_AreRefused_WithTheRule(string given)
    {
        Assert.False(UntilTime.TryRead(given, TestZones.PlusTwo, _now, out _, out var error));
        Assert.Equal($"until {UntilTime.Rule}", error);
    }

    [Fact]
    public void ATimeAlreadyPast_IsRefused_ShownOnThisComputersClock()
    {
        Assert.False(UntilTime.TryRecord("2026-01-14", Clock(TestZones.PlusTwo), out var recorded, out var error));

        Assert.Null(recorded);
        Assert.Equal("until 2026-01-14 23:59 +02:00 has already passed on this computer's clock", error);
    }

    [Fact]
    public void NoTime_AndNone_PassAsTheyAre()
    {
        Assert.True(UntilTime.TryRecord(null, Clock(TestZones.PlusTwo), out var nothing, out _));
        Assert.True(UntilTime.TryRecord(AccessEdit.None, Clock(TestZones.PlusTwo), out var none, out _));

        Assert.Null(nothing);
        Assert.Equal(AccessEdit.None, none);
    }

    /// <summary>
    /// A person reads an end on this computer's clock with its offset then, to the second where its seconds
    /// are not zero and the close of a day as its minute, and the owner's words naming a change are shown that way too.
    /// </summary>
    [Fact]
    public void AnEnd_IsShownOnThisComputersClock_WithItsOffsetThen()
    {
        var winter = new DateTimeOffset(2026, 12, 31, 21, 59, 59, TimeSpan.Zero);
        var edit = Edit(mode: "automatic", until: "2026-12-31T21:59:59Z");

        Assert.Equal("2026-12-31 23:59 +02:00", UntilTime.Shown(winter, TestZones.PlusTwo));
        Assert.Equal("2026-12-31 16:59:59 -05:00", UntilTime.Shown(winter, TestZones.MinusFive));
        Assert.Equal("2026-12-31 22:59:59 +01:00", UntilTime.Shown(winter, TestZones.Berlin));
        Assert.Equal("2026-07-01 23:59 +02:00", UntilTime.Shown(new DateTimeOffset(2026, 7, 1, 21, 59, 59, TimeSpan.Zero), TestZones.Berlin));
        Assert.Equal("2026-12-31 18:00 +02:00", UntilTime.Shown(new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero), TestZones.PlusTwo));
        Assert.Equal("2026-12-31 18:00:59 +02:00", UntilTime.Shown(new DateTimeOffset(2026, 12, 31, 16, 0, 59, TimeSpan.Zero), TestZones.PlusTwo));
        Assert.True(UntilTime.IsCloseOfDay(winter, TestZones.PlusTwo));
        Assert.False(UntilTime.IsCloseOfDay(winter, TestZones.Berlin));

        Assert.Equal("mode automatic, until 2026-12-31T21:59:59Z", edit.ToString());
        Assert.Equal(
            "a person confirmed it (claude-code: mode automatic, until 2026-12-31 23:59 +02:00)",
            edit.OnThisClock("a person confirmed it (claude-code: mode automatic, until 2026-12-31T21:59:59Z)", TestZones.PlusTwo));
        Assert.Equal(
            "until 2026-12-31 23:59 +02:00, until none, until 2026-12-31T21:59:59 and until 2026-12-31 16:00:30 +02:00",
            UntilTime.OnThisClock("until 2026-12-31T21:59:59Z, until none, until 2026-12-31T21:59:59 and until 2026-12-31T14:00:30Z", TestZones.PlusTwo));
    }

    /// <summary>A change's words show each character of a project name or selector that could hide or reorder text as an escape.</summary>
    [Fact]
    public void AChangesWords_ShowEveryCharacterOfWhatItNames()
    {
        Assert.True(AccessEdit.TryCreate("bill\u2028ing", "ask", null, null, null, null, null, false, out var row, out var error), error);
        Assert.True(AccessEdit.TryCreate(null, null, ["x\u200B/**"], null, null, null, null, false, out var see, out error), error);

        Assert.Equal("project bill\\u2028ing, mode ask", row.ToString());
        Assert.Equal("see x\\u200B/**", see.ToString());
    }

    /// <summary>Always for makes one environment Automatic from now on: an end its row had goes, and its hourly limit stays.</summary>
    [Fact]
    public void AlwaysFor_IsAutomaticForOneEnvironment_WithNoEnd()
    {
        var edit = AccessEdit.Automatic(Scope("billing:dev"));
        var applied = edit.Apply(AgentAccess.Defaults);
        var ended = AgentAccess.Defaults with
        {
            Projects = [Row("billing:dev", AgentMode.Automatic, new AccessLimit { PerHour = 5, Until = DateTimeOffset.UnixEpoch })],
        };

        Assert.Equal(Row("billing:dev", AgentMode.Automatic), Assert.Single(applied.Projects));
        Assert.Equal(AgentMode.AllowForAWhile, applied.Mode);
        Assert.Equal(Row("billing:dev", AgentMode.Automatic, new AccessLimit { PerHour = 5 }), Assert.Single(edit.Apply(ended).Projects));
        Assert.Equal("project billing:dev, mode automatic, until none", edit.ToString());
    }
}
