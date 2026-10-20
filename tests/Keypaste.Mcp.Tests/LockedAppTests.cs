using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// A bridge about to refuse its agent because nothing holds the vault tells the app open and locked on that
/// vault, and the agent is told to unlock it there; the call is refused and recorded as it was before (D-0466).
/// </summary>
public sealed class LockedAppTests
{
    private const string _nobody = "no keypaste process holds the vault unlocked";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _quiet = TimeSpan.FromSeconds(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Credential => new()
    {
        ["entry"] = "env/dev/STRIPE_KEY",
        ["field"] = "password",
        ["reason"] = "deploy the billing service to staging",
        ["ttl_seconds"] = 60,
    };

    private static Dictionary<string, object?> Change(string name, string value) =>
        new() { ["reason"] = "the deploy task runs the billing migration", [name] = value };

    private static Dictionary<string, object?> Run(McpHarness harness) => new()
    {
        ["command"] = new[] { Reporter.Path },
        ["directory"] = Path.GetDirectoryName(harness.VaultPath),
        ["project"] = "acme-api",
        ["reason"] = "run the pending migration",
    };

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static JsonElement Line(string text)
    {
        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    // An approver nobody runs, so every call finds nothing holding the vault, a listing included; in its own home, so a change for less has ceilings to lower.
    private static McpHarness WithNobodyHoldingTheVault() =>
        new("keypaste-locked-app-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))) { InItsOwnHome = true };

    // A request for a value, a run, a listing and a change for more, in that order.
    private static async Task<List<CallToolResult>> AskEveryWayAsync(McpClient client, McpHarness harness)
    {
        List<CallToolResult> results = [];

        results.Add(await client.CallToolAsync(ToolText.CredentialToolName, Credential, cancellationToken: Token));
        results.Add(await client.CallToolAsync(ToolText.RunToolName, Run(harness), cancellationToken: Token));
        results.Add(await client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token));
        results.Add(await client.CallToolAsync(ToolText.AccessToolName, Change("mode", "automatic"), cancellationToken: Token));

        return results;
    }

    [Fact]
    public async Task WithTheAppLockedOnThisVault_EachRefusalSaysToUnlockItThere_AndIsRecordedAsBefore()
    {
        await using var harness = WithNobodyHoldingTheVault();
        var client = await harness.StartAsync("--allow-run");
        using var heard = new SemaphoreSlim(0);
        using var app = LockedAppEndpoint.Listen(harness.LockedAppName!, () => heard.Release(), _ => { });

        var results = await AskEveryWayAsync(client, harness);

        Assert.All(results, result => Assert.True(result.IsError));
        Assert.Equal([ToolText.AppLocked, ToolText.AppLocked, ToolText.AppLockedForListing, ToolText.AccessAppLocked], results.Select(TextOf));

        var lines = harness.AuditLines().Select(Line).ToList();
        Assert.Equal(
            [
                (AuditTools.Credential, "denied", "no-approver"),
                (AuditTools.Run, "denied", "no-approver"),
                (AuditTools.List, "denied", "no-approver"),
                (AuditTools.AccessChange, "denied", "no-approver"),
            ],
            lines.Select(line => (Text(line, "tool"), Text(line, "decision"), Text(line, "method"))));
        Assert.Equal((_nobody, _nobody, ToolText.NoApproverForListing), (Text(lines[0], "reason"), Text(lines[1], "reason"), Text(lines[2], "reason")));
        Assert.Equal($"{_nobody} ({McpHarness.ClientLabel}: mode automatic)", Text(lines[3], "reason"));

        for (var told = 0; told < results.Count; told++)
        {
            Assert.True(await heard.WaitAsync(_wait, Token), $"the app heard {told} of {results.Count} refusals");
        }

        Assert.Equal(0, heard.CurrentCount);
    }

    [Fact]
    public async Task AnAgentsChangeForLess_AppliesWithoutTellingTheLockedApp()
    {
        await using var harness = WithNobodyHoldingTheVault();
        var client = await harness.StartAsync();
        using var heard = new SemaphoreSlim(0);
        using var app = LockedAppEndpoint.Listen(harness.LockedAppName!, () => heard.Release(), _ => { });

        var narrowed = await client.CallToolAsync(ToolText.AccessToolName, Change("values", "no"), cancellationToken: Token);

        Assert.False(narrowed.IsError);
        Assert.Equal(ToolText.AccessNarrowed, TextOf(narrowed));
        Assert.False(await heard.WaitAsync(_quiet, Token), "a change for less told the locked app");
    }

    [Fact]
    public async Task AnOwnerThatAnswers_NeverTellsTheLockedApp()
    {
        await using var approver = new FakeApprover();
        approver.Start();
        await using var harness = new McpHarness(approver.PipeName) { InItsOwnHome = true };
        var client = await harness.StartAsync("--allow-run");
        using var heard = new SemaphoreSlim(0);
        using var app = LockedAppEndpoint.Listen(harness.LockedAppName!, () => heard.Release(), _ => { });

        var results = await AskEveryWayAsync(client, harness);

        Assert.Single(approver.Received);
        Assert.Equal(1, approver.Runs);
        Assert.Single(approver.AccessReceived);
        Assert.DoesNotContain(results, result => TextOf(result).Contains("keypaste app is open", StringComparison.Ordinal));
        Assert.False(await heard.WaitAsync(_quiet, Token), "an owner's answer told the locked app");
    }

    [Fact]
    public async Task AnAppLockedOnAnotherVault_IsNotTold_AndTheRefusalIsTodays()
    {
        await using var harness = WithNobodyHoldingTheVault();
        var client = await harness.StartAsync("--allow-run");
        var another = LockedAppEndpoint.NameFor(VaultIdentity.Of(
            Path.GetDirectoryName(harness.AuditPath)!,
            Path.Combine(Path.GetDirectoryName(harness.VaultPath)!, "other.kdbx")));
        using var heard = new SemaphoreSlim(0);
        using var app = LockedAppEndpoint.Listen(another, () => heard.Release(), _ => { });

        var results = await AskEveryWayAsync(client, harness);

        Assert.NotEqual(harness.LockedAppName, another);
        Assert.Equal([ToolText.NoApprover, ToolText.NoApprover, ToolText.NoApproverForListing, ToolText.AccessNoOwner], results.Select(TextOf));
        Assert.False(await heard.WaitAsync(_quiet, Token), "a bridge told the app locked on another vault");
    }
}
