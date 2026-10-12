using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Clients;

/// <summary>How keypaste gets itself registered with one MCP client.</summary>
public enum McpWiring
{
    /// <summary>
    /// The client ships its own command for this, and keypaste calls it.
    /// </summary>
    /// <remarks>
    /// Always preferred where it exists: the tool's own command knows its scopes and its schema, and a running Claude
    /// Code rewrites <c>~/.claude.json</c> continuously, so its own command is the one writer that cannot race it.
    /// </remarks>
    OwnCli,

    /// <summary>
    /// The client has no command that adds and removes a server, so keypaste edits its configuration file.
    /// </summary>
    /// <remarks>
    /// keypaste edits only its own entry, keeps the first original beside the file, and checks every other byte before
    /// and after (D-0435).
    /// </remarks>
    ConfigFile,
}

/// <summary>The operating systems a client runs on.</summary>
[Flags]
public enum McpClientPlatforms
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>Windows.</summary>
    Windows = 1,

    /// <summary>macOS.</summary>
    MacOS = 2,

    /// <summary>Linux.</summary>
    Linux = 4,

    /// <summary>Every platform keypaste runs on.</summary>
    All = Windows | MacOS | Linux,
}

/// <summary>One MCP client keypaste knows how to wire itself into.</summary>
/// <param name="Id">What the user types after <c>--client</c>, and the audit log's client label.</param>
/// <param name="DisplayName">What the user reads.</param>
/// <param name="Wiring">Whether keypaste calls the tool's command or edits its file.</param>
/// <param name="Executable">
/// The client's own command, for <see cref="McpWiring.OwnCli"/>, found as <see cref="McpClientProgram"/> says. This
/// doubles as the detector: if it is not found or its <c>--version</c> cannot be started, the client is not installed.
/// </param>
public sealed record McpClient(
    string Id,
    string DisplayName,
    McpWiring Wiring,
    string? Executable)
{
    /// <summary>The platforms the client runs on.</summary>
    public McpClientPlatforms Platforms { get; init; } = McpClientPlatforms.All;

    /// <summary>
    /// The folders the tool's own installers put <see cref="Executable"/> in, searched after <c>PATH</c> by the macOS
    /// app (<see cref="McpClientMachine.LooksInInstallFolders"/>). A leading <c>~/</c> is the machine's home.
    /// </summary>
    public IReadOnlyList<string> InstallFolders { get; init; } = [];

    /// <summary>The file the tool keeps its MCP servers in, which <see cref="McpClientConfig"/> reads.</summary>
    public McpConfigLocation? Configuration { get; init; }

    /// <summary>What follows <c>mcp add</c> and <c>mcp remove</c> to choose the person's own configuration.</summary>
    public IReadOnlyList<string> Scope { get; init; } = [];

    /// <summary>What else <c>mcp add</c> needs, after <see cref="Scope"/>.</summary>
    public IReadOnlyList<string> AddOptions { get; init; } = [];

    /// <summary>Whether the program goes before <c>--</c> and only its arguments after it, as Gemini CLI reads them.</summary>
    public bool ProgramBeforeSeparator { get; init; }

    /// <summary>For a client configured by file, whether its entry names its transport, <c>"type": "stdio"</c>.</summary>
    public bool TypedEntry { get; init; }

    /// <summary>For a client configured by file, a command of its own whose presence on <c>PATH</c> shows it is installed. It is never started.</summary>
    public string? Launcher { get; init; }

    /// <summary>For a client configured by file, its app bundle, which the macOS app looks for in <c>/Applications</c> and <c>~/Applications</c>.</summary>
    public string? MacApp { get; init; }

    /// <summary>What the client expands in a server's command and arguments on every platform, so a registration holding it would start something else.</summary>
    public IReadOnlyList<string> Expands { get; init; } = [];

    /// <summary>What the client also expands on Windows.</summary>
    public IReadOnlyList<string> ExpandsOnWindows { get; init; } = [];

    /// <summary>Whether the client starts MCP servers only in folders the person has trusted.</summary>
    public bool StartsOnlyInTrustedFolders { get; init; }

    /// <summary>Whether the client reads its configuration only when it starts.</summary>
    public bool ReadsAtLaunch { get; init; }

    /// <summary>Whether the client runs on <paramref name="platform"/>.</summary>
    public bool RunsOn(McpClientPlatform platform) => (Platforms & Flag(platform)) != 0;

    /// <summary>What the client expands on <paramref name="platform"/>.</summary>
    public IReadOnlyList<string> ExpansionMarkers(McpClientPlatform platform) =>
        platform == McpClientPlatform.Windows ? [.. Expands, .. ExpandsOnWindows] : Expands;

    /// <summary>The arguments that register keypaste with this client.</summary>
    /// <remarks>
    /// The server argv follows <c>--</c> so nothing in it can be read as a flag of the client's own command, because a
    /// vault path is user data and may begin with a dash. Gemini CLI takes the program before <c>--</c>, and that is an
    /// absolute path, which never does.
    /// </remarks>
    public IReadOnlyList<string> AddArguments(McpServerRegistration server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var line = server.CommandLine();
        List<string> arguments = ["mcp", "add", .. Scope, .. AddOptions, McpClientCatalog.ServerName];

        if (ProgramBeforeSeparator)
        {
            arguments.AddRange([line[0], "--", .. line.Skip(1)]);
        }
        else
        {
            arguments.AddRange(["--", .. line]);
        }

        return arguments;
    }

    /// <summary>The arguments that remove keypaste from this client, leaving the rest alone.</summary>
    public IReadOnlyList<string> RemoveArguments() => ["mcp", "remove", .. Scope, McpClientCatalog.ServerName];

    internal const string ClaudeCodeId = "claude-code";

    private static McpClientPlatforms Flag(McpClientPlatform platform) => platform switch
    {
        McpClientPlatform.Windows => McpClientPlatforms.Windows,
        McpClientPlatform.MacOS => McpClientPlatforms.MacOS,
        _ => McpClientPlatforms.Linux,
    };
}

/// <summary>How a client starts the bridge: an executable and any arguments that come before ours.</summary>
/// <param name="Path">Absolute path to <c>keypaste</c>, or to the Linux link that starts the AppImage carrying it.</param>
/// <param name="Arguments">What selects the bridge: <see cref="McpServerLocator.BridgeArgument"/>.</param>
public sealed record McpServerCommand(string Path, IReadOnlyList<string> Arguments);

/// <summary>What a registration fixes with the bridge's own flags rather than leaving to the environment it starts in.</summary>
/// <param name="VaultPath">
/// The vault for <c>--vault</c>, or null so the bridge uses the vault the person chose (D-0389).
/// </param>
/// <param name="AuditLogPath">The log for <c>--audit-log</c>, or null for the one under the bridge's own home.</param>
/// <param name="ApproverName">The pipe for <c>--approver</c>, or null for the one the bridge derives.</param>
public sealed record McpServerPins(string? VaultPath, string? AuditLogPath, string? ApproverName)
{
    /// <summary>Nothing pinned: the bridge follows the chosen vault, under the home it resolves.</summary>
    public static McpServerPins None { get; } = new(VaultPath: null, AuditLogPath: null, ApproverName: null);
}

/// <summary>
/// What keypaste asks a client to launch: the bridge, and the flags that bound it.
/// </summary>
/// <param name="Server">How to start <c>keypaste mcp</c>.</param>
/// <param name="VaultPath">The absolute vault the bridge is pinned to with <c>--vault</c>, or null for none.</param>
/// <param name="AuditLogPath">The absolute log the bridge is pinned to with <c>--audit-log</c>, or null for none.</param>
/// <param name="ApproverName">The pipe the bridge is pinned to with <c>--approver</c>, or null for none.</param>
/// <param name="ClientLabel">What this client is called in the audit log.</param>
/// <param name="Expose">
/// Extra globs. Empty means the flag is omitted entirely, so the bridge's own default
/// (<see cref="EntryExposure.DefaultGlob"/>) applies — stated by absence rather than restated here,
/// so there is one place a reader can learn what the default is.
/// </param>
public sealed record McpServerRegistration(
    McpServerCommand Server,
    string? VaultPath,
    string? AuditLogPath,
    string? ApproverName,
    string ClientLabel,
    IReadOnlyList<string> Expose)
{
    /// <summary>The longest label the audit log keeps as written.</summary>
    public const int MaximumLabelLength = 64;

    /// <summary>
    /// Builds a registration the bridge will accept, or says why not before anything is written.
    /// </summary>
    /// <remarks>
    /// The globs go through the bridge's own <see cref="EntryExposure"/> rule and the approver through
    /// <see cref="ApproverEndpoint"/>'s, because a client configured with one the bridge refuses would fail on every
    /// launch, far from the person who typed it. A label must survive the audit log's sanitizer unchanged, so the name a
    /// person chose is the name the log and the prompt show.
    /// </remarks>
    public static bool TryCreate(
        McpServerCommand server,
        McpServerPins pins,
        string clientLabel,
        IReadOnlyList<string> expose,
        [NotNullWhen(true)] out McpServerRegistration? registration,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(clientLabel);
        ArgumentNullException.ThrowIfNull(expose);

        registration = null;

        if (clientLabel.Length == 0 || EntryNameSanitizer.Sanitize(clientLabel, MaximumLabelLength).WasAltered)
        {
            error = $"a label is 1 to {MaximumLabelLength} characters the audit log keeps as written: "
                + "no control, invisible or structural characters";
            return false;
        }

        if (expose.Count > 0 && !EntryExposure.TryCreate(expose, out _, out var globError))
        {
            error = $"exposure: {globError}";
            return false;
        }

        if (pins.ApproverName is { } approver && !ApproverEndpoint.IsValidName(approver, out var approverError))
        {
            error = $"approver: {approverError}";
            return false;
        }

        registration = new McpServerRegistration(
            server,
            Absolute(pins.VaultPath),
            Absolute(pins.AuditLogPath),
            pins.ApproverName,
            clientLabel,
            [.. expose]);
        error = string.Empty;
        return true;
    }

    /// <summary>The full argv a client should launch, executable first.</summary>
    public IReadOnlyList<string> CommandLine()
    {
        List<string> line = [Server.Path, .. Server.Arguments];

        if (VaultPath is not null)
        {
            line.Add("--vault");
            line.Add(VaultPath);
        }

        if (AuditLogPath is not null)
        {
            line.Add("--audit-log");
            line.Add(AuditLogPath);
        }

        if (ApproverName is not null)
        {
            line.Add("--approver");
            line.Add(ApproverName);
        }

        line.Add("--client-label");
        line.Add(ClientLabel);

        foreach (var glob in Expose)
        {
            line.Add("--expose");
            line.Add(glob);
        }

        return line;
    }

    /// <summary>The entry a client configured by file starts the bridge from.</summary>
    /// <param name="typed">Whether the entry names its transport, as VS Code's does.</param>
    public JsonObject FileEntry(bool typed)
    {
        var entry = new JsonObject();

        if (typed)
        {
            entry["type"] = "stdio";
        }

        entry["command"] = Server.Path;
        entry["args"] = new JsonArray([.. CommandLine().Skip(1).Select(argument => (JsonNode?)JsonValue.Create(argument))]);

        return entry;
    }

    private static string? Absolute(string? path) => string.IsNullOrEmpty(path) ? null : System.IO.Path.GetFullPath(path);
}

/// <summary>Every client keypaste knows about, and nothing about this machine.</summary>
/// <remarks>
/// Pure on purpose: it holds the knowledge, and <see cref="McpClientSetup"/> does the probing and
/// the running for both <c>keypaste setup</c> and the desktop's Connect (docs/PRODUCT.md law 4.2).
/// </remarks>
public static class McpClientCatalog
{
    /// <summary>The name keypaste registers itself under, in every client.</summary>
    public const string ServerName = "keypaste";

    /// <summary>
    /// The clients, in the order <c>keypaste setup</c> reports them: those with a command of their own, then those
    /// configured by file.
    /// </summary>
    public static IReadOnlyList<McpClient> All { get; } =
    [
        new(McpClient.ClaudeCodeId, "Claude Code", McpWiring.OwnCli, "claude")
        {
            // The native installer, then the Homebrew cask (code.claude.com/docs/en/setup).
            InstallFolders = ["~/.local/bin", "/opt/homebrew/bin", "/usr/local/bin"],
            Configuration = McpConfigLocation.ClaudeCode,
            Scope = ["--scope", "user"],
            AddOptions = ["--transport", "stdio"],
            Expands = ["${"],
        },
        new("codex", "Codex", McpWiring.OwnCli, "codex")
        {
            // The standalone installer, then the Homebrew cask and npm under Homebrew's or nodejs.org's Node (github.com/openai/codex).
            InstallFolders = ["~/.local/bin", "/opt/homebrew/bin", "/usr/local/bin"],
            Configuration = McpConfigLocation.Codex,
        },
        new("gemini-cli", "Gemini CLI", McpWiring.OwnCli, "gemini")
        {
            // npm under Homebrew's or nodejs.org's Node and Homebrew, then MacPorts (github.com/google-gemini/gemini-cli).
            InstallFolders = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"],
            Configuration = McpConfigLocation.GeminiCli,
            Scope = ["--scope", "user"],
            ProgramBeforeSeparator = true,
            Expands = ["$"],
            ExpandsOnWindows = ["%"],
            StartsOnlyInTrustedFolders = true,
        },
        new("devin", "Devin", McpWiring.OwnCli, "devin")
        {
            // Its install script, then the Homebrew cask devin-cli (docs.devin.ai).
            InstallFolders = ["~/.local/bin", "/opt/homebrew/bin", "/usr/local/bin"],
            Configuration = McpConfigLocation.Devin,
            Scope = ["-s", "user"],
            Expands = ["${"],
        },
        new("vscode", "VS Code", McpWiring.ConfigFile, Executable: null)
        {
            Configuration = McpConfigLocation.VsCode,
            TypedEntry = true,
            Launcher = "code",
            MacApp = "Visual Studio Code.app",
            Expands = ["${"],
        },
        new("cursor", "Cursor", McpWiring.ConfigFile, Executable: null)
        {
            Configuration = McpConfigLocation.Cursor,
            Launcher = "cursor",
            MacApp = "Cursor.app",
            Expands = ["${"],
            ReadsAtLaunch = true,
        },
        new("claude-desktop", "Claude Desktop", McpWiring.ConfigFile, Executable: null)
        {
            Platforms = McpClientPlatforms.Windows | McpClientPlatforms.MacOS,
            Configuration = McpConfigLocation.ClaudeDesktop,
            MacApp = "Claude.app",
            ReadsAtLaunch = true,
        },
    ];

    /// <summary>The clients that run on <paramref name="platform"/>, in the order <see cref="All"/> has them.</summary>
    public static IReadOnlyList<McpClient> For(McpClientPlatform platform) => [.. All.Where(client => client.RunsOn(platform))];

    /// <summary>Finds a client by the id a user typed, or null.</summary>
    public static McpClient? Find(string id)
    {
        foreach (var client in All)
        {
            if (string.Equals(client.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return client;
            }
        }

        return null;
    }
}
