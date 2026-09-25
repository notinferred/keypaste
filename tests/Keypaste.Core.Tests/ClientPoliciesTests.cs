using System.Text;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary><c>clients.toml</c>: a strict file of <c>[[client]]</c> rows, read whole or refused whole (D-0360).</summary>
public sealed class ClientPoliciesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-clients-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static bool Parse(string text, out ClientPolicies? policies, out string problem) =>
        ClientPolicies.TryParse(Encoding.UTF8.GetBytes(text), out policies, out problem);

    [Fact]
    public void AFile_RoundTrips()
    {
        var written = ClientPolicies.Empty
            .With("cursor", ClientPolicy.AskEveryTime)
            .With("claude-code", ClientPolicy.InjectOnly)
            .With(ClientPolicies.AnyClient, ClientPolicy.AskEveryTime);

        Assert.True(Parse(written.Format(), out var read, out var problem), problem);
        Assert.Equal(written.Rows, read!.Rows);
    }

    [Theory]
    [InlineData("[[client]]\nlabel = \"a\"\npolicy = \"ask\"\ncolour = \"red\"\n")]
    [InlineData("[[clients]]\nlabel = \"a\"\npolicy = \"ask\"\n")]
    [InlineData("[[client]]\nlabel = \"a\"\npolicy = \"sometimes\"\n")]
    [InlineData("[[client]]\nlabel = \"a\"\npolicy = \"ask\"\n[[client]]\nlabel = \"a\"\npolicy = \"session\"\n")]
    [InlineData("[[client]]\nlabel = \"a/b\"\npolicy = \"ask\"\n")]
    [InlineData("[[client]]\nlabel = \"\"\npolicy = \"ask\"\n")]
    [InlineData("[[client]]\nlabel = \"a\"\n")]
    [InlineData("[[client]]\nlabel = 3\npolicy = \"ask\"\n")]
    [InlineData("label = \"a\"\n")]
    public void AMalformedFile_IsRefusedWhole(string text) =>
        Assert.False(Parse(text, out _, out _));

    [Fact]
    public void MoreThanTheMostRows_IsMalformed()
    {
        var text = new StringBuilder();

        foreach (var i in Enumerable.Range(0, ClientPolicies.MaximumRows + 1))
        {
            text.Append($"[[client]]\nlabel = \"c{i}\"\npolicy = \"ask\"\n");
        }

        Assert.False(Parse(text.ToString(), out _, out _));
    }

    [Fact]
    public void For_PrefersItsOwnRow_ThenTheStar_ThenSessionGrants()
    {
        var policies = ClientPolicies.Empty.With("cursor", ClientPolicy.InjectOnly);

        Assert.Equal(ClientPolicy.InjectOnly, policies.For("cursor"));
        Assert.Equal(ClientPolicy.SessionGrants, policies.For("other"));
        Assert.Equal(ClientPolicy.SessionGrants, policies.For(null));

        var starred = policies.With(ClientPolicies.AnyClient, ClientPolicy.AskEveryTime);

        Assert.Equal(ClientPolicy.InjectOnly, starred.For("cursor"));
        Assert.Equal(ClientPolicy.AskEveryTime, starred.For("other"));
        Assert.Equal(ClientPolicy.AskEveryTime, starred.For(null));
    }

    [Fact]
    public void With_ReplacesARow_AndAppendsANewOne()
    {
        var policies = ClientPolicies.Empty
            .With("cursor", ClientPolicy.AskEveryTime)
            .With("cursor", ClientPolicy.InjectOnly)
            .With("zed", ClientPolicy.SessionGrants);

        Assert.Equal([new ClientPolicyRow("cursor", ClientPolicy.InjectOnly), new ClientPolicyRow("zed", ClientPolicy.SessionGrants)], policies.Rows);
    }

    [Fact]
    public void TrySave_WritesWhole_AndOwnerOnlyOnUnix()
    {
        var path = Path.Combine(_directory, "nested", "clients.toml");

        Assert.True(ClientPolicies.TrySave(path, ClientPolicies.Empty.With("cursor", ClientPolicy.AskEveryTime), out var error), error);
        Assert.True(ClientPolicies.TryLoad(path, out var read, out var problem), problem);
        Assert.Equal(ClientPolicy.AskEveryTime, read!.For("cursor"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void AnAbsentFile_IsEmpty()
    {
        Assert.True(ClientPolicies.TryLoad(Path.Combine(_directory, "absent.toml"), out var read, out _));
        Assert.Same(ClientPolicies.Empty, read);
    }

    [Theory]
    [InlineData("session", ClientPolicy.SessionGrants)]
    [InlineData("ask", ClientPolicy.AskEveryTime)]
    [InlineData("inject-only", ClientPolicy.InjectOnly)]
    public void TheWireWords_RoundTrip(string word, ClientPolicy policy)
    {
        Assert.Equal(word, ClientPolicies.Wire(policy));
        Assert.True(ClientPolicies.TryParseWire(word, out var parsed));
        Assert.Equal(policy, parsed);
    }

    [Fact]
    public void TheDescriptions_AreTheDesignsWords_AndSayWhatInjectOnlyCannotStop()
    {
        Assert.Equal("Ask every time", ClientPolicies.Describe(ClientPolicy.AskEveryTime));
        Assert.Equal("Session grants up to 1h", ClientPolicies.Describe(ClientPolicy.SessionGrants));
        Assert.Equal("Inject only: keypaste never hands it a value; only commands you approve can read the values", ClientPolicies.Describe(ClientPolicy.InjectOnly));
    }
}
