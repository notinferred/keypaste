using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// One bridge serving the two vaults a tool was allowed, Personal and Acme, each through its own owner on its own pipe:
/// searches span both and name each item's vault, a request goes to the one vault holding its item, and a locked vault
/// answers as locked without holding up the other.
/// </summary>
public sealed class BridgeAcrossVaultsTests
{
    private static readonly EntryName _github = new("env/ci", "DEPLOY_TOKEN");
    private static readonly EntryName _stripe = new("env/dev", "STRIPE_KEY");
    private static readonly EntryName _aws = new("env/dev", "AWS_KEY");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FoundAccess Item(EntryName name) =>
        new(new IndexedEntry(new ListedEntry(name, ["password"], []), ItemKind.Login, null, null), []);

    private static FindReply Found(params EntryName[] names) => new(true, [.. names.Select(Item)], string.Empty, true);

    private static Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments) =>
        client.CallToolAsync(tool, arguments, cancellationToken: Token).AsTask();

    private static Dictionary<string, object?> Request(EntryName name, string? vault = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["entry"] = EntryHandle.For(name),
            ["field"] = "password",
            ["reason"] = "deploy the billing service",
        };

        if (vault is not null)
        {
            arguments["vault"] = vault;
        }

        return arguments;
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static (string? Handle, string? Vault)[] Rows(CallToolResult result) =>
        [.. result.StructuredContent!.Value.GetProperty("items").EnumerateArray()
            .Select(row => (row.GetProperty("handle").GetString(), row.GetProperty("vault").GetString()))];

    private static (string? Name, string? State)[] Vaults(CallToolResult result) =>
        [.. result.StructuredContent!.Value.GetProperty("vaults").EnumerateArray()
            .Select(vault => (vault.GetProperty("name").GetString(), vault.GetProperty("state").GetString()))];

    [Fact]
    public async Task A_search_spans_both_unlocked_vaults_and_names_each_items_vault()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Found = Found(_github);
        acme.Found = Found(_stripe);
        personal.StartApproving();
        acme.StartApproving();
        var client = await harness.StartAsync();

        var result = await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "e" });

        Assert.False(result.IsError);
        Assert.Equal([(EntryHandle.For(_github), "Personal"), (EntryHandle.For(_stripe), "Acme")], Rows(result));
        Assert.Equal([("Personal", "open"), ("Acme", "open")], Vaults(result));
        Assert.Contains($"{EntryHandle.For(_stripe)}  vault=Acme  env/dev/STRIPE_KEY", TextOf(result), StringComparison.Ordinal);
        Assert.Single(personal.SearchesReceived);
        Assert.Single(acme.SearchesReceived);
        Assert.Equal(harness.VaultPath, Assert.Single(personal.SearchesReceived).Vault);
        Assert.EndsWith("Acme.kdbx", Assert.Single(acme.SearchesReceived).Vault, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_locked_vault_answers_as_locked_and_the_other_still_answers()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Found = Found(_github);
        personal.Names = new NamesReply(true, [new ListedEntry(_github, ["password"], [])], string.Empty, true);
        acme.AttachRefusal = AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked");
        personal.StartApproving();
        acme.Start();
        var client = await harness.StartAsync();

        var found = await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "e" });
        var listed = await CallAsync(client, ToolText.ListToolName, []);
        var asked = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe, vault: "Acme"));

        Assert.False(found.IsError);
        Assert.Equal([(EntryHandle.For(_github), "Personal")], Rows(found));
        Assert.Equal([("Personal", "open"), ("Acme", "locked")], Vaults(found));
        Assert.Contains("vault Acme: locked", TextOf(found), StringComparison.Ordinal);
        Assert.False(listed.IsError);
        Assert.Contains("vault Acme: locked", TextOf(listed), StringComparison.Ordinal);
        Assert.True(asked.IsError);
        Assert.StartsWith("Acme: ", TextOf(asked), StringComparison.Ordinal);
        Assert.Empty(acme.Received);
        Assert.Empty(personal.Received);
    }

    [Fact]
    public async Task With_both_vaults_locked_a_search_says_so_for_each()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.AttachRefusal = AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked");
        acme.AttachRefusal = AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked");
        personal.Start();
        acme.Start();
        var client = await harness.StartAsync();

        var result = await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "e" });

        Assert.True(result.IsError);
        Assert.Equal($"Personal: {ToolText.VaultLocked}\n\nAcme: {ToolText.VaultLocked}", TextOf(result));
    }

    [Fact]
    public async Task A_request_for_an_item_goes_to_the_vault_a_search_showed_it_in_alone()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Found = Found(_github);
        acme.Found = Found(_stripe);
        personal.StartApproving();
        acme.StartApproving();
        var client = await harness.StartAsync();

        await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "e" });
        var stripe = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe));
        var github = await CallAsync(client, ToolText.CredentialToolName, Request(_github));

        Assert.False(stripe.IsError);
        Assert.False(github.IsError);
        Assert.Equal(EntryHandle.For(_stripe), Assert.Single(acme.Received).Entry);
        Assert.Equal(EntryHandle.For(_github), Assert.Single(personal.Received).Entry);
        Assert.EndsWith("Acme.kdbx", Assert.Single(acme.Received).Vault, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_no_search_showed_is_found_by_listing_both_vaults_and_asked_of_its_own()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Names = new NamesReply(true, [new ListedEntry(_github, ["password"], [])], string.Empty, true);
        acme.Names = new NamesReply(true, [new ListedEntry(_stripe, ["password"], [])], string.Empty, true);
        personal.StartApproving();
        acme.StartApproving();
        var client = await harness.StartAsync();

        var result = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe));

        Assert.False(result.IsError);
        Assert.Single(acme.Received);
        Assert.Empty(personal.Received);
        Assert.Single(personal.ListingsReceived);
        Assert.Single(acme.ListingsReceived);
    }

    [Fact]
    public async Task An_item_both_vaults_hold_under_one_name_is_refused_until_the_request_names_its_vault()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Found = Found(_stripe);
        acme.Found = Found(_stripe);
        personal.StartApproving();
        acme.StartApproving();
        var client = await harness.StartAsync();

        await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "stripe" });
        var ambiguous = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe));

        Assert.True(ambiguous.IsError);
        Assert.Equal(ToolText.VaultAmbiguous(["Personal", "Acme"]), TextOf(ambiguous));
        Assert.Empty(personal.Received);
        Assert.Empty(acme.Received);

        var named = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe, vault: "acme"));

        Assert.False(named.IsError);
        Assert.Single(acme.Received);
        Assert.Empty(personal.Received);
        Assert.Contains("\"method\":\"invalid-request\"", harness.AuditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_in_neither_vault_is_refused_before_either_owner_is_asked()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Names = new NamesReply(true, [new ListedEntry(_github, ["password"], [])], string.Empty, true);
        acme.Names = new NamesReply(true, [new ListedEntry(_stripe, ["password"], [])], string.Empty, true);
        personal.StartApproving();
        acme.StartApproving();
        var client = await harness.StartAsync();

        var result = await CallAsync(client, ToolText.CredentialToolName, Request(_aws));

        Assert.True(result.IsError);
        Assert.Equal(ToolText.InNoVault, TextOf(result));
        Assert.Empty(personal.Received);
        Assert.Empty(acme.Received);
    }

    [Fact]
    public async Task An_item_only_a_locked_vault_could_hold_is_asked_of_it_and_answered_as_locked()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Names = new NamesReply(true, [new ListedEntry(_github, ["password"], [])], string.Empty, true);
        acme.AttachRefusal = AttachReply.Refused(AuditMethod.VaultLocked, "the vault is locked");
        personal.StartApproving();
        acme.Start();
        var client = await harness.StartAsync();

        var result = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe));

        Assert.True(result.IsError);
        Assert.Equal($"Acme: {ToolText.Refusal(AuditMethod.VaultLocked)}", TextOf(result));
        Assert.Empty(personal.Received);
    }

    [Fact]
    public async Task Each_request_reaches_one_owner_so_it_puts_one_prompt_before_the_person()
    {
        await using var harness = McpHarness.OfVaults("Personal", "Acme");
        var personal = harness.OwnerOf("Personal");
        var acme = harness.OwnerOf("Acme");
        personal.Found = Found(_github);
        acme.Found = Found(_stripe);
        personal.Hold = true;
        personal.StartApproving();
        acme.StartApproving();
        var client = await harness.StartAsync();

        await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "e" });
        var waiting = CallAsync(client, ToolText.CredentialToolName, Request(_github));
        await personal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        var other = await CallAsync(client, ToolText.CredentialToolName, Request(_stripe));
        personal.Held.TrySetResult();
        var first = await waiting;

        Assert.False(other.IsError);
        Assert.False(first.IsError);
        Assert.Single(personal.Received);
        Assert.Single(acme.Received);
    }

    [Fact]
    public async Task A_tool_allowed_only_Personal_cannot_reach_Acme_by_name()
    {
        await using var harness = McpHarness.OfVaults("Personal");
        harness.Approver.Found = Found(_github);
        harness.Approver.StartApproving();
        var client = await harness.StartAsync();

        var found = await CallAsync(client, ToolText.FindToolName, new() { ["text"] = "e", ["vault"] = "Acme" });
        var asked = await CallAsync(client, ToolText.CredentialToolName, Request(_github, vault: "Acme"));

        Assert.True(found.IsError);
        Assert.True(asked.IsError);
        Assert.Equal(ToolText.VaultNotServed(["Personal"]), TextOf(asked));
        Assert.Empty(harness.Approver.SearchesReceived);
        Assert.Empty(harness.Approver.Received);
        Assert.All(harness.AuditLines(), line => Assert.Equal("out-of-scope", JsonDocument.Parse(line).RootElement.GetProperty("method").GetString()));
    }

    [Fact]
    public async Task A_bridge_serving_several_vaults_tells_the_agent_so()
    {
        await using var several = McpHarness.OfVaults("Personal", "Acme");
        await using var one = McpHarness.OfVaults("Personal");

        Assert.Contains(ToolText.VaultsInstructions.Trim(), (await several.StartAsync()).ServerInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain(ToolText.VaultsInstructions.Trim(), (await one.StartAsync()).ServerInstructions, StringComparison.Ordinal);
    }
}
