using System.Security.Cryptography;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
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

    /// <summary>The log an owner is built with is the one its authority records what it answers in.</summary>
    [Fact]
    public async Task The_owners_audit_log_is_where_its_authority_records_requests()
    {
        using var lifetime = new SessionLifetime();
        var path = Path.Combine(_directory, "audit.jsonl");
        Assert.True(AuditLog.TryOpen(path, TimeProvider.System, out var audit, out var error), error);

        using (audit)
        {
            using var server = SessionServer.Listen(UniqueName(), Options(lifetime, []) with { Audit = () => audit });

            var reply = await server.Authority.ReleaseEnvAsync(
                new EnvRequest("dev", ["deploy"], _directory) { Vault = VaultPath, Session = lifetime.Id },
                "never-attached",
                TestContext.Current.CancellationToken);

            Assert.Equal(EnvOutcome.NoSession, reply.Set.Outcome);
        }

        Assert.Contains("\"method\":\"no-session\"", Assert.Single(File.ReadAllLines(path)), StringComparison.Ordinal);
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private SessionServerOptions Options(SessionLifetime lifetime, List<EventHandler<VaultEdit>> watching) => new()
    {
        Vault = VaultIdentity.Of(_directory, VaultPath),
        Lifetime = lifetime,
        CurrentLifetime = () => lifetime,
        UnlockedFor = _ => null,
        Channel = new FakeChannel(),
        Settings = IAgentSettings.Defaults,
        Hourly = new HourlyLimit(TimeProvider.System),
        Audit = () => null,
        WatchEdits = watching.Add,
        UnwatchEdits = handler => watching.Remove(handler),
    };

    private static string UniqueName() =>
        "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
