using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste clients</c> lists this computer's agent rules and the agents it knows, turns an agent's run on or off and
/// removes one, in <c>agents.toml</c>, needing nothing to hold the vault; every run is still asked.
/// </summary>
public sealed class ClientsVerbTests : IDisposable
{
    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Home => _harness.Environment[KeypasteHome.EnvironmentVariable];

    private AgentRulesFile Rules => new(KeypasteHome.AgentsPath(Home));

    [Fact]
    public void With_no_file_it_lists_every_category_asked_and_writes_nothing()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients"));

        Assert.Contains("passwords", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("passkeys", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("block", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("allow", _harness.Out, StringComparison.Ordinal);
        Assert.False(File.Exists(Rules.Path));
    }

    [Fact]
    public void Run_on_and_off_is_kept_on_this_computer_and_recorded()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--run", "on"));
        Assert.True(Rules.TryRead(out var on, out var problem), problem);
        Assert.True(on.Runs("claude-code"));
        Assert.Contains("run on; each run is still asked", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--run", "off"));
        Assert.True(Rules.TryRead(out var off, out problem), problem);
        Assert.False(off.Runs("claude-code"));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "--json"));
        Assert.Contains("\"name\":\"claude-code\",\"run\":\"off\"", _harness.Out, StringComparison.Ordinal);

        var log = File.ReadAllText(KeypasteHome.AuditPath(Home));
        Assert.Equal(2, log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"method\":\"rules-changed\"", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_forgets_a_known_agent_and_refuses_an_unknown_one()
    {
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--run", "on"));
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--remove"));
        Assert.True(Rules.TryRead(out var rules, out var problem), problem);
        Assert.Empty(rules.Agents);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("clients", "claude-code", "--remove"));
        Assert.Contains("knows no agent named claude-code", _harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("claude-code", "--run", "maybe")]
    [InlineData("claude-code", "--run", "on", "--remove")]
    [InlineData("*", "--run", "on")]
    [InlineData("--run", "on")]
    public void An_unusable_change_is_refused_and_writes_nothing(params string[] arguments)
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run(["clients", .. arguments]));

        Assert.False(File.Exists(Rules.Path));
    }

    [Fact]
    public void A_file_keypaste_cannot_read_is_named_and_left_as_it_is()
    {
        Directory.CreateDirectory(Home);
        File.WriteAllText(Rules.Path, "[[rules]]\npassword = \"sometimes\"\n");

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("clients", "claude-code", "--run", "on"));

        Assert.Contains("line 2", _harness.Err, StringComparison.Ordinal);
        Assert.Equal("[[rules]]\npassword = \"sometimes\"\n", File.ReadAllText(Rules.Path));
    }
}
