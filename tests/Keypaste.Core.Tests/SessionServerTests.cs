using System.IO.Pipes;
using System.Security.Cryptography;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>One owner's server for one unlock, built the same way for the desktop and <c>keypaste approve</c>.</summary>
public sealed class SessionServerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-session-server-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>The authority is built before the endpoint is bound, so a name another listener holds leaves no edit watched behind.</summary>
    [Fact]
    public void A_name_already_held_leaves_no_edit_watched()
    {
        using var lifetime = new SessionLifetime();
        List<EventHandler<VaultEdit>> watching = [];
        var name = UniqueName();
        using var held = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var refused = Record.Exception(() => SessionServer.Listen(name, Options(lifetime, watching)).Dispose());

        Assert.True(refused is IOException or UnauthorizedAccessException, refused?.ToString());
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

    /// <summary>An owner signs what it recorded when its session ends, with the key the vault's next save wrote.</summary>
    [Fact]
    public async Task The_owner_signs_what_it_recorded_when_its_session_ends()
    {
        using var lifetime = new SessionLifetime();
        using var vault = ClaimedVault.Create(VaultPath, EnvStoreTests.MasterPassword);
        var path = Path.Combine(_directory, "audit.jsonl");
        Assert.True(AuditLog.TryOpen(path, TimeProvider.System, out var audit, out var error), error);

        using (audit)
        {
            using var server = SessionServer.Build(Options(lifetime, []) with
            {
                Audit = () => audit,
                UnlockedFor = asked => asked.IsLive ? vault : null,
            });

            await server.Authority.ReleaseEnvAsync(
                new EnvRequest("dev", ["deploy"], _directory) { Vault = VaultPath, Session = lifetime.Id },
                "never-attached",
                TestContext.Current.CancellationToken);

            vault.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "MINE", Password = "secret-mine" });
            vault.Save();
        }

        var report = AuditChainVerifier.Verify(path, checkpoints: AuditCheckpointKey.Read(vault, VaultIdentity.Of(_directory, VaultPath)));

        Assert.Equal(2, report.Records);
        Assert.Equal(1, report.SignedCheckpoints);
        Assert.Equal(0, report.UnsignedRecords);
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private SessionServerOptions Options(SessionLifetime lifetime, List<EventHandler<VaultEdit>> watching) => new()
    {
        Vault = VaultIdentity.Of(_directory, VaultPath),
        Lifetime = lifetime,
        CurrentLifetime = () => lifetime,
        UnlockedFor = _ => null,
        Channel = new FakeChannel(),
        Rules = IAgentRules.Defaults,
        Audit = () => null,
    };

    private static string UniqueName() =>
        "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
