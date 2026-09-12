using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// One call to <c>list_entry_names</c>, and what it came back with when it did not come back with a
/// listing.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because of F.8.</b> On 2026-09-11
/// <see cref="ListingSizeTests.AVaultTooBigForOneReply_StillListsTheNamesThatFit"/> failed once on
/// <c>windows-2025</c> reading <c>StructuredContent</c> on a result that had none, and threw a
/// sentence of its own before anything read the text the tool had actually returned. Eight refusals
/// reach that shape; the audit method cannot separate three of them and the sentence can, so the one
/// fact worth having was discarded by the assertion that tripped over it.
/// </para>
/// <para>
/// <b>There is no constructor a caller can reach, and <see cref="ReadAsync"/> names the tool
/// itself.</b> That is what keeps <see cref="Report"/> off the credential path: a
/// <c>request_credential</c> result carries a live value in both its text and its structured content
/// (docs/PRODUCT.md law 3.5), and a diagnostic that quotes a result's text must be unable to receive
/// one. A helper taking a <see cref="CallToolResult"/> would be one argument away from printing a
/// credential into a run log, and a comment saying not to would not survive the first overload.
/// </para>
/// </remarks>
internal sealed class ListingCall
{
    /// <summary>How much of the reply is quoted.</summary>
    /// <remarks>
    /// Every refusal this path returns is shorter than this, so a refusal is quoted whole — which is
    /// what the two <c>Failed</c> causes are told apart by, since both are filed as
    /// <c>no-approver</c>. A rendered listing is seventy-six kilobytes of names and is identifiable
    /// from its first line, so cutting it costs nothing. Raise this if a refusal ever outgrows it;
    /// <see cref="ListingSizeTests.AListingThatCameBackWithoutStructuredContent_SaysWhatCameBackInstead"/>
    /// asserts a whole constant, so it goes red rather than quiet.
    /// </remarks>
    private const int _quoted = 600;

    private readonly CallToolResult _result;
    private readonly McpHarness _harness;
    private readonly TimeSpan _elapsed;
    private readonly PoolWatch _watch;

    private ListingCall(CallToolResult result, McpHarness harness, TimeSpan elapsed, PoolWatch watch)
    {
        _result = result;
        _harness = harness;
        _elapsed = elapsed;
        _watch = watch;
    }

    /// <summary>Calls the listing tool, and keeps enough to say what it answered.</summary>
    /// <param name="client">The connected MCP client.</param>
    /// <param name="harness">The harness serving it, for the audit log behind the answer.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call, answered.</returns>
    internal static async Task<ListingCall> ReadAsync(
        McpClient client,
        McpHarness harness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Timed apart from the test. F.8's thirteen seconds are attributable to no wait on this path
        // - the handshake grace is one second and the connect budget five hundred milliseconds - so
        // whether they were spent inside the call or around it is the next sighting's first question.
        // F.9 watches the pool for the length of the call rather than at the assertion, because a
        // reading taken after the overrun is a reading of a pool that has already drained. The
        // sampler starts only when a probe asked for one, so an ordinary run carries no extra timer.
        using var watch = PoolSnapshot.Watch("the listing call");
        PoolTimeline.Mark("listing-enter");

        var started = Stopwatch.GetTimestamp();
        var result = await client.CallToolAsync(ToolText.ListToolName, cancellationToken: cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);

        PoolTimeline.Mark("listing-exit", ((int)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

        return new ListingCall(result, harness, elapsed, watch);
    }

    /// <summary>The result itself, for assertions about the protocol shape.</summary>
    internal CallToolResult Result => _result;

    /// <summary>Every text block, concatenated.</summary>
    internal string Text =>
        string.Concat(_result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    /// <summary>The structured payload, or an exception saying what arrived instead of one.</summary>
    internal JsonElement Structured =>
        _result.StructuredContent
        ?? throw new InvalidOperationException($"the listing had no structured content. {Report()}");

    /// <summary>What this call came back with, short enough to read in a run log.</summary>
    /// <remarks>
    /// <para>
    /// Facts and no verdict, so it also serves as the message argument to an ordinary assertion.
    /// The sentence and the audit methods are both printed because neither is sufficient: an
    /// approver nobody started and an exchange that died are both <c>no-approver</c> and differ only
    /// in the sentence, while an audit write that failed and a throw in the renderer both return a
    /// refusal having written no line at all and differ only in the count.
    /// </para>
    /// <para>
    /// <b>The transcript is counted, never quoted, and the audit lines give up their method and
    /// nothing else.</b> One harness serves whatever else its test asked for — a released value
    /// reaches the transcript, and an entry name reaches a credential line's arguments — and this
    /// string is printed into a CI log that outlives the run.
    /// </para>
    /// </remarks>
    internal string Report()
    {
        var text = Text;
        var lines = _harness.AuditLines();
        var report = new StringBuilder();

        report.Append($"isError: {_result.IsError?.ToString() ?? "absent"}; ")
            .AppendLine($"the call itself took {(int)_elapsed.TotalMilliseconds} ms")
            .AppendLine($"content: {_result.Content.Count} block(s) [{string.Join(", ", _result.Content.Select(block => block.Type))}]")
            .AppendLine($"text ({text.Length} chars): {(text.Length <= _quoted ? text : text[.._quoted] + "…")}")
            .AppendLine($"audit ({lines.Length} line(s)): {string.Join(", ", lines.Select(MethodOf))}")
            .AppendLine($"transcript: {Encoding.UTF8.GetByteCount(_harness.Transcript)} bytes")
            .Append(_watch.Report());

        return report.ToString();
    }

    private static string MethodOf(string line)
    {
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.GetProperty("method").GetString()!;
    }
}
