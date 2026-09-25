using System.Security.Cryptography;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A <c>keypaste run --session</c> request over a real pipe: the owner releases a project's set only
/// to a connection attached to its current session, only after the person approves the command it
/// was shown, and only while that session lasts (E.1c, D-0341).
/// </summary>
public sealed class SessionAuthorityEnvTests : IDisposable
{
    private const string _token1 = "sk_live_env_owner_sentinel";
    private const string _token2 = "postgres://sentinel@db/app";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-env-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly Vault _vault;
    private SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityEnvTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            var store = new EnvStore(created);
            store.TrySet("dev", "TOKEN", _token1, out _);
            store.TrySet("dev", "DATABASE_URL", _token2, out _);
            created.AddEntry(new VaultEntry { GroupPath = "env/broken", Title = "BAD-NAME", Password = _token1 });
            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string WorkDirectory => Path.Combine(_directory, "work");

    public void Dispose()
    {
        _lifetime?.Dispose();
        _vault.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private EnvRequest Request(string session, string project = "dev", IReadOnlyList<string>? command = null) =>
        new(project, command ?? ["deploy", "--to", "staging area"], WorkDirectory) { Vault = VaultPath, Session = session };

    [Fact]
    public async Task Approve_releases_the_whole_set_after_showing_names_command_and_directory()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([new EnvVariable("DATABASE_URL", _token2), new EnvVariable("TOKEN", _token1)], reply.Set.Variables);

        var prompt = Assert.IsType<EnvReleasePrompt>(_fixture.Channel.LastEnvPrompt);
        Assert.Equal("dev", prompt.Project);
        Assert.Equal(["DATABASE_URL", "TOKEN"], prompt.Keys);
        Assert.Equal("deploy --to \"staging area\"", prompt.Command);
        Assert.Equal(WorkDirectory, prompt.Directory);
        Assert.False(prompt.CommandWasAltered);
        Assert.DoesNotContain(_token1, prompt.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_token2, prompt.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deny_releases_nothing_and_the_same_run_right_after_is_refused_unasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var denied = await client.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(denied);
        Assert.Equal(EnvOutcome.Declined, denied.Set.Outcome);
        Assert.Empty(denied.Set.Variables);
        Assert.Equal("the person asked said no", denied.Reason);

        // A new run is a new connection, and the cooldown still holds.
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var again = await AttachedAsync(owner);
        var cooled = await again.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(cooled);
        Assert.Equal(EnvOutcome.Declined, cooled.Set.Outcome);
        Assert.Contains("refused a moment ago", cooled.Reason, StringComparison.Ordinal);
        Assert.Equal(1, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task Nobody_answering_in_the_window_releases_nothing()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var pending = client.ReleaseEnvAsync(Request("session-one"), Token).AsTask();
        await _fixture.Channel.Waiting.WaitAsync(_wait, Token);
        _fixture.Clock.Advance(ApprovalLimits.Default.Window);
        var reply = await pending.WaitAsync(_wait, Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Declined, reply.Set.Outcome);
        Assert.Equal("nobody answered the prompt in time", reply.Reason);
        Assert.Empty(reply.Set.Variables);
        Assert.True(_fixture.Channel.Withdrawn);
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_withdraws_the_prompt_and_releases_nothing()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var pending = client.ReleaseEnvAsync(Request("session-one"), Token).AsTask();
        await _fixture.Channel.Waiting.WaitAsync(_wait, Token);
        _lifetime!.End();
        var reply = await pending.WaitAsync(_wait, Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Locked, reply.Set.Outcome);
        Assert.Empty(reply.Set.Variables);
        Assert.True(_fixture.Channel.Withdrawn);
    }

    [Fact]
    public async Task A_runner_that_hangs_up_withdraws_its_prompt()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        var client = await AttachedAsync(owner);
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var pending = client.ReleaseEnvAsync(Request("session-one"), giveUp.Token).AsTask();
        await _fixture.Channel.Waiting.WaitAsync(_wait, Token);
        await giveUp.CancelAsync();
        Assert.Null(await pending.WaitAsync(_wait, Token));
        await client.DisposeAsync();

        var deadline = DateTime.UtcNow + _wait;
        while (!_fixture.Channel.Withdrawn)
        {
            Assert.True(DateTime.UtcNow < deadline, "the prompt was never withdrawn");
            await Task.Delay(20, Token);
        }
    }

    [Fact]
    public async Task A_request_from_no_attachment_another_vault_or_an_ended_session_is_refused_unasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);

        await using var unattached = await ApproverClient.TryConnectAsync(owner.PipeName, _wait, Token);
        Assert.NotNull(unattached);
        var never = await unattached.ReleaseEnvAsync(Request("session-one"), Token);

        await using var client = await AttachedAsync(owner);
        var elsewhere = await client.ReleaseEnvAsync(Request("session-one") with { Vault = Path.Combine(_directory, "other.kdbx") }, Token);

        _lifetime!.End();
        _lifetime = new SessionLifetime("session-two");
        var ended = await client.ReleaseEnvAsync(Request("session-one"), Token);

        foreach (var reply in new[] { never, elsewhere, ended })
        {
            Assert.NotNull(reply);
            Assert.Equal(EnvOutcome.NoSession, reply.Set.Outcome);
            Assert.Empty(reply.Set.Variables);
        }

        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task A_locked_vault_refuses_unasked()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        _lifetime!.End();
        var reply = await client.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Locked, reply.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task An_unusable_set_is_refused_before_anybody_is_asked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one", project: "broken"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Unusable, reply.Set.Outcome);
        Assert.Equal("BAD-NAME", Assert.Single(reply.Set.Problems).Key);
        Assert.Empty(reply.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task A_command_that_cannot_be_shown_whole_is_refused_before_anybody_is_asked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var empty = await client.ReleaseEnvAsync(Request("session-one", command: []), Token);
        var tooLong = await client.ReleaseEnvAsync(
            Request("session-one", command: ["echo", new string('a', EnvReleasePrompt.MaximumCommandLength)]), Token);

        Assert.Equal(EnvOutcome.Invalid, empty?.Set.Outcome);
        Assert.Equal(EnvOutcome.Invalid, tooLong?.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task A_run_while_an_agent_is_being_asked_is_refused_rather_than_queued()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this, handler: CredentialHandler());
        await using var agent = await AttachedAsync(owner);
        await using var runner = await AttachedAsync(owner);

        var credential = agent.RequestAsync(
            new CredentialRequest
            {
                Entry = "env/dev/TOKEN",
                Field = "password",
                Reason = "deploy",
                TtlSeconds = 60,
                Exposure = ["env/**"],
                Vault = VaultPath,
                Session = "session-one",
            },
            Token).AsTask();
        await _fixture.Channel.Waiting.WaitAsync(_wait, Token);

        var reply = await runner.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Declined, reply.Set.Outcome);
        Assert.Contains("another request is waiting", reply.Reason, StringComparison.Ordinal);
        Assert.Equal(1, _fixture.Channel.Asked);

        _lifetime!.End();
        await credential.WaitAsync(_wait, Token);
    }

    [Fact]
    public async Task An_owner_composed_without_environments_refuses_every_set()
    {
        await using var owner = Owner.Start(this, environments: false);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.NoSession, reply.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnHourAnswer_LetsTheSameRunThroughUnasked_WithTheLatestValues()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        var first = await RunAsync(authority, Request("session-one"));

        new EnvStore(_vault).TrySet("dev", "TOKEN", "rotated-after-the-answer", out _);
        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        var second = await RunAsync(authority, Request("session-one"));

        Assert.Equal(EnvOutcome.Resolved, first.Set.Outcome);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal("rotated-after-the-answer", second.Set.Variables.Single(variable => variable.Key == "TOKEN").Value);
        Assert.Equal(1, _fixture.Channel.Asked);
    }

    /// <summary>A replay by any program of the person's is the grant's accepted risk (T-34), so each one it serves is said out loud.</summary>
    [Fact]
    public async Task ARunServedByAGrant_IsNarrated_AndAnAskedOneIsNot()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        List<string> narrated = [];
        var authority = EnvAuthority(grants, narrated.Add);

        await RunAsync(authority, Request("session-one"));
        Assert.Empty(narrated);

        _fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        await RunAsync(authority, Request("session-one"));

        var line = Assert.Single(narrated);
        Assert.Equal(
            $"released dev/dev to `deploy --to \"staging area\"` from a timed grant ({EnvGrantCache.CeilingSeconds - 60}s left)",
            line);
        Assert.DoesNotContain(_token1, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnHourAnswer_DoesNotCoverAnotherCommandDirectoryOrProject()
    {
        new EnvStore(_vault).TrySet("other", "KEY", _token1, out _);
        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        await RunAsync(authority, Request("session-one"));

        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        var command = await RunAsync(authority, Request("session-one", command: ["deploy", "--to", "production"]));
        var directory = await RunAsync(authority, Request("session-one") with { Directory = Path.Combine(_directory, "elsewhere") });
        var project = await RunAsync(authority, Request("session-one", project: "other"));

        Assert.All([command, directory, project], reply => Assert.Equal(EnvOutcome.Declined, reply.Set.Outcome));
        Assert.Equal(4, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AllowOnce_AsksAgainNextRun()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        var first = await RunAsync(authority, Request("session-one"));
        var second = await RunAsync(authority, Request("session-one"));

        Assert.Equal(EnvOutcome.Resolved, first.Set.Outcome);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal(2, _fixture.Channel.Asked);
        Assert.Empty(grants.InForce());
    }

    [Fact]
    public async Task AProtectedProfile_OffersNoTimedChoice_AndStoresNothing()
    {
        Assert.NotEqual(EnvSetOutcome.Rejected, new EnvStore(_vault).TrySet("dev", "prod", "TOKEN", _stagingToken, out _));
        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        var first = await RunAsync(authority, Request("session-one") with { Profile = "prod" });
        var second = await RunAsync(authority, Request("session-one") with { Profile = "prod" });

        Assert.Equal(EnvOutcome.Resolved, first.Set.Outcome);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.LastEnvPrompt!.GrantSeconds);
        Assert.Equal(2, _fixture.Channel.Asked);
        Assert.Empty(grants.InForce());
    }

    [Fact]
    public async Task TheEnvGrant_IsCappedAtFifteenMinutes()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        await RunAsync(authority, Request("session-one"));
        Assert.Equal(EnvGrantCache.CeilingSeconds, _fixture.Channel.LastEnvPrompt!.GrantSeconds);

        _fixture.Clock.Advance(TimeSpan.FromSeconds(EnvGrantCache.CeilingSeconds));
        await RunAsync(authority, Request("session-one"));

        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnEnvGrant_IsListedAndRevokedById_LikeAnAgentsGrant()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);
        await RunAsync(authority, Request("session-one"));
        var key = Assert.Single(authority.Activity.EnvGrants).Key;
        var connection = Guid.NewGuid().ToString("N");
        await authority.AttachAsync(new AttachRequest(VaultPath), connection, Token);

        var listed = await authority.GrantsAsync(new GrantsRequest { Vault = VaultPath, Session = "session-one" }, connection, Token);
        var row = Assert.Single(listed.Grants);
        var revoked = await authority.RevokeGrantsAsync(
            new RevokeGrantsRequest([row.Id], null, false) { Vault = VaultPath, Session = "session-one" }, connection, Token);

        Assert.Equal(
            new GrantSummary(GrantId.OfEnv(key), "env", "keypaste run", "dev · dev", "set", EnvGrantCache.CeilingSeconds),
            row);
        Assert.Equal(new RevokeGrantsReply(1, string.Empty), revoked);
        Assert.Empty(grants.InForce());
    }

    [Fact]
    public async Task Lock_ForgetsEnvGrants()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        _lifetime!.Own(grants);
        var authority = EnvAuthority(grants);

        await RunAsync(authority, Request("session-one"));
        var key = Assert.Single(authority.Activity.EnvGrants).Key;

        _lifetime.End();

        Assert.Empty(grants.InForce());
        Assert.False(grants.TryUse(key, ["DATABASE_URL", "TOKEN"], out _));
        Assert.Same(ApproverActivity.None, authority.Activity);
    }

    [Fact]
    public async Task RevokeAll_ForgetsEnvGrants()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        await RunAsync(authority, Request("session-one"));
        authority.RevokeAll();
        await RunAsync(authority, Request("session-one"));

        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task Activity_ListsEnvGrants_WithNamesOnly()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        await RunAsync(authority, Request("session-one"));

        var grant = Assert.Single(authority.Activity.EnvGrants);
        Assert.Equal("dev", grant.Project);
        Assert.Equal("dev", grant.Profile);
        Assert.Equal("deploy --to \"staging area\"", grant.Command);
        Assert.Equal(TimeSpan.FromSeconds(EnvGrantCache.CeilingSeconds), grant.Remaining);
        Assert.DoesNotContain(_token1, grant.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_token2, grant.ToString(), StringComparison.Ordinal);

        authority.RevokeEnvGrant(grant.Key);
        await RunAsync(authority, Request("session-one"));

        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AProfileRequest_ReleasesThatProfile()
    {
        AddStaging();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one") with { Profile = "staging" }, Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal("staging", reply.Set.Profile);
        Assert.Equal([new EnvVariable("TOKEN", _stagingToken)], reply.Set.Variables);

        var subset = await client.ReleaseEnvAsync(Request("session-one") with { Keys = ["TOKEN"] }, Token);
        Assert.Equal([new EnvVariable("TOKEN", _token1)], subset?.Set.Variables);
        Assert.Equal("dev", subset?.Set.Profile);
    }

    [Fact]
    public async Task AnInvalidProfile_IsRefusedBeforeAsking()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);

        await using (var client = await AttachedAsync(owner))
        {
            var missing = await client.ReleaseEnvAsync(Request("session-one") with { Profile = "qa" }, Token);
            Assert.Equal(EnvOutcome.NoProfile, missing?.Set.Outcome);
            Assert.Equal("qa", missing?.Set.Profile);

            // The wire never carries a profile keypaste would not resolve: the owner hangs up unasked.
            Assert.Null(await client.ReleaseEnvAsync(Request("session-one") with { Profile = "Prod" }, Token));
        }

        var authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            new SessionEnvironments(_fixture.Gate, lifetime => ReferenceEquals(lifetime, _lifetime) ? _vault : null, _fixture.Clock));
        Assert.True((await authority.AttachAsync(new AttachRequest(VaultPath), "in-process", Token)).Attached);

        var badProfile = await authority.ReleaseEnvAsync(Request("session-one") with { Profile = "Prod" }, "in-process", Token);
        var badKey = await authority.ReleaseEnvAsync(Request("session-one") with { Keys = ["BAD-KEY"] }, "in-process", Token);

        Assert.Equal(EnvOutcome.Invalid, badProfile.Set.Outcome);
        Assert.Equal("Prod", badProfile.Set.Profile);
        Assert.Equal(EnvOutcome.Invalid, badKey.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ThePromptNamesTheProfile()
    {
        AddStaging();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        await client.ReleaseEnvAsync(
            Request("session-one") with { Profile = "staging", Keys = ["TOKEN"], FileLines = ["API_TOKEN ← TOKEN", "PROXY=http://p‮"] },
            Token);

        var prompt = Assert.IsType<EnvReleasePrompt>(_fixture.Channel.LastEnvPrompt);
        Assert.Equal("staging", prompt.Profile);
        Assert.Equal(["TOKEN"], prompt.Keys);
        Assert.Equal("API_TOKEN ← TOKEN", prompt.FileLines[0]);
        Assert.DoesNotContain('‮', prompt.FileLines[1]);
        Assert.DoesNotContain(_stagingToken, prompt.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCooldownIsPerProfile()
    {
        AddStaging();
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);

        await using (var first = await AttachedAsync(owner))
        {
            Assert.Equal(EnvOutcome.Declined, (await first.ReleaseEnvAsync(Request("session-one") with { Profile = "staging" }, Token))?.Set.Outcome);
        }

        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var second = await AttachedAsync(owner);

        var dev = await second.ReleaseEnvAsync(Request("session-one"), Token);
        var stagingAgain = await second.ReleaseEnvAsync(Request("session-one") with { Profile = "staging" }, Token);

        Assert.Equal(EnvOutcome.Resolved, dev?.Set.Outcome);
        Assert.Equal(EnvOutcome.Declined, stagingAgain?.Set.Outcome);
        Assert.Contains("refused a moment ago", stagingAgain?.Reason, StringComparison.Ordinal);
        Assert.Equal(2, _fixture.Channel.Asked);
    }

    /// <summary>The owner's authority itself, with timed env grants, answering runs without a pipe.</summary>
    private SessionAuthority EnvAuthority(EnvGrantCache grants, Action<string>? narrate = null) =>
        new(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            new SessionEnvironments(
                _fixture.Gate,
                lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
                _fixture.Clock,
                grants,
                narrate));

    /// <summary>One run: a new connection that attaches and asks once, as every <c>keypaste run --session</c> is.</summary>
    private async Task<EnvReply> RunAsync(SessionAuthority authority, EnvRequest request)
    {
        var connection = Guid.NewGuid().ToString("N");
        await authority.AttachAsync(new AttachRequest(VaultPath), connection, Token);

        try
        {
            return await authority.ReleaseEnvAsync(request, connection, Token);
        }
        finally
        {
            authority.Disconnected(connection);
        }
    }

    private const string _stagingToken = "sk_live_env_owner_staging_sentinel";

    private void AddStaging()
    {
        Assert.NotEqual(EnvSetOutcome.Rejected, new EnvStore(_vault).TrySet("dev", "staging", "TOKEN", _stagingToken, out _));
        _vault.Save();
    }

    private ApproverHandler CredentialHandler() =>
        new(
            new VaultCredentialSource(() => _vault),
            new VaultEntryNameLister(() => _vault),
            _fixture.Gate,
            _fixture.Grants,
            _fixture.Policy);

    private async Task<ApproverClient> AttachedAsync(Owner owner)
    {
        var client = await ApproverClient.TryConnectAsync(owner.PipeName, _wait, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(VaultPath), Token))?.Attached);
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

        internal static Owner Start(SessionAuthorityEnvTests test, ApproverHandler? handler = null, bool environments = true)
        {
            var name = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                handler ?? test._fixture.Handler,
                environments
                    ? new SessionEnvironments(
                        test._fixture.Gate,
                        lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                        test._fixture.Clock)
                    : null);

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
