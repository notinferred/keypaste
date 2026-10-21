using System.Text;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>An env-named custom field leaves by name to an agent, a reference or a share; no other custom field does (C.5a).</summary>
public sealed class CustomFieldReleaseTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private const string _password = "SENTINEL-C5A-PASSWORD-1a2b";
    private const string _apiKey = "SENTINEL-C5A-OPENAI-3c4d";
    private const string _recovery = "SENTINEL-C5A-RECOVERY-5e6f";

    private const string _databaseUrl = "postgres://u:{PASSWORD}@db/{S:OPENAI_API_KEY}";
    private const string _databaseUrlFilled = "postgres://u:" + _password + "@db/" + _apiKey;

    private static readonly EntryName _openAi = new("api", "OpenAI");
    private static readonly string[] _everySentinel = [_password, _apiKey, _recovery];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-custom-release-").FullName;
    private readonly Vault _vault;

    public CustomFieldReleaseTests()
    {
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _openAi.GroupPath, Title = _openAi.Title, Password = _password });
        Assert.True(_vault.SetFields(_openAi, [new FieldWrite("OPENAI_API_KEY", _apiKey), new FieldWrite("Recovery codes", _recovery)]));
        _vault.Save();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("OPENAI_API_KEY")]
    [InlineData("A")]
    [InlineData("DATABASE_URL_2")]
    public void AnEnvNamedCustomField_IsReleasable(string field)
    {
        Assert.True(CredentialFields.IsReleasable(field));
        Assert.True(CredentialFields.IsCustom(field));
    }

    [Theory]
    [InlineData("Recovery codes")]
    [InlineData("otp")]
    [InlineData("KP2A_URL_1")]
    [InlineData("KPEX_PASSKEY")]
    [InlineData("KPXC_BROWSER")]
    [InlineData("URL")]
    [InlineData("PASSWORD")]
    [InlineData("TITLE")]
    [InlineData("openai_api_key")]
    [InlineData("_EXEC_CMD")]
    [InlineData("1KEY")]
    public void NoOtherCustomField_IsReleasable(string field)
    {
        Assert.False(CredentialFields.IsReleasable(field));
        Assert.False(ShareService.IsShareable(field));
    }

    [Fact]
    public async Task AnApprovedRequest_ReleasesExactlyTheCustomField_AndThePromptNamesIt()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await approver.Handler.RequestAsync(Request("OPENAI_API_KEY"), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(_apiKey, reply.Value);
        Assert.Equal("OPENAI_API_KEY", approver.Channel.LastPrompt!.Field);
        Assert.Equal("api/OpenAI", reply.Entry);
    }

    [Fact]
    public async Task AGrantForOneField_ServesNoneOfTheEntrysOthers()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var first = await approver.Handler.RequestAsync(Request("OPENAI_API_KEY"), "conn-1", Token);
        Assert.True(first.TtlSeconds > 0, "the approval was not a timed grant");

        var again = await approver.Handler.RequestAsync(Request("OPENAI_API_KEY"), "conn-1", Token);
        Assert.Equal(AuditMethod.GrantCache, again.Method);
        Assert.Equal(1, approver.Channel.Asked);

        approver.Channel.Answer = ApprovalAnswer.Denied;
        var password = await approver.Handler.RequestAsync(Request("password"), "conn-1", Token);

        Assert.Equal(2, approver.Channel.Asked);
        Assert.Equal("password", approver.Channel.LastPrompt!.Field);
        Assert.Equal(AuditDecision.Denied, password.Decision);
        Assert.Null(password.Value);
    }

    [Theory]
    [InlineData("Recovery codes")]
    [InlineData("otp")]
    [InlineData("KP2A_URL_1")]
    [InlineData("URL")]
    public async Task AnyOtherName_IsRefusedBeforeAnyoneIsAskedOrAnythingRead(string field)
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await approver.Handler.RequestAsync(Request(field), "conn-1", Token);

        Assert.Equal(AuditMethod.InvalidRequest, reply.Method);
        Assert.Equal($"the request's field must be {CredentialFields.RequestRule}", reply.Reason);
        Assert.Null(reply.Value);
        Assert.Equal(0, approver.Channel.Asked);
    }

    [Fact]
    public async Task AnAutomaticMode_ReleasesTheField_WithNobodyAsked()
    {
        using var approver = new Approver(_vault, ApproverFixture.SeesAll with { Mode = AgentMode.Automatic });
        approver.Channel.Answer = ApprovalAnswer.Denied;

        var released = await approver.Handler.RequestAsync(Request("OPENAI_API_KEY"), "conn-1", Token);

        Assert.Equal(AuditMethod.Automatic, released.Method);
        Assert.Equal(_apiKey, released.Value);
        Assert.Equal(0, approver.Channel.Asked);
    }

    /// <summary>No mode reaches a field that never leaves: the field's name is refused before Automatic is considered.</summary>
    [Fact]
    public async Task AnAutomaticMode_NeverReleasesAFieldThatNeverLeaves()
    {
        using var approver = new Approver(_vault, ApproverFixture.SeesAll with { Mode = AgentMode.Automatic });

        var refused = await approver.Handler.RequestAsync(Request("Recovery codes"), "conn-1", Token);

        Assert.Equal(AuditMethod.InvalidRequest, refused.Method);
        Assert.Null(refused.Value);
        Assert.Equal(0, approver.Channel.Asked);
    }

    [Fact]
    public async Task AnUnsavedFieldChange_ReleasesNothing()
    {
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;
        Assert.True(_vault.SetFields(_openAi, [new FieldWrite("OPENAI_API_KEY", "unsaved-value")]));

        var reply = await approver.Handler.RequestAsync(Request("OPENAI_API_KEY"), "conn-1", Token);

        Assert.Equal(AuditMethod.VaultChanged, reply.Method);
        Assert.Null(reply.Value);
    }

    [Fact]
    public void TheSourceReadsTheCustomFieldOnlyFromTheSavedFile()
    {
        var source = new VaultCredentialSource(() => _vault);

        Assert.True(source.TryRead(_openAi, "OPENAI_API_KEY", out var released, out var failure, out _), failure.ToString());
        using (released)
        {
            Assert.Equal(_apiKey, released.Value.ToString());
        }

        Assert.False(source.TryRead(_openAi, "Recovery codes", out _, out failure, out _));
        Assert.Equal(CredentialFailure.NoSuchField, failure);

        Assert.False(source.TryRead(_openAi, "MISSING_KEY", out _, out failure, out _));
        Assert.Equal(CredentialFailure.Empty, failure);
    }

    [Theory]
    [InlineData(false, CredentialFailure.Unsaved)]
    [InlineData(true, CredentialFailure.ChangedOnDisk)]
    public void AChangeAfterTheEntriesAreReadAndBeforeTheFieldIs_ReleasesNothing(bool onDisk, CredentialFailure expected)
    {
        // The source asks for the vault once to read the entries and again to read the custom field.
        var calls = 0;
        var source = new VaultCredentialSource(() =>
        {
            if (++calls == 2)
            {
                Change(onDisk);
            }

            return _vault;
        });

        var read = source.TryRead(_openAi, "OPENAI_API_KEY", out var released, out var failure, out _);
        using (released)
        {
            Assert.False(read);
            Assert.Null(released);
        }

        Assert.Equal(expected, failure);
        Assert.Equal(2, calls);
    }

    /// <summary>A value naming its own item's fields is released filled in, and a timed grant serves the filled value (D-0465).</summary>
    [Fact]
    public async Task AnApprovedRequest_ReleasesTheValueFilledIn_AndItsGrantServesThatValue()
    {
        Plant("DATABASE_URL", _databaseUrl);
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var first = await approver.Handler.RequestAsync(Request("DATABASE_URL"), "conn-1", Token);
        var again = await approver.Handler.RequestAsync(Request("DATABASE_URL"), "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, first.Method);
        Assert.Equal(_databaseUrlFilled, first.Value);
        Assert.Equal(AuditMethod.GrantCache, again.Method);
        Assert.Equal(_databaseUrlFilled, again.Value);
        Assert.Equal(1, approver.Channel.Asked);
        Assert.Equal("DATABASE_URL", approver.Channel.LastPrompt!.Field);
    }

    [Fact]
    public async Task AnAutomaticRelease_IsFilledIn()
    {
        Plant("DATABASE_URL", _databaseUrl);
        using var approver = new Approver(_vault, ApproverFixture.SeesAll with { Mode = AgentMode.Automatic });

        var released = await approver.Handler.RequestAsync(Request("DATABASE_URL"), "conn-1", Token);

        Assert.Equal(AuditMethod.Automatic, released.Method);
        Assert.Equal(_databaseUrlFilled, released.Value);
        Assert.Equal(0, approver.Channel.Asked);
    }

    /// <summary>
    /// A value naming a field that never leaves is refused after the person allowed it, whatever filled in
    /// before it, as unfillable, and the reason names the field asked for and the placeholder, never a value;
    /// asking again is refused without asking the person (D-0465).
    /// </summary>
    [Fact]
    public async Task AValueNamingAFieldThatNeverLeaves_IsRefusedAfterTheAllow_NamingThePlaceholder()
    {
        Plant("DATABASE_URL", "postgres://u:{PASSWORD}@db/{S:Recovery codes}");
        using var approver = new Approver(_vault);
        approver.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await approver.Handler.RequestAsync(Request("DATABASE_URL"), "conn-1", Token);
        var again = await approver.Handler.RequestAsync(Request("DATABASE_URL"), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Unfillable, reply.Method);
        Assert.Equal(
            "the item's DATABASE_URL holds the KeePass placeholder {S:Recovery codes}, which names a field keypaste never releases",
            reply.Reason);
        Assert.Null(reply.Value);
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(1, approver.Channel.Asked);
        AssertNoSentinel(reply.Reason);
    }

    [Theory]
    [InlineData(false, CredentialFailure.Unsaved)]
    [InlineData(true, CredentialFailure.ChangedOnDisk)]
    public void AChangeAfterTheFieldIsReadAndBeforeItIsFilledIn_ReleasesNothing(bool onDisk, CredentialFailure expected)
    {
        Plant("DATABASE_URL", _databaseUrl);

        // The source asks for the vault to read the entries, then the custom field, then the fields it names.
        var calls = 0;
        var source = new VaultCredentialSource(() =>
        {
            if (++calls == 3)
            {
                Change(onDisk);
            }

            return _vault;
        });

        var read = source.TryRead(_openAi, "DATABASE_URL", out var released, out var failure, out var unfilled);
        using (released)
        {
            Assert.False(read);
            Assert.Null(released);
        }

        Assert.Equal(expected, failure);
        Assert.Empty(unfilled);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AReference_ReleasesTheCustomField_AndAnyOtherNameStartsNothing()
    {
        var resolved = Resolve("OPENAI_API_KEY=kp:///api/OpenAI#OPENAI_API_KEY\n");

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal([new EnvVariable("OPENAI_API_KEY", _apiKey)], resolved.Variables);

        Assert.False(KpReferences.TryParse("kp:///api/OpenAI#Recovery%20codes", out _, out var error));
        Assert.Equal($"'Recovery%20codes' is not a field; use {CredentialFields.Rule}", error);
        Assert.False(KpReferences.TryParse("kp:///api/OpenAI#otp", out _, out _));

        var refused = Resolve("CODES=kp:///api/OpenAI#Recovery%20codes\n");
        Assert.Equal(EnvOutcome.Invalid, refused.Outcome);
        Assert.Empty(refused.Variables);
        AssertNoSentinel(refused.Refusal + string.Concat(refused.Problems.Select(problem => problem.Reason)));
    }

    [Fact]
    public async Task AShare_SealsTheOneFieldUnderItsName()
    {
        using var fixture = new ShareFixture();
        using (var vault = fixture.Open())
        {
            Assert.True(vault.SetFields(ShareFixture.Stripe, [new FieldWrite("OPENAI_API_KEY", _apiKey), new FieldWrite("Recovery codes", _recovery)]));
            vault.Save();
        }

        using (var vault = fixture.Open())
        {
            var outcome = await fixture.Service().CreateAsync(vault, ShareFixture.Request(field: "OPENAI_API_KEY"), CancellationToken.None);

            Assert.True(outcome.Ok, outcome.Message);
            Assert.True(ShareLink.TryParse(outcome.Link!, out var id, out var key));
            Assert.True(ShareCrypto.TryOpen(fixture.Server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out var payload, out var error), error);
            var field = Assert.Single(payload.Fields);
            Assert.Equal(("OPENAI_API_KEY", _apiKey), (field.Name, field.Value));
            AssertNoSentinel(fixture.AuditText());

            var refused = await fixture.Service().CreateAsync(vault, ShareFixture.Request(field: "Recovery codes"), CancellationToken.None);
            Assert.False(refused.Ok);
            Assert.Single(fixture.Server.Shares);
        }
    }

    /// <summary>A shared custom field is sealed with its own item's placeholders filled in (D-0465).</summary>
    [Fact]
    public async Task AShare_SealsTheCustomFieldFilledIn()
    {
        using var fixture = new ShareFixture();
        using var vault = fixture.Open();
        Assert.True(vault.SetFields(ShareFixture.Stripe, [new FieldWrite("OPENAI_API_KEY", _apiKey), new FieldWrite("DATABASE_URL", _databaseUrl)]));
        vault.Save();

        var outcome = await fixture.Service().CreateAsync(vault, ShareFixture.Request(field: "DATABASE_URL"), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.True(ShareLink.TryParse(outcome.Link!, out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(fixture.Server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out var payload, out var error), error);
        var field = Assert.Single(payload.Fields);
        Assert.Equal(("DATABASE_URL", "postgres://u:" + ShareFixture.StripeValue + "@db/" + _apiKey), (field.Name, field.Value));
        Assert.DoesNotContain(ShareFixture.StripeValue, fixture.AuditText(), StringComparison.Ordinal);
        AssertNoSentinel(fixture.AuditText());
    }

    private EnvResolved Resolve(string text)
    {
        _ = EnvReferenceFile.TryParse(Encoding.UTF8.GetBytes(text), out var document);
        return EnvReferenceResolution.Resolve(_vault, document, TimeProvider.System);
    }

    private void Plant(string field, string value)
    {
        Assert.True(_vault.SetFields(_openAi, [new FieldWrite(field, value)]));
        _vault.Save();
    }

    private void Change(bool onDisk)
    {
        if (!onDisk)
        {
            Assert.True(_vault.SetFields(_openAi, [new FieldWrite("OPENAI_API_KEY", "unsaved-value")]));
            return;
        }

        using var writer = Vault.Open(_vault.Path, _master);
        Assert.True(writer.SetFields(_openAi, [new FieldWrite("OPENAI_API_KEY", "external-value")]));
        writer.Save();
    }

    private static void AssertNoSentinel(string text)
    {
        foreach (var sentinel in _everySentinel)
        {
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
        }
    }

    private static CredentialRequest Request(string field) => new()
    {
        Entry = "api/OpenAI",
        Field = field,
        Reason = "call the model",
        TtlSeconds = 60,
        Exposure = ["api/**"],
        ClientName = "claude-code",
        ClientLabel = "claude-code",
    };

    /// <summary>A real handler over the real vault, with only the person faked.</summary>
    private sealed class Approver : IDisposable
    {
        internal Approver(Vault vault, AgentAccess? access = null)
        {
            Settings.Records["claude-code"] = access ?? ApproverFixture.SeesAll;
            Grants = new GrantCache(Clock);
            Gate = new ApprovalGate(Channel, Clock, ApprovalLimits.Default);
            Handler = new ApproverHandler(
                new VaultCredentialSource(() => vault),
                new VaultEntryNameLister(() => vault),
                Gate,
                Grants,
                Settings,
                clock: Clock);
        }

        internal FakeAgentSettings Settings { get; } = new();

        internal FakeChannel Channel { get; } = new();

        internal ManualClock Clock { get; } = new();

        internal GrantCache Grants { get; }

        internal ApprovalGate Gate { get; }

        internal ApproverHandler Handler { get; }

        public void Dispose()
        {
            Gate.Dispose();
            Grants.Dispose();
        }
    }
}
