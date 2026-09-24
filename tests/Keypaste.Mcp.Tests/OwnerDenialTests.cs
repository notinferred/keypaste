using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The shipped bridge against a real vault's owner: a request is answered by the owner's gate under
/// the session it attached to, and every way of not reaching that session is a denial the bridge
/// audits (4.3a).
/// </summary>
/// <remarks>
/// Nothing is faked but the person, who says yes to anything they are asked, so a request that
/// reached a gate it should not have would come back with the value.
/// </remarks>
public sealed class OwnerDenialTests : IDisposable
{
    internal const string Master = "correct horse battery staple";
    internal const string Sentinel = "SENTINEL-OWNER-DENIAL-5b19d0";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-owner-denial-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private static Dictionary<string, object?> Ask() => new()
    {
        ["entry"] = "env/dev/STRIPE_KEY",
        ["field"] = "password",
        ["reason"] = "deploy the billing service to staging",
        ["ttl_seconds"] = 60,
    };

    [Fact]
    public async Task ARequest_IsAnsweredByTheOwnersGate_AndTheAuditLineNamesItsSession()
    {
        await using var owner = Owner.Start(this);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        Assert.False(result.IsError);
        Assert.Contains(Sentinel, TextOf(result), StringComparison.Ordinal);
        Assert.Equal(1, owner.Asked);

        var line = Assert.Single(harness.AuditLines());
        Assert.Equal("granted", Field(line, "decision"));
        Assert.Equal("prompt", Field(line, "method"));
        Assert.Equal(owner.Session, Field(line, "session"));
    }

    [Fact]
    public async Task ALockedOwner_IsADenialReachingNoSession()
    {
        await using var owner = Owner.Start(this);
        owner.Lock();
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        AssertDenied(harness, result, owner, "vault-locked");
    }

    [Fact]
    public async Task NoOwner_IsADenialReachingNoSession()
    {
        await using var harness = new McpHarness("keypaste-nobody-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        AssertDenied(harness, result, owner: null, "no-approver");
    }

    [Fact]
    public async Task AnOwnerOfAnotherVault_RefusesTheAttachment_AndThatIsADenial()
    {
        await using var owner = Owner.Start(this, holds: Path.Combine(_directory, "other.kdbx"));
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        AssertDenied(harness, result, owner, "no-session");
    }

    /// <summary>
    /// The owner locks and unlocks while a request's reply is lost. The bridge sends it again under
    /// the session it was first sent in, and the owner refuses it rather than answering from the
    /// later unlock.
    /// </summary>
    [Fact]
    public async Task ARequestFromAStaleSession_IsADenial_AndReleasesNothing()
    {
        await using var owner = Owner.Start(this, relockOnFirstRequest: true);
        var first = owner.Session;
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        Assert.NotEqual(first, owner.Session);
        AssertDenied(harness, result, owner, "no-session");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static void AssertDenied(McpHarness harness, CallToolResult result, Owner? owner, string method)
    {
        Assert.True(result.IsError);
        Assert.DoesNotContain(Sentinel, harness.Transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, harness.AuditText, StringComparison.Ordinal);
        Assert.Equal(0, owner?.Asked ?? 0);

        var line = Assert.Single(harness.AuditLines());
        Assert.Equal("denied", Field(line, "decision"));
        Assert.Equal(method, Field(line, "method"));
        Assert.Null(Field(line, "session"));
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string? Field(string line, string name)
    {
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    /// <summary>A real owner of a real vault on its own pipe, whose session the test can end.</summary>
    private sealed class Owner : IAsyncDisposable, IApprovalChannel
    {
        private readonly Vault _vault;
        private readonly GrantCache _grants = new(TimeProvider.System);
        private readonly ApprovalGate _gate;
        private readonly ApproverListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        private SessionLifetime? _lifetime = new();

        private Owner(OwnerDenialTests test, string holds, bool relockOnFirstRequest)
        {
            _vault = Vault.Create(test.VaultPath, Master);
            _vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "STRIPE_KEY", Password = Sentinel });
            _vault.Save();

            _gate = new ApprovalGate(this, TimeProvider.System, ApprovalLimits.Default);

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => _vault),
                new VaultEntryNameLister(() => _vault),
                _gate,
                _grants,
                PolicyGate.None);

            IApproverHandler authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, holds), () => _lifetime, handler);

            if (relockOnFirstRequest)
            {
                authority = new RelockingOnce(authority, Relock);
            }

            PipeName = "keypaste-owner-denial-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            _listener = new ApproverListener(PipeName, authority);
            _serving = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal string? Session => _lifetime?.Id;

        internal int Asked { get; private set; }

        internal static Owner Start(OwnerDenialTests test, string? holds = null, bool relockOnFirstRequest = false) =>
            new(test, holds ?? test.VaultPath, relockOnFirstRequest);

        internal void Lock()
        {
            _lifetime?.Dispose();
            _lifetime = null;
        }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;
            return ValueTask.FromResult(ApprovalAnswer.Approved);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            _stop.Dispose();
            Lock();
            _gate.Dispose();
            _grants.Dispose();
            _vault.Dispose();
        }

        private void Relock()
        {
            Lock();
            _lifetime = new SessionLifetime();
        }
    }

    /// <summary>
    /// Locks and unlocks the owner when the first request arrives, and loses that request's reply by
    /// ending its connection unanswered.
    /// </summary>
    private sealed class RelockingOnce(IApproverHandler inner, Action relock) : IApproverHandler
    {
        private int _dropped;

        public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken) =>
            inner.AttachAsync(request, connectionId, cancellationToken);

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            inner.ListAsync(request, connectionId, cancellationToken);

        public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _dropped, 1) == 0)
            {
                relock();
                throw new IOException("the owner locked and unlocked before answering");
            }

            return inner.RequestAsync(request, connectionId, cancellationToken);
        }

        public void Disconnected(string connectionId) => inner.Disconnected(connectionId);
    }
}
