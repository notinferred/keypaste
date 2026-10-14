using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;

namespace Keypaste.Mcp;

/// <summary>
/// Everything the server was told on its command line, validated.
/// </summary>
/// <remarks>
/// <para>
/// Read with <see cref="CommandLine"/>, the parser every verb uses, and every rule this configures
/// (<see cref="EntryExposure"/>, <see cref="VaultLocation"/>, <see cref="KeypasteHome"/>) lives in the core.
/// </para>
/// <para>
/// Anything malformed is fatal. A typo in <c>--expose</c> must never leave a <em>different</em>
/// exposure quietly in force than the one the human wrote, because on this path the difference could
/// be a wider one.
/// </para>
/// </remarks>
internal sealed record ServerOptions
{
    internal const string Usage = """
        usage: keypaste mcp [--vault <path>] [--expose <glob>]... [--client-label <name>]
                            [--allow-run] [--audit-log <path>] [--clients <path>] [--approver <name>]
               keypaste mcp serve | setup | policy [options]

        An MCP server that lets an AI agent ask for one credential, with your approval and a full
        audit trail. An AI tool starts it; you do not run it yourself. See docs/mcp-setup.md.

          serve    approve agents' requests in this terminal (same as keypaste agent)
          setup    connect this computer's AI tools to your vault (same as keypaste setup)
          policy   show or change each agent's settings: what it can see, values, run and mode

          --vault <path>        which vault to ask, or set KEYPASTE_VAULT. Without either,
                                the vault chosen in the keypaste app or with `keypaste use`
          --expose <glob>       what this agent can see at most, repeatable: a group/title glob
                                reaches whole items, tag:env:<project>[:<environment>] the
                                variables of items tagged into it. Defaults to tag:env:*
          --client-label <name> the agent's name: its settings follow it, and the audit log uses it
          --allow-run           offer the run tool: start a command you approve with secrets
                                in its environment, if the agent's settings let it run commands.
                                A command can still reveal them.
          --audit-log <path>    where this bridge records a call it refuses itself or that
                                nothing holding the vault answered, or set KEYPASTE_HOME; the
                                app or keypaste agent records the calls it answers in its own log
          --clients <path>      this computer's ceilings, clients.toml, which an agent's change that
                                gives it less lowers while nothing holds the vault; or set KEYPASTE_HOME
          --approver <name>     which pipe to ask instead of the vault's own, or set KEYPASTE_APPROVER
          -h, --help            print this help

        Nothing is released unless a person says yes to that specific request, or set this agent to
        Automatic for it. Requests go to whichever keypaste process holds the vault unlocked: the
        desktop app or a `keypaste agent` the person started in their own terminal - so no agent can
        cause a master password prompt to appear. With nothing holding the vault, every request is
        denied but an agent's change that gives it less. `keypaste mcp policy` shows each agent's
        settings. An agent may ask for a change to its own; one that gives it more waits for the
        person to confirm it, and one that gives it less applies at once on this computer.
        """;

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("expose", TakesValue: true, Repeats: true),
        new("client-label", TakesValue: true),
        new("allow-run", TakesValue: false),
        new("audit-log", TakesValue: true),
        new("clients", TakesValue: true),
        new("approver", TakesValue: true),
    ];

    /// <summary>The vault to ask about. Empty when none was configured, which is not fatal: every
    /// request is then refused, saying so.</summary>
    internal required string VaultPath { get; init; }

    /// <summary>What this server may name at all.</summary>
    internal required EntryExposure Exposure { get; init; }

    /// <summary>Where the audit trail is appended.</summary>
    internal required string AuditPath { get; init; }

    /// <summary>
    /// This computer's ceilings, which an agent's own change that gives it nothing more lowers while
    /// nothing holds the vault; null when the bridge cannot tell they are the ones the vault's owner reads.
    /// </summary>
    /// <remarks>
    /// The file <c>--clients</c> names, which a registration for a home other than the default pins
    /// beside its log; otherwise its home's, and only while its log is its home's too, since a client
    /// that strips <c>KEYPASTE_HOME</c> from a bridge pinned to another home's log would leave it
    /// lowering a file that home's owner never reads while the agent was told its change applied.
    /// </remarks>
    internal string? ClientsPath { get; init; }

    /// <summary>Which pipe the vault's owner is expected on, or null when no vault was named.</summary>
    /// <remarks>
    /// Resolved at startup so a malformed name is a startup failure, but nothing connects until a
    /// call needs an answer: the bridge is spawned by a client long before anybody unlocks the
    /// vault, and refusing to start without an owner would make keypaste look broken in the client's
    /// log rather than saying so in an answer an agent can act on.
    /// </remarks>
    internal required string? ApproverName { get; init; }

    /// <summary>What to call this client in the audit log, or null.</summary>
    internal string? ClientLabel { get; init; }

    /// <summary>Whether <c>--help</c> was asked for.</summary>
    internal bool WantsHelp { get; init; }

    /// <summary>Whether <c>--allow-run</c> was given: the run tool is offered only then (D-0358).</summary>
    internal bool AllowRun { get; init; }

    /// <summary>The configured vault's identity key, for audit lines, or null without a vault.</summary>
    internal string? VaultKey { get; init; }

    /// <summary>Parses the command line.</summary>
    /// <param name="argv">The arguments, excluding the program name.</param>
    /// <param name="vaultFromEnvironment">The value of <c>KEYPASTE_VAULT</c>, or null.</param>
    /// <param name="homeFromEnvironment">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    /// <param name="approverFromEnvironment">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="chosenVault">The vault chosen in <c>app.toml</c>, or null.</param>
    /// <param name="options">The parsed options, on success.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="true"/> when the server may start.</returns>
    internal static bool TryParse(
        string[] argv,
        string? vaultFromEnvironment,
        string? homeFromEnvironment,
        string? approverFromEnvironment,
        string? chosenVault,
        [NotNullWhen(true)] out ServerOptions? options,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(argv);

        options = null;

        if (!CommandLine.TryParse(argv, 0, _options, out var line, out error))
        {
            return false;
        }

        if (line.WantsHelp || line.Operands is ["help"])
        {
            options = Help();
            return true;
        }

        if (line.Operands.Count > 0)
        {
            error = $"unexpected argument '{line.Operands[0]}'";
            return false;
        }

        var vault = line.Value("vault");
        var label = line.Value("client-label");
        var auditPath = line.Value("audit-log");
        var clientsPath = line.Value("clients");
        var approver = line.Value("approver");
        var allowRun = line.HasFlag("allow-run");
        var globs = line.Values("expose");

        // No globs means the default, applied here and on purpose. EntryExposure itself treats an
        // empty set as "nothing", so "the user said nothing" can never collapse into "everything".
        if (!EntryExposure.TryCreate(globs.Count == 0 ? [EntryExposure.DefaultGlob] : globs,
                out var exposure, out var globError))
        {
            error = $"--expose: {globError}";
            return false;
        }

        // The rule an agent's settings are kept under, so a bridge starts only with a name settings
        // can be kept for, and one the owner would reject on attach never reaches it.
        if (label is not null && !AgentNames.IsNamed(label))
        {
            error = $"--client-label must be 1 to {AgentNames.MaximumLength} characters with no quote, backslash, slash or control character, and not \"*\"";
            return false;
        }

        // A missing vault is deliberately not fatal. Malformed configuration should stop the
        // server; absent state should not, because a server that starts and says "no vault is
        // configured" is diagnosable, and one that exits leaves the client's log as the only clue.
        VaultLocation.TryResolve(vault, vaultFromEnvironment, chosenVault, out var vaultPath, out _);

        string? pipeName;
        var identity = vaultPath.Length > 0 ? VaultIdentity.Of(KeypasteHome.Resolve(homeFromEnvironment), vaultPath) : null;

        try
        {
            pipeName = ApproverEndpoint.Resolve(approver, approverFromEnvironment, identity);
        }
        catch (ArgumentException ex)
        {
            error = $"--approver: {ex.Message}";
            return false;
        }

        var homeLog = KeypasteHome.AuditPath(homeFromEnvironment);
        var log = auditPath is { Length: > 0 } ? Path.GetFullPath(auditPath) : homeLog;

        options = new ServerOptions
        {
            VaultPath = vaultPath,
            Exposure = exposure,
            ClientLabel = label,
            ApproverName = pipeName,
            AllowRun = allowRun,
            VaultKey = identity?.Key,
            AuditPath = log,
            ClientsPath = clientsPath is { Length: > 0 } ? Path.GetFullPath(clientsPath)
                : PathIdentity.SameFile(log, homeLog) ? KeypasteHome.ClientsPath(homeFromEnvironment)
                : null,
        };

        return true;
    }

    private static ServerOptions Help() => new()
    {
        VaultPath = string.Empty,
        Exposure = EntryExposure.Default,
        AuditPath = string.Empty,
        ApproverName = string.Empty,
        WantsHelp = true,
    };
}
