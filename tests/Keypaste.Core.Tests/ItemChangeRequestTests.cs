using System.Text;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An agent's change to an item (THREATS.md T-57), on a real vault through the owner's own authority and log, only the person
/// faked: each change is made only once the person allows it, is refused unasked outside what the agent sees whole, and a delete
/// for good reaches only an item the same agent created through a save the person approved.
/// </summary>
public sealed class ItemChangeRequestTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _label = "claude-code";
    private static readonly EntryName _github = new("work", "github");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-item-change-").FullName;
    private readonly ManualClock _clock = new();
    private readonly FakeChannel _channel = new();
    private readonly ApprovalGate _gate;
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly SessionAuthority _authority;
    private bool _logOpens = true;

    public ItemChangeRequestTests()
    {
        _vault = Vault.Create(VaultPath, _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "github-password" });
        _vault.AddEntry(new VaultEntry { GroupPath = "env/acme", Title = "dev", Password = "acme-password" });
        Assert.True(_vault.AddTag(new EntryName("env/acme", "dev"), "env:acme:dev"));
        Assert.True(_vault.WriteRecord(new EntryName(ReservedGroups.Shares, "link"), _ => Guid.NewGuid(), "note", "kept"));
        _vault.Save();
        _log = new OwnerLog(_directory);
        _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);
        _channel.SaveValue = "SENTINEL-NEW-TOKEN";
        Func<SessionLifetime, Vault?> unlockedFor = lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null;
        _authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            new ApproverHandler(new VaultCredentialSource(() => _lifetime.IsLive ? _vault : null, _clock), new VaultEntryNameLister(() => _vault), _gate, IAgentRules.Defaults),
            () => _logOpens ? _log.Log : null,
            clock: _clock,
            saver: new SessionSaver(_gate, unlockedFor, Write, _clock, narrate: null),
            changer: new SessionItemChanger(_gate, unlockedFor, Write, _clock, narrate: null));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _gate.Dispose();
        _lifetime.Dispose();
        _vault.Dispose();
        _log.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_move_to_Trash_is_made_only_once_the_person_allows_it()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await ChangeAsync(Change("work/github", ItemChangeRules.MoveToRecycleBin));

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, "work/github"), (reply.Decision, reply.Method, reply.Item));
        Assert.True(reply.Recorded);
        Assert.Equal("Move work/github to Trash", _channel.LastChangePrompt!.Says);
        Assert.Null(Reopened(vault => vault.Find(_github)));
        Assert.Single(Reopened(vault => vault.ReadRecycled()), row => row.Title == "github");
        var line = Assert.Single(_log.Lines());
        Assert.Equal(("request_item_change", "granted"), (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString()));
        Assert.Contains(ItemChangeRules.MoveToRecycleBin, line.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("github-password", _log.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denied_change_changes_nothing_and_holds_the_item_back_whatever_is_asked_next()
    {
        _channel.Answer = ApprovalAnswer.Denied;

        var denied = await ChangeAsync(Change("work/github", ItemChangeRules.MoveToRecycleBin));
        var again = await ChangeAsync(Change("work/github", ItemChangeRules.MarkNeedsUpdate));

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (denied.Decision, denied.Method));
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(1, _channel.Asked);
        Assert.NotNull(Reopened(vault => vault.Find(_github)));
        Assert.Equal(2, _log.Lines().Count);
    }

    [Fact]
    public async Task An_expiry_is_KeePass_own_and_a_day_given_starts_that_day()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var now = await ChangeAsync(Change("work/github", ItemChangeRules.MarkExpired));
        Assert.Equal("Mark work/github as expired now", _channel.LastChangePrompt!.Says);
        var nowExpires = Reopened(vault => vault.ReadTimes(_github)!.Expires);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var later = await ChangeAsync(Change("work/github", ItemChangeRules.MarkExpired) with { Expires = "2026-12-01" });

        Assert.Equal((AuditDecision.Granted, AuditDecision.Granted), (now.Decision, later.Decision));
        Assert.Equal(_clock.GetUtcNow().AddMinutes(-2).ToUnixTimeSeconds(), nowExpires!.Value.ToUnixTimeSeconds());
        Assert.Equal("Mark work/github as expired on 2026-12-01", _channel.LastChangePrompt!.Says);
        var expires = Reopened(vault => vault.ReadTimes(_github)!.Expires)!.Value;
        Assert.Equal(new DateTime(2026, 12, 1), TimeZoneInfo.ConvertTime(expires, _clock.LocalTimeZone).DateTime);
        Assert.Equal(2, Reopened(vault => vault.ReadHistory(_github)!.Count));
    }

    [Fact]
    public async Task Needs_update_is_a_tag_added_once()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var tagged = await ChangeAsync(Change("work/github", ItemChangeRules.MarkNeedsUpdate));
        var twice = await ChangeAsync(Change("work/github", ItemChangeRules.MarkNeedsUpdate));

        Assert.Equal(AuditDecision.Granted, tagged.Decision);
        Assert.Contains(ItemChangeRules.NeedsUpdateTag, Reopened(vault => vault.Tags(_github))!);
        Assert.Equal((AuditDecision.Denied, AuditMethod.InvalidRequest), (twice.Decision, twice.Method));
        Assert.Equal(1, _channel.Asked);
    }

    [Theory]
    [InlineData("other/**", "work/github")]
    [InlineData("**", ".keypaste/shares/link")]
    [InlineData("tag:env:acme:*", "env/acme/dev")]
    public async Task An_item_the_agent_does_not_see_whole_is_refused_unasked(string exposure, string entry)
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await ChangeAsync(Change(entry, ItemChangeRules.MoveToRecycleBin) with { Exposure = [exposure] });

        Assert.Equal((AuditDecision.Denied, AuditMethod.OutOfScope), (reply.Decision, reply.Method));
        Assert.Equal(0, _channel.Asked);
        Assert.Empty(Reopened(vault => vault.ReadRecycled()));
    }

    [Fact]
    public async Task A_project_tag_reach_is_told_it_changes_nothing()
    {
        var reply = await ChangeAsync(Change("env/acme/dev", ItemChangeRules.MarkNeedsUpdate) with { Exposure = ["tag:env:acme:*"] });

        Assert.Contains("project tag", reply.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handle_is_resolved_and_checked_by_the_owner()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var outside = await ChangeAsync(Change(EntryHandle.For(_github), ItemChangeRules.MarkNeedsUpdate) with { Exposure = ["other/**"] });
        var inside = await ChangeAsync(Change(EntryHandle.For(_github), ItemChangeRules.MarkNeedsUpdate));

        Assert.Equal(AuditMethod.OutOfScope, outside.Method);
        Assert.Equal(("work/github", 1), (inside.Item, _channel.Asked));
    }

    [Fact]
    public async Task Delete_is_refused_unasked_for_an_item_no_agent_created()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await ChangeAsync(Change("work/github", ItemChangeRules.Delete));

        Assert.Equal((AuditDecision.Denied, AuditMethod.InvalidRequest), (reply.Decision, reply.Method));
        Assert.Equal(0, _channel.Asked);
        Assert.NotNull(Reopened(vault => vault.Find(_github)));
    }

    [Fact]
    public async Task An_approved_save_marks_what_it_created_and_only_that_agent_may_delete_it_for_good()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        var scratch = new EntryName("work", "scratch-token");

        var saved = await SaveAsync(new SaveRequest { Item = "work/scratch-token", Field = "API_TOKEN", Reason = "a scratch token", Exposure = ["**"] });
        Assert.Equal(AuditDecision.Granted, saved.Decision);
        Assert.Equal(_label, Reopened(vault => vault.AgentThatCreated(scratch)));

        var other = await ChangeAsync(Change("work/scratch-token", ItemChangeRules.Delete), label: "codex");
        var replaced = await SaveAsync(new SaveRequest { Item = "work/github", Field = "NEW_TOKEN", Reason = "adds a field", Exposure = ["**"] });
        Assert.Equal(AuditDecision.Granted, replaced.Decision);
        Assert.Null(Reopened(vault => vault.AgentThatCreated(_github)));

        var deleted = await ChangeAsync(Change("work/scratch-token", ItemChangeRules.Delete));

        Assert.Equal(AuditMethod.InvalidRequest, other.Method);
        Assert.Equal(AuditDecision.Granted, deleted.Decision);
        Assert.True(_channel.LastChangePrompt!.DeletesForGood);
        Assert.StartsWith("Delete work/scratch-token for good. An agent created it on ", _channel.LastChangePrompt.Says, StringComparison.Ordinal);
        Assert.Null(Reopened(vault => vault.Find(scratch)));
        Assert.Empty(Reopened(vault => vault.ReadRecycled()));
    }

    [Fact]
    public async Task A_move_to_Trash_in_a_vault_whose_Trash_is_off_is_refused_unasked()
    {
        _vault.SetRecyclesDeletedEntries(false);
        _vault.Save();
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await ChangeAsync(Change("work/github", ItemChangeRules.MoveToRecycleBin));

        Assert.Equal((AuditDecision.Denied, AuditMethod.InvalidRequest), (reply.Decision, reply.Method));
        Assert.Equal(0, _channel.Asked);
        Assert.NotNull(Reopened(vault => vault.Find(_github)));
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_changes_nothing()
    {
        _channel.Hold = true;

        var asking = ChangeAsync(Change("work/github", ItemChangeRules.MoveToRecycleBin));
        await _channel.Waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _lifetime.End();
        var reply = await asking.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultLocked), (reply.Decision, reply.Method));
        Assert.True(_channel.Withdrawn);
        Assert.NotNull(Reopened(vault => vault.Find(_github)));
    }

    [Fact]
    public async Task An_item_renamed_while_the_person_is_asked_is_not_changed()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        _channel.WhileAsking = () => Assert.Equal(OrganizeOutcome.Renamed, _vault.RenameEntry(_github, "gitlab", out _));

        var reply = await ChangeAsync(Change("work/github", ItemChangeRules.MoveToRecycleBin));

        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultChanged), (reply.Decision, reply.Method));
        Assert.NotNull(_vault.Find(new EntryName("work", "gitlab")));
        Assert.Empty(_vault.ReadRecycled());
    }

    [Fact]
    public async Task A_change_whose_line_cannot_be_written_is_put_to_nobody_and_made_nowhere()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        _logOpens = false;

        var reply = await ChangeAsync(Change("work/github", ItemChangeRules.MoveToRecycleBin));

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed, false), (reply.Decision, reply.Method, reply.Recorded));
        Assert.Equal(0, _channel.Asked);
        Assert.NotNull(Reopened(vault => vault.Find(_github)));
    }

    [Theory]
    [InlineData("rename", "", "change")]
    [InlineData(ItemChangeRules.MarkExpired, "next week", "expires")]
    [InlineData(ItemChangeRules.MarkExpired, "2026-13-01", "expires")]
    [InlineData(ItemChangeRules.MoveToRecycleBin, "2026-12-01", "expires")]
    public void A_malformed_change_is_named_by_its_argument(string change, string expires, string argument)
    {
        var problem = ItemChangeRules.Check(Change("work/github", change) with { Expires = expires }, out _);

        Assert.Equal(argument, problem?.Argument);
    }

    [Fact]
    public void An_entry_or_reason_past_its_bound_is_refused()
    {
        Assert.Equal("entry", ItemChangeRules.Check(Change(new string('a', CredentialRequestRules.MaximumEntryLength + 1), ItemChangeRules.Delete), out _)?.Argument);
        Assert.Equal("reason", ItemChangeRules.Check(Change("work/github", ItemChangeRules.Delete) with { Reason = new string('r', CredentialRequestRules.MaximumReasonLength + 1) }, out _)?.Argument);
        Assert.Null(ItemChangeRules.Check(Change("work/github", ItemChangeRules.MarkExpired) with { Expires = "2026-12-01" }, out var change));
        Assert.Equal(new ItemChange(ItemChangeKind.MarkExpired, new DateOnly(2026, 12, 1)), change);
    }

    [Fact]
    public void A_change_request_and_reply_survive_the_wire_and_hold_no_value()
    {
        var request = Change("work/github", ItemChangeRules.MarkExpired) with { Expires = "2026-12-01", ClientLabel = _label, Vault = "/v", Session = "s" };
        var reply = new ItemChangeReply(AuditDecision.Granted, AuditMethod.Prompt, "changed") { Item = "work/github", Recorded = true, Session = "s" };

        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(request), out ItemChangeRequest? decoded));
        Assert.True(ApproverProtocol.TryDecode(ApproverProtocol.Encode(reply), out ItemChangeReply? replied));

        Assert.Equal((request.Entry, request.Change, request.Expires, request.ClientLabel), (decoded.Entry, decoded.Change, decoded.Expires, decoded.ClientLabel));
        Assert.Equal(reply, replied);
        Assert.Equal(ApproverMessageKind.ItemChange, ApproverProtocol.KindOf(ApproverProtocol.Encode(request)));
        Assert.DoesNotContain("password", Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
    }

    private static ItemChangeRequest Change(string entry, string change) =>
        new() { Entry = entry, Change = change, Reason = "tidying up", Exposure = ["**"] };

    private AgentSave Write(SessionLifetime asked, Func<Vault, bool> apply)
    {
        if (!ReferenceEquals(asked, _lifetime) || !_lifetime.IsLive)
        {
            return new AgentSave(AgentSaveOutcome.Locked, "the vault is locked");
        }

        if (!apply(_vault))
        {
            return AgentSave.Unchanged;
        }

        _vault.Save();
        return AgentSave.Saved;
    }

    private T Reopened<T>(Func<Vault, T> read)
    {
        using var reopened = Vault.Open(VaultPath, _master);
        return read(reopened);
    }

    private async Task<string> AttachAsync(string connection, string label)
    {
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", label) }, connection, Token);
        Assert.True(attached.Attached, attached.Reason);
        return attached.Session!;
    }

    private async Task<ItemChangeReply> ChangeAsync(ItemChangeRequest request, string label = _label)
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var session = await AttachAsync(connection, label);

        return await _authority.ChangeItemAsync(request with { ClientName = "Claude Code", ClientLabel = label, Vault = VaultPath, Session = session }, connection, Token);
    }

    private async Task<SaveReply> SaveAsync(SaveRequest request)
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var session = await AttachAsync(connection, _label);

        return await _authority.SaveAsync(request with { ClientName = "Claude Code", ClientLabel = _label, Vault = VaultPath, Session = session }, connection, Token);
    }
}
