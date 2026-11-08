using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// F.78a's acceptance on the request path: a real vault, the owner's own authority and log, and only the person faked.
/// Each value is decided by its field's category under this computer's rules, every agent alike, and each decision is
/// recorded as it was made.
/// </summary>
public sealed class FieldCategoryRequestTests : IDisposable
{
    private const string _agent = "claude-code";
    private const string _password = "SENTINEL-CATEGORY-PASSWORD";
    private const string _token = "SENTINEL-CATEGORY-TOKEN";
    private const string _region = "eu-west-1";
    private const string _notes = "SENTINEL-CATEGORY-NOTES";
    private const string _database = "postgres://SENTINEL-CATEGORY-DB@db/app";
    private const string _key = "deploy.pem";
    private static readonly byte[] _keyBytes = [.. "SENTINEL-CATEGORY-ATTACHMENT"u8];
    private static readonly EntryName _github = new("work", "github");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-field-category-").FullName;
    private readonly ManualClock _clock = new();
    private readonly FakeChannel _channel = new();
    private readonly FakeAgentRules _rules = new();
    private readonly ApprovalGate _gate;
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly SessionAuthority _authority;

    public FieldCategoryRequestTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            created.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Username = "octocat", Url = "https://github.com", Password = _password, Notes = _notes });
            Assert.True(created.SetFields(_github,
            [
                new FieldWrite("API_TOKEN", _token),
                new FieldWrite("REGION_NAME", _region, Protect: false),
                new FieldWrite("TimeOtp-Secret", "12345678901234567890"),
            ]));
            ProjectVariables.Set(created, "acme-api", "DATABASE_URL", _database);
            Assert.True(created.AddAttachment(_github, _key, new MemoryStream(_keyBytes), out _));

            foreach (var account in new[] { "first", "second" })
            {
                created.AddEntry(new VaultEntry { GroupPath = "shared", Title = "certificate", Username = account });
            }

            foreach (var item in created.ReadEntries().Where(entry => entry.GroupPath == "shared"))
            {
                Assert.True(created.AddAttachment(new EntryName("shared", "certificate") { Uuid = item.Uuid }, "cert.pem", new MemoryStream([.. System.Text.Encoding.UTF8.GetBytes(item.Username)]), out _));
            }

            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        _log = new OwnerLog(_directory);
        _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);
        _authority = Authority(_vault);
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

    /// <summary>Each value the categories decide, as a request names it.</summary>
    public static TheoryData<FieldCategory, string, string> Values => new()
    {
        { FieldCategory.Password, "work/github", "password" },
        { FieldCategory.CustomField, "work/github", "API_TOKEN" },
        { FieldCategory.CustomField, "work/github", "REGION_NAME" },
        { FieldCategory.ProjectVariable, "env/acme-api/.env", "DATABASE_URL" },
        { FieldCategory.Totp, "work/github", "totp" },
        { FieldCategory.Notes, "work/github", "notes" },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public async Task Each_category_is_asked_by_default_and_the_persons_answer_is_released_and_logged(FieldCategory category, string entry, string field)
    {
        Assert.Equal(category, Category(entry, field));

        _channel.Answer = ApprovalAnswer.Denied;
        var denied = await RequestAsync(entry, field);
        _clock.Advance(TimeSpan.FromMinutes(2));
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        var allowed = await RequestAsync(entry, field);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (denied.Decision, denied.Method, denied.Value));
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (allowed.Decision, allowed.Method));
        Assert.NotNull(allowed.Value);
        Assert.Equal(2, _channel.Asked);
        Assert.Equal([("denied", "prompt"), ("granted", "prompt")], Lines());
    }

    [Theory]
    [MemberData(nameof(Values))]
    public async Task Each_category_set_to_Allow_is_released_unasked_and_logged_as_allowed(FieldCategory category, string entry, string field)
    {
        _rules.Rules = AgentRules.Defaults.Allowing(category, confirmed: true);

        var reply = await RequestAsync(entry, field);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed), (reply.Decision, reply.Method));
        Assert.NotNull(reply.Value);
        Assert.Equal(0, _channel.Asked);
        Assert.Equal([("granted", "allowed")], Lines());
        Assert.Equal($"released without asking: the person allows {FieldCategories.Shown(category)} for every agent", Assert.Single(_log.Lines()).GetProperty("reason").GetString());
    }

    [Theory]
    [MemberData(nameof(Values))]
    public async Task Each_category_set_to_Block_is_refused_unasked_and_logged_as_blocked(FieldCategory category, string entry, string field)
    {
        _rules.Rules = AgentRules.Defaults.With(category, CategoryRule.Block);
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await RequestAsync(entry, field);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(0, _channel.Asked);
        Assert.Equal([("denied", "blocked")], Lines());
    }

    /// <summary>Allow is taken only with the person's confirmation: without it the category is still asked, and no other way sets it.</summary>
    [Fact]
    public async Task Allow_holds_only_once_the_person_confirmed_it()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentRules.Defaults.With(FieldCategory.Password, CategoryRule.Allow));
        _rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Password, confirmed: false);

        var unconfirmed = await RequestAsync("work/github", "password");
        _clock.Advance(TimeSpan.FromMinutes(2));
        _rules.Rules = _rules.Rules.Allowing(FieldCategory.Password, confirmed: true);
        var confirmed = await RequestAsync("work/github", "password");

        Assert.Equal(AuditMethod.Prompt, unconfirmed.Method);
        Assert.Null(unconfirmed.Value);
        Assert.Equal((AuditMethod.Allowed, _password), (confirmed.Method, confirmed.Value));
        Assert.Equal(1, _channel.Asked);
    }

    /// <summary>An item's username reaches agents unasked until Settings hides it, and the switch hides nothing else.</summary>
    [Fact]
    public async Task A_username_is_released_unasked_unless_Settings_hides_it()
    {
        Assert.Equal(FieldCategory.Username, Category("work/github", "username"));

        var shown = await RequestAsync("work/github", "username");
        _rules.Rules = AgentRules.Defaults with { ShowsUsername = false };
        var hidden = await RequestAsync("work/github", "username");
        var url = await RequestAsync("work/github", "url");

        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed, "octocat"), (shown.Decision, shown.Method, shown.Value));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (hidden.Decision, hidden.Method));
        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed, "https://github.com"), (url.Decision, url.Method, url.Value));
        Assert.Equal(1, _channel.Asked);
    }

    /// <summary>An item's URL reaches agents unasked under every rule.</summary>
    [Fact]
    public async Task A_URL_is_released_unasked_under_every_rule()
    {
        Assert.Equal(FieldCategory.Url, Category("work/github", "url"));
        var blockingEverything = FieldCategories.Ruled.Aggregate(AgentRules.Defaults with { ShowsUsername = false }, (rules, category) => rules.With(category, CategoryRule.Block));
        _rules.Rules = blockingEverything;

        var reply = await RequestAsync("work/github", "url");

        Assert.Equal((AuditMethod.Allowed, "released without asking: agents see an item's URL"), (reply.Method, Assert.Single(_log.Lines()).GetProperty("reason").GetString()));
        Assert.Equal(0, _channel.Asked);
    }

    /// <summary>Listing works whatever the username switch says: hiding usernames hides values, not which items there are.</summary>
    [Fact]
    public async Task Listing_works_with_usernames_hidden()
    {
        _rules.Rules = AgentRules.Defaults with { ShowsUsername = false };
        var connection = $"conn-{Guid.NewGuid():N}";
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", _agent) }, connection, Token);

        var names = await _authority.ListAsync(new NamesRequest(["**"]) { Vault = VaultPath, Session = attached.Session! }, connection, Token);

        Assert.True(names.VaultUnlocked, names.Reason);
        Assert.Contains(names.Names, entry => entry.Name == _github);
    }

    /// <summary>Changing a field's rule ends a person's refusal of it, and leaves standing a refusal of a field the change does not cover.</summary>
    [Fact]
    public async Task A_rule_change_clears_the_cooldown_only_for_the_fields_it_covers()
    {
        _channel.Answer = ApprovalAnswer.Denied;
        await RequestAsync("work/github", "password");
        await RequestAsync("work/github", "notes");
        var handler = Handler(_vault);

        Assert.True(handler.TryChangeRules(rules => rules.Allowing(FieldCategory.Password, confirmed: true), out var problem), problem);
        var password = await RequestAsync("work/github", "password", handler: handler);
        var notes = await RequestAsync("work/github", "notes", handler: handler);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed, _password), (password.Decision, password.Method, password.Value));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Cooldown), (notes.Decision, notes.Method));
        Assert.Equal(2, _channel.Asked);
    }

    [Fact]
    public async Task An_attachment_is_asked_by_default_and_released_as_base64_naming_it_in_the_log()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await RequestAttachmentAsync("work/github", _key);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, Convert.ToBase64String(_keyBytes)), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal($"attachment {_key}", _channel.LastPrompt!.Field);
        var line = Assert.Single(_log.Lines());
        Assert.Equal("attachment", line.GetProperty("args").GetProperty("field").GetString());
        Assert.EndsWith($"; attachment {_key}", line.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_attachment_is_released_unasked_under_Allow_and_refused_unasked_under_Block()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        _rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Attachment, confirmed: true);
        var allowed = await RequestAttachmentAsync("work/github", _key);
        _rules.Rules = AgentRules.Defaults.With(FieldCategory.Attachment, CategoryRule.Block);
        var blocked = await RequestAttachmentAsync("work/github", _key);

        Assert.Equal((AuditMethod.Allowed, Convert.ToBase64String(_keyBytes)), (allowed.Method, allowed.Value));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked, null), (blocked.Decision, blocked.Method, blocked.Value));
        Assert.Equal(0, _channel.Asked);
    }

    [Fact]
    public async Task An_attachment_name_the_item_does_not_hold_is_refused()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await RequestAttachmentAsync("work/github", "missing.pem");

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed, "the item holds no attachment of that name"), (reply.Decision, reply.Method, reply.Reason));
    }

    /// <summary>An attachment over the cap another program wrote into the vault is refused, and one at the cap crosses the pipe whole.</summary>
    [Fact]
    public async Task An_attachment_over_5_MiB_is_refused_and_one_at_5_MiB_is_released()
    {
        var interop = (Internal.KeePassInterop)typeof(Vault).GetField("_interop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_vault)!;
        var large = new byte[EntryAttachment.MaximumSize + 1];
        Assert.Equal(1, interop.SetAttachment(_github, "large.bin", large, large.Length, out _));
        Assert.Equal(1, interop.SetAttachment(_github, "most.bin", large, EntryAttachment.MaximumSize, out _));
        _vault.Save();
        _rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Attachment, confirmed: true);

        var over = await RequestAttachmentAsync("work/github", "large.bin");
        var most = await RequestAttachmentAsync("work/github", "most.bin");

        Assert.Equal((AuditDecision.Denied, "the attachment is larger than 5 MiB, the most keypaste releases"), (over.Decision, over.Reason));
        Assert.Equal(AuditDecision.Granted, most.Decision);
        Assert.Equal(EntryAttachment.MaximumSize, Convert.FromBase64String(most.Value!).Length);
        Assert.True(ApproverProtocol.Encode(most).Length < ApproverProtocol.MaximumAttachmentReplyFrameBytes);
    }

    [Fact]
    public async Task A_lock_while_an_attachment_is_asked_releases_nothing()
    {
        _channel.Hold = true;
        var asked = RequestAttachmentAsync("work/github", _key).AsTask();
        await _channel.Waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _lifetime.End();

        var withdrawn = await asked.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultLocked, null), (withdrawn.Decision, withdrawn.Method, withdrawn.Value));
    }

    /// <summary>Two items answering to the name are put to the person to pick, and exactly the picked item's attachment is released.</summary>
    [Fact]
    public async Task Two_items_answering_to_an_attachment_request_are_picked_from()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        _channel.Pick = prompt => prompt.Candidates.ToList().FindIndex(candidate => candidate.Username == "second");

        var reply = await RequestAttachmentAsync("shared/certificate", "cert.pem");

        Assert.Equal((AuditDecision.Granted, Convert.ToBase64String("second"u8.ToArray())), (reply.Decision, reply.Value));
        Assert.Equal(1, _channel.PickersAsked);
    }

    /// <summary>
    /// A passkey's private key signs in wherever it is registered, so no rule, detail switch or answer releases it: its
    /// category is Blocked under every rule, and a request naming one is refused before anybody is asked.
    /// </summary>
    [Fact]
    public async Task A_passkey_field_is_refused_under_every_rule()
    {
        Assert.Equal(FieldCategory.Passkey, FieldCategories.Of("KPEX_PASSKEY_PRIVATE_KEY_PEM", ["env:acme-api"]));

        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        var allowingEverything = FieldCategories.Ruled.Aggregate(AgentRules.Defaults, (rules, category) => rules.Allowing(category, confirmed: true));
        List<CredentialReply> replies = [];

        foreach (var rules in new[] { AgentRules.Defaults, allowingEverything, AgentRules.Defaults with { ShowsUsername = false } })
        {
            _rules.Rules = rules;
            replies.Add(await RequestAsync("work/github", "KPEX_PASSKEY_PRIVATE_KEY_PEM"));
        }

        Assert.All(replies, reply => Assert.Equal((AuditDecision.Denied, null), (reply.Decision, reply.Value)));
        Assert.All([AgentRules.Defaults, allowingEverything, AgentRules.Defaults with { ShowsUsername = false }], rules => Assert.Equal(CategoryRule.Block, rules.RuleFor(FieldCategory.Passkey)));
        Assert.Equal(0, _channel.Asked);
    }

    /// <summary>A lock while the person is asked withdraws the question, and after it nothing is released, an allowed category included.</summary>
    [Fact]
    public async Task A_lock_releases_nothing()
    {
        _channel.Hold = true;
        var asked = RequestAsync("work/github", "password").AsTask();
        await _channel.Waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _lifetime.End();
        var withdrawn = await asked.WaitAsync(TimeSpan.FromSeconds(10), Token);

        _rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Notes, confirmed: true);
        var afterwards = await RequestAsync("work/github", "notes", attach: false);

        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultLocked, null), (withdrawn.Decision, withdrawn.Method, withdrawn.Value));
        Assert.Equal(AuditDecision.Denied, afterwards.Decision);
        Assert.Null(afterwards.Value);
        Assert.True(_channel.Withdrawn);
        Assert.DoesNotContain(_password, _log.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain(_notes, _log.Text(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Agent records a vault already carries, even ones saying Automatic and always allowing this item, are ignored and
    /// left in place: the vault releases nothing unasked.
    /// </summary>
    [Fact]
    public async Task A_vault_carrying_an_agent_record_releases_nothing_unasked()
    {
        var item = _vault.EntryUuid(_github)!;
        var record = "{\"acaf\":1,\"see\":[\"**\"],\"values\":true,\"run\":true,\"mode\":\"automatic-commands\",\"always\":{\"items\":[\"" + item + "\"]}}";

        Assert.True(_vault.WriteRecord(new EntryName(ReservedGroups.Agents, _agent), AcafIds.Agent, "acaf", record));
        _vault.Save();

        using var reopened = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        var authority = Authority(reopened);
        _channel.Answer = ApprovalAnswer.Denied;

        var reply = await RequestAsync("work/github", "password", authority: authority);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(1, _channel.Asked);
        Assert.Contains(record, reopened.ReadRecords(ReservedGroups.Agents, "acaf").Select(read => read.Value));
    }

    /// <summary>Run stays off until it is turned on for the agent, and then every run is asked, whatever the rules allow.</summary>
    [Fact]
    public async Task A_run_is_asked_under_every_rule_once_run_is_on()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        var allowingEverything = FieldCategories.Ruled.Aggregate(AgentRules.Defaults, (rules, category) => rules.Allowing(category, confirmed: true));

        _rules.Rules = allowingEverything;
        var off = await RunAsync();
        _rules.Rules = allowingEverything.WithRun(_agent, run: true);
        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal((EnvOutcome.Unauthorized, AuditMethod.RunOff), (off.Set.Outcome, off.Method));
        Assert.All([first, second], run => Assert.Equal((EnvOutcome.Resolved, AuditMethod.Prompt), (run.Set.Outcome, run.Method)));
        Assert.Equal(2, _channel.Asked);
    }

    [Fact]
    public async Task A_run_whose_set_holds_a_Blocked_field_is_refused_unasked()
    {
        _channel.Answer = ApprovalAnswer.ApprovedOnce;
        _rules.Rules = AgentRules.Defaults.WithRun(_agent, run: true).With(FieldCategory.ProjectVariable, CategoryRule.Block);

        var reply = await RunAsync();

        Assert.Equal(AuditMethod.Blocked, reply.Method);
        Assert.Empty(reply.Set.Variables);
        Assert.Equal(0, _channel.Asked);
    }

    /// <summary>Rules that cannot be read refuse every agent request before anybody is asked.</summary>
    [Fact]
    public async Task Unreadable_rules_refuse_unasked()
    {
        _rules.Unreadable = "agents.toml: line 1: details is \"shown\" or \"hidden\"";
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await RequestAsync("work/github", "username");

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(0, _channel.Asked);
    }

    private ApproverHandler Handler(Vault vault) =>
        new(new VaultCredentialSource(() => _lifetime.IsLive ? vault : null, _clock), new VaultEntryNameLister(() => vault), _gate, _rules);

    private SessionAuthority Authority(Vault vault, ApproverHandler? handler = null)
    {
        handler ??= Handler(vault);

        return new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            handler,
            () => _log.Log,
            new SessionEnvironments(_gate, lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? vault : null, _clock),
            clock: _clock);
    }

    private FieldCategory Category(string entry, string field)
    {
        var name = EntryName.Parse(entry);
        return new ApproverHandler(new VaultCredentialSource(() => _vault), new VaultEntryNameLister(() => _vault), _gate, _rules).Category(name, field);
    }

    private ValueTask<CredentialReply> RequestAttachmentAsync(string entry, string attachment) =>
        RequestAsync(entry, CredentialFields.Attachment, attachment: attachment);

    private async ValueTask<CredentialReply> RequestAsync(
        string entry,
        string field,
        bool attach = true,
        SessionAuthority? authority = null,
        ApproverHandler? handler = null,
        string? attachment = null)
    {
        authority ??= handler is null ? _authority : Authority(_vault, handler);
        var connection = $"conn-{Guid.NewGuid():N}";
        var session = "session-one";

        if (attach)
        {
            var attached = await authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", _agent) }, connection, Token);
            Assert.True(attached.Attached, attached.Reason);
            session = attached.Session!;
        }

        return await authority.RequestAsync(
            new CredentialRequest
            {
                Entry = entry,
                Field = field,
                Attachment = attachment,
                Reason = "deploy the app",
                Exposure = ["**"],
                ClientName = "Claude Code",
                ClientLabel = _agent,
                Vault = VaultPath,
                Session = session,
            },
            connection,
            Token);
    }

    private async ValueTask<RunReply> RunAsync()
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", _agent) }, connection, Token);

        return await _authority.ReleaseRunAsync(
            new RunRequest
            {
                Program = OperatingSystem.IsWindows() ? @"C:\tools\npm.exe" : "/usr/bin/npm",
                Command = ["npm", "run", "migrate"],
                Directory = _directory,
                Project = "acme-api",
                Reason = "run the pending migration",
                Exposure = ["**"],
                ClientName = "Claude Code",
                ClientLabel = _agent,
                Vault = VaultPath,
                Session = attached.Session!,
            },
            connection,
            Token);
    }

    private List<(string Decision, string Method)> Lines() =>
        [.. _log.Lines().Select(line => (line.GetProperty("decision").GetString()!, line.GetProperty("method").GetString()!))];
}
