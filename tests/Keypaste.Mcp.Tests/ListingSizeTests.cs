using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// What an agent is told when a vault holds more names than one reply can carry.
/// </summary>
/// <remarks>
/// <para>
/// Through a real MCP client, a real <see cref="ApproverListener"/>, a real named pipe and the real
/// <c>ApproverEntryNameSource</c> — <c>McpHarness(pipeName)</c> leaves nothing faked but the human.
/// That matters here more than usual: the defect lived between the encoder and the framer, so a
/// test driving the tool against an in-process source would have encoded nothing and proved nothing.
/// </para>
/// <para>
/// Before F.3c an over-size reply threw inside the listener's write, took the connection down with
/// its grants, was retried once onto a fresh connection with the same result, and reached the agent
/// as "something went wrong while asking a person". Every assertion below distinguishes that from a
/// bounded reply that says what it left out.
/// </para>
/// </remarks>
public sealed class ListingSizeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static JsonElement StructuredOf(CallToolResult result) =>
        result.StructuredContent ?? throw new InvalidOperationException("the listing had no structured content");

    /// <summary>Names shaped as an ordinary vault's are: seventy-six encoded bytes each.</summary>
    private static IReadOnlyList<EntryName> Crowd(int count, string group = "env/dev/services") =>
        [.. Enumerable.Range(0, count)
            .Select(i => new EntryName(group, $"SERVICE_ACCOUNT_ACCESS_TOKEN_AB_{i:D4}"))];

    private static async Task<CallToolResult> ListAsync(McpClient client) =>
        await client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token);

    /// <summary>Stands up an approver holding the given names, and a bridge pointed at it.</summary>
    private static async Task<(FakeApprover Approver, McpHarness Harness, McpClient Client)> ConnectedAsync(
        IReadOnlyList<EntryName> names,
        string exposure = "env/**")
    {
        var approver = new FakeApprover { Names = new NamesReply(true, names, string.Empty, true) };
        approver.StartApproving();

        var harness = new McpHarness(approver.PipeName);
        var client = await harness.StartAsync("--expose", exposure);

        return (approver, harness, client);
    }

    /// <summary>A vault too big for one reply is listed, and the agent is told it is not the whole list.</summary>
    [Fact]
    public async Task AVaultTooBigForOneReply_TellsTheAgentTheListIsIncomplete()
    {
        var (approver, harness, client) = await ConnectedAsync(Crowd(1000));
        await using var _ = approver;
        await using var __ = harness;

        var result = await ListAsync(client);

        Assert.False(result.IsError);
        Assert.True(StructuredOf(result).GetProperty("truncated").GetBoolean());
        Assert.Contains(ToolText.ListingIncomplete, TextOf(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// And it is a listing, not an empty one. Without this, refusing everything would pass above.
    /// </summary>
    [Fact]
    public async Task AVaultTooBigForOneReply_StillListsTheNamesThatFit()
    {
        var (approver, harness, client) = await ConnectedAsync(Crowd(1000));
        await using var _ = approver;
        await using var __ = harness;

        var result = await ListAsync(client);

        Assert.True(StructuredOf(result).GetProperty("count").GetInt32() > 0);
        Assert.Contains("SERVICE_ACCOUNT_ACCESS_TOKEN_AB_0000", TextOf(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// A cut-off listing never presents itself as an inventory of the vault.
    /// </summary>
    /// <remarks>
    /// The header is asserted, not only the trailer. It is the line a model summarizes from, and
    /// "1000 entries, exposed by env/**" reads as a complete count — the old trailer named a number
    /// the bound no longer uses, and sat after a block of untrusted names long enough to be skimmed.
    /// </remarks>
    [Fact]
    public async Task AListingThatWasCutOff_NeverClaimsToBeTheWholeVault()
    {
        var (approver, harness, client) = await ConnectedAsync(Crowd(1000));
        await using var _ = approver;
        await using var __ = harness;

        var text = TextOf(result: await ListAsync(client));
        var header = text.Split('\n')[0];

        Assert.Contains("not all of them", header, StringComparison.Ordinal);
        Assert.DoesNotContain("cut off at", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A vault of exactly a thousand in-scope names is not truncated, and does not say it was.
    /// </summary>
    /// <remarks>
    /// The bridge used to decide this by comparing its own count against its own cap, so the one
    /// number that made the claim true also made it false: a vault holding exactly the cap was
    /// reported as cut off having lost nothing. A thousand short names is about thirty-nine
    /// kilobytes, comfortably inside one frame.
    /// </remarks>
    [Fact]
    public async Task ExactlyAThousandNamesInScope_IsNotReportedAsTruncated()
    {
        var (approver, harness, client) = await ConnectedAsync(
            [.. Enumerable.Range(0, 1000).Select(i => new EntryName("env/dev", $"KEY_{i:D4}"))]);

        await using var _ = approver;
        await using var __ = harness;

        var result = await ListAsync(client);

        Assert.False(StructuredOf(result).GetProperty("truncated").GetBoolean());
        Assert.Equal(1000, StructuredOf(result).GetProperty("count").GetInt32());
        Assert.DoesNotContain(ToolText.ListingIncomplete, TextOf(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// Names the approver dropped stay reported as missing even when the bridge filters more out.
    /// </summary>
    /// <remarks>
    /// The other direction of the same defect, and the one that was a silent omission. The bridge
    /// compared its post-filter count against its cap, so filtering anything out moved the count off
    /// that boundary and the answer came back claiming to be complete — while the approver had
    /// already cut the list short upstream, where the bridge could not see it. Here several hundred
    /// of the names are outside the exposure, so the two counts cannot coincide.
    /// </remarks>
    [Fact]
    public async Task NamesTheApproverDropped_AreNotHiddenByTheBridgesOwnFilter()
    {
        var mixed = Crowd(1500).Concat(Crowd(500, "personal/banking")).ToArray();
        var (approver, harness, client) = await ConnectedAsync(mixed);

        await using var _ = approver;
        await using var __ = harness;

        var result = await ListAsync(client);
        var structured = StructuredOf(result);

        Assert.True(structured.GetProperty("truncated").GetBoolean());
        Assert.NotEqual(1000, structured.GetProperty("count").GetInt32());
        Assert.DoesNotContain("personal/banking", TextOf(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// One name too big for any frame leaves an empty listing that says why, not one that reads as
    /// an empty vault.
    /// </summary>
    [Fact]
    public async Task ASingleEnormousName_LeavesAnEmptyButHonestListing()
    {
        var enormous = new EntryName("env/dev", new string('A', 100_000));
        var (approver, harness, client) = await ConnectedAsync([enormous]);

        await using var _ = approver;
        await using var __ = harness;

        var result = await ListAsync(client);

        Assert.False(result.IsError);
        Assert.Equal(0, StructuredOf(result).GetProperty("count").GetInt32());
        Assert.True(StructuredOf(result).GetProperty("truncated").GetBoolean());
        Assert.Contains(ToolText.ListingIncomplete, TextOf(result), StringComparison.Ordinal);
    }
}
