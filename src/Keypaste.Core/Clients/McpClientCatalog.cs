namespace Keypaste.Core.Clients;

/// <summary>How keypaste gets itself registered with one MCP client.</summary>
public enum McpWiring
{
    /// <summary>
    /// The client ships its own command for this, and keypaste calls it.
    /// </summary>
    /// <remarks>
    /// Always preferred where it exists. The alternative is editing the client's configuration
    /// file, and those files hold things that are not ours: a running Claude Code rewrites
    /// <c>~/.claude.json</c> continuously, and it carries the user's login state, so a
    /// read-modify-write from another process can lose data it never meant to touch. Calling the
    /// vendor's own command removes the merge, the clobber and the race together, and means
    /// keypaste does not have to track a schema it does not own.
    /// </remarks>
    OwnCli,

    /// <summary>
    /// The client has no such command, so wiring means writing its configuration file.
    /// </summary>
    /// <remarks>
    /// keypaste prints the block and the path instead of writing it. Silently creating a file no
    /// client reads — and reporting success — is a worse failure than asking for a paste, and the
    /// schemas here are not ones this project has verified against a real install.
    /// </remarks>
    ConfigFile,
}

/// <summary>One MCP client keypaste knows how to wire itself into.</summary>
/// <param name="Id">What the user types after <c>--client</c>, and the audit log's client label.</param>
/// <param name="DisplayName">What the user reads.</param>
/// <param name="Wiring">Whether keypaste calls a command or prints a block.</param>
/// <param name="Executable">
/// The client's own command, for <see cref="McpWiring.OwnCli"/>. This doubles as the detector:
/// if it cannot be launched, the client is not installed.
/// </param>
public sealed record McpClient(
    string Id,
    string DisplayName,
    McpWiring Wiring,
    string? Executable)
{
    /// <summary>The arguments that register keypaste with this client.</summary>
    /// <remarks>
    /// The server argv is appended after <c>--</c> so nothing in it can be read as a flag of the
    /// client's own command. That matters because a vault path is user data and may begin with a
    /// dash.
    /// </remarks>
    public IReadOnlyList<string> AddArguments(McpServerRegistration server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var arguments = new List<string> { "mcp", "add" };

        // Claude Code needs to be told the scope and the transport; Codex has neither concept and
        // rejects the flags. This is the whole of the difference between the two.
        if (Id == ClaudeCodeId)
        {
            arguments.AddRange(["--scope", "user", "--transport", "stdio"]);
        }

        arguments.Add(McpClientCatalog.ServerName);
        arguments.Add("--");
        arguments.AddRange(server.CommandLine());

        return arguments;
    }

    /// <summary>The arguments that remove keypaste from this client, leaving the rest alone.</summary>
    public IReadOnlyList<string> RemoveArguments()
    {
        return Id == ClaudeCodeId
            ? ["mcp", "remove", "--scope", "user", McpClientCatalog.ServerName]
            : ["mcp", "remove", McpClientCatalog.ServerName];
    }

    internal const string ClaudeCodeId = "claude-code";
}

/// <summary>
/// What keypaste asks a client to launch: the bridge, and the flags that bound it.
/// </summary>
/// <param name="ServerPath">Absolute path to <c>keypaste-mcp</c>.</param>
/// <param name="VaultPath">Absolute path to the vault. Absolute always — a client's working directory is not ours.</param>
/// <param name="ClientLabel">What this client is called in the audit log.</param>
/// <param name="Expose">
/// Extra globs. Empty means the flag is omitted entirely, so <c>keypaste-mcp</c>'s own default of
/// <c>env/**</c> applies — stated by absence rather than restated here, so there is one place a
/// reader can learn what the default is.
/// </param>
public sealed record McpServerRegistration(
    string ServerPath,
    string VaultPath,
    string ClientLabel,
    IReadOnlyList<string> Expose)
{
    /// <summary>The full argv a client should launch, executable first.</summary>
    public IReadOnlyList<string> CommandLine()
    {
        var line = new List<string>
        {
            ServerPath,
            "--vault",
            VaultPath,
            "--client-label",
            ClientLabel,
        };

        foreach (var glob in Expose)
        {
            line.Add("--expose");
            line.Add(glob);
        }

        return line;
    }
}

/// <summary>Every client keypaste knows about, and nothing about this machine.</summary>
/// <remarks>
/// Pure on purpose: it holds the knowledge, the CLI does the probing and the running. That split
/// is docs/PRODUCT.md law 4.2, and it is what lets the desktop app's future "Connect to…" button
/// (step 4.3) reuse this without a second copy of the argument grammar.
/// </remarks>
public static class McpClientCatalog
{
    /// <summary>The name keypaste registers itself under, in every client.</summary>
    public const string ServerName = "keypaste";

    /// <summary>
    /// The clients, in the order <c>keypaste setup</c> reports them.
    /// </summary>
    /// <remarks>
    /// Claude Code and Codex are wired by their own commands, both verified against real installs.
    /// Cursor and Claude Desktop have no such command and their file schemas are not verified here,
    /// so they are reported and explained rather than written.
    /// </remarks>
    public static IReadOnlyList<McpClient> All { get; } =
    [
        new(McpClient.ClaudeCodeId, "Claude Code", McpWiring.OwnCli, "claude"),
        new("codex", "Codex", McpWiring.OwnCli, "codex"),
        new("cursor", "Cursor", McpWiring.ConfigFile, Executable: null),
        new("claude-desktop", "Claude Desktop", McpWiring.ConfigFile, Executable: null),
    ];

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
