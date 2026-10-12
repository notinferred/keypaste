using System.Text;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// keypaste reads whether a tool's own configuration holds its entry from the file that tool reads, changes nothing in
/// doing so, and says Unknown rather than Absent whenever it cannot be sure (D-0437).
/// </summary>
public sealed class McpClientConfigTests : IDisposable
{
    private static readonly Dictionary<string, McpConfigLocation> _locations = new(StringComparer.Ordinal)
    {
        ["claude-code"] = McpConfigLocation.ClaudeCode,
        ["codex"] = McpConfigLocation.Codex,
        ["gemini-cli"] = McpConfigLocation.GeminiCli,
        ["devin"] = McpConfigLocation.Devin,
        ["vscode"] = McpConfigLocation.VsCode,
        ["cursor"] = McpConfigLocation.Cursor,
        ["claude-desktop"] = McpConfigLocation.ClaudeDesktop,
    };

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-client-config-").FullName;

    public static TheoryData<string, McpClientPlatform, string?> Files => new()
    {
        { "claude-code", McpClientPlatform.Windows, @"C:\Users\person\.claude.json" },
        { "claude-code", McpClientPlatform.MacOS, "/Users/person/.claude.json" },
        { "claude-code", McpClientPlatform.Linux, "/home/person/.claude.json" },
        { "codex", McpClientPlatform.Windows, @"C:\Users\person\.codex\config.toml" },
        { "codex", McpClientPlatform.MacOS, "/Users/person/.codex/config.toml" },
        { "codex", McpClientPlatform.Linux, "/home/person/.codex/config.toml" },
        { "gemini-cli", McpClientPlatform.Windows, @"C:\Users\person\.gemini\settings.json" },
        { "gemini-cli", McpClientPlatform.MacOS, "/Users/person/.gemini/settings.json" },
        { "gemini-cli", McpClientPlatform.Linux, "/home/person/.gemini/settings.json" },
        { "devin", McpClientPlatform.Windows, @"C:\Users\person\AppData\Roaming\devin\mcp_config.json" },
        { "devin", McpClientPlatform.MacOS, "/Users/person/.config/devin/mcp_config.json" },
        { "devin", McpClientPlatform.Linux, "/home/person/.config/devin/mcp_config.json" },
        { "vscode", McpClientPlatform.Windows, @"C:\Users\person\AppData\Roaming\Code\User\mcp.json" },
        { "vscode", McpClientPlatform.MacOS, "/Users/person/Library/Application Support/Code/User/mcp.json" },
        { "vscode", McpClientPlatform.Linux, "/home/person/.config/Code/User/mcp.json" },
        { "cursor", McpClientPlatform.Windows, @"C:\Users\person\.cursor\mcp.json" },
        { "cursor", McpClientPlatform.MacOS, "/Users/person/.cursor/mcp.json" },
        { "cursor", McpClientPlatform.Linux, "/home/person/.cursor/mcp.json" },
        { "claude-desktop", McpClientPlatform.Windows, @"C:\Users\person\AppData\Roaming\Claude\claude_desktop_config.json" },
        { "claude-desktop", McpClientPlatform.MacOS, "/Users/person/Library/Application Support/Claude/claude_desktop_config.json" },
        { "claude-desktop", McpClientPlatform.Linux, null },
    };

    public static TheoryData<string, McpEntryPresence> JsonReadings => new()
    {
        { """{"mcpServers": {"keypaste": {"command": "/opt/keypaste/keypaste"}}}""", McpEntryPresence.Present },
        {
            """
            // the tool's settings
            {
              "theme": "dark", /* kept */
              "mcpServers": {
                "other": { "command": "other" },
                "keypaste": { "command": "/opt/keypaste/keypaste", "args": ["mcp",], },
              },
            }
            """,
            McpEntryPresence.Present
        },
        { """{"mcpServers": {"other": {"command": "other"}}}""", McpEntryPresence.Absent },
        { """{"theme": "dark"}""", McpEntryPresence.Absent },
        { """{"keypaste": {}, "projects": {"/work": {"mcpServers": {"keypaste": {}}}}}""", McpEntryPresence.Absent },
        { string.Empty, McpEntryPresence.Absent },
        { """{"mcpServers": {"keypaste": {}""", McpEntryPresence.Unknown },
        { "[]", McpEntryPresence.Unknown },
        { """{"mcpServers": []}""", McpEntryPresence.Unknown },
        { """{"mcpServers": {"keypaste": {}, "keypaste": {}}}""", McpEntryPresence.Unknown },
        { """{"mcpServers": {}, "mcpServers": {"keypaste": {}}}""", McpEntryPresence.Unknown },
        { """{"mcpServers": {"keypaste": {}}, "theme": {"a": 1, "a": 2}}""", McpEntryPresence.Unknown },
    };

    public static TheoryData<string, McpEntryPresence> CodexReadings => new()
    {
        { "[mcp_servers.keypaste]\ncommand = \"/opt/keypaste/keypaste\"\nargs = [\"mcp\"]\n", McpEntryPresence.Present },
        { "[mcp_servers.\"keypaste\"]\n", McpEntryPresence.Present },
        { "[mcp_servers.'keypaste']\n", McpEntryPresence.Present },
        { "[ mcp_servers.keypaste ]\n", McpEntryPresence.Present },
        { "[\"mcp_servers\" . keypaste] # mine\n", McpEntryPresence.Present },
        { "[mcp_servers.keypaste.env]\nTOKEN = \"abc\"\n", McpEntryPresence.Present },
        { "\uFEFF[mcp_servers.keypaste]\r\ncommand = \"k\"\r\n", McpEntryPresence.Present },
        { "model = \"o3\"\n[features]\nweb_search = true\n[profiles.fast.tools]\nx = 1\n[[skills.config]]\npath = \"s\"\n[mcp_servers.keypaste]\ncommand = \"k\"\n", McpEntryPresence.Present },
        { string.Empty, McpEntryPresence.Absent },
        { "[mcp_servers.other]\ncommand = \"other\"\n", McpEntryPresence.Absent },
        { "[mcp_servers.keypaste-old]\ncommand = \"old\"\n", McpEntryPresence.Absent },
        { "# [mcp_servers.keypaste]\n", McpEntryPresence.Absent },
        { "[profiles.keypaste]\n[mcp_servers_archive.keypaste]\n[[skills.config]]\n", McpEntryPresence.Absent },
        { "mcp_servers.keypaste.command = \"k\"\n", McpEntryPresence.Unknown },
        { "mcp_servers = { keypaste = { command = \"k\" } }\n", McpEntryPresence.Unknown },
        { "[mcp_servers]\nkeypaste = { command = \"k\" }\n", McpEntryPresence.Unknown },
        { "[[mcp_servers]]\nname = \"keypaste\"\n", McpEntryPresence.Unknown },
        { "[mcp_servers.\"keyp\\u0061ste\"]\n", McpEntryPresence.Unknown },
        { "notes = \"\"\"\n[mcp_servers.keypaste]\n\"\"\"\n", McpEntryPresence.Unknown },
        { "[mcp_servers.keypaste]\nnotes = '''\nx\n'''\n", McpEntryPresence.Unknown },
    };

    public static TheoryData<string, string> Movers => new()
    {
        { "claude-code", "CLAUDE_CONFIG_DIR" },
        { "devin", "XDG_CONFIG_HOME" },
        { "vscode", "VSCODE_APPDATA" },
        { "vscode", "VSCODE_PORTABLE" },
    };

    private string Home => Path.Combine(_root, "home");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [MemberData(nameof(Files))]
    public void Each_tools_file_is_the_one_it_reads_on_each_platform(string id, McpClientPlatform platform, string? expected)
    {
        var machine = McpClientMachine.From(name => Described(platform, name), platform);

        Assert.Equal(expected, _locations[id].PathOn(machine));
    }

    [Theory]
    [InlineData(McpClientPlatform.Linux, "/home/person/xdg/", "/home/person/xdg/Code/User/mcp.json")]
    [InlineData(McpClientPlatform.Linux, "", "/home/person/.config/Code/User/mcp.json")]
    [InlineData(McpClientPlatform.Linux, "xdg", null)]
    [InlineData(McpClientPlatform.MacOS, "xdg", "/Users/person/Library/Application Support/Code/User/mcp.json")]
    public void Vs_code_on_linux_reads_its_file_in_xdg_config_home_and_a_relative_one_cannot_be_located(McpClientPlatform platform, string configHome, string? expected)
    {
        var machine = McpClientMachine.From(name => name == "XDG_CONFIG_HOME" ? configHome : Described(platform, name), platform);

        Assert.Equal(expected, McpConfigLocation.VsCode.PathOn(machine));
    }

    [Fact]
    public void A_relative_xdg_config_home_makes_vs_codes_reading_on_linux_unknown()
    {
        var machine = McpClientMachine.From(name => name == "XDG_CONFIG_HOME" ? "cfg" : Described(McpClientPlatform.Linux, name), McpClientPlatform.Linux);

        Assert.Equal(
            new McpEntryReading(McpEntryPresence.Unknown, Path: null, "keypaste cannot tell where VS Code keeps its configuration"),
            McpClientConfig.Read(McpClientCatalog.Find("vscode")!, machine));
    }

    [Theory]
    [InlineData(McpClientPlatform.Linux, "/srv/codex", "/srv/codex/config.toml")]
    [InlineData(McpClientPlatform.MacOS, "/srv/codex/", "/srv/codex/config.toml")]
    [InlineData(McpClientPlatform.Windows, @"D:\codex\", @"D:\codex\config.toml")]
    [InlineData(McpClientPlatform.Linux, "codex", null)]
    [InlineData(McpClientPlatform.Windows, "codex", null)]
    public void Codex_reads_its_file_in_codex_home_and_a_relative_one_cannot_be_located(McpClientPlatform platform, string codexHome, string? expected)
    {
        var machine = McpClientMachine.From(name => name == "CODEX_HOME" ? codexHome : Described(platform, name), platform);

        Assert.Equal(expected, McpConfigLocation.Codex.PathOn(machine));
    }

    [Theory]
    [InlineData(McpClientPlatform.Linux, "/srv/gemini", "/srv/gemini/.gemini/settings.json")]
    [InlineData(McpClientPlatform.MacOS, "/srv/gemini/", "/srv/gemini/.gemini/settings.json")]
    [InlineData(McpClientPlatform.Windows, @"D:\gemini\", @"D:\gemini\.gemini\settings.json")]
    [InlineData(McpClientPlatform.Linux, "gemini", null)]
    [InlineData(McpClientPlatform.Windows, "gemini", null)]
    public void Gemini_reads_its_file_in_gemini_cli_home_and_a_relative_one_cannot_be_located(McpClientPlatform platform, string geminiHome, string? expected)
    {
        var machine = McpClientMachine.From(name => name == "GEMINI_CLI_HOME" ? geminiHome : Described(platform, name), platform);

        Assert.Equal(expected, McpConfigLocation.GeminiCli.PathOn(machine));
    }

    [Fact]
    public void A_machine_that_names_no_home_or_appdata_locates_no_file_there()
    {
        var linux = McpClientMachine.From(_ => null, McpClientPlatform.Linux);
        var windows = McpClientMachine.From(name => name == "USERPROFILE" ? @"C:\Users\person" : null, McpClientPlatform.Windows);

        Assert.All(_locations.Values, location => Assert.Null(location.PathOn(linux)));
        Assert.Null(McpConfigLocation.Devin.PathOn(windows));
        Assert.Null(McpConfigLocation.VsCode.PathOn(windows));
        Assert.Null(McpConfigLocation.ClaudeDesktop.PathOn(windows));
        Assert.Equal(@"C:\Users\person\.cursor\mcp.json", McpConfigLocation.Cursor.PathOn(windows));
    }

    [Fact]
    public void The_packaged_claude_desktops_file_is_read_once_it_exists_and_appdatas_until_then()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the packaged Claude Desktop is Windows'");

        var machine = Machine();
        var roaming = Path.Combine(Home, "AppData", "Roaming", "Claude", "claude_desktop_config.json");
        var packaged = Path.Combine(
            Home, "AppData", "Local", "Packages", McpConfigLocation.ClaudeDesktopPackage, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json");

        Assert.Equal(roaming, McpConfigLocation.ClaudeDesktop.PathOn(machine));

        PlantAt(packaged, "{}");

        Assert.Equal(packaged, McpConfigLocation.ClaudeDesktop.PathOn(machine));
    }

    [Fact]
    public void Every_tool_in_the_catalog_names_the_file_it_keeps_its_servers_in()
    {
        Assert.All(McpClientCatalog.All, client => Assert.NotNull(client.Configuration));
        Assert.Same(McpConfigLocation.ClaudeCode, McpClientCatalog.Find("claude-code")!.Configuration);
        Assert.Same(McpConfigLocation.Codex, McpClientCatalog.Find("codex")!.Configuration);
    }

    [Theory]
    [MemberData(nameof(JsonReadings))]
    public void Each_json_tool_reads_as_its_servers_object_says(string text, McpEntryPresence expected)
    {
        var machine = Machine();
        var read = 0;

        foreach (var (id, location) in _locations.Where(pair => pair.Value.Format == McpConfigFormat.Json))
        {
            if (location.PathOn(machine) is not { } path)
            {
                continue;
            }

            PlantAt(path, text.Replace("\"mcpServers\"", $"\"{location.ServersKey}\"", StringComparison.Ordinal));

            var reading = McpClientConfig.Read(Tool(id, location), machine);

            Assert.Equal(expected, reading.Presence);
            Assert.Equal(path, reading.Path);
            Assert.Equal(expected == McpEntryPresence.Unknown, reading.Reason is not null);
            read++;
        }

        Assert.True(read >= 5, $"only {read} tools were read");
    }

    [Fact]
    public void A_missing_file_reads_as_absent_at_the_path_the_tool_reads()
    {
        var reading = McpClientConfig.Read(McpClientCatalog.Find("claude-code")!, Machine());

        Assert.Equal(new McpEntryReading(McpEntryPresence.Absent, Path.Combine(Home, ".claude.json"), Reason: null), reading);
    }

    [Fact]
    public void An_unknown_reading_names_the_file_and_never_quotes_it()
    {
        var path = Path.Combine(Home, ".claude.json");
        PlantAt(path, """{"oauthAccount": {"token": "sign-in-secret"}, "mcpServers": {""");

        var reading = McpClientConfig.Read(McpClientCatalog.Find("claude-code")!, Machine());

        Assert.Equal(McpEntryPresence.Unknown, reading.Presence);
        Assert.StartsWith(path + " is not valid JSON", reading.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("sign-in-secret", reading.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CodexReadings))]
    public void Codex_reads_present_only_from_the_table_headers_codex_writes(string text, McpEntryPresence expected)
    {
        Assert.Equal(expected, CodexConfigScan.Scan(Encoding.UTF8.GetBytes(text), "mcp_servers", out var problem));
        Assert.Equal(expected == McpEntryPresence.Unknown, problem is not null);
    }

    [Fact]
    public void Codex_is_read_in_codex_home_when_it_is_set_and_in_the_home_otherwise()
    {
        var codexHome = Path.Combine(_root, "codex-home");
        var moved = PlantAt(Path.Combine(codexHome, "config.toml"), "[mcp_servers.keypaste]\ncommand = \"k\"\n");
        var usual = PlantAt(Path.Combine(Home, ".codex", "config.toml"), "[mcp_servers.other]\ncommand = \"o\"\n");
        var codex = McpClientCatalog.Find("codex")!;

        Assert.Equal(new McpEntryReading(McpEntryPresence.Present, moved, Reason: null), McpClientConfig.Read(codex, Machine(("CODEX_HOME", codexHome))));
        Assert.Equal(new McpEntryReading(McpEntryPresence.Absent, usual, Reason: null), McpClientConfig.Read(codex, Machine()));
    }

    [Fact]
    public void Gemini_is_read_in_gemini_cli_home_when_it_is_set_and_in_the_home_otherwise()
    {
        var geminiHome = Path.Combine(_root, "gemini-home");
        var moved = PlantAt(Path.Combine(geminiHome, ".gemini", "settings.json"), """{"mcpServers": {"keypaste": {"command": "k"}}}""");
        var usual = PlantAt(Path.Combine(Home, ".gemini", "settings.json"), """{"mcpServers": {"other": {"command": "o"}}}""");
        var gemini = Tool("gemini-cli", McpConfigLocation.GeminiCli);

        Assert.Equal(new McpEntryReading(McpEntryPresence.Present, moved, Reason: null), McpClientConfig.Read(gemini, Machine(("GEMINI_CLI_HOME", geminiHome))));
        Assert.Equal(new McpEntryReading(McpEntryPresence.Absent, usual, Reason: null), McpClientConfig.Read(gemini, Machine()));
    }

    [Fact]
    public void A_relative_gemini_cli_home_reads_as_unknown()
    {
        PlantAt(Path.Combine(Home, ".gemini", "settings.json"), "{}");
        var gemini = Tool("gemini-cli", McpConfigLocation.GeminiCli);

        var reading = McpClientConfig.Read(gemini, Machine(("GEMINI_CLI_HOME", "gemini-home")));

        Assert.Equal(new McpEntryReading(McpEntryPresence.Unknown, Path: null, $"keypaste cannot tell where {gemini.DisplayName} keeps its configuration"), reading);
    }

    [Fact]
    public void A_relative_codex_home_reads_as_unknown()
    {
        PlantAt(Path.Combine(Home, ".codex", "config.toml"), string.Empty);

        var reading = McpClientConfig.Read(McpClientCatalog.Find("codex")!, Machine(("CODEX_HOME", "codex-home")));

        Assert.Equal(new McpEntryReading(McpEntryPresence.Unknown, Path: null, "keypaste cannot tell where Codex keeps its configuration"), reading);
    }

    [Theory]
    [MemberData(nameof(Movers))]
    public void A_variable_the_tool_documents_as_moving_its_file_makes_the_reading_unknown(string id, string variable)
    {
        var location = _locations[id];
        var tool = Tool(id, location);
        PlantAt(location.PathOn(Machine())!, """{"mcpServers": {"other": {}}}""");

        Assert.Equal(McpEntryPresence.Absent, McpClientConfig.Read(tool, Machine()).Presence);

        var reading = McpClientConfig.Read(tool, Machine((variable, Path.Combine(_root, "elsewhere"))));

        Assert.Equal(McpEntryPresence.Unknown, reading.Presence);
        Assert.Null(reading.Path);
        Assert.Equal($"{variable} is set, so {tool.DisplayName} may keep its configuration where keypaste does not look", reading.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void Xdg_config_home_naming_the_homes_own_config_folder_leaves_devins_file_readable(string trailing)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Devin reads %APPDATA% on Windows, where XDG_CONFIG_HOME names no default of its");

        var devin = McpClientCatalog.Find("devin")!;
        var path = PlantAt(McpConfigLocation.Devin.PathOn(Machine())!, """{"mcpServers": {"keypaste": {"command": "k"}}}""");

        var reading = McpClientConfig.Read(devin, Machine(("XDG_CONFIG_HOME", Path.Combine(Home, ".config") + trailing)));

        Assert.Equal(new McpEntryReading(McpEntryPresence.Present, path, Reason: null), reading);
    }

    [Theory]
    [InlineData(McpClientPlatform.Linux, "/home/person/.config/devin")]
    [InlineData(McpClientPlatform.Linux, "/home/person")]
    [InlineData(McpClientPlatform.Linux, "/home/person/.config/..")]
    [InlineData(McpClientPlatform.Linux, "/home/other/.config")]
    [InlineData(McpClientPlatform.Linux, ".config")]
    [InlineData(McpClientPlatform.MacOS, "/Users/person/Library/Application Support")]
    [InlineData(McpClientPlatform.Windows, @"C:\Users\person\.config")]
    public void Any_other_xdg_config_home_makes_devins_reading_unknown(McpClientPlatform platform, string configHome)
    {
        var machine = McpClientMachine.From(name => name == "XDG_CONFIG_HOME" ? configHome : Described(platform, name), platform);

        Assert.Equal(
            new McpEntryReading(McpEntryPresence.Unknown, Path: null, "XDG_CONFIG_HOME is set, so Devin may keep its configuration where keypaste does not look"),
            McpClientConfig.Read(McpClientCatalog.Find("devin")!, machine));
    }

    [Fact]
    public void Xdg_config_home_on_a_machine_that_names_no_home_makes_devins_reading_unknown()
    {
        var machine = McpClientMachine.From(name => name == "XDG_CONFIG_HOME" ? "/home/person/.config" : null, McpClientPlatform.Linux);

        Assert.Equal(McpEntryPresence.Unknown, McpClientConfig.Read(McpClientCatalog.Find("devin")!, machine).Presence);
        Assert.Equal("XDG_CONFIG_HOME", McpConfigLocation.Devin.MovedOn(machine));
    }

    [Fact]
    public void A_tool_whose_file_cannot_be_located_reads_as_unknown()
    {
        var claude = McpClientCatalog.Find("claude-code")!;
        var homeless = McpClientMachine.From(_ => null, McpClientMachine.CurrentPlatform);

        Assert.Equal(
            new McpEntryReading(McpEntryPresence.Unknown, Path: null, "keypaste cannot tell where Claude Code keeps its configuration"),
            McpClientConfig.Read(claude, homeless));
        Assert.Equal(McpEntryPresence.Unknown, McpClientConfig.Read(claude with { Configuration = null }, Machine()).Presence);
    }

    [Fact]
    public void A_file_larger_than_64_mib_reads_as_unknown()
    {
        var path = Path.Combine(Home, ".claude.json");
        Directory.CreateDirectory(Home);

        using (var file = new FileStream(path, FileMode.CreateNew))
        {
            file.SetLength(McpConfigFile.MaximumBytes + 1L);
        }

        var reading = McpClientConfig.Read(McpClientCatalog.Find("claude-code")!, Machine());

        Assert.Equal(McpEntryPresence.Unknown, reading.Presence);
        Assert.Equal($"could not read {path}: it is larger than 64 MiB", reading.Reason);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_reads_as_unknown()
    {
        var path = Path.Combine(Home, ".claude.json");
        Directory.CreateDirectory(path);

        var reading = McpClientConfig.Read(McpClientCatalog.Find("claude-code")!, Machine());

        Assert.Equal(McpEntryPresence.Unknown, reading.Presence);
        Assert.StartsWith($"could not read {path}: ", reading.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude-code", """{"mcpServers": {"keypaste": {"command": "k"}}, "oauthAccount": {}}""")]
    [InlineData("codex", "[mcp_servers.keypaste]\ncommand = \"k\"\n")]
    public void A_reading_leaves_the_file_and_its_folder_as_they_were(string id, string text)
    {
        var location = _locations[id];
        var path = PlantAt(location.PathOn(Machine())!, text);
        var written = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        var folder = Path.GetDirectoryName(path)!;
        var before = Directory.GetFileSystemEntries(folder);

        Assert.Equal(McpEntryPresence.Present, McpClientConfig.Read(Tool(id, location), Machine()).Presence);

        Assert.Equal(Encoding.UTF8.GetBytes(text), File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        Assert.Equal(before, Directory.GetFileSystemEntries(folder));
        Assert.False(File.Exists(McpConfigFile.BackupPath(path)));
    }

    private static string? Described(McpClientPlatform platform, string name) => (platform, name) switch
    {
        (McpClientPlatform.Windows, "USERPROFILE") => @"C:\Users\person",
        (McpClientPlatform.Windows, "APPDATA") => @"C:\Users\person\AppData\Roaming",
        (McpClientPlatform.Windows, "LOCALAPPDATA") => @"C:\Users\person\AppData\Local",
        (McpClientPlatform.Windows, "HOME") => "/c/Users/git-bash",
        (McpClientPlatform.MacOS, "HOME") => "/Users/person",
        (McpClientPlatform.Linux, "HOME") => "/home/person",
        _ => null,
    };

    private static McpClient Tool(string id, McpConfigLocation location) =>
        McpClientCatalog.Find(id) ?? new McpClient(id, id, McpWiring.OwnCli, Executable: null) { Configuration = location };

    private static string PlantAt(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private McpClientMachine Machine(params (string Name, string Value)[] set)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = Home,
            ["USERPROFILE"] = Home,
            ["APPDATA"] = Path.Combine(Home, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(Home, "AppData", "Local"),
        };

        foreach (var (name, value) in set)
        {
            variables[name] = value;
        }

        return McpClientMachine.From(name => variables.GetValueOrDefault(name), McpClientMachine.CurrentPlatform);
    }
}
