using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The bridge names its vault and the session it reached on every operation, and records which
/// session answered (D-0310).
/// </summary>
public sealed class SessionAttachmentTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Credential() => new()
    {
        ["entry"] = "env/dev/STRIPE_KEY",
        ["field"] = "password",
        ["reason"] = "deploy the billing service to staging",
        ["ttl_seconds"] = 60,
    };

    private static CredentialRequest Request() => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = "deploy the billing service to staging",
        TtlSeconds = 60,
        Exposure = ["env/**"],
    };

    [Fact]
    public async Task EveryRequestNamesTheVaultAndTheSessionItAttachedTo()
    {
        await using var harness = new McpHarness();
        harness.Approver.Session = "session-one";
        harness.Approver.StartApproving();
        var client = await harness.StartAsync("--expose", "env/**");

        await client.CallToolAsync(ToolText.CredentialToolName, Credential(), cancellationToken: Token);

        var received = Assert.Single(harness.Approver.Received);
        Assert.Equal(harness.VaultPath, received.Vault);
        Assert.Equal("session-one", received.Session);
        Assert.True(harness.Approver.Attaches >= 1);
    }

    [Fact]
    public async Task TheAuditLineNamesTheSessionThatAnswered()
    {
        await using var harness = new McpHarness();
        harness.Approver.Session = "session-one";
        harness.Approver.StartApproving();
        var client = await harness.StartAsync("--expose", "env/**");

        await client.CallToolAsync(ToolText.CredentialToolName, Credential(), cancellationToken: Token);

        Assert.Equal("session-one", Field(Assert.Single(harness.AuditLines()), "session"));
    }

    [Fact]
    public async Task AListingCarriesTheSessionThatAnswered()
    {
        await using var approver = new FakeApprover();
        approver.Session = "session-one";
        approver.Names = new NamesReply(true, [new EntryName("env/dev", "STRIPE_KEY")], string.Empty, true);
        approver.Start();

        var (source, connection) = Source(approver);
        await using var _ = connection;

        var listing = await source.ListAsync(Token);

        Assert.Equal(VaultAvailability.Available, listing.Availability);
        Assert.Equal("session-one", listing.Session);
    }

    [Fact]
    public async Task AListingAnOwnerWillNotAttach_IsRefusedAsNoSession()
    {
        await using var approver = new FakeApprover();
        approver.AttachRefusal = AttachReply.Refused(AuditMethod.NoSession, "another vault");
        approver.Names = new NamesReply(true, [new EntryName("env/dev", "STRIPE_KEY")], string.Empty, true);
        approver.Start();

        var (source, connection) = Source(approver);
        await using var _ = connection;

        var listing = await source.ListAsync(Token);

        Assert.Equal(VaultAvailability.NoSession, listing.Availability);
        Assert.Equal(ToolText.NoSession, listing.Reason);
        Assert.Empty(listing.Names);
    }

    [Fact]
    public async Task AnOwnerThatWillNotAttach_IsRefusedUnaskedAndRecordedAsNoSession()
    {
        await using var harness = new McpHarness();
        harness.Approver.AttachRefusal = AttachReply.Refused(AuditMethod.NoSession, "the keypaste process that answered holds a different vault");
        harness.Approver.StartApproving();
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Credential(), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.NoSession, TextOf(result));
        Assert.Empty(harness.Approver.Received);

        var line = Assert.Single(harness.AuditLines());
        Assert.Equal("denied", Field(line, "decision"));
        Assert.Equal("no-session", Field(line, "method"));
        Assert.Null(Field(line, "session"));
    }

    [Fact]
    public async Task AnOwnerThatIsLocked_IsReportedAsLocked()
    {
        await using var harness = new McpHarness();
        harness.Approver.AttachRefusal = AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked");
        harness.Approver.StartApproving();
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Credential(), cancellationToken: Token);

        Assert.Equal(ToolText.VaultLocked, TextOf(result));
        Assert.Equal("vault-locked", Field(Assert.Single(harness.AuditLines()), "method"));
    }

    [Fact]
    public async Task ABridgeWithNoVault_AsksNobody()
    {
        await using var approver = new FakeApprover();
        approver.StartApproving();
        await using var connection = new ApproverConnection(approver.PipeName, string.Empty);

        var (reply, outcome) = await connection.RequestAsync(Request(), Token);

        Assert.Null(reply);
        Assert.Equal(ApproverOutcome.NoVault, outcome);
        Assert.Equal(0, approver.Attaches);
        Assert.Empty(approver.Received);
    }

    /// <summary>
    /// A request whose reply was lost is sent again under the session it was first sent in, so an
    /// owner that locked and unlocked meanwhile refuses it instead of answering it from a later unlock.
    /// </summary>
    [Fact]
    public async Task ARetriedRequest_KeepsTheSessionItWasFirstSentIn()
    {
        await using var approver = new FakeApprover();
        approver.Session = "session-one";
        approver.DropOnce = () => approver.Session = "session-two";
        approver.StartApproving();
        await using var connection = new ApproverConnection(approver.PipeName, Path.Combine(Path.GetTempPath(), "retry.kdbx"));

        await connection.RequestAsync(Request(), Token);

        Assert.Equal(2, approver.Received.Count);
        Assert.All(approver.Received, request => Assert.Equal("session-one", request.Session));
        Assert.Equal(2, approver.Attaches);
    }

    private static (ApproverEntryNameSource Source, ApproverConnection Connection) Source(FakeApprover approver)
    {
        var vault = Path.Combine(Path.GetTempPath(), "listing.kdbx");
        Assert.True(ServerOptions.TryParse(["--vault", vault, "--expose", "env/**"], null, null, approver.PipeName, out var options, out var error), error);

        var connection = new ApproverConnection(options.ApproverName, options.VaultPath);
        return (new ApproverEntryNameSource(connection, options), connection);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string? Field(string line, string name)
    {
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }
}
