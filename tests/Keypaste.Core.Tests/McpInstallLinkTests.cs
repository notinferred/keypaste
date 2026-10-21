using System.Text;
using System.Text.Json.Nodes;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The links that have Cursor and VS Code add keypaste themselves: each decodes to exactly the entry <c>keypaste setup</c>
/// writes to that tool's file, whatever the paths hold.
/// </summary>
public sealed class McpInstallLinkTests
{
    private const string _vault = "/home/person/vaults/team.kdbx";

    public static TheoryData<string, string> Paths => new()
    {
        { "/opt/keypaste/keypaste", _vault },
        { "/Users/Zoë Smith/Applications/keypaste & co/keypaste", "/Users/Zoë Smith/vaults/a+b #1 100% \"team\".kdbx" },
        { "/home/佐藤/bin/keypaste", "/home/佐藤/保管庫/team=?.kdbx" },
    };

    private static McpClient Cursor => McpClientCatalog.Find("cursor")!;

    private static McpClient VsCode => McpClientCatalog.Find("vscode")!;

    private static McpServerRegistration Registration(string server, string? vault, string label)
    {
        var command = new McpServerCommand(server, [McpServerLocator.BridgeArgument]);
        Assert.True(McpServerRegistration.TryCreate(command, new McpServerPins(vault, null, null), label, [], out var registration, out var error), error);
        return registration;
    }

    private static string Link(McpClient client, McpServerRegistration registration)
    {
        Assert.True(McpInstallLink.TryCreate(client, McpClientPlatform.Linux, registration, out var link, out var error), error);
        return link;
    }

    private static JsonObject CursorConfig(string link)
    {
        Assert.StartsWith(McpInstallLink.CursorPrefix, link, StringComparison.Ordinal);
        var parameters = link[McpInstallLink.CursorPrefix.Length..].Split('&');

        Assert.Equal(["name", "config"], parameters.Select(parameter => parameter[..parameter.IndexOf('=', StringComparison.Ordinal)]));
        Assert.Equal(McpClientCatalog.ServerName, Uri.UnescapeDataString(parameters[0]["name=".Length..]));

        var config = Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(parameters[1]["config=".Length..])));
        return Assert.IsType<JsonObject>(JsonNode.Parse(config));
    }

    private static JsonObject VsCodeConfig(string link)
    {
        Assert.StartsWith(McpInstallLink.VsCodePrefix, link, StringComparison.Ordinal);
        return Assert.IsType<JsonObject>(JsonNode.Parse(Uri.UnescapeDataString(link[McpInstallLink.VsCodePrefix.Length..])));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Cursors_link_decodes_to_the_entry_setup_writes_to_its_file(string server, string vault)
    {
        var registration = Registration(server, vault, "cursor");

        var config = CursorConfig(Link(Cursor, registration));

        Assert.True(JsonNode.DeepEquals(registration.FileEntry(typed: false), config), config.ToJsonString());
        Assert.Equal(["command", "args"], config.Select(member => member.Key));

        string[] started = [(string)config["command"]!, .. config["args"]!.AsArray().Select(argument => (string)argument!)];
        Assert.Equal(registration.CommandLine().ToArray(), started);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void VS_Codes_link_decodes_to_the_typed_entry_setup_writes_with_its_name(string server, string vault)
    {
        var registration = Registration(server, vault, "vscode");

        var config = VsCodeConfig(Link(VsCode, registration));

        Assert.Equal(["name", "type", "command", "args"], config.Select(member => member.Key));
        Assert.Equal(McpClientCatalog.ServerName, (string?)config["name"]);
        config.Remove("name");
        Assert.True(JsonNode.DeepEquals(registration.FileEntry(typed: true), config), config.ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void A_link_escapes_every_path_into_one_address_that_parses_as_written(string server, string vault)
    {
        foreach (var client in new[] { Cursor, VsCode })
        {
            var link = Link(client, Registration(server, vault, client.Id));

            Assert.All(link, character => Assert.True(char.IsAscii(character) && !char.IsWhiteSpace(character) && character is not ('"' or '#'), link));
            Assert.Equal(client.InstallLink == McpInstallLinkFormat.Cursor ? 1 : 0, link.Count(character => character == '&'));
            Assert.Equal(link, new Uri(link).AbsoluteUri);
        }
    }

    /// <summary>Without a vault pinned the tool follows the chosen vault, and the link says no more than the file would.</summary>
    [Fact]
    public void A_link_without_a_pinned_vault_names_none()
    {
        var registration = Registration("/opt/keypaste/keypaste", vault: null, "cursor");

        var config = CursorConfig(Link(Cursor, registration));

        Assert.DoesNotContain("--vault", config.ToJsonString(), StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(registration.FileEntry(typed: false), config));
    }

    [Fact]
    public void A_registration_the_tool_would_rewrite_gets_no_link_for_the_reason_it_gets_no_plan()
    {
        var registration = Registration("/opt/keypaste/keypaste", "/home/person/${HOME}/team.kdbx", "cursor");
        var detection = new McpClientDetection(McpClientPlatform.Linux, Found: true, Program: null, ConfigPath: "/home/person/.cursor/mcp.json", Problem: null);

        Assert.False(McpInstallLink.TryCreate(Cursor, McpClientPlatform.Linux, registration, out var link, out var error));
        Assert.False(McpClientSetup.TryConnect(Cursor, detection, registration, link: null, out _, out var refusal));

        Assert.Null(link);
        Assert.Equal(refusal, error);
    }

    [Fact]
    public void Only_cursor_and_vs_code_take_a_link()
    {
        Assert.Equal(["vscode", "cursor"], McpClientCatalog.All.Where(client => client.InstallLink != McpInstallLinkFormat.None).Select(client => client.Id));

        var codex = McpClientCatalog.Find("codex")!;
        Assert.False(McpInstallLink.TryCreate(codex, McpClientPlatform.Linux, Registration("/opt/keypaste/keypaste", _vault, "codex"), out _, out var error));
        Assert.Equal("Codex takes no install link", error);
    }
}
