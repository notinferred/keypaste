using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A request two items answer to puts both in front of the person, who picks one: exactly that item's field is released,
/// by its UUID, and the owner's line names it; a refusal, a lock or an allow with no pick releases nothing, and a request
/// one item answers to is asked as it always was (F.76).
/// </summary>
public sealed class ItemPickerTests : IDisposable
{
    private const string _connection = "conn-1";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-item-picker-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionAuthority _authority;

    public ItemPickerTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            created.AddEntry(new VaultEntry { GroupPath = "services", Title = "Mail", Username = "alice", Url = "https://mail.example.com", Password = "alice-password" });
            created.AddEntry(new VaultEntry { GroupPath = "services", Title = "Mail", Username = "bob", Password = "bob-password" });
            created.AddEntry(new VaultEntry { GroupPath = "services", Title = "Forum", Username = "carol", Password = "carol-password" });
            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        _log = new OwnerLog(_directory);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => _lifetime.IsLive ? _vault : null),
            new VaultEntryNameLister(() => _vault),
            _fixture.Gate,
            _fixture.Rules);

        _authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            handler,
            () => _log.Log,
            new SessionEnvironments(_fixture.Gate, lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null, _fixture.Clock));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _lifetime.Dispose();
        _log.Dispose();
        _vault.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_request_two_items_answer_to_puts_both_in_front_of_the_person_with_none_picked()
    {
        var reply = await AskAsync("services/Mail");

        var prompt = Assert.IsType<ApprovalPrompt>(_fixture.Channel.LastPrompt);
        Assert.Equal(1, _fixture.Channel.PickersAsked);
        Assert.Equal("services/Mail", prompt.Entry);
        Assert.Equal(["alice", "bob"], prompt.Candidates.Select(candidate => candidate.Username));
        Assert.Equal(["https://mail.example.com", string.Empty], prompt.Candidates.Select(candidate => candidate.Url));
        Assert.All(prompt.Candidates, candidate => Assert.Equal(("Mail", "services"), (candidate.Title, candidate.Group)));
        Assert.Equal([Uuid("alice"), Uuid("bob")], prompt.Candidates.Select(candidate => candidate.Item));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (reply.Decision, reply.Method));
        Assert.Null(reply.Value);
        Assert.DoesNotContain("-password", prompt.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public async Task The_picked_items_field_alone_is_released_and_its_line_names_that_item(string username)
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        _fixture.Channel.Pick = prompt => prompt.Candidates.ToList().FindIndex(candidate => candidate.Username == username);

        var reply = await AskAsync("services/Mail");
        var line = Assert.Single(_log.Lines());

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (reply.Decision, reply.Method));
        Assert.Equal(username + "-password", reply.Value);
        Assert.Equal("services/Mail", line.GetProperty("args").GetProperty("entry").GetString());
        Assert.Equal(Uuid(username), line.GetProperty("args").GetProperty("item").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal("a person picked this item and approved this one request", line.GetProperty("reason").GetString());
        Assert.Equal(Uuid(username), Assert.Single(_authority.Released).Item);
        Assert.DoesNotContain("-password", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_allow_with_no_pick_or_a_pick_no_candidate_has_releases_nothing()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var unpicked = await AskAsync("services/Mail");

        _fixture.Clock.Advance(ApprovalLimits.Default.DenialCooldown);
        _fixture.Channel.Pick = _ => 2;
        var outside = await AskAsync("services/Mail");

        Assert.All([unpicked, outside], reply => Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value)));
        Assert.All(_log.Lines(), line => Assert.False(line.GetProperty("args").TryGetProperty("item", out _)));
    }

    [Fact]
    public async Task A_lock_while_the_person_picks_releases_nothing()
    {
        _fixture.Channel.Hold = true;
        _fixture.Channel.Pick = _ => 0;

        var asking = AskAsync("services/Mail");
        await _fixture.Channel.Waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _lifetime.End();
        var reply = await asking.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Null(reply.Value);
        Assert.True(_fixture.Channel.Withdrawn);
        Assert.Empty(_authority.Released);
    }

    [Fact]
    public async Task A_request_one_item_answers_to_is_asked_as_before_and_its_line_names_the_item()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await AskAsync("services/Forum");
        var line = Assert.Single(_log.Lines());

        Assert.Equal(0, _fixture.Channel.PickersAsked);
        Assert.Empty(_fixture.Channel.LastPrompt!.Candidates);
        Assert.Equal("carol-password", reply.Value);
        Assert.Equal("a person approved this one request", line.GetProperty("reason").GetString());
        Assert.Equal(Uuid("carol"), line.GetProperty("args").GetProperty("item").GetString());
    }

    private async Task<CredentialReply> AskAsync(string entry)
    {
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath), _connection, Token);

        return await _authority.RequestAsync(
            new CredentialRequest
            {
                Entry = entry,
                Field = "password",
                Reason = "send the weekly report",
                Exposure = ["services/**"],
                ClientName = "claude-code",
                ClientLabel = ApproverFixture.Agent,
                Vault = VaultPath,
                Session = attached.Session!,
            },
            _connection,
            Token);
    }

    private EntryName Name(string username) =>
        _vault.ReadEntries().Single(entry => entry.Username == username) is var entry ? EntryName.Of(entry) with { Uuid = entry.Uuid } : null!;

    private string Uuid(string username) => _vault.ReadEntries().Single(entry => entry.Username == username).Uuid!;
}
