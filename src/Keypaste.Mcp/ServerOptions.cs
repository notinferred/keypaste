using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Infrastructure;

namespace Keypaste.Mcp;

/// <summary>
/// Everything the server was told on its command line, validated.
/// </summary>
/// <remarks>
/// <para>
/// Read with <see cref="Syntax"/>, the syntax every verb uses, and every rule this configures
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
    private static readonly Option<string> _vault = Syntax.Value(
        "--vault", "path", "which vault to ask, or set KEYPASTE_VAULT; without either, the vault chosen in the keypaste app or with `keypaste use`");

    private static readonly Option<string[]> _expose = Syntax.Values(
        "--expose",
        "glob",
        "what this agent can see at most: a group/title glob reaches whole items, tag:env:<project>[:<environment>] the "
            + "variables of items tagged into it; tag:env:* by default");

    private static readonly Option<string> _clientLabel =
        Syntax.Value("--client-label", "name", "the agent's name: its settings follow it, and the audit log uses it");

    private static readonly Option<bool> _allowRun = Syntax.Flag(
        "--allow-run", "offer the run tool, which starts a command you approve with secrets in its environment if the agent's settings let it run commands; a command can still reveal them");

    private static readonly Option<string> _auditLog = Syntax.Value(
        "--audit-log",
        "path",
        "where this bridge records a call it refuses itself or that nothing holding the vault answered, or set KEYPASTE_HOME; "
            + "the app or `keypaste approve` records the calls it answers in its own log");

    private static readonly Option<string> _clients = Syntax.Value(
        "--clients", "path", "this computer's ceilings, clients.toml, which an agent's change that gives it less lowers while nothing holds the vault; or set KEYPASTE_HOME");

    private static readonly Option<string> _approver =
        Syntax.Value("--approver", "name", "which pipe to ask instead of the vault's own, or set KEYPASTE_APPROVER");

    private static readonly Argument<string?> _help = new("help") { Arity = ArgumentArity.ZeroOrOne, Hidden = true };

    private static readonly Command _root = new("keypaste")
    {
        Syntax.Help,
        new Verb(
            "mcp",
            "the MCP server your AI tools start\n\n"
                + "it lets an AI agent ask for one credential, with your approval and a full audit trail. an AI tool starts it; "
                + "you do not run it yourself, and `keypaste setup` connects this computer's AI tools to your vault.\n\n"
                + "nothing is released unless a person says yes to that specific request, or set this agent to Automatic for "
                + "it. requests go to whichever keypaste process holds the vault unlocked, the desktop app or `keypaste approve` "
                + "in the person's own terminal, so no agent can cause a master password prompt to appear. with nothing holding "
                + "the vault, every request is denied but an agent's change that gives it less. `keypaste clients` shows or "
                + "changes each agent's settings.")
        {
            _help, _vault, _expose, _clientLabel, _allowRun, _auditLog, _clients, _approver,
        },
    };

    /// <summary>What <c>keypaste mcp --help</c> prints.</summary>
    internal static string Usage { get; } = Render();

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

    /// <summary>
    /// Where the desktop app listens while it is open and locked on this vault, or null without a vault.
    /// </summary>
    /// <remarks>Named from the vault alone, so <c>--approver</c> and <c>KEYPASTE_APPROVER</c> never move it (D-0466).</remarks>
    internal string? LockedAppName { get; init; }

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

        string[] args = ["mcp", .. argv];
        var line = _root.Parse(args, Syntax.Parsing);

        if (Syntax.Problem(line, args) is { } problem)
        {
            error = problem;
            return false;
        }

        error = string.Empty;

        if (line.GetValue(Syntax.Help) || line.GetValue(_help) is "help")
        {
            options = Help();
            return true;
        }

        if (line.GetValue(_help) is { } unexpected)
        {
            error = $"unexpected argument '{unexpected}'";
            return false;
        }

        var vault = line.GetValue(_vault);
        var label = line.GetValue(_clientLabel);
        var auditPath = line.GetValue(_auditLog);
        var clientsPath = line.GetValue(_clients);
        var approver = line.GetValue(_approver);
        var allowRun = line.GetValue(_allowRun);
        var globs = line.GetValue(_expose) ?? [];

        // No globs means the default, applied here and on purpose. EntryExposure itself treats an
        // empty set as "nothing", so "the user said nothing" can never collapse into "everything".
        if (!EntryExposure.TryCreate(globs.Length == 0 ? [EntryExposure.DefaultGlob] : globs,
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
            LockedAppName = identity is null ? null : LockedAppEndpoint.NameFor(identity),
            AuditPath = log,
            ClientsPath = clientsPath is { Length: > 0 } ? Path.GetFullPath(clientsPath)
                : PathIdentity.SameFile(log, homeLog) ? KeypasteHome.ClientsPath(homeFromEnvironment)
                : null,
        };

        return true;
    }

    private static string Render()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Syntax.WriteHelp(writer, _root.Parse(["mcp"], Syntax.Parsing), text => text, text => text);
        return writer.ToString().TrimEnd();
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
