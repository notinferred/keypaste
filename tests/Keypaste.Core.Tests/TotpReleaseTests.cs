using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Totp;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>An agent is given an item's current TOTP code under a field's approval, once each time, and never the seed (9.2b).</summary>
public sealed class TotpReleaseTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _password = "SENTINEL-9-2B-PASSWORD-7a1c";

    // RFC 6238 Appendix B's SHA-1 seed, whose 8-digit codes are 07081804 at 1111111109 s, with 1 s left, and 14050471 at 1111111111 s.
    private const string _seed = "12345678901234567890";
    private const string _codeAt09 = "07081804";
    private const string _codeAt11 = "14050471";

    private static readonly EntryName _github = new("api", "GitHub");
    private static readonly EntryName _plain = new("api", "Plain");
    private static readonly DateTimeOffset _at09 = DateTimeOffset.FromUnixTimeSeconds(1111111109);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-totp-release-").FullName;
    private readonly Vault _vault;

    public TotpReleaseTests()
    {
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = _password });
        _vault.AddEntry(new VaultEntry { GroupPath = _plain.GroupPath, Title = _plain.Title, Password = _password });
        Assert.True(_vault.SetFields(_github, [new FieldWrite("TimeOtp-Secret", _seed), new FieldWrite("TimeOtp-Length", "8")]));
        _vault.Save();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task An_allowed_request_releases_the_current_code_once_for_the_seconds_it_has_left()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(_codeAt09, reply.Value);
        Assert.Equal(1, reply.TtlSeconds);
        Assert.Equal("api/GitHub", reply.Entry);

        var prompt = approver.Channel.LastPrompt!;
        Assert.Equal(CredentialFields.Totp, prompt.Field);
        Assert.Equal(0, prompt.TtlSeconds);
        Assert.Equal(OnceOnly.TotpCode, prompt.OnceOnly);
        Assert.Equal(0, approver.Grants.Count);
    }

    [Fact]
    public async Task The_next_code_is_asked_about_again_and_is_the_next_periods()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var first = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);
        approver.Clock.Advance(TimeSpan.FromSeconds(2));
        var second = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);

        Assert.Equal(_codeAt09, first.Value);
        Assert.Equal(_codeAt11, second.Value);
        Assert.Equal(29, second.TtlSeconds);
        Assert.Equal(AuditMethod.Prompt, second.Method);
        Assert.Equal(2, approver.Channel.Asked);
    }

    [Fact]
    public async Task A_denial_releases_no_code_and_cools_down_only_the_code()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Denied;

        var denied = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);
        var again = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);

        approver.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var password = await approver.Handler.RequestAsync(Request(_github, "password"), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, denied.Decision);
        Assert.Null(denied.Value);
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Null(again.Value);
        Assert.Equal(_password, password.Value);
        Assert.Equal(2, approver.Channel.Asked);
    }

    [Fact]
    public async Task An_Automatic_mode_releases_the_code_unasked_for_its_seconds_and_not_the_agents_ttl()
    {
        using var approver = new Approver(_vault, ApproverFixture.SeesAll with { Mode = AgentMode.Automatic });

        var reply = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);

        Assert.Equal(AuditMethod.Automatic, reply.Method);
        Assert.Equal(_codeAt09, reply.Value);
        Assert.Equal(1, reply.TtlSeconds);
        Assert.Equal(0, approver.Channel.Asked);
        Assert.Equal(0, approver.Grants.Count);
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("TimeOtp-Secret")]
    [InlineData("TimeOtp-Secret-Hex")]
    [InlineData("TimeOtp-Secret-Base32")]
    [InlineData("TimeOtp-Secret-Base64")]
    [InlineData("TimeOtp-Length")]
    public async Task No_field_the_seed_is_read_from_is_asked_about_or_read(string field)
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;
        var source = new VaultCredentialSource(() => _vault, approver.Clock);

        var reply = await approver.Handler.RequestAsync(Request(_github, field), "conn-1", Token);

        Assert.False(CredentialFields.IsReleasable(field));
        Assert.Equal(AuditMethod.InvalidRequest, reply.Method);
        Assert.Equal($"the request's field must be {CredentialFields.RequestRule}", reply.Reason);
        Assert.Null(reply.Value);
        Assert.Equal(0, approver.Channel.Asked);

        var read = source.TryRead(_github, field, out var value, out var failure, out _);

        using (value)
        {
            Assert.False(read);
            Assert.Null(value);
            Assert.Equal(CredentialFailure.NoSuchField, failure);
        }
    }

    [Fact]
    public async Task An_item_with_no_TOTP_is_refused_after_the_yes_with_nothing_released()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await approver.Handler.RequestAsync(Request(_plain), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Equal("the item has no TOTP keypaste can compute", reply.Reason);
        Assert.Null(reply.Value);
    }

    [Fact]
    public void A_code_is_computed_from_the_saved_file_with_how_long_it_stays_current()
    {
        var source = new VaultCredentialSource(() => _vault, new ManualClock(_at09));
        Assert.True(_vault.SetFields(_github, [new FieldWrite("TimeOtp-Length", "6")]));

        var before = source.TryRead(_github, CredentialFields.Totp, out var unsaved, out var failure, out _);

        using (unsaved)
        {
            Assert.False(before);
            Assert.Null(unsaved);
            Assert.Equal(CredentialFailure.Unsaved, failure);
        }

        _vault.Save();

        var after = source.TryRead(_github, CredentialFields.Totp, out var released, out failure, out _);

        using (released)
        {
            Assert.True(after);
            Assert.Equal(CredentialFailure.None, failure);
            Assert.Equal(CredentialFields.Totp, released!.Field);
            Assert.Equal("081804", released.Value.ToString());
            Assert.Equal(TimeSpan.FromSeconds(1), released.CurrentFor);
        }
    }

    [Fact]
    public void The_listing_names_totp_for_an_item_with_one_and_never_a_seed_field()
    {
        Assert.Equal(SavedRead.Current, _vault.ReadSavedListing(out var listing));

        Assert.Equal(["password", CredentialFields.Totp], listing!.Single(entry => entry.Name == _github).Fields);
        Assert.Equal(["password"], listing.Single(entry => entry.Name == _plain).Fields);
        Assert.Empty(listing.Single(entry => entry.Name == _github).Within(ExposureReach.ProjectFields).Fields);
        Assert.False(EntryExposure.Permits(ExposureReach.ProjectFields, CredentialFields.Totp));
    }

    [Theory]
    [InlineData("totp", "totp")]
    [InlineData("TimeOtp-Secret", "invalid")]
    [InlineData("otp", "invalid")]
    public void The_audit_line_names_a_code_as_totp_and_a_seed_field_as_invalid(string field, string recorded)
    {
        var record = RequestRecords.Credential(
            AuditClient.Of("claude-code", "1.0", "claude-code"),
            "api/GitHub",
            field,
            60,
            "sign in to finish the deploy",
            AuditDecision.Granted,
            AuditMethod.Prompt,
            "a person approved this one request",
            "api/GitHub",
            1,
            ["api/**"],
            null,
            null);

        Assert.Equal(recorded, record.Args.Field);
    }

    [Fact]
    public async Task Nothing_the_operator_is_told_holds_the_code_or_the_seed()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await approver.Handler.RequestAsync(Request(_github), "conn-1", Token);

        Assert.Equal(_codeAt09, reply.Value);
        Assert.NotEmpty(approver.Narration);
        Assert.All(approver.Narration, line =>
        {
            Assert.DoesNotContain(_codeAt09, line, StringComparison.Ordinal);
            Assert.DoesNotContain(_seed, line, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(_codeAt09, reply.ToString(), StringComparison.Ordinal);
    }

    private static CredentialRequest Request(EntryName entry, string field = CredentialFields.Totp) => new()
    {
        Entry = $"{entry.GroupPath}/{entry.Title}",
        Field = field,
        Reason = "sign in to finish the deploy",
        TtlSeconds = 60,
        Exposure = ["api/**"],
        ClientName = "claude-code",
        ClientLabel = "claude-code",
    };

    /// <summary>A real handler and source over the real vault on one clock at 1111111109 s, with only the person faked.</summary>
    private sealed class Approver : IDisposable
    {
        internal Approver(Vault vault, AgentAccess? access = null)
        {
            Settings.Records["claude-code"] = access ?? ApproverFixture.SeesAll;
            Grants = new GrantCache(Clock);
            Gate = new ApprovalGate(Channel, Clock, ApprovalLimits.Default);
            Handler = new ApproverHandler(
                new VaultCredentialSource(() => vault, Clock),
                new VaultEntryNameLister(() => vault),
                Gate,
                Grants,
                Settings,
                Narration.Add,
                clock: Clock);
        }

        internal FakeAgentSettings Settings { get; } = new();

        internal FakeChannel Channel { get; } = new();

        internal ManualClock Clock { get; } = new(_at09);

        internal GrantCache Grants { get; }

        internal ApprovalGate Gate { get; }

        internal ApproverHandler Handler { get; }

        internal List<string> Narration { get; } = [];

        public void Dispose()
        {
            Gate.Dispose();
            Grants.Dispose();
        }
    }
}
