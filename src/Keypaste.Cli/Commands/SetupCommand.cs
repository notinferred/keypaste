using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Settings;

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
/// what that path can release is still bounded by the bridge's own exposure default, and
/// still needs a running <c>keypaste agent</c> and a human saying yes.
/// </para>
/// </remarks>
internal static class SetupCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("client", TakesValue: true),
        new("label", TakesValue: true),
        new("expose", TakesValue: true),
        new("dry-run", TakesValue: false),
        new("remove", TakesValue: false),
    ];

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

        McpServerCommand? server = null;
        string? vault = null;
        IReadOnlyList<string> expose = [];
        if (!removing)
        {
            if (!TryReadTarget(line, context, out server, out vault, out expose, out var reason))
            {
                context.Stderr.WriteLine($"keypaste: {reason}");
                return CliApp.ExitUsageError;
            }
        }

        List<(McpClient Client, McpServerRegistration? Registration)> targets = [];
        foreach (var client in clients)
        {
            McpServerRegistration? registration = null;
            if (!removing && !McpServerRegistration.TryCreate(server!, vault, Label(line, client), expose, out registration, out var invalid))
            {
                context.Stderr.WriteLine($"keypaste: {invalid}");
                return CliApp.ExitUsageError;
            }

            targets.Add((client, registration));
        }

        if (!removing)
        {
            WriteHeader(context, targets[0].Registration!, ChosenVault.Read(context.Environment.Get(KeypasteHome.EnvironmentVariable)));
        }

        var installed = 0;

        foreach (var (client, registration) in targets)
        {
            var plan = removing ? McpClientSetup.Remove(client) : McpClientSetup.Connect(client, registration!);

            if (!plan.RunsCommands)
            {
                ReportManual(context, client, plan);
                continue;
            }

            if (!McpClientSetup.IsInstalled(client, context.ProcessRunner))
            {
                context.Stdout.WriteLine($"  {client.Id,-16} not installed on this machine");
                continue;
            }

            installed++;

            if (dryRun)
            {
                foreach (var command in plan.Commands)
                {
                    context.Stdout.WriteLine($"  {client.Id,-16} would run: {command.Display}");
                }

                continue;
            }

            Report(context, client, McpClientSetup.Apply(plan, context.ProcessRunner), removing);
        }

        if (installed == 0)
        {
            context.Stderr.WriteLine(
                "keypaste: no client with a command of its own is installed here. Nothing was changed.");
            return CliApp.ExitNotFound;
        }

        if (!dryRun && !removing)
        {
            WriteNextStep(context, targets[0].Registration!);
        }

        return CliApp.ExitSuccess;
    }

    private static void Report(CliContext context, McpClient client, McpSetupResult result, bool removing)
    {
        if (result.Status == McpSetupStatus.Done)
        {
            context.Stdout.WriteLine($"  {client.Id,-16} {(removing ? "removed" : "configured")}");
            return;
        }

        // The client ran and refused. Its own message is the useful one: keypaste does not know
        // what that client's scopes or config are, and paraphrasing would only lose detail.
        context.Stdout.WriteLine($"  {client.Id,-16} {client.DisplayName} refused");

        if (result.ClientSaid is { } said)
        {
            context.Stderr.WriteLine($"keypaste: {client.DisplayName} said: {said}");
        }
    }

    private static void ReportManual(CliContext context, McpClient client, McpSetupPlan plan)
    {
        if (plan.PasteBlock is not { } block)
        {
            context.Stdout.WriteLine($"  {client.Id,-16} remove keypaste by hand — see docs/mcp-setup.md");
            return;
        }

        context.Stdout.WriteLine($"  {client.Id,-16} has no command of its own; add this by hand:");
        context.Stdout.WriteLine();
        foreach (var blockLine in block)
        {
            context.Stdout.WriteLine("      " + blockLine);
        }

        context.Stdout.WriteLine();
        context.Stdout.WriteLine("      docs/mcp-setup.md says which file, per platform.");
    }

    private static bool TryReadTarget(
        CommandLine line,
        CliContext context,
        out McpServerCommand? server,
        out string? vault,
        out IReadOnlyList<string> expose,
        out string reason)
    {
        server = null;
        expose = [];

        // Only a vault named here and not already chosen is written into the client; otherwise its bridge uses the chosen one (D-0389).
        vault = VaultLocator.IsNamed(line, context.Environment)
            && VaultLocator.TryResolve(line, context.Environment, out var named, out _)
            && !ChosenVault.Same(ChosenVault.Read(context.Environment.Get(KeypasteHome.EnvironmentVariable)), named)
                ? named
                : null;

        // The client starts this binary, so setup and the bridge it registers are always one version.
        if (context.ProcessPath is not { } self
            || !string.Equals(Path.GetFileName(self), McpServerLocator.ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"setup registers the {McpServerLocator.FileName} that runs it, and this is {context.ProcessPath ?? "an unknown program"}; "
                + $"run {McpServerLocator.FileName} itself rather than through dotnet";
            return false;
        }

        server = new McpServerCommand(Path.GetFullPath(self), [McpServerLocator.BridgeArgument]);

        // The label is filled in per client; a single run wires several, and the audit log exists
        // to tell them apart.
        expose = line.Value("expose") is { Length: > 0 } globs
            ? globs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        reason = string.Empty;
        return true;
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

    private static void WriteHeader(CliContext context, McpServerRegistration registration, string? chosen)
    {
        context.Stdout.WriteLine($"keypaste mcp   {registration.Server.Path}");
        context.Stdout.WriteLine(registration.VaultPath is { } pinned
            ? $"vault          {pinned}"
            : $"vault          {chosen ?? "none chosen yet"} (the chosen vault; the client's entry names none)");
        context.Stdout.WriteLine(registration.Expose.Count == 0
            ? "exposure       env/** (the default; nothing else in the vault can even be named)"
            : $"exposure       {string.Join(", ", registration.Expose)}");

        if (registration.VaultPath is null && chosen is null)
        {
            context.Stderr.WriteLine(
                "keypaste: no vault is chosen yet. Wiring it anyway; open your vault in the keypaste app "
                + "or run `keypaste use <path>` before an agent asks.");
        }
        else if ((registration.VaultPath ?? chosen) is { } vault && !File.Exists(vault))
        {
            context.Stderr.WriteLine(
                $"keypaste: there is no vault at {vault} yet. Wiring it anyway; "
                + "create it with `keypaste init` before an agent asks.");
        }

        context.Stdout.WriteLine();
    }

    private static void WriteNextStep(CliContext context, McpServerRegistration registration)
    {
        context.Stdout.WriteLine();
        context.Stdout.WriteLine("Nothing is granted yet. keypaste mcp holds no vault and decides nothing.");
        context.Stdout.WriteLine("Unlock the vault in the keypaste app, or start the process that decides in a terminal:");
        context.Stdout.WriteLine();
        context.Stdout.WriteLine(registration.VaultPath is { } pinned ? $"  keypaste agent --vault {pinned}" : "  keypaste agent");
    }

    /// <summary>What the audit log will call this client: <c>--label</c>, else the client's own id.</summary>
    private static string Label(CommandLine line, McpClient client) =>
        line.Value("label") is { Length: > 0 } explicitLabel ? explicitLabel : client.Id;

    private static string KnownClientIds() =>
        string.Join(", ", McpClientCatalog.All.Select(client => client.Id));

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste setup [--vault <path>] [--client <a,b>] [--label <name>]");
        writer.WriteLine("                      [--expose <glob,glob>] [--dry-run] [--remove]");
        writer.WriteLine();
        writer.WriteLine("Finds the AI clients on this machine and points them at your vault.");
        writer.WriteLine();
        writer.WriteLine("  --vault <path>      pin the clients to this vault instead of the chosen one");
        writer.WriteLine("  --client <a,b>      only these, from: " + KnownClientIds());
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
