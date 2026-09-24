using Keypaste.Core.Processes;

namespace Keypaste.Core.Clients;

/// <summary>One command a client's own program is asked to run.</summary>
/// <param name="Executable">The client's command, such as <c>claude</c>.</param>
/// <param name="Arguments">Its arguments, passed without a shell.</param>
/// <param name="MayFail">
/// Whether a refusal is expected and ignored: the removal that clears an earlier entry before an
/// add, which finds nothing to remove on a first run.
/// </param>
public sealed record McpClientCommand(string Executable, IReadOnlyList<string> Arguments, bool MayFail)
{
    /// <summary>The command as a person reads it, each argument that needs it quoted.</summary>
    public string Display => string.Join(' ', new[] { Executable }.Concat(Arguments).Select(Quote));

    private static string Quote(string value) =>
        value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')
            ? value
            : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Whether a plan connects keypaste to a client or takes it out again.</summary>
public enum McpSetupAction
{
    /// <summary>Registers the bridge with the client.</summary>
    Connect,

    /// <summary>Removes keypaste from the client, leaving its other servers alone.</summary>
    Remove,
}

/// <summary>
/// Exactly what connecting or removing one client will do, composed before anything runs.
/// </summary>
/// <remarks>
/// The same object is shown and then run, so what a person confirmed and what happened cannot
/// differ. A client configured by file has no commands: its <see cref="PasteBlock"/> is shown, and
/// nothing is written or claimed.
/// </remarks>
/// <param name="Client">The client this plan is for.</param>
/// <param name="Action">Connect or remove.</param>
/// <param name="Commands">The client's own commands, in the order they run.</param>
/// <param name="PasteBlock">For a client configured by file when connecting, the block to paste.</param>
public sealed record McpSetupPlan(
    McpClient Client,
    McpSetupAction Action,
    IReadOnlyList<McpClientCommand> Commands,
    IReadOnlyList<string>? PasteBlock)
{
    /// <summary>Whether running this plan changes anything, or only shows what to do by hand.</summary>
    public bool RunsCommands => Commands.Count > 0;

    /// <summary>The plan as the person reads it before confirming: one line per command, or the block.</summary>
    public string Display => RunsCommands
        ? string.Join('\n', Commands.Select(command => command.Display))
        : string.Join('\n', PasteBlock ?? []);
}

/// <summary>What running a plan did.</summary>
public enum McpSetupStatus
{
    /// <summary>The client's own command ran and succeeded.</summary>
    Done,

    /// <summary>The client ran and said no; <see cref="McpSetupResult.ClientSaid"/> has its words.</summary>
    Refused,

    /// <summary>The client's command could not be started at all.</summary>
    NotInstalled,

    /// <summary>The client has no command of its own, so nothing was run.</summary>
    ByHand,
}

/// <summary>The outcome of running a plan.</summary>
/// <param name="Status">What happened.</param>
/// <param name="ClientSaid">The first line the client printed when it refused.</param>
public sealed record McpSetupResult(McpSetupStatus Status, string? ClientSaid);

/// <summary>
/// Connects keypaste to a client and removes it again, for <c>keypaste setup</c> and the desktop.
/// </summary>
/// <remarks>
/// Where a client ships its own command it is called rather than its files edited; see
/// <see cref="McpWiring"/>. Before an add, any earlier keypaste entry is removed, because the
/// clients disagree about adding twice: Codex overwrites, Claude Code refuses with "already
/// exists". Running it again is the ordinary case — the vault moved, or the binary did — so it has
/// to be the same plan either way.
/// </remarks>
public static class McpClientSetup
{
    /// <summary>How long a client's own command may take before keypaste gives up on it.</summary>
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A shorter budget for "are you installed", which must not stall a whole report.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private static readonly Encoding _utf8 = new UTF8Encoding(false);

    /// <summary>Whether the client's own command can be launched at all.</summary>
    /// <remarks>
    /// <c>--version</c> rather than <c>mcp list</c>: listing makes a client check the health of
    /// every server it already has, which takes long enough to look hung. All this answers is
    /// whether the executable exists. A client configured by file is never probed.
    /// </remarks>
    public static bool IsInstalled(McpClient client, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(runner);

        return client.Executable is { } executable
            && runner.Run(executable, ["--version"], stdin: null, _utf8, ProbeTimeout).ToolFound;
    }

    /// <summary>The plan that registers <paramref name="registration"/> with <paramref name="client"/>.</summary>
    public static McpSetupPlan Connect(McpClient client, McpServerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(registration);

        return client.Executable is { } executable
            ? new McpSetupPlan(
                client,
                McpSetupAction.Connect,
                [new(executable, client.RemoveArguments(), MayFail: true), new(executable, client.AddArguments(registration), MayFail: false)],
                PasteBlock: null)
            : new McpSetupPlan(client, McpSetupAction.Connect, [], registration.ConfigBlock());
    }

    /// <summary>The plan that removes keypaste from <paramref name="client"/>.</summary>
    public static McpSetupPlan Remove(McpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new McpSetupPlan(
            client,
            McpSetupAction.Remove,
            client.Executable is { } executable ? [new(executable, client.RemoveArguments(), MayFail: false)] : [],
            PasteBlock: null);
    }

    /// <summary>Runs exactly the plan's commands, in order, stopping at the first refusal.</summary>
    public static McpSetupResult Apply(McpSetupPlan plan, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runner);

        if (!plan.RunsCommands)
        {
            return new McpSetupResult(McpSetupStatus.ByHand, ClientSaid: null);
        }

        foreach (var command in plan.Commands)
        {
            var result = runner.Run(command.Executable, command.Arguments, stdin: null, _utf8, ClientTimeout);

            if (!result.ToolFound)
            {
                return new McpSetupResult(McpSetupStatus.NotInstalled, ClientSaid: null);
            }

            if (!result.Succeeded && !command.MayFail)
            {
                return new McpSetupResult(
                    McpSetupStatus.Refused,
                    FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput));
            }
        }

        return new McpSetupResult(McpSetupStatus.Done, ClientSaid: null);
    }

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
}
