using System.Diagnostics;
using System.Globalization;
using System.Text;
using Keypaste.Core.Infrastructure;
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

        Assert.Equal(ApproverMessageKind.EnvProfile, ApproverProtocol.KindOf(ApproverProtocol.Encode(sent)));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(sent), out EnvRequest? request));

        Assert.Equal("billing", request.Project);
        Assert.Equal("dev", request.Profile);
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

    /// <summary>A set travels in a reply of its own bound, so one far past a listing's or a credential's frame arrives whole (D-0454).</summary>
    [Fact]
    public void A_set_of_a_mebibyte_round_trips_whole()
    {
        var certificate = new string('c', 1024 * 1024);
        var reply = new EnvReply(EnvResolved.Released("billing", [new EnvVariable("A", _value), new EnvVariable("CERTIFICATE", certificate)]), string.Empty);

        var frame = ApproverProtocol.Encode(reply);

        Assert.True(frame.Length > MessageFramer.MaximumPayloadBytes);
        Assert.True(ApproverProtocol.TryDecode(frame, out EnvReply? decoded));
        Assert.Equal(EnvOutcome.Resolved, decoded.Set.Outcome);
        Assert.Equal([new EnvVariable("A", _value), new EnvVariable("CERTIFICATE", certificate)], decoded.Set.Variables);
    }

    /// <summary>
    /// The env reply's bound carries any set macOS can start a program with: a value as long as
    /// <c>execve</c> takes arguments and environment together, of the character that escapes to six bytes.
    /// </summary>
    [Fact]
    public void On_macOS_a_value_of_ARG_MAX_characters_that_escape_to_six_bytes_each_fits_one_reply()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("The bound is sized to macOS's ARG_MAX.");
            return;
        }

        var start = new ProcessStartInfo("/usr/bin/getconf") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("ARG_MAX");
        using var getconf = Process.Start(start)!;
        var argMax = int.Parse(getconf.StandardOutput.ReadToEnd().Trim(), CultureInfo.InvariantCulture);
        getconf.WaitForExit();

        var reply = new EnvReply(EnvResolved.Released("billing", [new EnvVariable("CERTIFICATE", new string('+', argMax))]), string.Empty);

        Assert.Equal(0, getconf.ExitCode);
        Assert.True(ApproverProtocol.Fits(reply));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out EnvReply? decoded));
        Assert.Equal(EnvOutcome.Resolved, decoded.Set.Outcome);
    }

    [Fact]
    public void A_set_too_large_for_one_frame_is_refused_whole_rather_than_trimmed()
    {
        var huge = new string('x', ApproverProtocol.MaximumEnvReplyFrameBytes);
        var reply = new EnvReply(EnvResolved.Released("billing", [new EnvVariable("A", _value), new EnvVariable("B", huge)]), string.Empty);

        var frame = ApproverProtocol.Encode(reply);

        Assert.False(ApproverProtocol.Fits(reply));
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
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":"npm start","directory":"/d","profile":"dev"}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","project":"p","command":["npm"],"directory":"/d","profile":"dev"}""")]
    [InlineData("""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm",1],"directory":"/d","profile":"dev"}""")]
    public void A_malformed_request_is_refused(string frame)
    {
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvRequest? request));
        Assert.Null(request);
    }

    [Fact]
    public void ADevWholeSetRequest_IsEnvProfileNamingDev()
    {
        var request = new EnvRequest("billing", ["npm", "start"], "/d") { Vault = "/v", Session = "s" };

        Assert.Equal(
            """{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"billing","command":["npm","start"],"directory":"/d","profile":"dev"}""",
            Encoding.UTF8.GetString(ApproverProtocol.Encode(request)));
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

    /// <summary>The reply's kind is no request: an owner answers a request only as <c>env-profile</c> (D-0416).</summary>
    [Theory]
    [InlineData("""{"v":2,"kind":"env","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"env","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"dev"}""")]
    public void AnEnvFrame_IsNoRequest(string frame)
    {
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvRequest? request));
        Assert.Null(request);
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

    /// <summary>A launch pass travels with the request it was issued for, and a request without one says none.</summary>
    [Fact]
    public void A_launch_pass_round_trips_and_its_absence_decodes_as_none()
    {
        var pass = new string('A', 20) + "-_" + new string('z', 21);
        var sent = new EnvRequest("billing", ["/bin/sh", "-c", "exec \"${SHELL:-/bin/sh}\"", "keypaste"], "/d") { Vault = "/v", Session = "s", Launch = pass };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(sent), out EnvRequest? launched));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(sent with { Launch = null }), out EnvRequest? asked));

        Assert.Equal(pass, launched.Launch);
        Assert.Equal(sent.Command, launched.Command);
        Assert.Null(asked.Launch);
        Assert.DoesNotContain(pass, sent.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(pass, launched.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A pass of any other shape is no request, so an empty or broken one never decodes as a request to ask about.</summary>
    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("43")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+\"")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"")]
    [InlineData("\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAé\"")]
    public void A_malformed_launch_pass_is_refused_at_decode(string launch)
    {
        var frame = $$"""{"v":2,"kind":"env-profile","vault":"/v","session":"s","project":"p","command":["npm"],"directory":"/d","profile":"dev","launch":{{launch}}}""";

        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out EnvRequest? request));
        Assert.Null(request);
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

        var huge = new EnvReply(EnvResolved.Refused("billing", EnvOutcome.Unusable, [new EnvProblem("A", new string('x', ApproverProtocol.MaximumEnvReplyFrameBytes))], "prod"), string.Empty);
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(huge), out EnvReply? tooLarge));
        Assert.Equal(EnvOutcome.TooLarge, tooLarge.Set.Outcome);
        Assert.Equal("prod", tooLarge.Set.Profile);
    }
}
