using System.Text.Json;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste mcp policy</c>, the one place to read and change agents' settings: through the process
/// holding the vault, where a change that gives more waits for the person, and with nothing holding
/// it, only a lowering of this computer's ceilings.
/// </summary>
public sealed class McpPolicyVerbTests : IDisposable
{
    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Home => _harness.Environment[KeypasteHome.EnvironmentVariable];

    private string ClientsPath => KeypasteHome.ClientsPath(Home);

    private AccessCaps Caps()
    {
        Assert.True(AccessCaps.TryLoad(ClientsPath, out var caps, out var problem), problem);
        return caps;
    }

    [Fact]
    public void WithNoOwner_TheListShowsTheDefaults_AndSaysTheVaultsSettingsAreNotShown()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy"));

        Assert.Contains("  AGENT", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("*   (default)", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("allow-for-a-while  yes     off  tag:env:*", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("so only this computer's ceilings are shown", _harness.Err, StringComparison.Ordinal);
    }

    /// <summary>With nothing holding the vault, a change that only lowers this computer's ceilings is made, and recorded.</summary>
    [Fact]
    public void WithNoOwner_ALowering_IsWrittenAsACeiling()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "cursor", "--mode", "ask", "--values", "no"));

        Assert.Contains("applied on this computer, since it gives the agent nothing more (mode ask, values no)", _harness.Err, StringComparison.Ordinal);
        var row = Assert.Single(Caps().Rows);
        Assert.Equal(("cursor", AgentMode.Ask, false), (row.Label, row.Mode, row.Values));

        var line = Assert.Single(File.ReadAllLines(KeypasteHome.AuditPath(Home)));
        using var parsed = JsonDocument.Parse(line);
        Assert.Equal("access_change", parsed.RootElement.GetProperty("tool").GetString());
        Assert.Equal("narrowing", parsed.RootElement.GetProperty("method").GetString());
        Assert.Equal("cursor", parsed.RootElement.GetProperty("client").GetProperty("label").GetString());
    }

    /// <summary>With nothing holding the vault, anything that could give more, or remove what only the vault holds, is refused, saying how to start an owner.</summary>
    [Theory]
    [InlineData("--mode", "automatic")]
    [InlineData("--values", "yes")]
    [InlineData("--run", "on")]
    [InlineData("--per-hour", "none")]
    [InlineData("--reset", null)]
    public void WithNoOwner_AWidening_IsRefused(string option, string? value)
    {
        string[] args = value is null ? ["mcp", "policy", "cursor", option] : ["mcp", "policy", "cursor", option, value];

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run(args));

        Assert.Contains("Unlock the vault in the app or run `keypaste agent`, then run this again.", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(ClientsPath));
    }

    /// <summary>With nothing holding the vault, a limit on Automatic only lowers this computer's ceilings, so it is made there and listed with them.</summary>
    [Fact]
    public void WithNoOwner_ALimit_IsKeptAsACeiling()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "cursor", "--per-hour", "5"));
        _harness.Stdout.GetStringBuilder().Clear();

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--json"));

        Assert.Equal("""[{"kind":"ceiling","label":"cursor","per_hour":5}]""" + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public void Json_ListsEachCeilingAsARow()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "*", "--values", "no"));
        _harness.Stdout.GetStringBuilder().Clear();

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--json"));

        Assert.Equal("""[{"kind":"ceiling","label":"*","values":"no"}]""" + Environment.NewLine, _harness.Out);
    }

    [Theory]
    [InlineData("has/slash", "--mode", "ask")]
    [InlineData("cursor", "--mode", "sometimes")]
    [InlineData("cursor", "--values", "maybe")]
    [InlineData("cursor", "--project", "a:b:c")]
    [InlineData("cursor", "--until", "tomorrow")]
    [InlineData("cursor", null, null)]
    public void BadWords_AreUsage(string agent, string? option, string? value)
    {
        string[] args = option is null ? ["mcp", "policy", agent] : ["mcp", "policy", agent, option, value!];

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(args));

        Assert.False(File.Exists(ClientsPath));
    }

    /// <summary>The file before agents had settings is not this file: its <c>policy</c> form is refused, naming its line, and left as it was.</summary>
    [Fact]
    public void TheOldClientsFile_IsRefused_AndUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClientsPath)!);
        File.WriteAllText(ClientsPath, "[[client]]\nlabel = \"cursor\"\npolicy = \"ask\"\n");
        var before = File.ReadAllBytes(ClientsPath);

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("mcp", "policy"));
        Assert.Contains("line 3", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("fix it or delete it", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("mcp", "policy", "cursor", "--mode", "ask"));
        Assert.Equal(before, File.ReadAllBytes(ClientsPath));
    }

    [Fact]
    public async Task ThroughAnOwner_TheListShowsTheVaultsSettings()
    {
        _harness.SeedVault("unused-master");
        await using var owner = SessionOwner.Start(_harness);
        owner.Settings.Records["cursor"] = AgentAccess.Defaults with { Mode = AgentMode.Ask, Projects = [Row("billing:dev")] };

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--vault", _harness.VaultPath));

        Assert.Contains("  cursor ", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("billing:dev", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("automatic", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("ceilings are shown", _harness.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Through an owner, every agent is listed however long the listing, asked for in turns, and an agent
    /// whose record alone is too long for one reply is listed with its settings for the whole agent and a
    /// line saying how many project rows are not shown and where they all are.
    /// </summary>
    [Fact]
    public async Task ThroughAnOwner_EveryAgentIsListed_ARecordTooLongForOneReplyIncluded()
    {
        _harness.SeedVault("unused-master");
        await using var owner = SessionOwner.Start(_harness);
        owner.Settings.Records["a-agent"] = AgentAccess.Defaults with { Mode = AgentMode.Ask };
        owner.Settings.Records["b-huge"] = AgentAccess.Defaults with
        {
            Mode = AgentMode.Ask,
            Projects = [.. Enumerable.Range(0, AgentAccess.MaximumProjects).Select(i => Row($"{new string('é', 100)}{i}"))],
        };
        owner.Settings.Records["c-agent"] = AgentAccess.Defaults with { Run = true };

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--vault", _harness.VaultPath));

        Assert.Contains("  a-agent ", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("  b-huge ", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("  c-agent ", _harness.Out, StringComparison.Ordinal);
        Assert.Contains(
            $"b-huge has {AgentAccess.MaximumProjects} project rows not shown here, more than one reply carries; the app's Agents screen shows them all",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.DoesNotContain("than one reply holds", _harness.Err, StringComparison.Ordinal);

        var before = _harness.Out.Length;
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--json", "--vault", _harness.VaultPath));

        using var listed = JsonDocument.Parse(_harness.Out[before..]);
        var agents = listed.RootElement.EnumerateArray().Where(row => row.GetProperty("kind").GetString() == "agent").ToList();
        Assert.Equal(["a-agent", "b-huge", "c-agent"], agents.Select(row => row.GetProperty("name").GetString()));
        Assert.Equal(AgentAccess.MaximumProjects, agents[1].GetProperty("projects_not_shown").GetInt32());
        Assert.False(agents[0].TryGetProperty("projects_not_shown", out _));
    }

    /// <summary>
    /// Through an owner, a change that gives more waits for the person: confirmed it is saved, refused
    /// nothing changes, and the next request for more, for any agent, is refused unasked with when to try again.
    /// </summary>
    [Fact]
    public async Task ThroughAnOwner_AWidening_WaitsForThePerson()
    {
        _harness.SeedVault("unused-master");
        await using var owner = SessionOwner.Start(_harness, records: true);

        owner.Asked.Change = ApprovalAnswer.ApprovedOnce;
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "cursor", "--values", "no", "--vault", _harness.VaultPath));
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "cursor", "--project", "billing", "--mode", "automatic", "--vault", _harness.VaultPath));

        Assert.Contains("applied at once, since it gives the agent nothing more (cursor: values no)", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("a person confirmed it (cursor: project billing, mode automatic)", _harness.Err, StringComparison.Ordinal);
        var saved = owner.Settings.Records["cursor"];
        Assert.False(saved.Values);
        Assert.Equal(AgentMode.Automatic, Assert.Single(saved.Projects).Mode);

        owner.Asked.Change = ApprovalAnswer.Denied;
        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("mcp", "policy", "cursor", "--run", "on", "--vault", _harness.VaultPath));
        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("mcp", "policy", "codex", "--mode", "automatic", "--vault", _harness.VaultPath));

        Assert.Contains("not changed: a person refused this change (cursor: run on)", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("not changed: a request from an agent or a terminal for more access, or to change the vault, was refused or went unanswered a moment ago", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("try again in ", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("or make it in the app's own agent settings, which do not wait (codex: mode automatic)", _harness.Err, StringComparison.Ordinal);
        Assert.False(owner.Settings.Records["cursor"].Run);
        Assert.False(owner.Settings.Records.ContainsKey("codex"));
        Assert.Equal(AccessOrigin.Terminal, owner.Asked.LastChange!.Origin);
    }

    /// <summary>
    /// <c>--until</c> is read on this computer's clock and sent in UTC, which the vault's record and the
    /// audit line hold, and the change and the list say it back on this computer's clock with its offset.
    /// </summary>
    [Fact]
    public async Task ThroughAnOwner_UntilIsReadAndShownOnThisComputersClock_AndSentInUtc()
    {
        _harness.SeedVault("unused-master");
        _harness.Clock.Zone = TestZones.PlusTwo;
        await using var owner = SessionOwner.Start(_harness, records: true);
        owner.Asked.Change = ApprovalAnswer.ApprovedOnce;

        _harness.AssertExit(
            CliApp.ExitSuccess,
            _harness.Run("mcp", "policy", "cursor", "--mode", "automatic", "--until", "2026-12-31", "--per-hour", "20", "--vault", _harness.VaultPath));

        Assert.Equal(
            new AccessLimit { PerHour = 20, Until = new DateTimeOffset(2026, 12, 31, 21, 59, 59, TimeSpan.Zero) },
            owner.Settings.Records["cursor"].Limit);
        Assert.Contains("a person confirmed it (cursor: mode automatic, per-hour 20, until 2026-12-31 23:59 +02:00)", _harness.Err, StringComparison.Ordinal);
        Assert.True(AuditReader.TryRead(KeypasteHome.AuditPath(Home), out var entries, out _, out var error), error);
        Assert.EndsWith("(cursor: mode automatic, per-hour 20, until 2026-12-31T21:59:59Z)", entries[^1].Reason, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("mcp", "policy", "--vault", _harness.VaultPath));

        Assert.Contains("automatic, until 2026-12-31 23:59 +02:00, at most 20 an hour", _harness.Out, StringComparison.Ordinal);
    }

    /// <summary>An end already past on this computer's clock is refused before anything is asked, shown on that clock.</summary>
    [Fact]
    public void AnUntilAlreadyPast_IsRefused_BeforeAnythingIsAsked()
    {
        _harness.Clock.Zone = TestZones.PlusTwo;

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("mcp", "policy", "cursor", "--until", "2026-07-26 17:00"));

        Assert.Contains("until 2026-07-26 17:00 +02:00 has already passed on this computer's clock", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(ClientsPath));
    }

    private static ProjectAccess Row(string name)
    {
        Assert.True(ProjectScope.TryParseName(name, out var scope, out var error), error);
        return new ProjectAccess(scope, AgentMode.Automatic);
    }
}
