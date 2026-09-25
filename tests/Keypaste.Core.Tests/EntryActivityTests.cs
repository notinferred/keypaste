using Keypaste.Core.Activity;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Which entries agents use, from this vault's granted audit lines, the session's release ledger and
/// what the owner holds now (D-0361). Names and times, never a value.
/// </summary>
public sealed class EntryActivityTests
{
    private const string _vault = "0123456789abcdef";
    private static readonly DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly EntryName _stripe = new("env/acme-api", "STRIPE_KEY");
    private static readonly EntryName _github = new("personal", "github");

    private static AuditEntry Line(string entry, int minutesAgo, string decision = "granted", string vault = _vault, string tool = "request_credential", string label = "claude-code", string reason = "a person approved") => new()
    {
        Line = 1,
        At = _now.AddMinutes(-minutesAgo),
        Tool = tool,
        Entry = tool == "request_credential" ? entry : string.Empty,
        Entries = tool == "request_credential" ? [] : [entry],
        Decision = decision,
        Method = tool == "run" && reason.StartsWith("token", StringComparison.Ordinal) ? "token" : "prompt",
        Label = label,
        Client = label,
        Reason = reason,
        Vault = vault,
    };

    private static EntryActivity Build(IReadOnlyList<AuditEntry>? audit = null, ApproverActivity? live = null, IReadOnlyList<ReleaseSeen>? session = null) =>
        EntryActivity.Build(audit ?? [], _vault, live ?? ApproverActivity.None, session ?? [], _now);

    [Fact]
    public void AGrantInForce_IsInUse()
    {
        var grant = new EnvGrantInForce("k", "acme-api", "dev", "npm test", TimeSpan.FromMinutes(5)) { Entries = ["env/acme-api/STRIPE_KEY"] };

        var activity = Build(live: ApproverActivity.None with { EnvGrants = [grant] });

        Assert.Equal(EntryUseState.InUse, activity.Use(_stripe).State);
        Assert.Equal(EntryUseState.Idle, activity.Use(_github).State);
    }

    [Fact]
    public void AWaitingCredentialEnvOrRun_IsInUse()
    {
        var credential = new WaitingRequest(ApprovalPrompt.For("claude", _github, "password", "why", 0), TimeSpan.FromSeconds(30));
        var env = new WaitingEnv(EnvReleasePrompt.For(new EnvPreview("acme-api", ["STRIPE_KEY"]), ["npm", "test"], "/work"), TimeSpan.FromSeconds(30));

        Assert.Equal(EntryUseState.InUse, Build(live: new ApproverActivity([credential], [])).Use(_github).State);
        Assert.Equal(EntryUseState.InUse, Build(live: ApproverActivity.None with { WaitingEnvs = [env] }).Use(_stripe).State);
    }

    [Fact]
    public void AGrantedLineInsideTheWindow_IsRecent()
    {
        var use = Build([Line("env/acme-api/STRIPE_KEY", 30)]).Use(_stripe);

        Assert.Equal(EntryUseState.Recent, use.State);
        Assert.Equal(_now.AddMinutes(-30), use.LastUsed);
    }

    [Fact]
    public void Older_IsIdle_WithLastUsed()
    {
        var use = Build([Line("env/acme-api/STRIPE_KEY", 60 * 5)]).Use(_stripe);

        Assert.Equal(EntryUseState.Idle, use.State);
        Assert.Equal(_now.AddHours(-5), use.LastUsed);
    }

    [Fact]
    public void DeniedLines_DoNotCount()
    {
        Assert.Null(Build([Line("env/acme-api/STRIPE_KEY", 1, decision: "denied")]).Use(_stripe).LastUsed);
    }

    [Fact]
    public void AnotherVaultsLine_DoesNotCount_ButALineFromBeforeVaultsWereNamedDoes()
    {
        Assert.Null(Build([Line("env/acme-api/STRIPE_KEY", 1, vault: "ffffffffffffffff")]).Use(_stripe).LastUsed);
        Assert.NotNull(Build([Line("env/acme-api/STRIPE_KEY", 1, vault: string.Empty)]).Use(_stripe).LastUsed);
    }

    [Fact]
    public void RunAndTokenLines_CountForEachEntry()
    {
        var activity = Build(
        [
            Line("env/acme-api/STRIPE_KEY", 10, tool: "run"),
            Line("env/acme-api/STRIPE_KEY", 5, tool: "run", label: string.Empty, reason: "token abc123 'ci-staging': 2 variable(s)"),
        ]);

        var access = activity.Access(_stripe);

        Assert.Equal(["ci-staging · token", "claude-code"], access.Clients.Select(client => client.Client));
        Assert.Equal("run", access.Clients[1].How);
    }

    [Fact]
    public void TheSessionLedger_Counts()
    {
        var use = Build(session: [new ReleaseSeen("env/acme-api/STRIPE_KEY", "keypaste run", "run --session", _now.AddMinutes(-2))]).Use(_stripe);

        Assert.Equal(EntryUseState.Recent, use.State);
        Assert.Equal(_now.AddMinutes(-2), use.LastUsed);
    }

    [Fact]
    public void Access_ListsClientsNewestFirst_WithGrants()
    {
        var grant = new EnvGrantInForce("k", "p", "dev", "npm test", TimeSpan.FromMinutes(5)) { Client = "Claude", Entries = ["personal/github"] };
        var activity = Build(
            [Line("personal/github", 50, label: "cursor"), Line("personal/github", 20, label: "claude-code"), Line("personal/github", 90, label: "cursor")],
            ApproverActivity.None with { EnvGrants = [grant] });

        var access = activity.Access(_github);

        Assert.Equal(["claude-code", "cursor"], access.Clients.Select(client => client.Client));
        Assert.Equal(2, access.Clients[1].Releases);
        Assert.Equal("run", Assert.Single(access.Grants).Kind);
        Assert.Equal(EntryUseState.InUse, access.Use.State);
        Assert.False(access.Waiting);
    }

    [Fact]
    public void KeyOf_IsWhatPromptsAndAuditLinesWrite()
    {
        var name = new EntryName("env/acme-api/prod", "DATABASE_URL");

        Assert.Equal("env/acme-api/prod/DATABASE_URL", EntryActivity.KeyOf(name));
        Assert.Equal(
            EntryNameSanitizer.SanitizePath(ApprovalPrompt.Shown(name), maximumLength: AuditArgs.EntryLength).Text,
            EntryActivity.KeyOf(name));
    }
}
