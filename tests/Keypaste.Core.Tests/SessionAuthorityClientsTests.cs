using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Which bridges are attached to the live session, as they describe their clients (D-0361): display
/// and counting only, never a decision (THREATS.md T-3).
/// </summary>
public sealed class SessionAuthorityClientsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-clients-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly SessionLifetime _lifetime = new("session-one");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _lifetime.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private SessionAuthority Authority() =>
        new(VaultIdentity.Of(_directory, VaultPath), () => _lifetime, _fixture.Handler, clock: _fixture.Clock);

    private Task<AttachReply> Attach(SessionAuthority authority, string connection, AttachClient? client) =>
        authority.AttachAsync(new AttachRequest(VaultPath) { Client = client }, connection, Token).AsTask();

    [Fact]
    public async Task ABridgeWithIdentity_IsListed()
    {
        var authority = Authority();

        await Attach(authority, "conn-1", new AttachClient("claude-code", "2.1", "cc"));

        var client = Assert.Single(authority.Clients);
        Assert.Equal(("conn-1", "claude-code", "2.1", "cc"), (client.ConnectionId, client.Name, client.Version, client.Label));
        Assert.Equal(_fixture.Clock.GetUtcNow(), client.AttachedAt);
    }

    [Fact]
    public async Task ARunnerWithout_IsNot()
    {
        var authority = Authority();

        await Attach(authority, "conn-1", null);

        Assert.Empty(authority.Clients);
    }

    [Fact]
    public async Task ADisconnect_RemovesIt()
    {
        var authority = Authority();
        await Attach(authority, "conn-1", new AttachClient("claude-code", null, null));

        authority.Disconnected("conn-1");

        Assert.Empty(authority.Clients);
    }

    [Fact]
    public async Task ALock_EmptiesTheList()
    {
        var authority = Authority();
        await Attach(authority, "conn-1", new AttachClient("claude-code", null, null));

        _lifetime.End();

        Assert.Empty(authority.Clients);
    }

    [Fact]
    public async Task LastRequest_Advances_AndTheMostRecentIsFirst()
    {
        var authority = Authority();
        await Attach(authority, "conn-1", new AttachClient("first", null, null));
        await Attach(authority, "conn-2", new AttachClient("second", null, null));

        _fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        await authority.ListAsync(new NamesRequest(["env/**"]) { Vault = VaultPath, Session = "session-one" }, "conn-1", Token);

        var clients = authority.Clients;
        Assert.Equal(["first", "second"], clients.Select(client => client.Name));
        Assert.Equal(_fixture.Clock.GetUtcNow(), clients[0].LastRequestAt);
        Assert.True(clients[1].LastRequestAt < clients[0].LastRequestAt);
    }
}
