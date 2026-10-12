namespace Keypaste.Core.Clients;

/// <summary>How a tool's configuration file is written.</summary>
public enum McpConfigFormat
{
    /// <summary>JSON, comments and trailing commas allowed, with the servers in one member of the top-level object.</summary>
    Json,

    /// <summary>Codex's TOML, with each server in a table named under the servers key.</summary>
    Toml,
}

/// <summary>The file a tool keeps its MCP servers in.</summary>
/// <param name="Format">How the file is written.</param>
/// <param name="ServersKey">The member or table that holds the servers.</param>
/// <param name="Candidates">
/// The files the tool reads on a machine, in its order: it reads the first that exists, else the last. Empty when the
/// machine does not say where, such as when it names no home.
/// </param>
public sealed record McpConfigLocation(
    McpConfigFormat Format,
    string ServersKey,
    Func<McpClientMachine, IReadOnlyList<string>> Candidates)
{
    /// <summary>The package family of the packaged (MSIX) Claude Desktop on Windows.</summary>
    public const string ClaudeDesktopPackage = "Claude_pzs8sxrjxfjjc";

    private const string _mcpServers = "mcpServers";

    private const string _claudeDesktopFile = "claude_desktop_config.json";

    /// <summary>
    /// The variables the tool documents as moving its file somewhere keypaste does not follow, so a reading taken while one
    /// is set (<see cref="MovedOn"/>) cannot tell whether the tool holds keypaste.
    /// </summary>
    public IReadOnlyList<string> MovedBy { get; init; } = [];

    /// <summary>Claude Code's user scope: the top-level <c>mcpServers</c> of <c>~/.claude.json</c>, which also holds its sign-in.</summary>
    public static McpConfigLocation ClaudeCode { get; } =
        new(McpConfigFormat.Json, _mcpServers, machine => Under(machine, machine.Home, ".claude.json")) { MovedBy = ["CLAUDE_CONFIG_DIR"] };

    /// <summary>Codex's <c>config.toml</c> in <c>CODEX_HOME</c>, else in <c>~/.codex</c>.</summary>
    public static McpConfigLocation Codex { get; } = new(McpConfigFormat.Toml, "mcp_servers", CodexFile);

    /// <summary>Gemini CLI's user settings, <c>.gemini/settings.json</c> in <c>GEMINI_CLI_HOME</c>, else in the home.</summary>
    public static McpConfigLocation GeminiCli { get; } = new(McpConfigFormat.Json, _mcpServers, GeminiFile);

    /// <summary>Devin's user scope: <c>~/.config/devin/mcp_config.json</c>, or <c>%APPDATA%\devin\mcp_config.json</c> on Windows (docs.devin.ai).</summary>
    public static McpConfigLocation Devin { get; } = new(McpConfigFormat.Json, _mcpServers, DevinFile) { MovedBy = ["XDG_CONFIG_HOME"] };

    /// <summary>VS Code's user <c>mcp.json</c>, whose servers are under <c>servers</c>.</summary>
    public static McpConfigLocation VsCode { get; } =
        new(McpConfigFormat.Json, "servers", VsCodeFile) { MovedBy = ["VSCODE_APPDATA", "VSCODE_PORTABLE"] };

    /// <summary>Cursor's <c>~/.cursor/mcp.json</c>, on every platform.</summary>
    public static McpConfigLocation Cursor { get; } =
        new(McpConfigFormat.Json, _mcpServers, machine => Under(machine, machine.Home, ".cursor", "mcp.json"));

    /// <summary>Claude Desktop's <c>claude_desktop_config.json</c>, on macOS and Windows.</summary>
    public static McpConfigLocation ClaudeDesktop { get; } = new(McpConfigFormat.Json, _mcpServers, ClaudeDesktopFiles) { Packages = ClaudeDesktopPackages };

    /// <summary>The folders a packaged tool is installed in, whose presence shows it is installed before its file exists.</summary>
    public Func<McpClientMachine, IReadOnlyList<string>> Packages { get; init; } = _ => [];

    /// <summary>The folders whose presence shows the tool is installed on <paramref name="machine"/>: those its file would be in, and its package's.</summary>
    /// <param name="machine">Where the tool runs.</param>
    /// <returns>The folders, which need not exist.</returns>
    public IReadOnlyList<string> Folders(McpClientMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        char[] separators = machine.Platform == McpClientPlatform.Windows ? ['\\', '/'] : ['/'];

        return [.. Candidates(machine).Select(path => path[..path.LastIndexOfAny(separators)]), .. Packages(machine)];
    }

    /// <summary>The variable among <see cref="MovedBy"/> that <paramref name="machine"/> sets to move the tool's file, if any.</summary>
    /// <param name="machine">Where the tool runs.</param>
    /// <returns>The first such variable, or null when none is set or each set one names the folder the tool reads without it.</returns>
    public string? MovedOn(McpClientMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return MovedBy.FirstOrDefault(variable => machine.Variable(variable) is { Length: > 0 } value && !NamesItsDefault(machine, variable, value));
    }

    /// <summary>The file the tool reads on <paramref name="machine"/>.</summary>
    /// <param name="machine">Where the tool runs.</param>
    /// <returns>The first candidate that exists, else the last, or null when the machine does not say where.</returns>
    public string? PathOn(McpClientMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var candidates = Candidates(machine);

        return candidates.Count == 0 ? null : candidates.FirstOrDefault(File.Exists) ?? candidates[^1];
    }

    private static IReadOnlyList<string> CodexFile(McpClientMachine machine) =>
        machine.Variable("CODEX_HOME") is { Length: > 0 } codexHome
            ? UnderSet(machine, codexHome, "config.toml")
            : Under(machine, machine.Home, ".codex", "config.toml");

    private static IReadOnlyList<string> GeminiFile(McpClientMachine machine) =>
        machine.Variable("GEMINI_CLI_HOME") is { Length: > 0 } geminiHome
            ? UnderSet(machine, geminiHome, ".gemini", "settings.json")
            : Under(machine, machine.Home, ".gemini", "settings.json");

    private static IReadOnlyList<string> DevinFile(McpClientMachine machine) =>
        machine.Platform == McpClientPlatform.Windows
            ? Under(machine, machine.AppData, "devin", "mcp_config.json")
            : Under(machine, machine.Home, ".config", "devin", "mcp_config.json");

    private static IReadOnlyList<string> VsCodeFile(McpClientMachine machine) => machine.Platform switch
    {
        McpClientPlatform.Windows => Under(machine, machine.AppData, "Code", "User", "mcp.json"),
        McpClientPlatform.MacOS => Under(machine, machine.Home, "Library", "Application Support", "Code", "User", "mcp.json"),
        _ => machine.Variable("XDG_CONFIG_HOME") is { Length: > 0 } configHome
            ? UnderSet(machine, configHome, "Code", "User", "mcp.json")
            : Under(machine, machine.Home, ".config", "Code", "User", "mcp.json"),
    };

    // The packaged app reads its private copy of an AppData file once it has one, and the real file until then
    // (anthropics/claude-code#26073).
    private static IReadOnlyList<string> ClaudeDesktopFiles(McpClientMachine machine) => machine.Platform switch
    {
        McpClientPlatform.MacOS => Under(machine, machine.Home, "Library", "Application Support", "Claude", _claudeDesktopFile),
        McpClientPlatform.Windows when machine.AppData is not null =>
            [.. Under(machine, machine.LocalAppData, "Packages", ClaudeDesktopPackage, "LocalCache", "Roaming", "Claude", _claudeDesktopFile), .. Under(machine, machine.AppData, "Claude", _claudeDesktopFile)],
        _ => [],
    };

    // The tool reads a relative variable against the directory it starts in, which a reading cannot know.
    private static IReadOnlyList<string> UnderSet(McpClientMachine machine, string root, params ReadOnlySpan<string> parts) =>
        McpClientMachine.IsAbsolute(root, machine.Platform) ? Under(machine, root, parts) : [];

    // XDG_CONFIG_HOME set to $HOME/.config, where Devin reads outside Windows when it is unset, moves nothing; on Windows
    // Devin reads %APPDATA% instead, so no value of it is known to be harmless there.
    private static bool NamesItsDefault(McpClientMachine machine, string variable, string value) =>
        variable == "XDG_CONFIG_HOME"
        && machine.Platform != McpClientPlatform.Windows
        && Under(machine, machine.Home, ".config") is [var standard]
        && string.Equals(value.TrimEnd('/'), standard, StringComparison.Ordinal);

    private static IReadOnlyList<string> ClaudeDesktopPackages(McpClientMachine machine) =>
        machine.Platform == McpClientPlatform.Windows ? Under(machine, machine.LocalAppData, "Packages", ClaudeDesktopPackage) : [];

    // Joined by the machine's own separator, so a Windows machine can be described on Linux and the other way round.
    private static IReadOnlyList<string> Under(McpClientMachine machine, string? root, params ReadOnlySpan<string> parts)
    {
        if (root is null)
        {
            return [];
        }

        var windows = machine.Platform == McpClientPlatform.Windows;
        string[] segments = [windows ? root.TrimEnd('\\', '/') : root.TrimEnd('/'), .. parts];

        return [string.Join(windows ? '\\' : '/', segments)];
    }
}
