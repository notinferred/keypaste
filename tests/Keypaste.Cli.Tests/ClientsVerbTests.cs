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

    /// <summary>The list shows each agent's own rules and the answers kept for it, each by the key --forget takes (F.78b).</summary>
    [Fact]
    public void The_list_shows_an_agents_own_rules_and_kept_answers_by_their_keys()
    {
        var answer = new RememberedAnswer("claude-code", "0123456789abcdef0123456789abcdef", "password", CategoryRule.Allow);
        var pick = new RememberedPick("claude-code", "services/Mail", "password", "fedcba9876543210fedcba9876543210");
        Directory.CreateDirectory(Home);
        Assert.True(Rules.TryWrite(AgentRules.Defaults.With("claude-code", FieldCategory.Notes, CategoryRule.Block).Remembering(answer, confirmed: true).Remembering(pick, confirmed: true), seal: null, out var problem), problem);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients"));
        var text = _harness.Out;
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "--json"));

        Assert.Contains("ITS OWN RULE", text, StringComparison.Ordinal);
        Assert.Contains($"{AgentRules.KeyOf(answer)}  always allows password of item 0123456789abcdef0123456789abcdef", text, StringComparison.Ordinal);
        Assert.Contains($"{AgentRules.KeyOf(pick)}  always picks item fedcba9876543210fedcba9876543210 for password of services/Mail", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"agent-rule\",\"agent\":\"claude-code\",\"value\":\"notes\",\"rule\":\"block\"", _harness.Out, StringComparison.Ordinal);
        Assert.Contains($"\"key\":\"{AgentRules.KeyOf(pick)}\"", _harness.Out, StringComparison.Ordinal);
    }

    /// <summary>With the vault held, a set and a forget are made by the owner; one that would give more is refused.</summary>
    [Fact]
    public async Task Set_and_forget_go_to_the_process_holding_the_vault_and_give_less_only()
    {
        await using var owner = SessionOwner.Start(_harness, records: true);
        var answer = new RememberedAnswer("claude-code", "0123456789abcdef0123456789abcdef", "password", CategoryRule.Allow);
        owner.Settings.Rules = AgentRules.Defaults.Allowing(FieldCategory.Totp, confirmed: true).Remembering(answer, confirmed: true);

        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--set", "totp=block", "--vault", _harness.VaultPath));
        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("clients", "claude-code", "--set", "totp=settings", "--vault", _harness.VaultPath));
        Assert.Contains("only the app's agent page sets", _harness.Err, StringComparison.Ordinal);
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("clients", "claude-code", "--forget", AgentRules.KeyOf(answer), "--vault", _harness.VaultPath));

        Assert.Equal(CategoryRule.Block, owner.Settings.Rules.RuleFor("claude-code", FieldCategory.Totp));
        Assert.Empty(owner.Settings.Rules.Answers);
        Assert.Contains("sealed by the process holding the vault", _harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--set", "password=allow")]
    [InlineData("--set", "passkey=block")]
    [InlineData("--set", "password")]
    [InlineData("--forget", "0123456789ab")]
    public void A_set_or_forget_that_cannot_be_made_from_a_terminal_writes_nothing(string option, string value)
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("clients", "claude-code", option, value));

        Assert.False(File.Exists(Rules.Path));
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
    [InlineData("claude-code", "--set", "notes=block", "--forget", "0123456789ab")]
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
