using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A linked set is asked about whole, every time, and released whole or not at all: each field is still decided by its category, an
/// item must be the anchor or one find_access links to it, and nothing is read until the person allowed it once (PRODUCT §2, §3.2).
/// </summary>
public sealed class ApproverHandlerSetTests
{
    private const string _stripe = "env/dev/STRIPE_KEY";
    private const string _bank = "personal/bank";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SetRequest Set(params SetItem[] items) => new()
    {
        Items = items.Length == 0 ? [new(_stripe, "password"), new(_bank, "password")] : items,
        Reason = "deploy billing to staging",
        Exposure = ["**"],
        ClientName = "claude-code",
        ClientLabel = ApproverFixture.Agent,
    };

    [Fact]
    public async Task AnAllowedSet_ReleasesEveryField_InTheOrderAsked_AfterOnePrompt()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new(_bank, "username"), new(_stripe, "url")), "conn-1", Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (reply.Decision, reply.Method));
        Assert.Equal([(_stripe, "password"), (_bank, "username"), (_stripe, "url")], reply.Fields.Select(field => (field.Entry, field.Field)));
        Assert.All(reply.Fields, field => Assert.Equal(ApproverFixture.Sentinel, field.Value));
        Assert.Equal(1, fixture.Channel.SetsAsked);
        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(3, fixture.Source.Reads);

        var prompt = fixture.Channel.LastSetPrompt!;
        Assert.Equal([(_stripe, "password"), (_bank, "username"), (_stripe, "url")], prompt.Fields.Select(field => (field.Entry, field.Field)));
        Assert.Equal("deploy billing to staging", prompt.Reason);
        Assert.Equal(ApproverFixture.Agent, prompt.Label);
    }

    /// <summary>A set is never released by a rule alone: with every field's category Allow, the person is still asked.</summary>
    [Fact]
    public async Task EveryFieldAllowed_StillAsksThePerson()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Password, confirmed: true);

        var refused = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (refused.Decision, refused.Method));
        Assert.Equal(1, fixture.Channel.SetsAsked);
        Assert.Equal(0, fixture.Source.Reads);
    }

    [Fact]
    public async Task ASetHoldingAProtectedEnvironment_IsAskedUnderAnAllow()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Password, confirmed: true);
        fixture.Source.Protected.Add(new EntryName("env/dev", "STRIPE_KEY"));
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (reply.Decision, reply.Method));
        Assert.Equal(1, fixture.Channel.SetsAsked);
    }

    [Fact]
    public async Task ADeniedSet_ReadsAndReleasesNothing_AndHoldsEachFieldBack()
    {
        using var fixture = new ApproverFixture();

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (reply.Decision, reply.Method));
        Assert.Empty(reply.Fields);
        Assert.Equal(0, fixture.Source.Reads);
        Assert.Equal([_stripe, _bank], reply.Entries);

        // The person's no holds back a set naming either field, and the single request for one, without asking again.
        var again = await fixture.Handler.RequestSetAsync(Set(new(_bank, "password"), new(_stripe, "username")), "conn-1", Token);
        var single = await fixture.Handler.RequestAsync(new CredentialRequest { Entry = _bank, Field = "password", Reason = "r", Exposure = ["**"], ClientLabel = ApproverFixture.Agent }, "conn-1", Token);

        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(AuditMethod.Cooldown, single.Method);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    /// <summary>An always answer is no answer for a set: a channel that says one has allowed nothing.</summary>
    [Fact]
    public async Task AnAlwaysAnswer_RefusesTheSet()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.AlwaysAllowed;

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(0, fixture.Source.Reads);
        Assert.Empty(fixture.Rules.Rules.Answers);
    }

    [Fact]
    public async Task ABlockedField_RefusesTheWholeSet_BeforeAnybodyIsAsked()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Rules.Rules = AgentRules.Defaults.With(FieldCategory.Notes, CategoryRule.Block);

        var reply = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new(_bank, "notes")), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked), (reply.Decision, reply.Method));
        Assert.Contains("the notes of personal/bank", reply.Reason, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Source.Reads);
    }

    [Fact]
    public async Task ARecentRefusalOfOneField_RefusesTheWholeSet_Unasked()
    {
        using var fixture = new ApproverFixture();
        await fixture.Handler.RequestAsync(new CredentialRequest { Entry = _bank, Field = "password", Reason = "r", Exposure = ["**"], ClientLabel = ApproverFixture.Agent }, "conn-1", Token);
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal(AuditMethod.Cooldown, reply.Method);
        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Source.Reads);
    }

    /// <summary>An item the bridge does not let the agent see refuses the set as out of scope, as one that does not exist does (T-4).</summary>
    [Fact]
    public async Task TheExposure_IsHonouredForEveryItem()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var hidden = await fixture.Handler.RequestSetAsync(Set() with { Exposure = ["env/**"] }, "conn-1", Token);
        var missing = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new("personal/nothing", "password")), "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, hidden.Method);
        Assert.Equal("the item is outside what this agent can see through this bridge", hidden.Reason);
        Assert.Equal(AuditMethod.OutOfScope, missing.Method);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task AProjectTagsReach_RefusesAnyFieldButAProjectVariable()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Tagged[new EntryName("personal", "bank")] = ["env:billing:dev"];

        var reply = await fixture.Handler.RequestSetAsync(Set() with { Exposure = ["env/**", "tag:env:billing"] }, "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Contains("project tag", reply.Reason, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnItemNotLinkedToTheAnchor_RefusesTheSet_Unasked()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Added.Add(Item("ops", "jenkins", "https://ci.example.org"));

        var reply = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new("ops/jenkins", "password")), "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Equal("ops/jenkins is not linked to env/dev/STRIPE_KEY, the set's first item, as find_access links items", reply.Reason);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    /// <summary>Two fields of the anchor alone are a set: an item is linked to itself.</summary>
    [Fact]
    public async Task FieldsOfTheAnchorAlone_AreASet()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "username"), new(_stripe, "password")), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, reply.Decision);
    }

    [Fact]
    public async Task AnItemLinkedBySite_IsAsked() => await AssertLinkedAsync(
        fixture => fixture.Source.Added.Add(Item("ops", "stripe-cli", "https://api.stripe.com")),
        new SetItem(_stripe, "password"),
        new SetItem("ops/stripe-cli", "password"));

    [Fact]
    public async Task AnItemLinkedByProject_IsAsked() => await AssertLinkedAsync(
        fixture =>
        {
            fixture.Source.Added.Add(Item("ops", "db", "https://db.example.org"));
            fixture.Source.Added.Add(Item("ops", "cache", "https://cache.example.net"));
            fixture.Source.Tagged[new EntryName("ops", "db")] = ["env:acme:dev"];
            fixture.Source.Tagged[new EntryName("ops", "cache")] = ["env:acme:prod"];
        },
        new SetItem("ops/db", "password"),
        new SetItem("ops/cache", "password"));

    /// <summary>A shared account links two items only while Settings show agents usernames, as a search shows the link.</summary>
    [Fact]
    public async Task AnItemLinkedByAccount_IsAsked_OnlyWhileUsernamesAreShown()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Added.Add(Item("ops", "db", "https://db.example.org", username: "deploy"));
        fixture.Source.Added.Add(Item("ops", "cache", "https://cache.example.net", username: "deploy"));
        fixture.Rules.Rules = AgentRules.Defaults with { ShowsUsername = false };
        var set = Set(new("ops/db", "password"), new("ops/cache", "password"));

        var hidden = await fixture.Handler.RequestSetAsync(set, "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, hidden.Method);
        Assert.Equal(0, fixture.Channel.Asked);

        fixture.Rules.Rules = AgentRules.Defaults;
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var shown = await fixture.Handler.RequestSetAsync(set, "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, shown.Decision);
    }

    /// <summary>A reference links two items only while Settings show agents hosts and references, as a search shows the link.</summary>
    [Fact]
    public async Task AnItemLinkedByReference_IsAsked_OnlyWhileReferencesAreShown()
    {
        using var fixture = new ApproverFixture();
        var target = Item("ops", "root", "https://root.example.org", uuid: "0123456789ABCDEF0123456789ABCDEF");
        fixture.Source.Added.Add(target);
        fixture.Source.Added.Add(Item("ops", "derived", "https://derived.example.net") with { References = ["0123456789ABCDEF0123456789ABCDEF"] });
        var set = Set(new("ops/derived", "password"), new("ops/root", "password"));

        var hidden = await fixture.Handler.RequestSetAsync(set, "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, hidden.Method);

        fixture.Rules.Rules = AgentRules.Defaults with { ShowsLinkedHosts = true };
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var shown = await fixture.Handler.RequestSetAsync(set, "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, shown.Decision);
    }

    /// <summary>A lock while the person is asked withdraws the prompt, and nothing is read.</summary>
    [Fact]
    public async Task AWithdrawalMidPrompt_ReleasesNothing()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Hold = true;
        using var locking = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var asking = fixture.Handler.RequestSetAsync(Set(), "conn-1", locking.Token).AsTask();
        await fixture.Channel.Waiting.WaitAsync(Token);
        await locking.CancelAsync();
        var reply = await asking;

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.True(fixture.Channel.Withdrawn);
        Assert.Equal(0, fixture.Source.Reads);
        Assert.Empty(reply.Fields);
    }

    [Fact]
    public async Task AFieldThatCannotBeRead_RefusesTheWholeSet()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Source.FailReads = true;

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (reply.Decision, reply.Method));
        Assert.Empty(reply.Fields);
    }

    /// <summary>Values each of which one reply carries, and together will not fit one, refuse the set whole, never a part of it, and hold it back.</summary>
    [Fact]
    public async Task ASetTooLargeForOneReply_IsRefusedWhole()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Source.Value = new string('x', 40_000);

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);
        var again = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Undeliverable), (reply.Decision, reply.Method));
        Assert.Empty(reply.Fields);
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.DoesNotContain(fixture.Narration, said => said.StartsWith("released", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task ASetOfTheWrongSize_IsRefusedAsInvalid(int count)
    {
        using var fixture = new ApproverFixture();

        var reply = await fixture.Handler.RequestSetAsync(Set([.. Enumerable.Range(0, count).Select(index => new SetItem(_stripe, $"F{index}"))]), "conn-1", Token);

        Assert.Equal(AuditMethod.InvalidRequest, reply.Method);
        Assert.Equal(0, fixture.Source.Resolves);
    }

    [Fact]
    public async Task AnAttachmentOrAFieldNamedTwice_IsRefusedAsInvalid()
    {
        using var fixture = new ApproverFixture();

        var attachment = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new(_bank, CredentialFields.Attachment)), "conn-1", Token);
        var twice = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new(EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY")), "password")), "conn-1", Token);

        Assert.Equal(AuditMethod.InvalidRequest, attachment.Method);
        Assert.Equal(AuditMethod.InvalidRequest, twice.Method);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task ANameMoreThanOneItemAnswersTo_RefusesTheSet()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Ambiguous.Add("work/github");

        var reply = await fixture.Handler.RequestSetAsync(Set(new(_stripe, "password"), new("work/github", "password")), "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Contains("handle", reply.Reason, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task UnreadableRules_RefuseTheSet()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Unreadable = "agents.toml is damaged";

        var reply = await fixture.Handler.RequestSetAsync(Set(), "conn-1", Token);

        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    private static async Task AssertLinkedAsync(Action<ApproverFixture> arrange, SetItem anchor, SetItem linked)
    {
        using var fixture = new ApproverFixture();
        arrange(fixture);
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestSetAsync(Set(anchor, linked), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(1, fixture.Channel.SetsAsked);
    }

    private static IndexedEntry Item(string group, string title, string url, string? username = null, string? uuid = null) =>
        new(
            new ListedEntry(new EntryName(group, title) { Uuid = uuid }, ["password", "username"], []),
            ItemKind.Login,
            WebOrigin.TryReadItem(url, out var origin) ? origin : throw new ArgumentException(url, nameof(url)),
            username);
}
