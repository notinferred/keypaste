using System.Text;
using Keypaste.Cli.Clipboard;
using Keypaste.Core.Clients;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Finds the AI clients installed on this machine and points them at this vault.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the alternative was a page of instructions. Wiring keypaste by hand means
/// finding an absolute path to a binary, choosing a scope, and getting a JSON or TOML block right
/// in a file that belongs to somebody else's program — once per client. It was the longest thing
/// between installing keypaste and using it, and none of it was interesting.
/// </para>
/// <para>
/// <b>Where a client ships its own command, keypaste calls it rather than editing its files.</b>
/// See <see cref="McpWiring"/> for the argument; the short version is that those files hold state
/// that is not ours, and one of them is rewritten by a running process while we would be reading
/// it. Where no such command exists, keypaste prints the block and stops — writing an unverified
/// schema and reporting success is the failure that would waste the most of a user's time, because
/// it looks exactly like having worked.
/// </para>
/// <para>
/// Nothing here touches a vault or a secret. <c>setup</c> writes a path into a configuration file;
/// what that path can release is still bounded by <c>keypaste-mcp</c>'s own exposure default, and
/// still needs a running <c>keypaste agent</c> and a human saying yes.
/// </para>
/// </remarks>
internal static class SetupCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("client", TakesValue: true),
        new("server-path", TakesValue: true),
        new("label", TakesValue: true),
        new("expose", TakesValue: true),
        new("dry-run", TakesValue: false),
        new("remove", TakesValue: false),
    ];

    /// <summary>The bridge's file name, without the platform's extension.</summary>
    private const string _serverFileName = "keypaste-mcp";

    /// <summary>How long a client's own command may take before keypaste gives up on it.</summary>
    private static readonly TimeSpan _clientTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A shorter budget for "are you installed", which must not stall the whole report.</summary>
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Stands in until each client's own label replaces it.</summary>
    private const string _clientLabelPlaceholder = "";

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste: {error}");
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (!TrySelectClients(line.Value("client"), out var clients, out var unknown))
        {
            context.Stderr.WriteLine($"keypaste: no client called '{unknown}'.");
            context.Stderr.WriteLine("keypaste: known clients: " + KnownClientIds());
            return CliApp.ExitUsageError;
        }

        var removing = line.HasFlag("remove");
        var dryRun = line.HasFlag("dry-run");

        McpServerRegistration? registration = null;
        if (!removing)
        {
            if (!TryBuildRegistration(line, context, out registration, out var reason))
            {
                context.Stderr.WriteLine($"keypaste: {reason}");
                return CliApp.ExitUsageError;
            }

            WriteHeader(context, registration!);
        }

        var installed = 0;

        foreach (var client in clients)
        {
            if (client.Wiring == McpWiring.ConfigFile)
            {
                ReportManual(context, client, registration, removing);
                continue;
            }

            if (!IsInstalled(client, context))
            {
                context.Stdout.WriteLine($"  {client.Id,-16} not installed on this machine");
                continue;
            }

            installed++;

            var arguments = removing
                ? client.RemoveArguments()
                : client.AddArguments(registration! with { ClientLabel = Label(line, client) });

            if (dryRun)
            {
                context.Stdout.WriteLine(
                    $"  {client.Id,-16} would run: {client.Executable} {string.Join(' ', arguments)}");
                continue;
            }

            if (!removing)
            {
                // Clear any previous entry first, because the clients disagree about what adding
                // twice means: Codex overwrites, Claude Code refuses with "already exists". Running
                // setup again is the ordinary case — the vault moved, or the binary did — so it has
                // to be the same command either way rather than one that works only once.
                Clear(client, context);
            }

            Apply(client, arguments, context, removing);
        }

        if (installed == 0)
        {
            context.Stderr.WriteLine(
                "keypaste: no client with a command of its own is installed here. Nothing was changed.");
            return CliApp.ExitNotFound;
        }

        if (!dryRun && !removing)
        {
            WriteNextStep(context, registration!);
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>
    /// Whether the client's own command can be launched at all.
    /// </summary>
    /// <remarks>
    /// <c>--version</c> rather than <c>mcp list</c>: listing makes a client check the health of
    /// every server it already has, which on a machine with a few of them takes long enough that a
    /// four-client report would look hung. All this has to answer is whether the executable exists,
    /// which is <see cref="ProcessResult.ToolFound"/> and nothing else.
    /// </remarks>
    private static bool IsInstalled(McpClient client, CliContext context)
    {
        var result = context.ProcessRunner.Run(
            client.Executable!,
            ["--version"],
            stdin: null,
            new UTF8Encoding(false),
            _probeTimeout);

        return result.ToolFound;
    }

    /// <summary>
    /// Removes any existing registration, ignoring whether there was one.
    /// </summary>
    /// <remarks>
    /// Deliberately silent: "there was nothing to remove" is the expected outcome on a first run
    /// and is not news. A failure here is not fatal either — the add that follows is what the user
    /// asked for, and it will report its own outcome.
    /// </remarks>
    private static void Clear(McpClient client, CliContext context)
    {
        context.ProcessRunner.Run(
            client.Executable!,
            client.RemoveArguments(),
            stdin: null,
            new UTF8Encoding(false),
            _clientTimeout);
    }

    private static void Apply(
        McpClient client,
        IReadOnlyList<string> arguments,
        CliContext context,
        bool removing)
    {
        var result = context.ProcessRunner.Run(
            client.Executable!,
            arguments,
            stdin: null,
            new UTF8Encoding(false),
            _clientTimeout);

        if (result.Succeeded)
        {
            context.Stdout.WriteLine($"  {client.Id,-16} {(removing ? "removed" : "configured")}");
            return;
        }

        // The client ran and refused. Its own message is the useful one: keypaste does not know
        // what that client's scopes or config are, and paraphrasing would only lose detail.
        context.Stdout.WriteLine($"  {client.Id,-16} {client.DisplayName} refused");

        var said = FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput);
        if (said is not null)
        {
            context.Stderr.WriteLine($"keypaste: {client.DisplayName} said: {said}");
        }
    }

    private static void ReportManual(
        CliContext context,
        McpClient client,
        McpServerRegistration? registration,
        bool removing)
    {
        if (removing || registration is null)
        {
            context.Stdout.WriteLine($"  {client.Id,-16} remove keypaste by hand — see docs/mcp-setup.md");
            return;
        }

        context.Stdout.WriteLine($"  {client.Id,-16} has no command of its own; add this by hand:");
        context.Stdout.WriteLine();
        context.Stdout.WriteLine("      " + Quote(McpClientCatalog.ServerName) + ": {");
        context.Stdout.WriteLine("        " + Quote("command") + ": " + Quote(registration.ServerPath) + ",");

        var labelled = registration with { ClientLabel = client.Id };
        var rest = labelled.CommandLine().Skip(1).Select(Quote);
        context.Stdout.WriteLine("        " + Quote("args") + ": [" + string.Join(", ", rest) + "]");
        context.Stdout.WriteLine("      }");
        context.Stdout.WriteLine();
        context.Stdout.WriteLine("      docs/mcp-setup.md says which file, per platform.");
    }

    private static bool TryBuildRegistration(
        CommandLine line,
        CliContext context,
        out McpServerRegistration? registration,
        out string reason)
    {
        registration = null;

        if (!VaultLocator.TryResolve(line, context.Environment, out var vault, out reason))
        {
            return false;
        }

        vault = Path.GetFullPath(vault);

        if (!TryFindServer(line.Value("server-path"), context, out var server))
        {
            // Naming the directory it looked in is the difference between a one-minute fix and
            // a puzzle: the usual cause is the two binaries having been built to separate trees.
            var lookedIn = Path.GetDirectoryName(Environment.ProcessPath) ?? "(unknown)";
            reason = $"cannot find {_serverFileName}. Looked beside keypaste, in {lookedIn}, "
                + "and on PATH. Build it, or pass --server-path.";
            return false;
        }

        var expose = line.Value("expose") is { Length: > 0 } globs
            ? globs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        // The label is filled in per client below; a single run wires several, and the audit log
        // exists to tell them apart.
        registration = new McpServerRegistration(server, vault, _clientLabelPlaceholder, expose);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Locates <c>keypaste-mcp</c>, preferring the copy that shipped beside this binary.
    /// </summary>
    /// <remarks>
    /// Beside-first is deliberate: the two are released together, and a mismatched pair is a class
    /// of bug nobody would enjoy diagnosing. PATH is the fallback, and <c>--server-path</c> beats
    /// both, because a developer running out of a build tree is a real case.
    /// </remarks>
    private static bool TryFindServer(string? explicitPath, CliContext context, out string server)
    {
        if (explicitPath is { Length: > 0 })
        {
            server = Path.GetFullPath(explicitPath);
            return File.Exists(server);
        }

        var executableName = OperatingSystem.IsWindows() ? _serverFileName + ".exe" : _serverFileName;

        var beside = Path.GetDirectoryName(Environment.ProcessPath);
        if (beside is { Length: > 0 })
        {
            var candidate = Path.Combine(beside, executableName);
            if (File.Exists(candidate))
            {
                server = candidate;
                return true;
            }
        }

        var path = context.Environment.Get("PATH");
        if (path is { Length: > 0 })
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory.Trim(), executableName);
                if (File.Exists(candidate))
                {
                    server = Path.GetFullPath(candidate);
                    return true;
                }
            }
        }

        server = string.Empty;
        return false;
    }

    private static bool TrySelectClients(
        string? requested,
        out IReadOnlyList<McpClient> clients,
        out string unknown)
    {
        unknown = string.Empty;

        if (requested is not { Length: > 0 })
        {
            clients = McpClientCatalog.All;
            return true;
        }

        List<McpClient> chosen = [];
        foreach (var id in requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var client = McpClientCatalog.Find(id);
            if (client is null)
            {
                unknown = id;
                clients = [];
                return false;
            }

            chosen.Add(client);
        }

        clients = chosen;
        return true;
    }

    private static void WriteHeader(CliContext context, McpServerRegistration registration)
    {
        context.Stdout.WriteLine($"keypaste-mcp   {registration.ServerPath}");
        context.Stdout.WriteLine($"vault          {registration.VaultPath}");
        context.Stdout.WriteLine(registration.Expose.Count == 0
            ? "exposure       env/** (the default; nothing else in the vault can even be named)"
            : $"exposure       {string.Join(", ", registration.Expose)}");

        if (!File.Exists(registration.VaultPath))
        {
            context.Stderr.WriteLine(
                $"keypaste: there is no vault at {registration.VaultPath} yet. Wiring it anyway; "
                + "create it with `keypaste init` before an agent asks.");
        }

        context.Stdout.WriteLine();
    }

    private static void WriteNextStep(CliContext context, McpServerRegistration registration)
    {
        context.Stdout.WriteLine();
        context.Stdout.WriteLine("Nothing is granted yet. keypaste-mcp holds no vault and decides nothing.");
        context.Stdout.WriteLine("Start the process that does:");
        context.Stdout.WriteLine();
        context.Stdout.WriteLine($"  keypaste agent --vault {registration.VaultPath}");
    }

    /// <summary>What the audit log will call this client: <c>--label</c>, else the client's own id.</summary>
    private static string Label(CommandLine line, McpClient client) =>
        line.Value("label") is { Length: > 0 } explicitLabel ? explicitLabel : client.Id;

    private static string KnownClientIds() =>
        string.Join(", ", McpClientCatalog.All.Select(client => client.Id));

    private static string Quote(string value) =>
        "\""
        + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
        + "\"";

    private static string? FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(candidate => candidate.Trim())
            .FirstOrDefault(candidate => candidate.Length > 0);
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste setup [--vault <path>] [--client <a,b>] [--server-path <path>]");
        writer.WriteLine("                      [--label <name>] [--expose <glob,glob>] [--dry-run] [--remove]");
        writer.WriteLine();
        writer.WriteLine("Finds the AI clients on this machine and points them at your vault.");
        writer.WriteLine();
        writer.WriteLine("  --vault <path>      which vault the clients should ask about");
        writer.WriteLine("  --client <a,b>      only these, from: " + KnownClientIds());
        writer.WriteLine("  --server-path <p>   where keypaste-mcp is, if not beside keypaste or on PATH");
        writer.WriteLine("  --label <name>      what the audit log calls the client (default: its id)");
        writer.WriteLine("  --expose <globs>    widen what may be named. Default is env/** and nothing else");
        writer.WriteLine("  --dry-run           print the exact commands and change nothing");
        writer.WriteLine("  --remove            take keypaste out again, leaving everything else alone");
        writer.WriteLine();
        writer.WriteLine("A client that ships its own command is configured with it. One that does not is");
        writer.WriteLine("printed for you to paste, because writing a format keypaste has not verified and");
        writer.WriteLine("then calling it success would waste more of your time than asking.");
    }
}
