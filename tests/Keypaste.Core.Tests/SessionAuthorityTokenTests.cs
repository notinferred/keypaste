using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A <c>keypaste run --token</c> request at the owner: verified against the vault as saved,
/// released only within its scope, asked about live only for a protected profile, and audited by
/// the owner before any reply leaves.
/// </summary>
public sealed class SessionAuthorityTokenTests : IDisposable
{
    private const string _connection = "runner";
    private const string _database = "postgres://token-sentinel@db/app";
    private const string _apiKey = "sk_live_token_sentinel";
    private const string _prod = "postgres://prod-sentinel@db/app";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-token-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly Vault _vault;
    private readonly AuditLog _audit;
    private readonly SessionLifetime _lifetime = new("session-one");

    public SessionAuthorityTokenTests()
    {
        _vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        _vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api/staging", Title = "DATABASE_URL", Password = _database });
        _vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api/staging", Title = "API_KEY", Password = _apiKey });
        _vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api/prod", Title = "DATABASE_URL", Password = _prod });
        _vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api", Title = "DEV_ONLY", Password = "dev" });
        _vault.Save();

        Assert.True(AuditLog.TryOpen(AuditPath, _fixture.Clock, out var audit, out var error), error);
        _audit = audit;
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string AuditPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose()
    {
        _lifetime.Dispose();
        _audit.Dispose();
        _vault.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task AValidToken_ReleasesItsScope_WithoutAsking()
    {
        var token = Mint("ci-staging", "read:acme-api/staging/*");
        var authority = await AttachedAsync();

        var reply = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([new EnvVariable("API_KEY", _apiKey), new EnvVariable("DATABASE_URL", _database)], reply.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AValidToken_IsAnsweredOverTheOwnersPipe()
    {
        var token = Mint("ci-staging", "read:acme-api/staging/*");
        var pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        using var stop = new CancellationTokenSource();
        using var listener = new ApproverListener(pipe, Authority());
        var running = listener.RunAsync(stop.Token);

        try
        {
            await using var client = await ApproverClient.TryConnectAsync(pipe, _wait, Cancel);
            Assert.NotNull(client);
            var attached = await client.AttachAsync(new AttachRequest(VaultPath), Cancel);
            Assert.True(attached?.Attached);

            var reply = await client.ReleaseTokenEnvAsync(Request(token, "staging") with { Session = attached!.Session! }, Cancel);

            Assert.NotNull(reply);
            Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
            Assert.Equal(2, reply.Set.Variables.Count);
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }
        }
    }

    [Fact]
    public async Task AKeyScopedToken_ReleasesOnlyThoseKeys()
    {
        var token = Mint("ci-key", "read:acme-api/staging/API_KEY");
        var authority = await AttachedAsync();

        var reply = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([new EnvVariable("API_KEY", _apiKey)], reply.Set.Variables);
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused()
    {
        var token = Mint("ci", "read:acme-api/staging/*", TimeSpan.FromMinutes(1));
        var authority = await AttachedAsync();
        _fixture.Clock.AdvanceWallOnly(TimeSpan.FromMinutes(1));

        var reply = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
        Assert.Equal("the token has expired", reply.Reason);
        Assert.Empty(reply.Set.Variables);
    }

    [Fact]
    public async Task ARevokedToken_IsRefused()
    {
        var token = Mint("ci", "read:acme-api/staging/*");
        Assert.True(new TokenStore(_vault).Revoke("ci"));
        _vault.Save();
        var authority = await AttachedAsync();

        var revoked = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);
        var neverMinted = await authority.ReleaseTokenEnvAsync(Request(TokenSecret.New(out _, out _), "staging"), _connection, Cancel);

        foreach (var reply in new[] { revoked, neverMinted })
        {
            Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
            Assert.Equal("the token is not valid for this vault", reply.Reason);
            Assert.Empty(reply.Set.Variables);
        }
    }

    [Fact]
    public async Task AScopeMiss_IsRefused()
    {
        var token = Mint("ci", "read:acme-api/staging/*");
        var authority = await AttachedAsync();

        var otherProfile = await authority.ReleaseTokenEnvAsync(Request(token, "dev"), _connection, Cancel);
        var otherProject = await authority.ReleaseTokenEnvAsync(Request(token, "staging", project: "billing"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Unauthorized, otherProfile.Set.Outcome);
        Assert.Equal("the token's scope does not cover acme-api/dev", otherProfile.Reason);
        Assert.Equal(EnvOutcome.Unauthorized, otherProject.Set.Outcome);
        Assert.Empty(otherProfile.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AProtectedProfileWithoutAllowProd_IsRefusedUnasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        var token = Mint("prod-ci", "read:acme-api/prod/*", allowProd: true);
        var info = Assert.Single(new TokenStore(_vault).List());
        var entry = _vault.Find(new EntryName(ReservedGroups.Tokens, info.Id))!;
        _vault.UpdateEntry(entry with { Notes = entry.Notes.Replace("\"allow_prod\":true", "\"allow_prod\":false", StringComparison.Ordinal) });
        _vault.Save();
        Assert.False(Assert.Single(new TokenStore(_vault).List()).AllowProd);
        var authority = await AttachedAsync();

        var reply = await authority.ReleaseTokenEnvAsync(Request(token, "prod"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
        Assert.Empty(reply.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AProtectedProfileWithAllowProd_AsksEveryTime_OnceOnly()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        var token = Mint("prod-ci", "read:acme-api/prod/*", allowProd: true);
        var authority = await AttachedAsync();

        var first = await authority.ReleaseTokenEnvAsync(Request(token, "prod"), _connection, Cancel);
        var second = await authority.ReleaseTokenEnvAsync(Request(token, "prod"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Resolved, first.Set.Outcome);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal(_prod, Assert.Single(second.Set.Variables).Value);
        Assert.Equal(2, _fixture.Channel.Asked);

        var prompt = Assert.IsType<EnvReleasePrompt>(_fixture.Channel.LastEnvPrompt);
        Assert.Equal(0, prompt.GrantSeconds);
        Assert.Equal("prod", prompt.Profile);
        Assert.StartsWith("token 'prod-ci' (kpt_", prompt.Requester, StringComparison.Ordinal);
        Assert.DoesNotContain(token[13..], prompt.ToString(), StringComparison.Ordinal);

        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        var denied = await authority.ReleaseTokenEnvAsync(Request(token, "prod"), _connection, Cancel);
        Assert.Equal(EnvOutcome.Declined, denied.Set.Outcome);
        Assert.Equal("the person asked said no", denied.Reason);
    }

    [Fact]
    public async Task ALockedSession_RefusesTokens()
    {
        var token = Mint("ci", "read:acme-api/staging/*");
        var authority = await AttachedAsync();
        _lifetime.End();

        var reply = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Locked, reply.Set.Outcome);
        Assert.Empty(reply.Set.Variables);
    }

    [Fact]
    public async Task AChangedOnDiskVault_SaysSo_NotInvalid()
    {
        var token = Mint("ci", "read:acme-api/staging/*");
        using (var other = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            other.AddEntry(new VaultEntry { Title = "elsewhere", Password = "x" });
            other.Save();
        }

        var authority = await AttachedAsync();
        var reply = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        Assert.Equal(EnvOutcome.ChangedOnDisk, reply.Set.Outcome);
        Assert.DoesNotContain("not valid", reply.Reason, StringComparison.Ordinal);
        Assert.Contains("another program saved", reply.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryOutcome_IsAuditedByTheOwner()
    {
        var token = Mint("ci", "read:acme-api/staging/*", TimeSpan.FromMinutes(5));
        var authority = await AttachedAsync();

        var released = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);
        Assert.Single(Lines());
        var missed = await authority.ReleaseTokenEnvAsync(Request(token, "dev"), _connection, Cancel);
        Assert.Equal(2, Lines().Count);
        _fixture.Clock.AdvanceWallOnly(TimeSpan.FromMinutes(5));
        var expired = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        Assert.Equal(EnvOutcome.Resolved, released.Set.Outcome);
        Assert.Equal(EnvOutcome.Unauthorized, missed.Set.Outcome);
        Assert.Equal(EnvOutcome.Unauthorized, expired.Set.Outcome);

        var lines = Lines();
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal("token", line.GetProperty("method").GetString()));
        Assert.All(lines, line => Assert.Equal("session-one", line.GetProperty("session").GetString()));
        Assert.Equal(["granted", "denied", "denied"], lines.Select(line => line.GetProperty("decision").GetString()));
        Assert.Contains("2 variable(s)", lines[0].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Contains("'ci'", lines[0].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Contains("does not cover", lines[1].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Contains("expired", lines[2].GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal("env/acme-api/staging", lines[0].GetProperty("args").GetProperty("entry").GetString());
    }

    [Fact]
    public async Task ReleaseTokenEnv_AuditUnwritable_ReleasesNothing()
    {
        var token = Mint("ci", "read:acme-api/staging/*");

        var unaudited = await AttachedAsync(audit: false);
        var noLog = await unaudited.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);

        var authority = await AttachedAsync();
        EnvReply locked;
        using (new FileStream(AuditPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            locked = await authority.ReleaseTokenEnvAsync(Request(token, "staging"), _connection, Cancel);
        }

        foreach (var reply in new[] { noLog, locked })
        {
            Assert.Equal(EnvOutcome.Unreadable, reply.Set.Outcome);
            Assert.Equal("the audit log could not be written", reply.Reason);
            Assert.Empty(reply.Set.Variables);
            Assert.DoesNotContain(_database, reply.ToString(), StringComparison.Ordinal);
        }

        Assert.Empty(Lines());
    }

    [Fact]
    public async Task EveryRefusal_CarriesTheRequestedProfile()
    {
        var token = Mint("ci", "read:acme-api/staging/*,read:acme-api/qa/*");
        var authority = await AttachedAsync();

        var replies = new List<EnvReply>
        {
            await authority.ReleaseTokenEnvAsync(Request(token, "qa"), _connection, Cancel),
            await authority.ReleaseTokenEnvAsync(Request(token, "dev"), _connection, Cancel),
            await authority.ReleaseTokenEnvAsync(Request("kpt_malformed", "staging"), _connection, Cancel),
            await authority.ReleaseTokenEnvAsync(Request(token, "staging", command: []), _connection, Cancel),
            await authority.ReleaseTokenEnvAsync(Request(token, "staging") with { Session = "ended" }, _connection, Cancel),
        };

        Assert.Equal(EnvOutcome.NoProfile, replies[0].Set.Outcome);
        Assert.Equal("qa", replies[0].Set.Profile);
        Assert.Equal("dev", replies[1].Set.Profile);
        Assert.Equal("staging", replies[2].Set.Profile);
        Assert.Equal(EnvOutcome.Invalid, replies[3].Set.Outcome);
        Assert.Equal("staging", replies[3].Set.Profile);
        Assert.Equal(EnvOutcome.NoSession, replies[4].Set.Outcome);
        Assert.Equal("staging", replies[4].Set.Profile);

        var bad = await authority.ReleaseTokenEnvAsync(Request(token, "Bad Profile"), _connection, Cancel);
        Assert.Equal(EnvOutcome.Invalid, bad.Set.Outcome);
        Assert.Equal("Bad Profile", bad.Set.Profile);
    }

    [Fact]
    public async Task TheTokenAppearsInNoReplyReasonOrToString()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        var token = Mint("ci", "read:acme-api/staging/*,read:acme-api/prod/*", allowProd: true);
        var secret = token[13..];
        var authority = await AttachedAsync();

        List<(TokenEnvRequest Request, EnvReply Reply)> exchanges = [];
        foreach (var request in new[]
        {
            Request(token, "staging"),
            Request(token, "prod"),
            Request(token, "dev"),
            Request(token[..^2] + "AA", "staging"),
            Request(token + "x", "staging"),
        })
        {
            exchanges.Add((request, await authority.ReleaseTokenEnvAsync(request, _connection, Cancel)));
        }

        foreach (var (request, reply) in exchanges)
        {
            foreach (var text in new[] { reply.Reason, reply.ToString(), request.ToString(), reply.Set.Refusal })
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
                Assert.DoesNotContain(secret[..20], text, StringComparison.Ordinal);
            }
        }

        _audit.Dispose();
        var log = File.ReadAllText(AuditPath);
        Assert.DoesNotContain(secret[..20], log, StringComparison.Ordinal);
        Assert.DoesNotContain(_database, log, StringComparison.Ordinal);
    }

    private string Mint(string name, string scopes, TimeSpan? ttl = null, bool allowProd = false)
    {
        Assert.True(TokenScope.TryParseList(scopes, out var parsed, out var error), error);
        Assert.True(
            new TokenStore(_vault).TryCreate(name, parsed, ttl ?? TimeSpan.FromDays(30), allowProd, _fixture.Clock.GetUtcNow(), out _, out var token, out error),
            error);
        _vault.Save();
        return token;
    }

    private TokenEnvRequest Request(string token, string profile, string project = "acme-api", IReadOnlyList<string>? command = null) =>
        new(token, project, profile, command ?? ["deploy", "--to", "staging"], Path.Combine(_directory, "work"))
        {
            Vault = VaultPath,
            Session = "session-one",
        };

    private SessionAuthority Authority(bool audit = true) =>
        new(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            new SessionEnvironments(
                _fixture.Gate,
                lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
                _fixture.Clock,
                Audit: audit ? () => _audit : null));

    private async Task<SessionAuthority> AttachedAsync(bool audit = true)
    {
        var authority = Authority(audit);
        Assert.True((await authority.AttachAsync(new AttachRequest(VaultPath), _connection, Cancel)).Attached);
        return authority;
    }

    private List<JsonElement> Lines()
    {
        using var stream = new FileStream(AuditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Parse)];

        static JsonElement Parse(string line)
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }
}
