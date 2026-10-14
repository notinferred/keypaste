using System.Text;
using System.Text.Json;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Each kind of agent request has one line shape, whichever process records it: the fields every
/// builder fills, the client as a line keeps it, nothing a reply released, and a run line's vault,
/// entries and command (D-0361), which still fit after a person approved the largest run (R14).
/// </summary>
public sealed class RequestRecordsTests : IDisposable
{
    private const string _vault = "0123456789abcdef";
    private const string _value = "sk_live_REQUEST_RECORDS_SENTINEL";
    private const string _session = "session-one";

    private static readonly AuditClient _client = new("claude-code", "1.2.3", "cc");
    private static readonly string[] _exposure = ["env/**"];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-request-records-").FullName;

    private string LogPath => Path.Combine(_directory, "audit.jsonl");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private AuditLog Open()
    {
        Assert.True(AuditLog.TryOpen(LogPath, TimeProvider.System, out var log, out var error), error);
        return log;
    }

    private string[] Lines()
    {
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static RunArguments Asked(
        IReadOnlyList<string>? command = null,
        string? project = "acme-api",
        string? profile = "staging",
        IReadOnlyList<RunReference>? references = null,
        string reason = "run the pending migration") =>
        new(command ?? ["npm", "run", "migrate"], "/work/acme-api", project, profile, null, references, reason, RunRequestRules.DefaultTimeoutSeconds);

    private static AuditRecord RunLine(IReadOnlyList<string>? entries = null, RunArguments? asked = null, AuditClient? client = null) =>
        RequestRecords.Run(
            client ?? _client,
            asked ?? Asked(),
            "/usr/bin/npm",
            AuditDecision.Granted,
            AuditMethod.Prompt,
            "a person approved this one run",
            0,
            entries ?? ["env/acme-api/DATABASE_URL"],
            _exposure,
            _session,
            _vault);

    private static EnvReply Released(string project = "acme-api", string profile = "staging") =>
        new(
            EnvResolved.Released(
                project,
                [new EnvVariable("DATABASE_URL", _value), new EnvVariable("STRIPE_KEY", _value)],
                profile,
                [
                    new EnvSource("DATABASE_URL", new EntryName("env/acme-api", "DATABASE_URL"), "DATABASE_URL"),
                    new EnvSource("STRIPE_KEY", new EntryName("services", "Stripe"), "STRIPE_KEY"),
                ]),
            string.Empty);

    [Fact]
    public void TheToolNames_AreTheOnesEveryLineWasWrittenWith()
    {
        Assert.Equal(("list_entry_names", "request_credential", "run"), (AuditTools.List, AuditTools.Credential, AuditTools.Run));
    }

    [Fact]
    public void AClient_IsSanitizedAndCapped_AndWhatItDidNotSayStaysAbsent()
    {
        var client = AuditClient.Of("evil\nclient\u202e" + new string('n', 200), null, "work-laptop");

        Assert.DoesNotContain('\n', client.Name!);
        Assert.DoesNotContain('\u202e', client.Name!);
        Assert.True(client.Name!.Length <= AuditClient.MaximumTextLength);
        Assert.Null(client.Version);
        Assert.Equal("work-laptop", client.Label);
        Assert.Equal(AuditClient.Unknown, AuditClient.Of(null, null, null));
        Assert.Equal(_client, AuditClient.Of("claude-code", "1.2.3", "cc"));
    }

    [Fact]
    public void AListingRefusal_NamesTheClientTheExposureAndTheVault_AndNoArguments()
    {
        var line = RequestRecords.Listing(_client, AuditDecision.Denied, AuditMethod.VaultLocked, "the vault is locked", _exposure, null, _vault);

        Assert.Equal(AuditTools.List, line.Tool);
        Assert.Equal(_client, line.Client);
        Assert.Same(AuditArgs.None, line.Args);
        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultLocked, "the vault is locked"), (line.Decision, line.Method, line.Reason));
        Assert.Equal(_exposure, line.Exposure);
        Assert.Null(line.Session);
        Assert.Equal(_vault, line.Vault);
        Assert.Null(line.GrantedSeconds);
        Assert.Null(line.Entries);
        Assert.Null(line.Command);
        Assert.Null(line.CommandSha256);
    }

    [Fact]
    public void AListing_CountsWhatItNamed_UnderTheSessionThatAnswered()
    {
        var line = RequestRecords.Listed(_client, 3, _exposure, _session, _vault);

        Assert.Equal(AuditTools.List, line.Tool);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Exposure, "listed 3 item names"), (line.Decision, line.Method, line.Reason));
        Assert.Equal((_session, _vault), (line.Session, line.Vault));
        Assert.Equal(_exposure, line.Exposure);
        Assert.Same(AuditArgs.None, line.Args);
    }

    [Fact]
    public void ACredentialGrant_RecordsWhatWasAskedAndHowLongItWasGranted()
    {
        var line = RequestRecords.Credential(
            _client, "env/dev/STRIPE_KEY", "password", 900, "deploy billing",
            AuditDecision.Granted, AuditMethod.Prompt, "a person approved this request", null, 900, _exposure, _session, _vault);

        Assert.Equal(AuditTools.Credential, line.Tool);
        Assert.Equal(_client, line.Client);
        Assert.Equal(AuditArgs.ForCredentialRequest("env/dev/STRIPE_KEY", "password", 900, "deploy billing"), line.Args);
        Assert.Equal(EntryAddressKind.Path, line.Args.EntryKind);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, "a person approved this request"), (line.Decision, line.Method, line.Reason));
        Assert.Equal(900, line.GrantedSeconds);
        Assert.Equal(_exposure, line.Exposure);
        Assert.Equal((_session, _vault), (line.Session, line.Vault));
        Assert.Null(line.Entries);
        Assert.Null(line.Command);
    }

    [Fact]
    public void ACredentialAskedByHandle_RecordsTheEntryItResolvedTo()
    {
        var resolved = RequestRecords.Credential(
            _client, "k1_0123456789abcdef", "password", 60, "why",
            AuditDecision.Denied, AuditMethod.Prompt, "a person refused this request", "env/dev/STRIPE_KEY", null, _exposure, _session, _vault);
        var unresolved = RequestRecords.Credential(
            _client, "k1_0123456789abcdef", "password", 60, "why",
            AuditDecision.Denied, AuditMethod.OutOfScope, "the item is outside what this agent can see through this bridge", null, null, _exposure, null, _vault);

        Assert.Equal("env/dev/STRIPE_KEY", resolved.Args.Entry);
        Assert.Equal(EntryAddressKind.Path, resolved.Args.EntryKind);
        Assert.Equal("k1_0123456789abcdef", unresolved.Args.Entry);
        Assert.Equal(EntryAddressKind.Handle, unresolved.Args.EntryKind);
    }

    [Fact]
    public void ACredentialDenial_KeepsNoGrantLength_AndAFieldKeypasteDoesNotReleaseIsInvalid()
    {
        var line = RequestRecords.Credential(
            _client, "env/dev/STRIPE_KEY", "Recovery codes", 900, "why",
            AuditDecision.Denied, AuditMethod.InvalidRequest, "the request did not satisfy the tool's schema", null, 900, _exposure, null, _vault);

        Assert.Equal("invalid", line.Args.Field);
        Assert.Equal(900, line.Args.TtlSeconds);
        Assert.Null(line.GrantedSeconds);
    }

    [Fact]
    public void ARunLine_NamesTheSetTheCommandAndItsHash()
    {
        var line = RunLine();

        Assert.Equal(AuditTools.Run, line.Tool);
        Assert.Equal(_client, line.Client);
        Assert.Equal(AuditArgs.ForRun("env/acme-api/staging", "run the pending migration"), line.Args);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, "a person approved this one run"), (line.Decision, line.Method, line.Reason));
        Assert.Equal(0, line.GrantedSeconds);
        Assert.Equal(["env/acme-api/DATABASE_URL"], line.Entries!);
        Assert.Equal("npm run migrate", line.Command);
        Assert.Equal(AuditRecord.HashOf(["/usr/bin/npm", "run", "migrate"]), line.CommandSha256);
        Assert.Equal(_exposure, line.Exposure);
        Assert.Equal((_session, _vault), (line.Session, line.Vault));
    }

    [Fact]
    public void ARunByReference_NamesNoSet_AndOneRefusedBeforeItsProgramResolved_HashesTheCommandAsNamed()
    {
        var line = RequestRecords.Run(
            _client,
            Asked(project: null, profile: null, references: [new RunReference("DATABASE_URL", "kp://env/acme-api/DATABASE_URL")]),
            null,
            AuditDecision.Denied,
            AuditMethod.OutOfScope,
            "a reference is outside what this agent can see through this bridge",
            0,
            null,
            _exposure,
            null,
            _vault);

        Assert.Null(line.Args.Entry);
        Assert.Null(line.GrantedSeconds);
        Assert.Null(line.Entries);
        Assert.Equal(AuditRecord.HashOf(["npm", "run", "migrate"]), line.CommandSha256);
    }

    [Fact]
    public void ARunOfTheDefaultProfile_NamesTheProjectsSet_AndAnEmptyCommandHasNoHash()
    {
        var line = RequestRecords.Run(
            _client, Asked(command: [], profile: null), null, AuditDecision.Denied, AuditMethod.InvalidRequest,
            "the request did not satisfy the tool's schema", 0, null, _exposure, null, _vault);

        Assert.Equal("env/acme-api", line.Args.Entry);
        Assert.Equal(string.Empty, line.Command);
        Assert.Null(line.CommandSha256);
    }

    [Fact]
    public void ARunLine_CarriesCommandEntriesAndVault()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine(), out var error), error);
        }

        using var parsed = JsonDocument.Parse(Assert.Single(Lines()));
        var root = parsed.RootElement;
        Assert.Equal(_vault, root.GetProperty("vault").GetString());
        Assert.Equal(["env/acme-api/DATABASE_URL"], root.GetProperty("entries").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("npm run migrate", root.GetProperty("command").GetString());
        Assert.Equal(64, root.GetProperty("command_sha256").GetString()!.Length);

        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out _));
        var entry = Assert.Single(entries);
        Assert.Equal((_vault, "npm run migrate"), (entry.Vault, entry.Command));
        Assert.Equal(["env/acme-api/DATABASE_URL"], entry.Entries);
    }

    [Fact]
    public void OldLines_ReadWithEmptyNewFields_AndAMixedLogStillVerifies()
    {
        using (var log = Open())
        {
            Assert.True(log.TryAppend(RunLine() with { Vault = null, Entries = null, Command = null, CommandSha256 = null }, out var error), error);
            Assert.True(log.TryAppend(RunLine(), out error), error);
        }

        Assert.True(AuditReader.TryRead(LogPath, out var entries, out _, out _));
        Assert.Equal((string.Empty, string.Empty), (entries[0].Vault, entries[0].Command));
        Assert.Empty(entries[0].Entries);
        Assert.Equal(AuditChainVerdict.Intact, AuditChainVerifier.Verify(LogPath).Verdict);
    }

    [Fact]
    public void AMaximalRunLine_Fits()
    {
        var entries = Enumerable.Range(0, 32).Select(i => $"env/acme-api/KEY_{i}_" + new string('K', 40)).ToList();
        Assert.True(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(entries)) <= 2048);

        var line = RunLine(
            entries,
            Asked(command: [new string('é', 4 * AuditRecord.CommandLength)], project: new string('p', 100), reason: new string('r', 2000)),
            AuditClient.Of(new string('n', 200), new string('v', 200), new string('l', 200)));

        Assert.Equal(AuditRecord.CommandLength, line.Command!.Length);
        Assert.EndsWith("…", line.Command, StringComparison.Ordinal);

        using var log = Open();
        Assert.True(log.TryAppend(line, out var error), error);
        Assert.Contains("command_sha256", Lines()[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnOversizedRunLine_IsRefused()
    {
        var entries = Enumerable.Range(0, AuditLog.MaximumRecordBytes / 64).Select(i => $"env/acme-api/KEY_{i}_" + new string('K', 60)).ToList();

        using var log = Open();
        Assert.False(log.TryAppend(RunLine(entries), out var error));
        Assert.Contains("over the", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionRun_NamesTheSetItsEntriesAndItsCount()
    {
        var line = RequestRecords.EnvRun("acme-api", "staging", AuditMethod.Prompt, Released(), _session, _vault);

        Assert.Equal(AuditTools.Run, line.Tool);
        Assert.Equal(new AuditClient("keypaste run --session", CoreInfo.Version, null), line.Client);
        Assert.Equal(new AuditArgs { Entry = "env/acme-api/staging" }, line.Args);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, "2 variable(s)"), (line.Decision, line.Method, line.Reason));
        Assert.Equal(["env/acme-api/DATABASE_URL", "services/Stripe"], line.Entries!);
        Assert.Equal((_session, _vault), (line.Session, line.Vault));
        Assert.Empty(line.Exposure);
        Assert.Null(line.GrantedSeconds);
        Assert.Null(line.Command);
    }

    [Fact]
    public void ASessionRunRefused_GivesTheOwnersWordsOrTheSetsRefusal_AndNoEntries()
    {
        var worded = RequestRecords.EnvRun(
            "acme-api", "staging", AuditMethod.Prompt,
            new EnvReply(EnvResolved.Refused("acme-api", EnvOutcome.Declined, profile: "staging"), "a person refused the set"), _session, _vault);
        var unworded = RequestRecords.EnvRun(
            "acme-api", "dev", AuditMethod.OutOfScope,
            new EnvReply(EnvResolved.Refused("acme-api", EnvOutcome.NoProject, profile: "dev"), string.Empty), _session, _vault);

        Assert.Equal((AuditDecision.Denied, "a person refused the set"), (worded.Decision, worded.Reason));
        Assert.Null(worded.Entries);
        Assert.Equal("no env set for 'acme-api'", unworded.Reason);
        Assert.Equal("env/acme-api/dev", unworded.Args.Entry);
    }

    [Fact]
    public void ATokenRun_NamesTheTokenInItsReason_OnceTheTokenVerified()
    {
        var token = new TokenInfo("t7d2e", "ci\u202estaging", [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), AllowProd: false);

        var verified = RequestRecords.TokenRun("acme-api", "staging", token, Released(), _session, _vault);
        var unknown = RequestRecords.TokenRun(
            "acme-api", "staging", null,
            new EnvReply(EnvResolved.Refused("acme-api", EnvOutcome.Unauthorized, profile: "staging"), "the token is not valid for this vault"), _session, _vault);

        Assert.Equal(new AuditClient("keypaste run --token", CoreInfo.Version, null), verified.Client);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Token), (verified.Decision, verified.Method));
        Assert.Equal(TokenAuditReason.Format("t7d2e", EntryNameSanitizer.Sanitize(token.Name).Text, "2 variable(s)"), verified.Reason);
        Assert.DoesNotContain('\u202e', verified.Reason);
        Assert.Equal(["env/acme-api/DATABASE_URL", "services/Stripe"], verified.Entries!);
        Assert.Equal((AuditDecision.Denied, AuditMethod.Token, "the token is not valid for this vault"), (unknown.Decision, unknown.Method, unknown.Reason));
    }

    /// <summary>The builders that are handed a released set write its names and its count, never what it holds.</summary>
    [Fact]
    public void NoLine_CarriesAReleasedValue()
    {
        var token = new TokenInfo("t7d2e", "ci", [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), AllowProd: false);

        using (var log = Open())
        {
            AuditRecord[] lines =
            [
                RequestRecords.EnvRun("acme-api", "staging", AuditMethod.Prompt, Released(), _session, _vault),
                RequestRecords.TokenRun("acme-api", "staging", token, Released(), _session, _vault),
                RequestRecords.Listed(_client, 2, _exposure, _session, _vault),
                RequestRecords.Credential(
                    _client, "env/dev/STRIPE_KEY", "password", 900, "deploy billing",
                    AuditDecision.Granted, AuditMethod.Prompt, "a person approved this request", "env/dev/STRIPE_KEY", 900, _exposure, _session, _vault),
                RunLine(),
            ];

            foreach (var line in lines)
            {
                Assert.True(log.TryAppend(line, out var error), error);
            }
        }

        Assert.Equal(5, Lines().Length);
        Assert.DoesNotContain(_value, File.ReadAllText(LogPath), StringComparison.Ordinal);
    }
}
