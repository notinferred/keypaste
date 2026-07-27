using System.Text;
using System.Text.Json;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The wire between the bridge and the approver. It carries a credential on exactly one message, so
/// what it refuses matters more than what it round-trips.
/// </summary>
public sealed class ApproverProtocolTests
{
    private static CredentialRequest Request(string reason = "deploy billing to staging") => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = reason,
        TtlSeconds = 900,
        Exposure = ["env/**"],
        ClientName = "claude-code",
        ClientVersion = "1.2.3",
    };

    private static CredentialReply Granted(string value = "sk_live_sentinel") => new()
    {
        Decision = AuditDecision.Granted,
        Method = AuditMethod.Prompt,
        Reason = "a person approved this request",
        Entry = "env/dev/STRIPE_KEY",
        TtlSeconds = 300,
        Value = value,
    };

    [Fact]
    public void ACredentialRequestSurvivesTheRoundTrip()
    {
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request()), out CredentialRequest? decoded));

        // Member by member, not Assert.Equal on the records: a record's generated equality compares
        // the Exposure list by reference, so a whole-object comparison here would be asserting that
        // two lists are the same object rather than that the message survived.
        var original = Request();

        Assert.Equal(original.Entry, decoded.Entry, StringComparer.Ordinal);
        Assert.Equal(original.Field, decoded.Field, StringComparer.Ordinal);
        Assert.Equal(original.Reason, decoded.Reason, StringComparer.Ordinal);
        Assert.Equal(original.TtlSeconds, decoded.TtlSeconds);
        Assert.Equal(original.Exposure, decoded.Exposure);
        Assert.Equal(original.ClientName, decoded.ClientName, StringComparer.Ordinal);
        Assert.Equal(original.ClientVersion, decoded.ClientVersion, StringComparer.Ordinal);
    }

    [Fact]
    public void ACredentialReplySurvivesTheRoundTrip()
    {
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Granted()), out CredentialReply? decoded));

        // This one really can compare whole records: nothing on it is a collection.
        Assert.Equal(Granted(), decoded);
        Assert.Equal("sk_live_sentinel", decoded.Value, StringComparer.Ordinal);
    }

    [Fact]
    public void ANamesReplySurvivesTheRoundTrip()
    {
        var reply = new NamesReply(true, [new EntryName("env/dev", "STRIPE_KEY"), new EntryName("", "LOOSE")], "");

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out NamesReply? decoded));

        Assert.True(decoded.VaultUnlocked);
        Assert.Equal(reply.Names, decoded.Names);
        Assert.Equal(reply.Reason, decoded.Reason, StringComparer.Ordinal);
    }

    [Fact]
    public void ANamesRequestSurvivesTheRoundTrip()
    {
        var request = new NamesRequest(["env/**", "servers/staging/*"]);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(request), out NamesRequest? decoded));

        Assert.Equal(request.Exposure, decoded.Exposure);
    }

    /// <summary>
    /// The most dangerous line in this file's subject matter, and it is a <c>ToString</c>. A record
    /// prints every member by default, so one interpolated string in a log line, an exception
    /// message or a trace would put a live credential somewhere it can never be taken back from.
    /// </summary>
    [Fact]
    public void AReplyNeverPrintsTheCredentialItCarries()
    {
        var reply = Granted("sk_live_leak_me");

        var printed = $"{reply}";

        Assert.DoesNotContain("sk_live_leak_me", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_leak_me", reply.ToString(), StringComparison.Ordinal);
        Assert.Contains("redacted", printed, StringComparison.Ordinal);

        // ...and the value is still genuinely there, so this is not passing because the field is
        // empty. That is the trap a "no secret in the output" test usually falls into.
        Assert.Equal("sk_live_leak_me", reply.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// A grant with nothing in it is not a grant. Decoding it as one would hand an agent an empty
    /// string dressed as a credential, which is the failure mode that looks like success.
    /// </summary>
    [Fact]
    public void AGrantedReplyWithNoValue_IsRefused()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":1,"kind":"credential","decision":"granted","method":5,"reason":"ok","ttl_seconds":300}""");

        Assert.False(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Null(reply);
    }

    /// <summary>
    /// The delimiter cannot be forged. Frames are newline-separated, so a reason containing a
    /// newline would otherwise let one message be read as two.
    /// </summary>
    [Fact]
    public void NoEncodedFrame_ContainsTheDelimiter()
    {
        var hostile = "line one\nline two\r\n{\"kind\":\"credential\",\"decision\":\"granted\"}";

        foreach (var frame in new[]
                 {
                     ApproverProtocol.Encode(Request(hostile)),
                     ApproverProtocol.Encode(Granted() with { Reason = hostile }),
                     ApproverProtocol.Encode(new NamesReply(true, [new EntryName(hostile, hostile)], hostile)),
                 })
        {
            Assert.DoesNotContain((byte)'\n', frame);
            Assert.DoesNotContain((byte)'\r', frame);
        }
    }

    [Fact]
    public void AReasonWithANewlineStillArrivesIntact()
    {
        var hostile = "first\nsecond";

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(Request(hostile)), out CredentialRequest? decoded));

        Assert.Equal(hostile, decoded.Reason, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("""{"v":1}""")]
    [InlineData("""{"v":1,"kind":"credential"}""")]
    [InlineData("""{"v":2,"kind":"credential","entry":"a","field":"password","reason":"r","ttl_seconds":1,"exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"nonsense","entry":"a","field":"password","reason":"r","ttl_seconds":1,"exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"credential","entry":1,"field":"password","reason":"r","ttl_seconds":1,"exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"credential","entry":"a","field":"password","reason":"r","ttl_seconds":"nine","exposure":[]}""")]
    [InlineData("""{"v":1,"kind":"credential","entry":"a","field":"password","reason":"r","ttl_seconds":1,"exposure":[7]}""")]
    public void AMalformedFrame_IsRefusedRatherThanThrowing(string json)
    {
        var frame = Encoding.UTF8.GetBytes(json);

        Assert.False(ApproverProtocol.TryDecode(frame, out CredentialRequest? request));
        Assert.Null(request);
    }

    /// <summary>
    /// A version this build does not speak is refused rather than best-guessed. The bridge turns
    /// that into a denial, which is the fail-closed direction when two halves of keypaste have
    /// drifted apart.
    /// </summary>
    [Fact]
    public void AFrameFromAnotherVersion_IsRefused()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":99,"kind":"names","unlocked":true,"reason":"","names":[]}""");

        Assert.False(ApproverProtocol.TryDecode(frame, out NamesReply? reply));
        Assert.Null(reply);
    }

    /// <summary>
    /// A method number from a newer approver becomes <see cref="AuditMethod.Failed"/> rather than
    /// refusing the whole reply, so the exchange still produces a denial <em>and</em> an audit line.
    /// Refusing outright would lose the line, which is the one thing law 3.3 will not have.
    /// </summary>
    [Fact]
    public void AnUnknownMethod_BecomesAFailureRatherThanLosingTheReply()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":1,"kind":"credential","decision":"denied","method":9999,"reason":"who knows","ttl_seconds":0}""");

        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
    }

    /// <summary>
    /// A denied reply that somehow carries a value must not smuggle it through. Belt and braces:
    /// nothing keypaste writes does this, and a peer is not keypaste.
    /// </summary>
    [Fact]
    public void ADeniedReplyCarryingAValue_ArrivesWithoutIt()
    {
        var frame = Encoding.UTF8.GetBytes(
            """{"v":1,"kind":"credential","decision":"denied","method":7,"reason":"nobody answered","ttl_seconds":0,"value":"sk_live_smuggled"}""");

        Assert.True(ApproverProtocol.TryDecode(frame, out CredentialReply? reply));
        Assert.Null(reply.Value);
    }

    [Fact]
    public void KindOfNamesTheMessageWithoutCommittingToParsingIt()
    {
        Assert.Equal(ApproverMessageKind.Credential, ApproverProtocol.KindOf(ApproverProtocol.Encode(Request())));
        Assert.Equal(ApproverMessageKind.Names, ApproverProtocol.KindOf(ApproverProtocol.Encode(new NamesRequest([]))));
        Assert.Equal(ApproverMessageKind.Unknown, ApproverProtocol.KindOf(Encoding.UTF8.GetBytes("{}")));
        Assert.Equal(ApproverMessageKind.Unknown, ApproverProtocol.KindOf(Encoding.UTF8.GetBytes("nope")));
    }

    [Fact]
    public void EveryFrameCarriesItsVersion()
    {
        using var parsed = JsonDocument.Parse(ApproverProtocol.Encode(Request()));

        Assert.Equal(ApproverProtocol.Version, parsed.RootElement.GetProperty("v").GetInt32());
    }

    [Fact]
    public void EncodingRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((CredentialRequest)null!));
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((CredentialReply)null!));
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((NamesRequest)null!));
        Assert.Throws<ArgumentNullException>(() => ApproverProtocol.Encode((NamesReply)null!));
    }
}
