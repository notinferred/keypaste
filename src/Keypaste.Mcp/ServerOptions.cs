using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.Mcp;

/// <summary>
/// Everything the server was told on its command line, validated.
/// </summary>
/// <remarks>
/// <para>
/// A hand-written parser rather than <c>Keypaste.Cli.CommandLine</c>. That looks like the kind of
/// duplication docs/PRODUCT.md law 4.3 forbids and is not: the CLI's parser rejects a repeated option, which
/// is exactly what <c>--expose</c> needs to allow, and widening it would change behaviour for five
/// shipped verbs to serve one new caller. Two parsers for two different grammars is not two
/// implementations of one rule — and every rule this configures (<see cref="EntryExposure"/>,
/// <see cref="VaultLocation"/>, <see cref="KeypasteHome"/>) does live in the core.
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
        usage: keypaste-mcp [--vault <path>] [--expose <glob>]... [--client-label <name>]
                            [--audit-log <path>] [--approver <name>]

        An MCP server that lets an AI agent ask for one credential, with your approval and a full
        audit trail. It speaks the protocol on stdin and stdout, so it is started by an MCP client
        rather than by you. See docs/mcp-setup.md.

          --vault <path>        which vault to expose, or set KEYPASTE_VAULT
          --expose <glob>       what may be named, repeatable. Defaults to env/**
          --client-label <name> what to call this client in the audit log
          --audit-log <path>    where to append the audit trail, or set KEYPASTE_HOME
          --approver <name>     which keypaste agent to ask, or set KEYPASTE_APPROVER

        Nothing is released unless a person says yes to that specific request, or a rule they wrote
        in advance covers it. They are reached through `keypaste agent`, which they start themselves
        in their own terminal - so no agent can cause a master password prompt to appear. With no
        agent running, every credential request is denied. `keypaste policy ls` shows the standing
        rules, if there are any.
        """;

    /// <summary>The vault to expose. Empty when none was configured, which is not fatal.</summary>
    internal required string VaultPath { get; init; }

    /// <summary>What this server may name at all.</summary>
    internal required EntryExposure Exposure { get; init; }

    /// <summary>Where the audit trail is appended.</summary>
    internal required string AuditPath { get; init; }

    /// <summary>Which pipe <c>keypaste agent</c> is expected on.</summary>
    /// <remarks>
    /// Resolved at startup so a malformed name is a startup failure, but nothing connects until a
    /// call needs an answer: the bridge is spawned by a client long before anybody starts an
    /// approver, and refusing to start without one would make keypaste look broken in the client's
    /// log rather than saying so in an answer an agent can act on.
    /// </remarks>
    internal required string ApproverName { get; init; }

    /// <summary>What to call this client in the audit log, or null.</summary>
    internal string? ClientLabel { get; init; }

    /// <summary>Whether <c>--help</c> was asked for.</summary>
    internal bool WantsHelp { get; init; }

    /// <summary>Parses the command line.</summary>
    /// <param name="argv">The arguments, excluding the program name.</param>
    /// <param name="vaultFromEnvironment">The value of <c>KEYPASTE_VAULT</c>, or null.</param>
    /// <param name="homeFromEnvironment">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    /// <param name="approverFromEnvironment">The value of <c>KEYPASTE_APPROVER</c>, or null.</param>
    /// <param name="options">The parsed options, on success.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="true"/> when the server may start.</returns>
    internal static bool TryParse(
        string[] argv,
        string? vaultFromEnvironment,
        string? homeFromEnvironment,
        string? approverFromEnvironment,
        [NotNullWhen(true)] out ServerOptions? options,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(argv);

        options = null;
        error = string.Empty;

        string? vault = null;
        string? label = null;
        string? auditPath = null;
        string? approver = null;
        List<string> globs = [];

        for (var i = 0; i < argv.Length; i++)
        {
            var argument = argv[i];

            if (string.Equals(argument, "--help", StringComparison.Ordinal)
                || string.Equals(argument, "-h", StringComparison.Ordinal))
            {
                options = Help();
                return true;
            }

            if (!TryTakeValue(argv, ref i, out var name, out var value, out error))
            {
                return false;
            }

            switch (name)
            {
                case "--vault":
                    if (!Once(ref vault, value, name, out error))
                    {
                        return false;
                    }

                    break;

                case "--client-label":
                    if (!Once(ref label, value, name, out error))
                    {
                        return false;
                    }

                    break;

                case "--audit-log":
                    if (!Once(ref auditPath, value, name, out error))
                    {
                        return false;
                    }

                    break;

                case "--expose":
                    globs.Add(value);
                    break;

                case "--approver":
                    if (!Once(ref approver, value, name, out error))
                    {
                        return false;
                    }

                    break;

                default:
                    error = $"unknown option '{name}'";
                    return false;
            }
        }

        // No globs means the default, applied here and on purpose. EntryExposure itself treats an
        // empty set as "nothing", so "the user said nothing" can never collapse into "everything".
        if (!EntryExposure.TryCreate(globs.Count == 0 ? [EntryExposure.DefaultGlob] : globs,
                out var exposure, out var globError))
        {
            error = $"--expose: {globError}";
            return false;
        }

        // A missing vault is deliberately not fatal. Malformed configuration should stop the
        // server; absent state should not, because a server that starts and says "no vault is
        // configured" is diagnosable, and one that exits leaves the client's log as the only clue.
        VaultLocation.TryResolve(vault, vaultFromEnvironment, out var vaultPath, out _);

        string pipeName;

        try
        {
            pipeName = ApproverEndpoint.Resolve(approver, approverFromEnvironment);
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

    private static bool Once(ref string? slot, string value, string name, out string error)
    {
        if (slot is not null)
        {
            error = $"{name} was given more than once";
            return false;
        }

        slot = value;
        error = string.Empty;
        return true;
    }

    /// <summary>Reads <c>--name value</c> or <c>--name=value</c>.</summary>
    private static bool TryTakeValue(
        string[] argv,
        ref int index,
        out string name,
        out string value,
        out string error)
    {
        var argument = argv[index];
        name = argument;
        value = string.Empty;
        error = string.Empty;

        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"unexpected argument '{argument}'";
            return false;
        }

        var equals = argument.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            name = argument[..equals];
            value = argument[(equals + 1)..];
            return true;
        }

        if (index + 1 >= argv.Length)
        {
            error = $"{name} needs a value";
            return false;
        }

        value = argv[++index];
        return true;
    }
}
