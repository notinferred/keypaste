using System.Text;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The env message on the owner's endpoint: a whole set on the one reply that releases it, and
/// nothing on any other.
/// </summary>
public sealed class ApproverEnvProtocolTests
{
    private const string _value = "sk_live_env_protocol_sentinel";

    [Fact]
    public void A_request_round_trips_with_its_command_as_arguments()
    {
        var sent = new EnvRequest("billing", ["npm", "run", "a b", "\"quoted\""], "/home/me/billing") { Vault = "/v.kdbx", Session = "s1" };

        Assert.Equal(ApproverMessageKind.Env, ApproverProtocol.KindOf(ApproverProtocol.Encode(sent)));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(sent), out EnvRequest? request));

        Assert.Equal("billing", request.Project);
        Assert.Equal(sent.Command, request.Command);
        Assert.Equal("/home/me/billing", request.Directory);
        Assert.Equal("/v.kdbx", request.Vault);
        Assert.Equal("s1", request.Session);
    }

    [Fact]
    public void A_released_set_round_trips_whole_and_its_description_holds_no_value()
    {
        var reply = new EnvReply(EnvResolved.Released("billing", [new EnvVariable("A", _value), new EnvVariable("EMPTY", string.Empty)]), string.Empty);

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out EnvReply? decoded));

        Assert.Equal(EnvOutcome.Resolved, decoded.Set.Outcome);
        Assert.Equal([new EnvVariable("A", _value), new EnvVariable("EMPTY", string.Empty)], decoded.Set.Variables);
        Assert.DoesNotContain(_value, reply.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_value, decoded.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_round_trips_its_problems_and_words()
    {
        var reply = new EnvReply(
            EnvResolved.Refused("billing", EnvOutcome.Unusable, [new EnvProblem("OLD", "expired 2020-01-02 03:04:05Z")]),
            "cannot be used");

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out EnvReply? decoded));

        Assert.Equal(EnvOutcome.Unusable, decoded.Set.Outcome);
        Assert.Equal([new EnvProblem("OLD", "expired 2020-01-02 03:04:05Z")], decoded.Set.Problems);
        Assert.Empty(decoded.Set.Variables);
        Assert.Equal("cannot be used", decoded.Reason);
    }

    [Fact]
    public void A_set_too_large_for_one_frame_is_refused_whole_rather_than_trimmed()
    {
        var huge = new string('x', MessageFramer.MaximumPayloadBytes);
        var reply = new EnvReply(EnvResolved.Released("billing", [new EnvVariable("A", _value), new EnvVariable("B", huge)]), string.Empty);

        var frame = ApproverProtocol.Encode(reply);

        Assert.True(frame.Length <= MessageFramer.MaximumPayloadBytes);
        Assert.DoesNotContain(_value, Encoding.UTF8.GetString(frame), StringComparison.Ordinal);
        Assert.True(ApproverProtocol.TryDecode(frame, out EnvReply? decoded));
        Assert.Equal(EnvOutcome.TooLarge, decoded.Set.Outcome);
        Assert.Empty(decoded.Set.Variables);
    }

    /// <summary>A reply that refuses and carries values, or releases with problems or no list, is not a release.</summary>
    [Theory]
    [InlineData("""{"v":2,"kind":"env","project":"p","outcome":7,"reason":"no","problems":[],"variables":[{"key":"A","value":"v"}]}""")]
    [InlineData("""{"v":2,"kind":"env","project":"p","outcome":0,"reason":"","problems":[{"key":"A","reason":"r"}],"variables":[]}""")]
    [InlineData("""{"v":2,"kind":"env","project":"p","outcome":0,"reason":"","problems":[]}""")]
    [InlineData("""{"v":2,"kind":"env","project":"p","outcome":99,"reason":"","problems":[]}""")]
    [InlineData("""{"v":2,"kind":"env","project":"p","outcome":0,"reason":"","problems":[],"variables":[{"key":"A"}]}""")]
    public void A_malformed_reply_is_refused(string frame)
    {
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvReply? reply));
        Assert.Null(reply);
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"env","vault":"/v","session":"s","project":"p","command":"npm start","directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"env","vault":"/v","project":"p","command":["npm"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"env","vault":"/v","session":"s","project":"p","command":["npm",1],"directory":"/d"}""")]
    public void A_malformed_request_is_refused(string frame)
    {
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvRequest? request));
        Assert.Null(request);
    }

    [Fact]
    public void ADevWholeSetRequest_IsTheOldBytes()
    {
        var request = new EnvRequest("billing", ["npm", "start"], "/d") { Vault = "/v", Session = "s" };

        Assert.Equal(
            """{"v":2,"kind":"env","vault":"/v","session":"s","project":"billing","command":["npm","start"],"directory":"/d"}""",
            Encoding.UTF8.GetString(ApproverProtocol.Encode(request)));
        Assert.Equal(ApproverMessageKind.Env, ApproverProtocol.KindOf(ApproverProtocol.Encode(request with { Profile = "dev" })));
    }

    [Fact]
    public void AProfileRequest_IsEnvProfile()
    {
        var sent = new EnvRequest("billing", ["npm"], "/d") { Vault = "/v", Session = "s", Profile = "staging" };
        var frame = ApproverProtocol.Encode(sent);

        Assert.Equal(ApproverMessageKind.EnvProfile, ApproverProtocol.KindOf(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out EnvRequest? request));
        Assert.Equal("staging", request.Profile);
        Assert.Null(request.Keys);
        Assert.Null(request.FileLines);
        Assert.Equal("billing", request.Project);
    }

    [Fact]
    public void AKeysSubset_IsEnvProfile()
    {
        var sent = new EnvRequest("billing", ["npm"], "/d")
        {
            Vault = "/v",
            Session = "s",
            Keys = ["B", "A"],
            FileLines = ["DB ← B", "PROXY=http://p"],
        };
        var frame = ApproverProtocol.Encode(sent);

        Assert.Equal(ApproverMessageKind.EnvProfile, ApproverProtocol.KindOf(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out EnvRequest? request));
        Assert.Equal("dev", request.Profile);
        Assert.Equal(["B", "A"], request.Keys);
        Assert.Equal(["DB ← B", "PROXY=http://p"], request.FileLines);
    }

    [Fact]
    public void AnEnvFrame_DecodesAsDev()
    {
        var frame = """{"v":2,"kind":"env","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"prod","keys":["A"]}""";

        Assert.True(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvRequest? request));
        Assert.Equal("dev", request.Profile);
        Assert.Null(request.Keys);
        Assert.Null(request.FileLines);
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"Prod"}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":1}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"dev","keys":["BAD-KEY"]}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"dev","keys":"A"}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"dev","file_lines":[1]}""")]
    public void AnEnvProfileFrameWithABadProfileOrKey_IsRejected(string frame)
    {
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvRequest? request));
        Assert.Null(request);
    }

    [Fact]
    public void An_env_profile_frame_past_its_bounds_is_rejected()
    {
        var sent = new EnvRequest("p", ["npm"], "/d") { Keys = [.. Enumerable.Repeat("A", ApproverProtocol.MaximumEnvListLength + 1)] };
        var longLine = new EnvRequest("p", ["npm"], "/d") { FileLines = [new string('x', ApproverProtocol.MaximumFileLineLength + 1)] };

        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(sent), out EnvRequest? _));
        Assert.False(ApproverProtocol.TryDecode(ApproverProtocol.Encode(longLine), out EnvRequest? _));
    }

    [Fact]
    public void TheReplyCarriesTheProfile()
    {
        var released = new EnvReply(EnvResolved.Refused("billing", EnvOutcome.Declined, profile: "staging"), "no");
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(released), out EnvReply? refused));
        Assert.Equal("staging", refused.Set.Profile);

        var frame = """{"v":2,"kind":"env","project":"p","outcome":0,"reason":"","problems":[],"variables":[{"key":"A","value":"v"}]}""";
        Assert.True(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvReply? old));
        Assert.Equal("dev", old.Set.Profile);

        var huge = new EnvReply(EnvResolved.Refused("billing", EnvOutcome.Unusable, [new EnvProblem("A", new string('x', MessageFramer.MaximumPayloadBytes))], "prod"), string.Empty);
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(huge), out EnvReply? tooLarge));
        Assert.Equal(EnvOutcome.TooLarge, tooLarge.Set.Outcome);
        Assert.Equal("prod", tooLarge.Set.Profile);
    }
}
