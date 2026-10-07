using System.Security.Cryptography;
using Keypaste.Core.Clients;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>One owner's server for one unlock, built the same way for the desktop and <c>keypaste agent</c> (F.55).</summary>
public sealed class SessionServerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-session-server-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Every owner watches its edits while it serves, so an edit withdraws the grants naming what it touched (D-0318).</summary>
    [Fact]
    public void Edits_are_watched_while_serving_and_no_longer_once_disposed()
    {
        using var lifetime = new SessionLifetime();
        List<EventHandler<VaultEdit>> watching = [];

        using (SessionServer.Listen(UniqueName(), Options(lifetime, watching)))
        {
            var handler = Assert.Single(watching);
            handler(this, VaultEdit.Of(new EntryName("env/acme", "API_KEY")));
        }

        Assert.Empty(watching);
    }

    private SessionServerOptions Options(SessionLifetime lifetime, List<EventHandler<VaultEdit>> watching) => new()
    {
        Vault = VaultIdentity.Of(_directory, Path.Combine(_directory, "vault.kdbx")),
        Lifetime = lifetime,
        CurrentLifetime = () => lifetime,
        UnlockedFor = _ => null,
        Channel = new FakeChannel(),
        Policy = PolicyGate.None,
        Clients = new ClientPolicySource(Path.Combine(_directory, "clients.toml")),
        Audit = () => null,
        WatchEdits = watching.Add,
        UnwatchEdits = handler => watching.Remove(handler),
    };

    private static string UniqueName() =>
        "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
