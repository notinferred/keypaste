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

    /// <summary>With the vault held, the owner makes and seals the change, and records it.</summary>
    [Fact]
    public async Task Run_on_and_off_go_to_the_process_holding_the_vault()
    {
        await using var owner = SessionOwner.Start(_harness, records: true);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--run", "on", "--vault", _harness.VaultPath));
        Assert.True(owner.Settings.Rules.Runs("claude-code"));
        Assert.Contains("run on; each run is still asked; sealed by the process holding the vault", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--run", "off", "--vault", _harness.VaultPath));
        Assert.False(owner.Settings.Rules.Runs("claude-code"));
    }

    /// <summary>With nothing holding the vault, nothing can be sealed: turning run on is refused, and a change for less is written unsealed, which no owner follows.</summary>
    [Fact]
    public void With_nothing_holding_the_vault_only_less_is_written_and_unsealed()
    {
        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("clients", "claude-code", "--run", "on"));
        Assert.Contains("only the process holding it can seal", _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Rules.Path));

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--run", "off"));
        Assert.True(Rules.TryReadFile(out var read, out var problem), problem);
        Assert.Null(read.Seal);
        Assert.Contains("written unsealed", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("\"method\":\"rules-changed\"", File.ReadAllText(KeypasteHome.AuditPath(Home)), StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients"));
        Assert.Contains("is not sealed, so keypaste ignores it", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_refuses_an_agent_this_computer_does_not_know()
    {
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
