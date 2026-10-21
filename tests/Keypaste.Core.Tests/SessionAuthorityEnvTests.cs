using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Keypaste.Core.Projects;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A <c>keypaste run --session</c> request over a real pipe: the owner releases a project's set only
/// to a connection attached to its current session, only after the person approves the command it
/// was shown, in the owner's prompt or in the app's Run confirmation whose launch pass the request
/// carries, and only while that session lasts (E.1c, D-0341, D-0452).
/// </summary>
public sealed class SessionAuthorityEnvTests : IDisposable
{
    private const string _token1 = "sk_live_env_owner_sentinel";
    private const string _token2 = "postgres://sentinel@db/app";
    private const string _launchSpent = "the run you confirmed in the app expired or was already used";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly string[] _launched = ["/bin/sh", "-c", "eval \"$1\"; exec \"${SHELL:-/bin/sh}\"", "keypaste", "npm start"];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-env-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly Vault _vault;
    private readonly AuditLog _audit;
    private readonly LaunchPasses _launches;
    private SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityEnvTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            ProjectVariables.Set(created, "dev", "TOKEN", _token1);
            ProjectVariables.Set(created, "dev", "DATABASE_URL", _token2);
            ProjectVariables.Set(created, "broken", "REF", "{REF:T@I:0123}");
            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        Assert.True(AuditLog.TryOpen(AuditPath, _fixture.Clock, out var audit, out var error), error);
        _audit = audit;
        _launches = new LaunchPasses(_fixture.Clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string AuditPath => Path.Combine(_directory, "audit.jsonl");

    private string WorkDirectory => Path.Combine(_directory, "work");

    public void Dispose()
    {
        _lifetime?.Dispose();
        _launches.Dispose();
        _vault.Dispose();
        _audit.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private EnvRequest Request(string session, string project = "dev", IReadOnlyList<string>? command = null) =>
        new(project, command ?? ["deploy", "--to", "staging area"], WorkDirectory) { Vault = VaultPath, Session = session };

    /// <summary>What the app's card showed for one of dev's environments, which its pass names.</summary>
    private LaunchClaim Claim(string profile = EnvProfileNames.Default) =>
        new("dev", profile, WorkDirectory, _launched, EnvResolution.Resolve(_vault, "dev", profile, keys: null, _fixture.Clock).Preview);

    /// <summary>The request the window's <c>keypaste run --session</c> sends for a claim.</summary>
    private EnvRequest Launched(LaunchClaim claim, string pass, string session = "session-one") =>
        new(claim.Project, claim.Command, claim.Directory) { Vault = VaultPath, Session = session, Profile = claim.Profile, Launch = pass };

    /// <summary>Gives the <c>wide</c> project one variable from each of <paramref name="count"/> items, each titled in <paramref name="length"/> characters, and returns the items as a line names them.</summary>
    private List<string> Wide(int count, int length)
    {
        List<string> items = [];

        foreach (var i in Enumerable.Range(0, count))
        {
            var key = $"KEY_{i}_";
            var wide = new EntryName("env/wide", key + new string('W', length - key.Length));
            _vault.AddEntry(new VaultEntry { GroupPath = wide.GroupPath, Title = wide.Title });
            Assert.True(_vault.SetFields(wide, [new FieldWrite(wide.Title, $"w{i}")]));
            Assert.True(_vault.AddTag(wide, "env:wide"));
            items.Add(ApprovalPrompt.Shown(wide));
        }

        _vault.Save();
        return items;
    }

    /// <summary>The log as the owner still holds it open for writing, which File.ReadAllText cannot share on Windows.</summary>
    private string AuditText()
    {
        using var stream = new FileStream(AuditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private List<JsonElement> AuditLines()
    {
        return [.. AuditText().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Parse)];

        static JsonElement Parse(string line)
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

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

    /// <summary>
    /// The owner records a run's release itself, as it records a token's: a run has no bridge to write
    /// its line (T-30). The line names the set and the entries it came from, never a value.
    /// </summary>
    [Fact]
    public async Task A_run_release_and_a_refusal_are_each_audited_by_the_owner()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using (var client = await AttachedAsync(owner))
        {
            Assert.Equal(EnvOutcome.Resolved, (await client.ReleaseEnvAsync(Request("session-one"), Token))?.Set.Outcome);
        }

        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using (var client = await AttachedAsync(owner))
        {
            Assert.Equal(EnvOutcome.Declined, (await client.ReleaseEnvAsync(Request("session-one", command: ["deploy", "--other"]), Token))?.Set.Outcome);
        }

        var lines = AuditLines();
        Assert.Equal(["granted", "denied"], lines.Select(line => line.GetProperty("decision").GetString()));
        Assert.All(lines, line =>
        {
            Assert.Equal("run", line.GetProperty("tool").GetString());
            Assert.Equal("keypaste run --session", line.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal("prompt", line.GetProperty("method").GetString());
            Assert.Equal("session-one", line.GetProperty("session").GetString());
        });
        Assert.Equal(["env/dev/.env"], lines[0].GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()).Distinct());

        var text = AuditText();
        Assert.DoesNotContain(_token1, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_token2, text, StringComparison.Ordinal);
    }

    /// <summary>A set drawn from 300 items, whose names take far more than 2 KiB, is released and its one line names every item (D-0454).</summary>
    [Fact]
    public async Task A_run_whose_set_comes_from_300_items_is_released_and_its_line_names_every_one()
    {
        var items = Wide(300, 76);
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one", project: "wide"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal(300, reply.Set.Variables.Count);
        Assert.Equal(1, _fixture.Channel.Asked);

        var line = Assert.Single(AuditLines());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal(items.Order(StringComparer.Ordinal), line.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()!).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A set naming more items than one audit line can record is refused before anybody is asked, so
    /// a person never approves a release that is then refused for want of its line.
    /// </summary>
    [Fact]
    public async Task A_run_whose_entries_would_not_fit_its_audit_line_is_refused_unasked()
    {
        // Each item's name takes more bytes in the line than its title has characters.
        Wide((SessionAuthority.MaximumAuditedEntriesBytes / EntryNameSanitizer.MaximumLength) + 1, EntryNameSanitizer.MaximumLength);
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one", project: "wide"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Invalid, reply.Set.Outcome);
        Assert.Equal("the run names more items than one audit line can record", reply.Reason);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal("invalid-request", Assert.Single(AuditLines()).GetProperty("method").GetString());
    }

    /// <summary>No record, no release: a set whose audit line cannot be written is refused whole.</summary>
    [Fact]
    public async Task A_run_whose_audit_line_cannot_be_written_releases_nothing()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        EnvReply? reply;
        using (new FileStream(AuditPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            reply = await client.ReleaseEnvAsync(Request("session-one"), Token);
        }

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Unreadable, reply.Set.Outcome);
        Assert.Equal("the audit log could not be written", reply.Reason);
        Assert.Empty(reply.Set.Variables);
        Assert.DoesNotContain(_token1, reply.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A set past one ordinary frame is released whole over the owner's pipe, in the reply only a runner reads, and its line names its items (D-0454).</summary>
    [Fact]
    public async Task A_set_of_200_KiB_is_released_whole_and_recorded_with_its_items()
    {
        var large = Enumerable.Range(0, 7).Select(i => new EnvVariable($"BLOB_{i}", new string((char)('a' + i), 30_000))).ToList();
        foreach (var variable in large)
        {
            ProjectVariables.Set(_vault, "dev", variable.Key, variable.Value);
        }

        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([.. large, new EnvVariable("DATABASE_URL", _token2), new EnvVariable("TOKEN", _token1)], reply.Set.Variables);
        Assert.True(large.Sum(variable => variable.Value.Length) > 200 * 1024);

        var line = Assert.Single(AuditLines());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal(["env/dev/.env"], line.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()).Distinct());
        Assert.DoesNotContain(large[0].Value, AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tagged_field_joins_the_set_the_prompt_names_each_source_and_a_prod_tagged_member_is_asked_once_only()
    {
        var stripe = new EntryName("services", "Stripe");
        _vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "stripe-login" });
        Assert.True(_vault.SetFields(stripe, [new FieldWrite("STRIPE_KEY", "stripe-field-sentinel")]));
        Assert.True(_vault.AddTag(stripe, "env:dev"));
        _vault.Save();

        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal(["DATABASE_URL", "STRIPE_KEY", "TOKEN"], reply.Set.Variables.Select(variable => variable.Key));
        var prompt = Assert.IsType<EnvReleasePrompt>(_fixture.Channel.LastEnvPrompt);
        Assert.Equal(["env/dev/.env", "services/Stripe", "env/dev/.env"], prompt.Entries);
        Assert.True(prompt.GrantSeconds > 0);
        Assert.DoesNotContain("stripe-field-sentinel", prompt.ToString(), StringComparison.Ordinal);

        Assert.True(_vault.AddTag(stripe, "env:dev:prod"));
        _vault.Save();

        await using var again = await AttachedAsync(owner);
        var once = await again.ReleaseEnvAsync(Request("session-one", command: ["deploy", "--again"]), Token);

        Assert.NotNull(once);
        Assert.Equal(EnvOutcome.Resolved, once.Set.Outcome);
        var asked = Assert.IsType<EnvReleasePrompt>(_fixture.Channel.LastEnvPrompt);
        Assert.Equal(0, asked.GrantSeconds);
        Assert.Contains("services/Stripe", asked.Entries);
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

    /// <summary>
    /// A request refused before it is considered is recorded by the owner too, under no session, and
    /// against this vault only when it named this vault.
    /// </summary>
    [Fact]
    public async Task A_request_from_no_attachment_another_vault_or_an_ended_session_is_refused_unasked_and_recorded()
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

        var lines = AuditLines();
        Assert.Equal(
            [
                "the request came from a connection attached to no unlock of the vault",
                "the request named a different vault from its attachment",
                "the request belongs to an unlock of the vault that has ended",
            ],
            lines.Select(line => line.GetProperty("reason").GetString()));
        Assert.All(lines, line =>
        {
            Assert.Equal("run", line.GetProperty("tool").GetString());
            Assert.Equal("keypaste run --session", line.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal("denied", line.GetProperty("decision").GetString());
            Assert.Equal("no-session", line.GetProperty("method").GetString());
            Assert.False(line.TryGetProperty("session", out _));
        });

        var key = VaultIdentity.Of(_directory, VaultPath).Key;
        Assert.Equal(new[] { key, null, key }, lines.Select(line => line.TryGetProperty("vault", out var vault) ? vault.GetString() : null));
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

        var line = Assert.Single(AuditLines());
        Assert.Equal("vault-locked", line.GetProperty("method").GetString());
        Assert.Equal("the vault is locked", line.GetProperty("reason").GetString());
        Assert.False(line.TryGetProperty("session", out _));
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
        Assert.Equal("REF", Assert.Single(reply.Set.Problems).Key);
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

        var lines = AuditLines();
        Assert.Equal(["invalid-request", "invalid-request"], lines.Select(line => line.GetProperty("method").GetString()));
        Assert.Equal(
            ["the request names no command", $"the command is longer than the {EnvReleasePrompt.MaximumCommandLength} characters a prompt shows whole"],
            lines.Select(line => line.GetProperty("reason").GetString()));
        Assert.All(lines, line => Assert.Equal("session-one", line.GetProperty("session").GetString()));
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
                Entry = "env/dev/.env",
                Field = "password",
                Reason = "deploy",
                TtlSeconds = 60,
                Exposure = ["env/**"],
                ClientLabel = ApproverFixture.Agent,
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

        var line = Assert.Single(AuditLines());
        Assert.Equal("no-session", line.GetProperty("method").GetString());
        Assert.Equal("session-one", line.GetProperty("session").GetString());
    }

    [Fact]
    public async Task AnHourAnswer_LetsTheSameRunThroughUnasked_WithTheLatestValues()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        var first = await RunAsync(authority, Request("session-one"));

        ProjectVariables.Set(_vault, "dev", "TOKEN", "rotated-after-the-answer");
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
            $"released dev/dev to `deploy --to \"staging area\"` under an allowance ({EnvGrantCache.CeilingSeconds - 60}s left)",
            line);
        Assert.DoesNotContain(_token1, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnHourAnswer_DoesNotCoverAnotherCommandDirectoryOrProject()
    {
        ProjectVariables.Set(_vault, "other", "KEY", _token1);
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
        ProjectVariables.Set(_vault, "dev", "prod", "TOKEN", _stagingToken);
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
    public async Task AnHourAnswer_IsNotUsed_OnceAMemberIsTaggedIntoProd()
    {
        var stripe = new EntryName("services", "Stripe");
        _vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "stripe-login" });
        Assert.True(_vault.SetFields(stripe, [new FieldWrite("STRIPE_KEY", "stripe-field-sentinel")]));
        Assert.True(_vault.AddTag(stripe, "env:dev"));
        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);

        var first = await RunAsync(authority, Request("session-one"));
        Assert.True(_vault.AddTag(stripe, "env:dev:prod"));
        _vault.Save();
        var second = await RunAsync(authority, Request("session-one"));

        Assert.Equal(EnvOutcome.Resolved, first.Set.Outcome);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal(2, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Channel.LastEnvPrompt!.GrantSeconds);
        Assert.Single(grants.InForce());
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
            () => _audit,
            new SessionEnvironments(_fixture.Gate, lifetime => ReferenceEquals(lifetime, _lifetime) ? _vault : null, _fixture.Clock));
        Assert.True((await authority.AttachAsync(new AttachRequest(VaultPath), "in-process", Token)).Attached);

        var badProfile = await authority.ReleaseEnvAsync(Request("session-one") with { Profile = "Prod" }, "in-process", Token);
        var badKey = await authority.ReleaseEnvAsync(Request("session-one") with { Keys = ["BAD-KEY"] }, "in-process", Token);

        Assert.Equal(EnvOutcome.Invalid, badProfile.Set.Outcome);
        Assert.Equal("Prod", badProfile.Set.Profile);
        Assert.Equal(EnvOutcome.Invalid, badKey.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);

        // Every request the owner read is recorded; the frame it hung up on was never read.
        Assert.Equal(["out-of-scope", "invalid-request", "invalid-request"], AuditLines().Select(line => line.GetProperty("method").GetString()));
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

    /// <summary>
    /// The app's Run confirmation on macOS is the person's answer to the one request its pass names:
    /// the owner releases the whole set without asking again, and records it as that answer, naming
    /// the items and never a value (D-0452).
    /// </summary>
    [Fact]
    public async Task A_launch_pass_releases_the_whole_set_unasked_and_is_recorded_as_confirmed_in_the_app()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        var reply = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([new EnvVariable("DATABASE_URL", _token2), new EnvVariable("TOKEN", _token1)], reply.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
        Assert.Contains(owner.Authority.Released, seen => seen is { Entry: "env/dev/.env", How: "run --session" });

        var line = Assert.Single(AuditLines());
        Assert.Equal("keypaste run --session", line.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal("2 variable(s), confirmed in the app", line.GetProperty("reason").GetString());
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.Equal(["env/dev/.env"], line.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()));

        var text = AuditText();
        Assert.DoesNotContain(_token1, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_token2, text, StringComparison.Ordinal);
        Assert.DoesNotContain(pass, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spent_pass_is_refused_unasked_and_recorded()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        var first = await client.ReleaseEnvAsync(Launched(claim, pass), Token);
        var again = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.Equal(EnvOutcome.Resolved, first?.Set.Outcome);
        Assert.NotNull(again);
        Assert.Equal(EnvOutcome.Unauthorized, again.Set.Outcome);
        Assert.Equal(_launchSpent, again.Reason);
        Assert.Empty(again.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);

        var lines = AuditLines();
        Assert.Equal(["granted", "denied"], lines.Select(line => line.GetProperty("decision").GetString()));
        Assert.Equal("invalid-request", lines[1].GetProperty("method").GetString());
        Assert.Equal(_launchSpent, lines[1].GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("never issued")]
    [InlineData("expired")]
    [InlineData("withdrawn")]
    public async Task A_pass_never_issued_expired_or_withdrawn_is_refused_unasked(string which)
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = which == "never issued"
            ? Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))
            : owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        if (which == "expired")
        {
            _fixture.Clock.Advance(LaunchPasses.Lifetime);
        }
        else if (which == "withdrawn")
        {
            owner.Authority.WithdrawLaunch(pass);
        }

        var reply = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
        Assert.Equal(_launchSpent, reply.Reason);
        Assert.Equal(0, _fixture.Channel.Asked);

        var line = Assert.Single(AuditLines());
        Assert.Equal("invalid-request", line.GetProperty("method").GetString());
        Assert.Equal(_launchSpent, line.GetProperty("reason").GetString());
    }

    /// <summary>A pass dies with the unlock its card was answered in, and none is issued for a session that is not the live one.</summary>
    [Fact]
    public async Task A_pass_belongs_to_the_session_its_card_was_answered_in()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        var claim = Claim();

        Assert.Null(owner.Authority.IssueLaunch(claim, "session-zero"));
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        _lifetime!.End();
        Assert.Null(owner.Authority.IssueLaunch(claim, "session-one"));
        _lifetime = new SessionLifetime("session-two");
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseEnvAsync(Launched(claim, pass, "session-two"), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
        Assert.Equal(_launchSpent, reply.Reason);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    /// <summary>Any admitted presentation spends the pass, an invalid one too, so a broken request cannot be retried under it.</summary>
    [Fact]
    public async Task An_invalid_request_carrying_a_valid_pass_spends_it()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        var invalid = await client.ReleaseEnvAsync(Launched(claim, pass) with { Command = [] }, Token);
        var exact = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.Equal(EnvOutcome.Invalid, invalid?.Set.Outcome);
        Assert.Equal(EnvOutcome.Unauthorized, exact?.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(["invalid-request", "invalid-request"], AuditLines().Select(line => line.GetProperty("method").GetString()));
    }

    /// <summary>
    /// The card shows a mapped command whole up to 4096 characters, so a pass releases Terminal's run of
    /// one, whose command line is longer than the owner's prompt shows; the same request without the pass
    /// is still refused before anyone is asked (D-0452, T-30).
    /// </summary>
    [Fact]
    public async Task A_pass_releases_the_longest_command_a_mapping_holds_and_without_it_the_request_is_refused_unasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var longest = string.Concat(Enumerable.Repeat("say \"it's\" 'a \"b\"' ", 300))[..ProjectMappings.MaximumCommandLength];
        var claim = Claim() with { Command = ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", longest] };
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        var released = await client.ReleaseEnvAsync(Launched(claim, pass), Token);
        var unpassed = await client.ReleaseEnvAsync(Launched(claim, pass) with { Launch = null }, Token);

        Assert.NotNull(released);
        Assert.Equal(EnvOutcome.Resolved, released.Set.Outcome);
        Assert.Equal([new EnvVariable("DATABASE_URL", _token2), new EnvVariable("TOKEN", _token1)], released.Set.Variables);
        Assert.NotNull(unpassed);
        Assert.Equal(EnvOutcome.Invalid, unpassed.Set.Outcome);
        Assert.Equal($"the command is longer than the {EnvReleasePrompt.MaximumCommandLength} characters a prompt shows whole", unpassed.Reason);
        Assert.Empty(unpassed.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);

        var lines = AuditLines();
        Assert.Equal(["granted", "denied"], lines.Select(line => line.GetProperty("decision").GetString()));
        Assert.Equal(["prompt", "invalid-request"], lines.Select(line => line.GetProperty("method").GetString()));
    }

    /// <summary>A pass is issued only for what the card can show and Terminal's run sends: one command a mapping can hold, in Terminal's script (D-0452).</summary>
    [Fact]
    public async Task No_pass_is_issued_for_a_command_longer_than_a_mapping_holds_or_one_Terminal_does_not_start()
    {
        await using var owner = Owner.Start(this);
        var claim = Claim();
        var tooLong = claim with { Command = ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", new string('a', ProjectMappings.MaximumCommandLength + 1)] };
        var notTerminals = claim with { Command = ["deploy", "--to", "staging area"] };

        Assert.Throws<ArgumentException>(() => owner.Authority.IssueLaunch(tooLong, "session-one"));
        Assert.Throws<ArgumentException>(() => owner.Authority.IssueLaunch(notTerminals, "session-one"));
        Assert.NotNull(owner.Authority.IssueLaunch(claim, "session-one"));
    }

    /// <summary>The pass covers only the request the card named; presented for anything else it is spent and nobody is asked.</summary>
    [Theory]
    [InlineData("project")]
    [InlineData("environment")]
    [InlineData("directory")]
    [InlineData("command")]
    [InlineData("keys")]
    [InlineData("file lines")]
    public async Task A_pass_presented_for_anything_the_card_did_not_name_is_spent_and_refused_unasked(string differs)
    {
        AddStaging();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);
        var exact = Launched(claim, pass);

        var other = differs switch
        {
            "project" => exact with { Project = "other" },
            "environment" => exact with { Profile = "staging" },
            "directory" => exact with { Directory = Path.Combine(_directory, "elsewhere") },
            "command" => exact with { Command = [.. claim.Command.SkipLast(1), "npm test"] },
            "keys" => exact with { Keys = ["TOKEN"] },
            _ => exact with { FileLines = ["API_TOKEN ← TOKEN"] },
        };

        var refused = await client.ReleaseEnvAsync(other, Token);
        var afterwards = await client.ReleaseEnvAsync(exact, Token);

        foreach (var reply in new[] { refused, afterwards })
        {
            Assert.NotNull(reply);
            Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
            Assert.Equal(_launchSpent, reply.Reason);
            Assert.Empty(reply.Set.Variables);
        }

        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.All(AuditLines(), line =>
        {
            Assert.Equal("denied", line.GetProperty("decision").GetString());
            Assert.Equal("invalid-request", line.GetProperty("method").GetString());
        });
    }

    /// <summary>The card showed names, the items they come from and the protection; a set that differs in any of them was not what the person confirmed.</summary>
    [Theory]
    [InlineData("a variable added")]
    [InlineData("a variable moved to another item")]
    [InlineData("the environment protected")]
    public async Task A_set_that_changed_after_the_card_is_refused_as_changed_unasked(string change)
    {
        var stripe = new EntryName("services", "Stripe");
        _vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe" });
        Assert.True(_vault.SetFields(stripe, [new FieldWrite("STRIPE_KEY", "stripe-field-sentinel")]));
        Assert.True(_vault.AddTag(stripe, "env:dev"));
        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        switch (change)
        {
            case "a variable added":
                ProjectVariables.Set(_vault, "dev", "NEW_KEY", "new-key-sentinel");
                break;
            case "a variable moved to another item":
                Assert.True(_vault.RemoveTags(stripe, ["env:dev"]));
                ProjectVariables.Set(_vault, "dev", "STRIPE_KEY", "stripe-field-sentinel");
                break;
            default:
                Assert.True(_vault.AddTag(stripe, "env:dev:prod"));
                break;
        }

        _vault.Save();

        var reply = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.ChangedWhileAsked, reply.Set.Outcome);
        Assert.Equal("dev's variables changed after you confirmed in the app", reply.Reason);
        Assert.Empty(reply.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);

        var line = Assert.Single(AuditLines());
        Assert.Equal("vault-changed", line.GetProperty("method").GetString());
        Assert.Equal("dev's variables changed after you confirmed in the app", line.GetProperty("reason").GetString());
        Assert.DoesNotContain("sentinel", AuditText(), StringComparison.Ordinal);
    }

    /// <summary>A protected environment needs a live answer every time (D-0348, D-0391); the card is one, so its pass releases the set once.</summary>
    [Fact]
    public async Task A_protected_environment_is_released_once_under_its_pass()
    {
        ProjectVariables.Set(_vault, "dev", "prod", "TOKEN", _stagingToken);
        _vault.Save();
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim("prod");
        Assert.True(claim.Preview.RequiresLiveApproval);
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        var released = await client.ReleaseEnvAsync(Launched(claim, pass), Token);
        var again = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.NotNull(released);
        Assert.Equal(EnvOutcome.Resolved, released.Set.Outcome);
        Assert.Equal([new EnvVariable("TOKEN", _stagingToken)], released.Set.Variables);
        Assert.Equal(EnvOutcome.Unauthorized, again?.Set.Outcome);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
    }

    /// <summary>
    /// A pass is one answer, not a grant: it stores nothing a later run could use, a refusal's cooldown
    /// does not stand in its way, and it sets none.
    /// </summary>
    [Fact]
    public async Task A_pass_neither_stores_a_timed_grant_nor_meets_or_sets_the_cooldown()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        using var grants = new EnvGrantCache(_fixture.Clock);
        var authority = EnvAuthority(grants);
        var claim = Claim();
        var asked = Launched(claim, "unused") with { Launch = null };

        var first = await RunAsync(authority, Launched(claim, authority.IssueLaunch(claim, "session-one")!));
        var denied = await RunAsync(authority, asked);
        var second = await RunAsync(authority, Launched(claim, authority.IssueLaunch(claim, "session-one")!));

        Assert.Equal(EnvOutcome.Resolved, first.Set.Outcome);
        Assert.Equal(EnvOutcome.Declined, denied.Set.Outcome);
        Assert.Equal("the person asked said no", denied.Reason);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal(1, _fixture.Channel.Asked);
        Assert.Empty(grants.InForce());
    }

    /// <summary>No record, no release: a pass whose release cannot be recorded releases nothing.</summary>
    [Fact]
    public async Task A_pass_release_whose_audit_line_cannot_be_written_releases_nothing()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        var claim = Claim();
        var pass = owner.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        EnvReply? reply;
        using (new FileStream(AuditPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            reply = await client.ReleaseEnvAsync(Launched(claim, pass), Token);
        }

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Unreadable, reply.Set.Outcome);
        Assert.Equal("the audit log could not be written", reply.Reason);
        Assert.Empty(reply.Set.Variables);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    /// <summary><c>keypaste approve</c> builds the same server as the app, whose pass store nothing issues to: a pass presented to it is refused unasked.</summary>
    [Fact]
    public async Task An_owner_that_issued_no_pass_refuses_one_unasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        using var server = SessionServer.Listen(
            "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)),
            new SessionServerOptions
            {
                Vault = VaultIdentity.Of(_directory, VaultPath),
                Lifetime = _lifetime!,
                CurrentLifetime = () => _lifetime,
                UnlockedFor = lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
                Channel = _fixture.Channel,
                Settings = _fixture.Settings,
                Hourly = _fixture.Hourly,
                Audit = () => _audit,
                WatchEdits = _ => { },
                UnwatchEdits = _ => { },
                Clock = _fixture.Clock,
            });
        Assert.True((await server.Authority.AttachAsync(new AttachRequest(VaultPath), "runner", Token)).Attached);

        var reply = await server.Authority.ReleaseEnvAsync(
            Launched(Claim(), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))), "runner", Token);

        Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
        Assert.Equal(_launchSpent, reply.Reason);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal("invalid-request", Assert.Single(AuditLines()).GetProperty("method").GetString());
    }

    /// <summary>The owner's authority itself, with timed env grants, answering runs without a pipe.</summary>
    private SessionAuthority EnvAuthority(EnvGrantCache grants, Action<string>? narrate = null) =>
        new(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            () => _audit,
            new SessionEnvironments(
                _fixture.Gate,
                lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
                _fixture.Clock,
                grants,
                narrate,
                _launches));

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
        ProjectVariables.Set(_vault, "dev", "staging", "TOKEN", _stagingToken);
        _vault.Save();
    }

    private ApproverHandler CredentialHandler() =>
        new(
            new VaultCredentialSource(() => _vault),
            new VaultEntryNameLister(() => _vault),
            _fixture.Gate,
            _fixture.Grants,
            _fixture.Settings);

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

        private Owner(string pipeName, SessionAuthority authority, ApproverListener listener)
        {
            PipeName = pipeName;
            Authority = authority;
            _listener = listener;
            _running = listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal SessionAuthority Authority { get; }

        internal static Owner Start(SessionAuthorityEnvTests test, ApproverHandler? handler = null, bool environments = true)
        {
            var name = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                handler ?? test._fixture.Handler,
                () => test._audit,
                environments
                    ? new SessionEnvironments(
                        test._fixture.Gate,
                        lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                        test._fixture.Clock,
                        Launches: test._launches)
                    : null);

            return new Owner(name, authority, new ApproverListener(name, authority));
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
