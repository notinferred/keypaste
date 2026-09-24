using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault's owner over a real pipe: only a connection attached to the session now holding the vault
/// it names is answered (D-0310), and nothing is released across the lock that ends it (D-0313).
/// </summary>
public sealed class SessionAuthorityTests : IDisposable
{
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-tests-").FullName;
    private readonly ApproverFixture _fixture = new();
    private SessionLifetime? _lifetime = new("session-one");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private CredentialRequest Request(string session, string? vault = null) => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = "deploy billing to staging",
        TtlSeconds = 60,
        Exposure = ["env/**"],
        Vault = vault ?? VaultPath,
        Session = session,
    };

    private NamesRequest Listing(string session) => new(["env/**"]) { Vault = VaultPath, Session = session };

    [Fact]
    public async Task AnAttachedListing_IsAnsweredFromTheVaultAndNamesTheSession()
    {
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var attached = await client.AttachAsync(new AttachRequest(VaultPath), Token);
        Assert.NotNull(attached);
        Assert.True(attached.Attached);
        Assert.Equal("session-one", attached.Session);

        var reply = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.True(reply.VaultUnlocked);
        Assert.Equal([new EntryName("env/dev", "STRIPE_KEY")], reply.Names);
        Assert.Equal("session-one", reply.Session);
    }

    [Fact]
    public async Task AnAttachedRequest_IsDecidedAndNamesTheSession()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(ApproverFixture.Sentinel, reply.Value);
        Assert.Equal("session-one", reply.Session);
    }

    [Fact]
    public async Task ARequestFromAConnectionThatNeverAttached_IsRefusedUnasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Null(reply.Value);
        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Empty(listing.Names);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task AnAttachmentNamingAnotherVault_IsRefused()
    {
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var elsewhere = Path.Combine(_directory, "other.kdbx");
        var attached = await client.AttachAsync(new AttachRequest(elsewhere), Token);
        var reply = await client.RequestAsync(Request("session-one", elsewhere), Token);

        Assert.NotNull(attached);
        Assert.False(attached.Attached);
        Assert.Equal(AuditMethod.NoSession, attached.Refusal);
        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ARequestNamingAnotherVaultThanItsAttachment_IsRefused()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one", Path.Combine(_directory, "other.kdbx")), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ARequestFromASessionThatEnded_IsRefusedAfterTheVaultIsUnlockedAgain()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        _lifetime = new SessionLifetime("session-two");

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Null(reply.Session);
        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task ARequestPresentingTheNewSessionOnAnOldAttachment_IsRefused()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        _lifetime = new SessionLifetime("session-two");

        var reply = await client.RequestAsync(Request("session-two"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ALockedOwner_RefusesAttachmentAndEveryRequest()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        _lifetime = null;

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var attached = await client.AttachAsync(new AttachRequest(VaultPath), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.NotNull(attached);
        Assert.False(attached.Attached);
        Assert.Equal(AuditMethod.VaultLocked, attached.Refusal);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnotherSpellingOfTheVault_AttachesToIt()
    {
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var respelled = Path.Combine(_directory, ".", "vault.kdbx");
        var attached = await client.AttachAsync(new AttachRequest(respelled), Token);
        var listing = await client.ListAsync(new NamesRequest(["env/**"]) { Vault = respelled, Session = "session-one" }, Token);

        Assert.NotNull(attached);
        Assert.True(attached.Attached);
        Assert.NotNull(listing);
        Assert.True(listing.VaultUnlocked);
    }

    [Fact]
    public async Task ARequestWaitingForAPerson_IsWithdrawnAndDeniedAsLockedWhenTheLifetimeEnds()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var pending = client.RequestAsync(Request("session-one"), Token);
        await _fixture.Channel.Waiting.WaitAsync(_connectTimeout, Token);

        _lifetime!.End();
        var reply = await pending.AsTask().WaitAsync(_connectTimeout, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal("the vault was locked before anybody answered", reply.Reason);
        Assert.Equal("session-one", reply.Session);
        Assert.Null(reply.Value);
        Assert.True(_fixture.Channel.Withdrawn);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task AnApprovalRacingTheLock_ReleasesNothingAndKeepsNoGrant()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        var lifetime = _lifetime!;
        lifetime.Own(_fixture.Grants);
        _fixture.Source.During = lifetime.End;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal("the vault was locked before this was released", reply.Reason);
        Assert.Null(reply.Value);
        Assert.Equal(0, _fixture.Grants.Count);
    }

    [Fact]
    public async Task APolicyReleaseRacingTheLock_IsRefused()
    {
        using var policed = new ApproverFixture(ApproverHandlerPolicyTests.Policy());
        policed.Source.During = _lifetime!.End;
        await using var owner = Owner.Start(this, policed.Handler);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one") with { ClientLabel = "billing-bot" }, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(1, policed.Source.Reads);
        Assert.Equal(0, policed.Channel.Asked);
    }

    [Fact]
    public async Task AGrantGivenBeforeALock_ReleasesNothingAfterTheNextUnlock()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        _lifetime!.Own(_fixture.Grants);
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var granted = await client.RequestAsync(Request("session-one"), Token);
        Assert.NotNull(granted);
        Assert.Equal(AuditMethod.Prompt, granted.Method);
        Assert.Equal(1, _fixture.Grants.Count);

        _lifetime.End();
        Assert.Equal(0, _fixture.Grants.Count);

        _lifetime = new SessionLifetime("session-two");
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var again = await client.RequestAsync(Request("session-two"), Token);

        Assert.NotNull(again);
        Assert.Equal(AuditDecision.Denied, again.Decision);
        Assert.Equal(AuditMethod.Prompt, again.Method);
        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AListingRacingTheLock_IsRefused()
    {
        _fixture.Source.During = _lifetime!.End;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Empty(listing.Names);
    }

    [Fact]
    public async Task AfterAnotherProgramSavesTheVault_RequestsAndListingsAreRefusedAsChanged_AndNothingIsAsked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var vault = SavedVault("v1");
        await using var owner = Owner.Start(this, Over(vault));
        await using var client = await ConnectAsync(owner.PipeName);
        await client.AttachAsync(new AttachRequest(VaultPath), Token);

        var before = await client.RequestAsync(Request("session-one"), Token);
        Assert.NotNull(before);
        Assert.Equal("v1", before.Value);
        Assert.Equal(1, _fixture.Grants.Count);

        var external = SaveElsewhere("external");

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.VaultChanged, reply.Method);
        Assert.Contains("another program changed the vault file", reply.Reason, StringComparison.Ordinal);
        Assert.Null(reply.Value);
        Assert.NotNull(listing);
        Assert.Empty(listing.Names);
        Assert.Equal(AuditMethod.VaultChanged, listing.Refusal);
        Assert.Equal(1, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Grants.Count);
        Assert.Equal(external, File.ReadAllBytes(VaultPath));
        Assert.Contains(_fixture.Narration, line => line.Contains("restart keypaste agent", StringComparison.Ordinal));
    }

    /// <summary>
    /// The grant is zeroed when the change is seen, not merely stepped over while the file differs:
    /// with the old bytes put back, the same request is asked again rather than served from it.
    /// </summary>
    [Fact]
    public async Task AGrantGivenBeforeAnExternalChange_IsGoneEvenIfTheFileIsPutBack()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var vault = SavedVault("v1");
        await using var owner = Owner.Start(this, Over(vault));
        await using var client = await ConnectAsync(owner.PipeName);
        await client.AttachAsync(new AttachRequest(VaultPath), Token);

        await client.RequestAsync(Request("session-one"), Token);
        var original = File.ReadAllBytes(VaultPath);

        SaveElsewhere("external");
        var refused = await client.RequestAsync(Request("session-one"), Token);
        Assert.NotNull(refused);
        Assert.Equal(AuditMethod.VaultChanged, refused.Method);

        File.WriteAllBytes(VaultPath, original);
        var again = await client.RequestAsync(Request("session-one"), Token);

        Assert.NotNull(again);
        Assert.Equal(AuditMethod.Prompt, again.Method);
        Assert.Equal("v1", again.Value);
        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task APolicyRelease_IsRefusedAfterAnExternalChange()
    {
        using var vault = SavedVault("v1");
        using var policed = new ApproverFixture(ApproverHandlerPolicyTests.Policy());
        await using var owner = Owner.Start(this, Over(vault, policed));
        await using var client = await ConnectAsync(owner.PipeName);
        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var request = Request("session-one") with { ClientLabel = "billing-bot" };

        var before = await client.RequestAsync(request, Token);
        Assert.NotNull(before);
        Assert.Equal(AuditMethod.Policy, before.Method);

        SaveElsewhere("external");
        var after = await client.RequestAsync(request, Token);

        Assert.NotNull(after);
        Assert.Equal(AuditMethod.VaultChanged, after.Method);
        Assert.Null(after.Value);
        Assert.Equal(0, policed.Channel.Asked);
    }

    public static TheoryData<string, string, string, string, int> OutsideTheLimits => new()
    {
        { "entry", string.Empty, "password", "deploy", 60 },
        { "entry", new string('a', CredentialRequestRules.MaximumEntryLength + 1), "password", "deploy", 60 },
        { "field", "env/dev/STRIPE_KEY", "password,username", "deploy", 60 },
        { "field", "env/dev/STRIPE_KEY", "totp", "deploy", 60 },
        { "reason", "env/dev/STRIPE_KEY", "password", string.Empty, 60 },
        { "reason", "env/dev/STRIPE_KEY", "password", new string('r', CredentialRequestRules.MaximumReasonLength + 1), 60 },
        { "ttl_seconds", "env/dev/STRIPE_KEY", "password", "deploy", 0 },
        { "ttl_seconds", "env/dev/STRIPE_KEY", "password", "deploy", -5 },
        { "ttl_seconds", "env/dev/STRIPE_KEY", "password", "deploy", ApprovalLimits.MaximumRequestableTtlSeconds + 1 },
        { "ttl_seconds", "env/dev/STRIPE_KEY", "password", "deploy", int.MaxValue },
    };

    /// <summary>
    /// A request sent to the owner without the bridge meets the limits the bridge would have applied,
    /// with a person ready to say yes, so a missing check would release (D-0324).
    /// </summary>
    [Theory]
    [MemberData(nameof(OutsideTheLimits))]
    public async Task ARequestOutsideTheToolsLimits_IsRefusedAsInvalidByTheOwner_Unasked(
        string argument, string entry, string field, string reason, int ttl)
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);
        await client.AttachAsync(new AttachRequest(VaultPath), Token);

        var reply = await client.RequestAsync(
            Request("session-one") with { Entry = entry, Field = field, Reason = reason, TtlSeconds = ttl }, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.InvalidRequest, reply.Method);
        Assert.StartsWith($"the request's {argument} ", reply.Reason, StringComparison.Ordinal);
        Assert.Null(reply.Value);
        Assert.Equal("session-one", reply.Session);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task TheLongestRequestableTtl_IsStillGranted_AtTheOwnersCeiling()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);
        await client.AttachAsync(new AttachRequest(VaultPath), Token);

        var reply = await client.RequestAsync(
            Request("session-one") with { TtlSeconds = ApprovalLimits.MaximumRequestableTtlSeconds }, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(ApprovalLimits.DefaultMaximumTtlSeconds, reply.TtlSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnexposedEntry_IsRefusedByTheOwner_ByPathAndByHandle_Unasked(bool byHandle)
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);
        await client.AttachAsync(new AttachRequest(VaultPath), Token);

        var entry = byHandle ? EntryHandle.For(new EntryName("personal", "bank")) : "personal/bank";
        var reply = await client.RequestAsync(Request("session-one") with { Entry = entry }, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    /// <summary>
    /// A frame that names a second field, or leaves one out, is not a request: the owner hangs up
    /// without answering, and nobody is asked (D-0325).
    /// </summary>
    [Theory]
    [InlineData("field named twice")]
    [InlineData("field as a list")]
    [InlineData("no ttl_seconds")]
    public async Task AMalformedRequest_EndsTheConnectionUnanswered_AndAsksNobody(string shape)
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);

        var wellFormed = Encoding.UTF8.GetString(ApproverProtocol.Encode(Request("session-one")));
        var frame = shape switch
        {
            "field named twice" => "{\"field\":\"username\"," + wellFormed[1..],
            "field as a list" => wellFormed.Replace("\"field\":\"password\"", "\"field\":[\"password\",\"username\"]", StringComparison.Ordinal),
            _ => wellFormed.Replace(",\"ttl_seconds\":60", string.Empty, StringComparison.Ordinal),
        };

        Assert.NotEqual(wellFormed, frame);
        Assert.True(await HangsUpOnAsync(owner.PipeName, frame));
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    public void Dispose()
    {
        _lifetime?.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>The test's vault, saved, holding the one entry requests name.</summary>
    private Vault SavedVault(string password)
    {
        var vault = Vault.Create(VaultPath, VaultCredentialSourceTests.MasterPassword);
        vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "STRIPE_KEY", Password = password });
        vault.Save();
        return vault;
    }

    /// <summary>Another program saving the test's vault; returns the bytes it wrote.</summary>
    private byte[] SaveElsewhere(string password)
    {
        using (var writer = Vault.Open(VaultPath, VaultCredentialSourceTests.MasterPassword))
        {
            writer.UpdateEntry(new VaultEntry { GroupPath = "env/dev", Title = "STRIPE_KEY", Password = password });
            writer.Save();
        }

        return File.ReadAllBytes(VaultPath);
    }

    /// <summary>The fixture's gate, grants and policy in front of a real vault.</summary>
    private ApproverHandler Over(Vault vault, ApproverFixture? fixture = null)
    {
        fixture ??= _fixture;

        return new ApproverHandler(
            new VaultCredentialSource(() => vault),
            new VaultEntryNameLister(() => vault),
            fixture.Gate,
            fixture.Grants,
            fixture.Policy,
            fixture.Narration.Add);
    }

    /// <summary>Attaches over a raw pipe, sends one frame as written, and says whether the owner hung up unanswered.</summary>
    private async Task<bool> HangsUpOnAsync(string pipeName, string frame)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(Token);
        using var framer = new MessageFramer(pipe, ownsStream: false);

        await framer.WriteAsync(ApproverProtocol.Encode(new AttachRequest(VaultPath)), Token);
        Assert.True(ApproverProtocol.TryDecode(await framer.ReadAsync(Token) ?? [], out AttachReply? attached));
        Assert.True(attached.Attached);

        await framer.WriteAsync(Encoding.UTF8.GetBytes(frame), Token);

        try
        {
            return await framer.ReadAsync(Token) is null;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static async Task<ApproverClient> ConnectAsync(string pipeName)
    {
        var client = await ApproverClient.TryConnectAsync(pipeName, _connectTimeout, Token);

        Assert.NotNull(client);
        return client;
    }

    /// <summary>An owner of the test's vault on its own pipe, whose session the test changes.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;

        private Owner(string pipeName, ApproverListener listener)
        {
            PipeName = pipeName;
            _listener = listener;
            _running = listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal static Owner Start(SessionAuthorityTests test, ApproverHandler? handler = null)
        {
            var name = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                handler ?? test._fixture.Handler);

            return new Owner(name, new ApproverListener(name, authority));
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
