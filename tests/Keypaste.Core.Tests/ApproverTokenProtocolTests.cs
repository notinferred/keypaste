using System.Text;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The token-env message: every field round trips, anything short of it is refused, and no description prints the token.</summary>
public sealed class ApproverTokenProtocolTests
{
    private static readonly string _token = TokenSecret.New(out _, out _);

    private static TokenEnvRequest Request() =>
        new(_token, "acme-api", "staging", ["npm", "run", "a b"], "/home/me/acme") { Vault = "/v.kdbx", Session = "s1" };

    [Fact]
    public void ARequest_RoundTrips()
    {
        var frame = ApproverProtocol.Encode(Request());

        Assert.Equal(ApproverMessageKind.TokenEnv, ApproverProtocol.KindOf(frame));
        Assert.True(ApproverProtocol.TryDecode(frame, out TokenEnvRequest? decoded));

        Assert.Equal(_token, decoded.Token);
        Assert.Equal("acme-api", decoded.Project);
        Assert.Equal("staging", decoded.Profile);
        Assert.Equal(["npm", "run", "a b"], decoded.Command);
        Assert.Equal("/home/me/acme", decoded.Directory);
        Assert.Equal("/v.kdbx", decoded.Vault);
        Assert.Equal("s1", decoded.Session);
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"token-env","vault":"/v","session":"s","project":"p","profile":"dev","command":["x"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"token-env","vault":"/v","session":"s","token":"t","project":"p","command":["x"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"token-env","vault":"/v","session":"s","token":"t","project":"p","profile":"dev","command":"x","directory":"/d"}""")]
    [InlineData("""{"v":1,"kind":"token-env","vault":"/v","session":"s","token":"t","project":"p","profile":"dev","command":["x"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"env","vault":"/v","session":"s","token":"t","project":"p","profile":"dev","command":["x"],"directory":"/d"}""")]
    [InlineData("""{"v":2,"kind":"token-env","vault":"/v","session":"s","token":"t","token":"u","project":"p","profile":"dev","command":["x"],"directory":"/d"}""")]
    [InlineData("not json")]
    public void AMalformedRequest_IsRejected(string frame) =>
        Assert.False(ApproverProtocol.TryDecode(Encoding.UTF8.GetBytes(frame), out TokenEnvRequest? _));

    [Fact]
    public void ToString_Redacts()
    {
        var request = Request();

        Assert.DoesNotContain(_token, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_token[13..], request.ToString(), StringComparison.Ordinal);
        Assert.Contains("acme-api", request.ToString(), StringComparison.Ordinal);
    }
}
