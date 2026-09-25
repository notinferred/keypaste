using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste grants</c> and <c>grants revoke</c> against a real owner on a real pipe: names and ids
/// only, never a value, and never a password prompt (D-0351).
/// </summary>
public sealed class GrantsVerbTests : IDisposable
{
    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args) => _harness.Run([.. args, "--vault", _harness.VaultPath]);

    [Fact]
    public async Task Grants_ListsIdAgentEntryFieldLeft_AndNoValue()
    {
        await using var owner = SessionOwner.Start(_harness);
        var id = owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants"));

        var lines = _harness.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(["ID", "AGENT", "ENTRY", "FIELD", "LEFT"], lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal([id, "claude-code", "env/acme-api/STRIPE_KEY", "password", "42m"], lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("  ", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(SessionOwner.Sentinel, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task Grants_Json()
    {
        await using var owner = SessionOwner.Start(_harness);
        var id = owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants", "--json"));

        Assert.Single(_harness.Out.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(_harness.Out);
        var grant = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(id, grant.GetProperty("id").GetString());
        Assert.Equal("credential", grant.GetProperty("kind").GetString());
        Assert.Equal("claude-code", grant.GetProperty("agent").GetString());
        Assert.Equal("env/acme-api/STRIPE_KEY", grant.GetProperty("scope").GetString());
        Assert.Equal("password", grant.GetProperty("field").GetString());
        Assert.InRange(grant.GetProperty("seconds_left").GetInt32(), 2500, 2520);
        Assert.Equal(6, grant.EnumerateObject().Count());
        Assert.DoesNotContain(SessionOwner.Sentinel, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grants_None_SaysSo()
    {
        await using var owner = SessionOwner.Start(_harness);

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants"));

        Assert.Equal("  no active grants" + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public void Grants_NothingUnlocked_SaysSo_ExitsZero()
    {
        _harness.Environment[Core.Ipc.ApproverEndpoint.EnvironmentVariable] = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants"));
        Assert.Empty(_harness.Out);
        Assert.Contains($"keypaste grants: nothing holds {_harness.VaultPath} unlocked, so nothing is granted", _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants", "--json"));
        Assert.Equal("[]" + Environment.NewLine, _harness.Out);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task Grants_ALockedOwner_HoldsNothing()
    {
        await using var owner = SessionOwner.Start(_harness);
        owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");
        owner.Lifetime.End();

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants", "--json"));

        Assert.Equal("[]" + Environment.NewLine, _harness.Out);
    }

    [Fact]
    public async Task Revoke_ById()
    {
        await using var owner = SessionOwner.Start(_harness);
        var first = owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");
        var second = owner.Grant("claude-code", "env/acme-api", "DB_URL");

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants", "revoke", first.ToUpperInvariant()));

        Assert.Contains("  ✓ revoked 1 grant" + Environment.NewLine, _harness.Err, StringComparison.Ordinal);
        Assert.Equal(second, Core.Approval.GrantId.Of(Assert.Single(owner.Authority.Activity.Grants).Key));
    }

    [Fact]
    public async Task Revoke_ByAgent()
    {
        await using var owner = SessionOwner.Start(_harness);
        owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");
        owner.Grant("claude-code", "env/acme-api", "DB_URL");
        owner.Grant("cursor", "env/acme-api", "STRIPE_KEY");

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants", "revoke", "claude-code"));

        Assert.Contains("  ✓ revoked 2 grants", _harness.Err, StringComparison.Ordinal);
        Assert.Equal("cursor", Assert.Single(owner.Authority.Activity.Grants).Approved.Client);
    }

    [Fact]
    public async Task Revoke_All()
    {
        await using var owner = SessionOwner.Start(_harness);
        owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");
        owner.Grant("cursor", "env/acme-api", "DB_URL");

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants", "revoke", "--all"));

        Assert.Contains("  ✓ revoked 2 grants", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(owner.Authority.Activity.Grants);

        _harness.AssertExit(CliApp.ExitSuccess, Run("grants"));
        Assert.Contains("no active grants", _harness.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("deadbeef")]
    public async Task Revoke_NothingMatched_Exits3(string target)
    {
        await using var owner = SessionOwner.Start(_harness);
        owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");

        _harness.AssertExit(CliApp.ExitNotFound, Run("grants", "revoke", target));

        Assert.Contains($"keypaste grants revoke: nothing matched '{target}'; nothing was revoked", _harness.Err, StringComparison.Ordinal);
        Assert.Single(owner.Authority.Activity.Grants);
    }

    [Fact]
    public void Revoke_ArgumentAndAll_IsUsage()
    {
        _harness.AssertExit(CliApp.ExitUsageError, Run("grants", "revoke", "claude-code", "--all"));
        _harness.AssertExit(CliApp.ExitUsageError, Run("grants", "revoke"));

        Assert.Contains("not both", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }
}
