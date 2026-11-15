using System.Text;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Infrastructure;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The <c>set</c> frames: a linked set of two to four fields round-trips, anything outside its bounds is refused, and a reply carries
/// every field or none, never a set cut short to fit.
/// </summary>
public sealed class ApproverSetProtocolTests
{
    private const string _sentinel = "SET-WIRE-SENTINEL-4e1a";

    private static SetRequest Request(int fields = 3) => new()
    {
        Items = [.. Enumerable.Range(0, fields).Select(index => new SetItem($"work/db{index}", "password"))],
        Reason = "migrate the database",
        Exposure = ["work/**"],
        ClientName = "claude-code",
        ClientVersion = "2.1.0",
        ClientLabel = "billing-bot",
        Vault = "/vault.kdbx",
        Session = "session-one",
    };

    private static SetReply Granted(params string[] values) => new()
    {
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "a person approved this one set",
        Fields = [.. values.Select((value, index) => new SetField { Entry = $"work/db{index}", Field = "password", Value = value, CodeSeconds = index })],
        Session = "session-one",
        Recorded = true,
    };

    [Fact]
    public void A_request_round_trips()
    {
        var frame = ApproverProtocol.Encode(Request());

        Assert.Equal(ApproverMessageKind.Set, ApproverProtocol.KindOf(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out SetRequest? decoded));
        Assert.Equal(Request().Items, decoded.Items);
        Assert.Equal(("migrate the database", "claude-code", "2.1.0", "billing-bot"), (decoded.Reason, decoded.ClientName, decoded.ClientVersion, decoded.ClientLabel));
        Assert.Equal(("/vault.kdbx", "session-one"), (decoded.Vault, decoded.Session));
        Assert.Equal(["work/**"], decoded.Exposure);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void A_request_of_the_wrong_size_is_refused(int fields) =>
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request(fields)), out SetRequest? _));

    [Fact]
    public void A_request_past_any_bound_is_refused()
    {
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request() with { Items = [new(new string('e', 513), "password"), new("a", "b")] }), out SetRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request() with { Items = [new("a", new string('f', 129)), new("a", "b")] }), out SetRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request() with { Reason = new string('r', 2001) }), out SetRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request() with { Exposure = [.. Enumerable.Repeat("**", ApproverProtocol.MaximumExposureGlobs + 1)] }), out SetRequest? _));
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"set","vault":"/v","session":"s","items":[{"entry":"a","field":"p"},null],"reason":"r","exposure":[]}""")]
    [InlineData("""{"v":2,"kind":"set","vault":"/v","session":"s","items":[{"entry":"a"},{"entry":"b","field":"p"}],"reason":"r","exposure":[]}""")]
    [InlineData("""{"v":2,"kind":"set","vault":"/v","session":"s","items":"a","reason":"r","exposure":[]}""")]
    [InlineData("""{"v":2,"kind":"set","vault":"/v","session":"s","items":[{"entry":"a","field":"p"},{"entry":"b","field":"p"}],"reason":"r","reason":"s","exposure":[]}""")]
    public void A_malformed_request_is_refused(string frame) =>
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out SetRequest? _));

    [Fact]
    public void A_granted_reply_round_trips_every_field_in_order()
    {
        var frame = ApproverProtocol.Encode(Granted(_sentinel + "0", _sentinel + "1", _sentinel + "2"));

        Assert.True(ApproverProtocol.TryDecode(frame, out SetReply? decoded));
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, true, "session-one"), (decoded.Decision, decoded.Method, decoded.Recorded, decoded.Session));
        Assert.Equal(
            [("work/db0", "password", _sentinel + "0", 0), ("work/db1", "password", _sentinel + "1", 1), ("work/db2", "password", _sentinel + "2", 2)],
            decoded.Fields.Select(field => (field.Entry, field.Field, field.Value, field.CodeSeconds)));
        Assert.DoesNotContain(_sentinel, decoded.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_sentinel, decoded.Fields[0].ToString(), StringComparison.Ordinal);
    }

    /// <summary>A reply too large for one frame is sent as a refusal carrying none of its values, never the fields that fit.</summary>
    [Fact]
    public void A_reply_too_large_for_one_frame_is_refused_whole()
    {
        var large = new string('x', MessageFramer.MaximumPayloadBytes / 3);
        var reply = Granted(large, large, large, large);

        var frame = ApproverProtocol.Encode(reply);

        Assert.False(ApproverProtocol.Fits(reply));
        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out SetReply? decoded));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Undeliverable), (decoded.Decision, decoded.Method));
        Assert.Empty(decoded.Fields);
        Assert.DoesNotContain("xxxx", Encoding.UTF8.GetString(frame), StringComparison.Ordinal);
    }

    /// <summary>A grant must carry two to four fields, each with a value; a refusal must carry none.</summary>
    [Theory]
    [InlineData("""{"v":2,"kind":"set","decision":"granted","method":1,"reason":"r","fields":[{"entry":"a","field":"p","value":"v","code_seconds":0}]}""")]
    [InlineData("""{"v":2,"kind":"set","decision":"granted","method":1,"reason":"r","fields":[{"entry":"a","field":"p","value":"","code_seconds":0},{"entry":"b","field":"p","value":"v","code_seconds":0}]}""")]
    [InlineData("""{"v":2,"kind":"set","decision":"denied","method":1,"reason":"r","fields":[{"entry":"a","field":"p","value":"v","code_seconds":0},{"entry":"b","field":"p","value":"v","code_seconds":0}]}""")]
    [InlineData("""{"v":2,"kind":"set","decision":"granted","method":1,"reason":"r"}""")]
    public void A_reply_outside_its_bounds_is_refused(string frame) =>
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out SetReply? _));

    [Fact]
    public void A_refusal_round_trips_with_no_fields()
    {
        var frame = ApproverProtocol.Encode(new SetReply { Decision = AuditDecision.Denied, Method = AuditMethod.Blocked, Reason = "blocked", Recorded = true });

        Assert.True(ApproverProtocol.TryDecode(frame, out SetReply? decoded));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked, "blocked", true), (decoded.Decision, decoded.Method, decoded.Reason, decoded.Recorded));
        Assert.Empty(decoded.Fields);
    }
}
