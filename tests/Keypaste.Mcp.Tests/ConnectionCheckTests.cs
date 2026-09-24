using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The desktop's connection check, speaking to the real server rather than a script of it.
/// </summary>
/// <remarks>
/// The check is written by hand rather than with the SDK's client, so the only proof that it speaks
/// what the bridge accepts is the bridge answering it: the handshake, the listing and a credential
/// request each reach the real tools and come back as the check expects.
/// </remarks>
public sealed class ConnectionCheckTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_real_bridge_lists_for_the_check_and_releases_to_it_without_the_check_keeping_the_value()
    {
        await using var harness = new McpHarness();
        harness.Approver.StartApproving();
        harness.Source.With("env/dev", "STRIPE_KEY");
        harness.Source.Availability = VaultAvailability.Available;

        var channels = harness.Serve();
        await using var check = new McpConnectionCheck(channels.ClientWrites, channels.ClientReads);

        var listing = await check.ListAsync(Token);
        var entry = Assert.Single(listing.Entries);
        Assert.Equal("env/dev/STRIPE_KEY", entry.Name);

        var answer = await check.RequestAsync(entry, Token);

        Assert.Equal(McpCheckOutcome.Granted, answer.Outcome);
        Assert.DoesNotContain(FakeApprover.Sentinel, answer.ToString(), StringComparison.Ordinal);
        Assert.Contains(FakeApprover.Sentinel, harness.Transcript, StringComparison.Ordinal);

        var request = Assert.Single(harness.Approver.Received);
        Assert.Equal(McpConnectionCheck.Reason, request.Reason);
        Assert.Equal(McpConnectionCheck.ClientName, request.ClientName);
        Assert.Equal(McpHarness.ClientLabel, request.ClientLabel);
        Assert.Equal(McpConnectionCheck.TtlSeconds, request.TtlSeconds);
    }

    [Fact]
    public async Task A_refusal_from_the_real_bridge_is_a_denial_in_its_words()
    {
        await using var harness = new McpHarness();
        harness.Approver.StartRefusing(AuditMethod.Prompt);
        harness.Source.With("env/dev", "STRIPE_KEY");
        harness.Source.Availability = VaultAvailability.Available;

        var channels = harness.Serve();
        await using var check = new McpConnectionCheck(channels.ClientWrites, channels.ClientReads);

        var answer = await check.RequestAsync(Assert.Single((await check.ListAsync(Token)).Entries), Token);

        Assert.Equal(McpCheckOutcome.Denied, answer.Outcome);
        Assert.StartsWith("keypaste: DENIED.", answer.Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_locked_vault_is_the_listings_problem_rather_than_an_empty_list()
    {
        await using var harness = new McpHarness();
        harness.Approver.Start();

        var channels = harness.Serve();
        await using var check = new McpConnectionCheck(channels.ClientWrites, channels.ClientReads);

        var listing = await check.ListAsync(Token);

        Assert.Empty(listing.Entries);
        Assert.False(string.IsNullOrEmpty(listing.Problem));
    }
}
