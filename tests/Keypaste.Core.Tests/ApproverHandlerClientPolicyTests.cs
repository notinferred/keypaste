using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The per-client policy in <c>clients.toml</c> only narrows (D-0360): inject-only refuses a value
/// before anything is resolved, ask-every-time asks every time, and a file that cannot be read
/// refuses every release rather than widening.
/// </summary>
public sealed class ApproverHandlerClientPolicyTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-client-policy-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ClientsPath => Path.Combine(_directory, "clients.toml");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static CredentialRequest Request(string? label) => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = "deploy billing to staging",
        TtlSeconds = 900,
        Exposure = ["env/**"],
        ClientName = "claude-code",
        ClientLabel = label,
    };

    private ApproverHandler Handler(ApproverFixture fixture, bool withSource = true) =>
        new(fixture.Source, fixture.Source, fixture.Gate, fixture.Grants, fixture.Policy, clients: withSource ? new ClientPolicySource(ClientsPath) : null);

    private void Write(string text) => File.WriteAllText(ClientsPath, text);

    [Fact]
    public async Task InjectOnly_RefusesBeforeResolving_EvenWithAGrantStored()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        var handler = Handler(fixture);

        Assert.Equal(AuditDecision.Granted, (await handler.RequestAsync(Request("cursor"), "conn-1", Token)).Decision);
        var resolved = fixture.Source.Resolves;

        Write("[[client]]\nlabel = \"cursor\"\npolicy = \"inject-only\"\n");
        var reply = await handler.RequestAsync(Request("cursor"), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.InjectOnly, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(resolved, fixture.Source.Resolves);
    }

    [Fact]
    public async Task AskEveryTime_PromptsOnceOnly_IgnoringGrantsAndRules()
    {
        Assert.True(Toml.TryParse("[[allow]]\nclient = \"cursor\"\nentries = [\"env/**\"]\nfields = [\"password\"]\nmax_ttl_seconds = 60\n", out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var rules, out var error), error);
        using var fixture = new ApproverFixture(rules);
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        Write("[[client]]\nlabel = \"cursor\"\npolicy = \"ask\"\n");
        var handler = Handler(fixture);

        var first = await handler.RequestAsync(Request("cursor"), "conn-1", Token);
        var second = await handler.RequestAsync(Request("cursor"), "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, first.Method);
        Assert.Equal(AuditMethod.Prompt, second.Method);
        Assert.Equal(2, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Channel.LastPrompt!.TtlSeconds);
        Assert.Equal(OnceOnly.ClientPolicy, fixture.Channel.LastPrompt.OnceOnly);
        Assert.Empty(handler.Activity().Grants);
    }

    [Fact]
    public async Task SessionGrants_IsUnchanged()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        Write("[[client]]\nlabel = \"cursor\"\npolicy = \"session\"\n");
        var handler = Handler(fixture);

        await handler.RequestAsync(Request("cursor"), "conn-1", Token);
        var second = await handler.RequestAsync(Request("cursor"), "conn-1", Token);

        Assert.Equal(AuditMethod.GrantCache, second.Method);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    [Fact]
    public async Task TheStarRow_CoversUnlabeledClients()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        Write("[[client]]\nlabel = \"*\"\npolicy = \"inject-only\"\n[[client]]\nlabel = \"trusted\"\npolicy = \"session\"\n");
        var handler = Handler(fixture);

        Assert.Equal(AuditMethod.InjectOnly, (await handler.RequestAsync(Request(null), "conn-1", Token)).Method);
        Assert.Equal(AuditMethod.InjectOnly, (await handler.RequestAsync(Request("other"), "conn-2", Token)).Method);
        Assert.Equal(AuditDecision.Granted, (await handler.RequestAsync(Request("trusted"), "conn-3", Token)).Decision);
    }

    [Fact]
    public async Task AMalformedFile_RefusesEveryRequest_AndListingStillAnswers()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        Write("[[client]]\nlabel = \"cursor\"\npolicy = \"maybe\"\n");
        var handler = Handler(fixture);

        var reply = await handler.RequestAsync(Request("anyone"), "conn-1", Token);
        var listing = await handler.ListAsync(new NamesRequest(["env/**"]), "conn-1", Token);

        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Contains("clients file", reply.Reason, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.True(listing.VaultUnlocked);
        Assert.NotEmpty(listing.Names);
    }

    [Fact]
    public async Task AClientsPathThatCannotBeReadAsAFile_RefusesEveryRequest()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        Directory.CreateDirectory(ClientsPath);
        var handler = Handler(fixture);

        var reply = await handler.RequestAsync(Request("anyone"), "conn-1", Token);

        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Contains("clients file", reply.Reason, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.False(ClientPolicies.TryLoad(ClientsPath, out _, out _));
    }

    [Fact]
    public async Task AChangedFile_AppliesToTheNextRequest_EvenAtTheSameLength()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var handler = Handler(fixture);

        Write("[[client]]\nlabel = \"cursor\"\npolicy = \"ask\"\n");
        Assert.Equal(AuditDecision.Granted, (await handler.RequestAsync(Request("cursor"), "conn-1", Token)).Decision);

        // Same length, and written within the same timestamp tick: only the bytes say it changed.
        var written = File.GetLastWriteTimeUtc(ClientsPath);
        Write("[[client]]\nlabel = \"cursor\"\npolicy = \"bad\"\n");
        File.SetLastWriteTimeUtc(ClientsPath, written);

        Assert.Equal(AuditMethod.Failed, (await handler.RequestAsync(Request("cursor"), "conn-1", Token)).Method);
    }

    [Fact]
    public async Task NoSource_IsSessionGrants()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        Write("[[client]]\nlabel = \"*\"\npolicy = \"inject-only\"\n");
        var handler = Handler(fixture, withSource: false);

        Assert.Equal(AuditDecision.Granted, (await handler.RequestAsync(Request("cursor"), "conn-1", Token)).Decision);
        Assert.True(handler.TryPolicyFor("cursor", out var policy, out _));
        Assert.Equal(ClientPolicy.SessionGrants, policy);
    }
}
