using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
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
                            [--allow-run] [--audit-log <path>] [--approver <name>]
               keypaste mcp serve | setup | policy [options]

        An MCP server that lets an AI agent ask for one credential, with your approval and a full
        audit trail. It speaks the protocol on stdin and stdout, so it is started by an MCP client
        rather than by you. See docs/mcp-setup.md.

          serve    approve agents' requests in this terminal (same as keypaste agent)
          setup    point this machine's AI clients at your vault (same as keypaste setup)
          policy   show or set how each client is asked: session, ask or inject-only

          --vault <path>        which vault to expose, or set KEYPASTE_VAULT. Without either,
                                the vault chosen in the keypaste app or with `keypaste use`
          --expose <glob>       what may be named, repeatable: a group/title glob reaches whole
                                entries, tag:env:<project>[:<environment>] the variables of
                                entries tagged into it. Defaults to tag:env:*
          --client-label <name> what to call this client in the audit log and in clients.toml
          --allow-run           offer the run tool: start a command you approve with secrets
                                in its environment. A command can still reveal them.
          --audit-log <path>    where to append the audit trail, or set KEYPASTE_HOME
          --approver <name>     which pipe to ask instead of the vault's own, or set KEYPASTE_APPROVER
          -h, --help            print this help

        Nothing is released unless a person says yes to that specific request, or a rule they wrote
        in advance covers it. Requests go to whichever keypaste process holds the vault unlocked:
        the desktop app or a `keypaste agent` the person started in their own terminal - so no agent
        can cause a master password prompt to appear. With nothing holding the vault, every request
        is denied. `keypaste policy ls` shows the standing rules, if there are any.
        """;

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("expose", TakesValue: true, Repeats: true),
        new("client-label", TakesValue: true),
        new("allow-run", TakesValue: false),
        new("audit-log", TakesValue: true),
        new("approver", TakesValue: true),
    ];

    /// <summary>The vault to ask about. Empty when none was configured, which is not fatal: every
    /// request is then refused, saying so.</summary>
    internal required string VaultPath { get; init; }

    /// <summary>What this server may name at all.</summary>
    internal required EntryExposure Exposure { get; init; }

    /// <summary>Where the audit trail is appended.</summary>
    internal required string AuditPath { get; init; }

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

        // The same rule a clients.toml row is held to, so a label a policy can name is the only kind a
        // bridge starts with, and one the owner would reject on attach never reaches it.
        if (label is not null && (string.Equals(label, ClientPolicies.AnyClient, StringComparison.Ordinal)
            || !ClientPolicies.IsValidLabel(label, out _)))
        {
            error = $"--client-label must be 1 to {ClientPolicies.MaximumLabelLength} characters with no quote, backslash, slash or control character, and not \"*\"";
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

        options = new ServerOptions
        {
            VaultPath = vaultPath,
            Exposure = exposure,
            ClientLabel = label,
            ApproverName = pipeName,
            AllowRun = allowRun,
            VaultKey = identity?.Key,
            AuditPath = auditPath is { Length: > 0 }
                ? Path.GetFullPath(auditPath)
                : KeypasteHome.AuditPath(homeFromEnvironment),
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
