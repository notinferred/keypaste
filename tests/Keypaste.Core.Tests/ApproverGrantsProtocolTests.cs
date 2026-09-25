using System.Text;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The grants, revoke-grants and lock messages: names and counts only, every read a <c>Try</c>, and a
/// listing bounded to one frame.
/// </summary>
public sealed class ApproverGrantsProtocolTests
{
    private static readonly GrantSummary _grant = new("3f9a1c02", "credential", "claude-code", "env/acme-api/STRIPE_KEY", "password", 2520);

    private static string Text(byte[] frame) => Encoding.UTF8.GetString(frame);

    private static byte[] Frame(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void AGrantsRequest_RoundTrips()
    {
        var frame = ApproverProtocol.Encode(new GrantsRequest { Vault = "/v.kdbx", Session = "s1" });

        Assert.Equal("""{"v":2,"kind":"grants","vault":"/v.kdbx","session":"s1"}""", Text(frame));
        Assert.Equal(ApproverMessageKind.Grants, ApproverProtocol.KindOf(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out GrantsRequest? request));
        Assert.Equal(new GrantsRequest { Vault = "/v.kdbx", Session = "s1" }, request);
    }

    [Fact]
    public void AGrantsReply_RoundTrips()
    {
        var frame = ApproverProtocol.Encode(new GrantsReply(true, [_grant], true, string.Empty));

        Assert.Equal(
            """{"v":2,"kind":"grants","answered":true,"complete":true,"reason":"","grants":[{"id":"3f9a1c02","kind":"credential","client":"claude-code","scope":"env/acme-api/STRIPE_KEY","field":"password","seconds_left":2520}]}""",
            Text(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out GrantsReply? reply));
        Assert.True(reply.Answered);
        Assert.True(reply.Complete);
        Assert.Equal([_grant], reply.Grants);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new GrantsReply(false, [], true, "the vault is locked")), out GrantsReply? refused));
        Assert.False(refused.Answered);
        Assert.Equal("the vault is locked", refused.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("claude-code")]
    public void ARevokeRequest_RoundTrips(string? client)
    {
        var sent = new RevokeGrantsRequest(["3f9a1c02", "00ff00ff"], client, All: false) { Vault = "/v.kdbx", Session = "s1" };
        var frame = ApproverProtocol.Encode(sent);

        Assert.Equal(ApproverMessageKind.RevokeGrants, ApproverProtocol.KindOf(frame));
        Assert.Contains(client is null ? "\"client\":null" : "\"client\":\"claude-code\"", Text(frame), StringComparison.Ordinal);
        Assert.True(ApproverProtocol.TryDecode(frame, out RevokeGrantsRequest? request));
        Assert.Equal(sent.Ids, request.Ids);
        Assert.Equal(client, request.Client);
        Assert.False(request.All);
        Assert.Equal("/v.kdbx", request.Vault);
        Assert.Equal("s1", request.Session);
    }

    [Fact]
    public void ARevokeReply_RoundTrips()
    {
        var frame = ApproverProtocol.Encode(new RevokeGrantsReply(2, string.Empty));

        Assert.Equal("""{"v":2,"kind":"revoke-grants","revoked":2,"reason":""}""", Text(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out RevokeGrantsReply? reply));
        Assert.Equal(new RevokeGrantsReply(2, string.Empty), reply);
    }

    [Fact]
    public void ALockRequestAndReply_RoundTrip()
    {
        var request = ApproverProtocol.Encode(new LockRequest { Vault = "/v.kdbx", Session = "s1" });
        var reply = ApproverProtocol.Encode(new LockReply(true, string.Empty));

        Assert.Equal("""{"v":2,"kind":"lock","vault":"/v.kdbx","session":"s1"}""", Text(request));
        Assert.Equal("""{"v":2,"kind":"lock","locking":true,"reason":""}""", Text(reply));
        Assert.Equal(ApproverMessageKind.Lock, ApproverProtocol.KindOf(request));
        Assert.True(ApproverProtocol.TryDecode(request, out LockRequest? decodedRequest));
        Assert.Equal("s1", decodedRequest.Session);
        Assert.True(ApproverProtocol.TryDecode(reply, out LockReply? decodedReply));
        Assert.Equal(new LockReply(true, string.Empty), decodedReply);
    }

    /// <summary>A request is never read as a reply, nor one kind as another.</summary>
    [Fact]
    public void AMessageIsOnlyItsOwnKind()
    {
        var grants = ApproverProtocol.Encode(new GrantsRequest { Vault = "/v", Session = "s" });
        var lockNow = ApproverProtocol.Encode(new LockRequest { Vault = "/v", Session = "s" });

        Assert.False(ApproverProtocol.TryDecode(grants, out GrantsReply? _));
        Assert.False(ApproverProtocol.TryDecode(grants, out LockRequest? _));
        Assert.False(ApproverProtocol.TryDecode(lockNow, out GrantsRequest? _));
        Assert.False(ApproverProtocol.TryDecode(lockNow, out LockReply? _));
    }

    public static TheoryData<string> Malformed => new()
    {
        """{"v":1,"kind":"grants","vault":"/v","session":"s"}""",
        """{"v":2,"kind":"grants","vault":"/v"}""",
        """{"v":2,"kind":"grants","vault":"/v","session":"s","session":"t"}""",
        """{"v":2,"kind":"revoke-grants","vault":"/v","session":"s","ids":["not-an-id"],"client":null,"all":false}""",
        """{"v":2,"kind":"revoke-grants","vault":"/v","session":"s","ids":["3f9a1c02"],"client":7,"all":false}""",
        """{"v":2,"kind":"revoke-grants","vault":"/v","session":"s","ids":["3f9a1c02"],"all":false}""",
        """{"v":2,"kind":"revoke-grants","vault":"/v","session":"s","ids":"3f9a1c02","client":null,"all":false}""",
        """{"v":2,"kind":"revoke-grants","vault":"/v","session":"s","ids":[],"client":null,"all":"yes"}""",
        """{"v":2,"kind":"lock","vault":"/v"}""",
        "[]",
        "not json",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedRequest_IsRefused(string json)
    {
        var frame = Frame(json);

        Assert.False(ApproverProtocol.TryDecode(frame, out GrantsRequest? _));
        Assert.False(ApproverProtocol.TryDecode(frame, out RevokeGrantsRequest? _));
        Assert.False(ApproverProtocol.TryDecode(frame, out LockRequest? _));
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"grants","answered":true,"reason":"","grants":[{"id":"x","kind":"credential","client":"c","scope":"e","field":"password","seconds_left":-1}]}""")]
    [InlineData("""{"v":2,"kind":"grants","answered":true,"reason":"","grants":[{"id":"x","kind":"credential","client":"c","scope":"e","field":"password"}]}""")]
    [InlineData("""{"v":2,"kind":"grants","answered":"yes","reason":"","grants":[]}""")]
    [InlineData("""{"v":2,"kind":"revoke-grants","revoked":-1,"reason":""}""")]
    [InlineData("""{"v":2,"kind":"lock","locking":"true","reason":""}""")]
    public void AMalformedReply_IsRefused(string json)
    {
        var frame = Frame(json);

        Assert.False(ApproverProtocol.TryDecode(frame, out GrantsReply? _));
        Assert.False(ApproverProtocol.TryDecode(frame, out RevokeGrantsReply? _));
        Assert.False(ApproverProtocol.TryDecode(frame, out LockReply? _));
    }

    /// <summary>Absence of <c>complete</c> reads as not complete, as a names reply's does.</summary>
    [Fact]
    public void AGrantsReplyWithoutComplete_IsNotComplete()
    {
        Assert.True(ApproverProtocol.TryDecode(Frame("""{"v":2,"kind":"grants","answered":true,"reason":"","grants":[]}"""), out GrantsReply? reply));
        Assert.False(reply.Complete);
    }

    [Fact]
    public void MoreIdsOrRowsThanOneMessageAllows_AreRefused()
    {
        var ids = string.Join(',', Enumerable.Range(0, ApproverProtocol.MaximumGrantRows + 1).Select(i => $"\"{i:x8}\""));
        var row = """{"id":"3f9a1c02","kind":"credential","client":"c","scope":"e","field":"password","seconds_left":1}""";
        var rows = string.Join(',', Enumerable.Repeat(row, ApproverProtocol.MaximumGrantRows + 1));

        Assert.False(ApproverProtocol.TryDecode(
            Frame($$"""{"v":2,"kind":"revoke-grants","vault":"/v","session":"s","ids":[{{ids}}],"client":null,"all":false}"""),
            out RevokeGrantsRequest? _));
        Assert.False(ApproverProtocol.TryDecode(
            Frame($$"""{"v":2,"kind":"grants","answered":true,"complete":true,"reason":"","grants":[{{rows}}]}"""),
            out GrantsReply? _));
    }

    [Fact]
    public void TheGrantsEncoder_KeepsAtMostTheRowLimit_AndClearsComplete()
    {
        var many = Enumerable.Range(0, ApproverProtocol.MaximumGrantRows + 44)
            .Select(i => _grant with { Id = $"{i:x8}" })
            .ToList();

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new GrantsReply(true, many, true, string.Empty)), out GrantsReply? reply));

        Assert.Equal(ApproverProtocol.MaximumGrantRows, reply.Grants.Count);
        Assert.Equal(many.Take(ApproverProtocol.MaximumGrantRows), reply.Grants);
        Assert.False(reply.Complete);
    }

    [Fact]
    public void TheGrantsEncoder_TrimsToOneFrame_AndClearsComplete()
    {
        var wide = new string('é', 2000);
        var many = Enumerable.Range(0, 200)
            .Select(i => _grant with { Id = $"{i:x8}", Scope = wide })
            .ToList();

        var frame = ApproverProtocol.Encode(new GrantsReply(true, many, true, string.Empty));

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out GrantsReply? reply));
        Assert.InRange(reply.Grants.Count, 1, many.Count - 1);
        Assert.Equal(many.Take(reply.Grants.Count), reply.Grants);
        Assert.False(reply.Complete);
    }

    [Fact]
    public void AGrantsReplyThatFits_StaysComplete()
    {
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(new GrantsReply(true, [_grant, _grant with { Id = "00000001" }], true, string.Empty)), out GrantsReply? reply));

        Assert.Equal(2, reply.Grants.Count);
        Assert.True(reply.Complete);
    }
}
