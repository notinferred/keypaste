using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
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
/// <b>Where a client ships its own command, keypaste calls it; otherwise it changes only its own entry in the
/// client's file</b>, keeping the first original beside it (<see cref="McpWiring"/>, D-0435). Every step is printed
/// before anything runs, and one answer or <c>--yes</c> covers the whole run; without a terminal and without
/// <c>--yes</c>, nothing changes (D-0437). A removal first reads each client's own configuration, and a client holding
/// no keypaste entry is not connected and is left alone.
/// </para>
/// <para>
/// Nothing here touches a vault or a secret. <c>setup</c> writes a path into a configuration file, with run offered
/// and every item in reach unless <c>--expose</c> narrows it, because what that path can release is decided by each
/// agent's settings, which the app or <c>keypaste approve</c> holding the vault applies, and a person's answer or a mode
/// they set.
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
        new("yes", TakesValue: false),
        new("link", TakesValue: false),
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

        var platform = McpClientMachine.CurrentPlatform;

        if (!TrySelectClients(line.Value("client"), platform, out var clients, out var unknown))
        {
            context.Stderr.WriteLine($"keypaste: no AI tool called '{unknown}'.");
            context.Stderr.WriteLine("keypaste: known AI tools: " + KnownClientIds(platform));
            return CliApp.ExitUsageError;
        }

        if (line.HasFlag("link"))
        {
            return WriteLinks(line, context, clients, platform);
        }

        var removing = line.HasFlag("remove");
        var dryRun = line.HasFlag("dry-run");
        var assumeYes = line.HasFlag("yes");

        // A change to other programs' configuration is not something to have answered by whatever the next line of stdin is.
        if (!dryRun && !assumeYes && !context.Prompt.IsInteractive)
        {
            context.Stderr.WriteLine("keypaste setup: --yes is required when stdin is not a terminal; nothing was changed");
            return CliApp.ExitUsageError;
        }

        var machine = McpClientMachine.From(context.Environment.Get, platform);
        McpBridgeTarget? target = null;
        Dictionary<string, McpServerRegistration> registrations = new(StringComparer.Ordinal);

        if (!removing && !TryRegister(line, context, machine, clients, out target, registrations))
        {
            return CliApp.ExitUsageError;
        }

        var tools = clients.Select(client => Compose(client, machine, context, removing ? null : registrations[client.Id])).ToList();

        if (!tools.Any(tool => tool.Installed))
        {
            WriteSteps(context, tools, link: null, dryRun, offersLinks: false);
            context.Stderr.WriteLine("keypaste: no AI tool keypaste connects is installed here. Nothing was changed.");
            return CliApp.ExitNotFound;
        }

        if (!removing)
        {
            WriteHeader(context, registrations[clients[0].Id], target!.Note, ChosenVault.Read(context.Environment.Get(KeypasteHome.EnvironmentVariable)));
        }

        var acting = tools.Where(tool => tool.Plan is { IsNotConnected: false }).ToList();
        var link = acting.Count > 0 ? target?.Link : null;
        WriteSteps(context, tools, link, dryRun, offersLinks: target is { Link: null });

        if (dryRun)
        {
            return CliApp.ExitSuccess;
        }

        if (acting.Count == 0)
        {
            return tools.Any(tool => tool.Plan is { IsNotConnected: true }) ? CliApp.ExitSuccess : CliApp.ExitClientsRefused;
        }

        if (!assumeYes && !Confirmed(context, Question(removing, link, acting.Count)))
        {
            context.Stderr.WriteLine("Cancelled. Nothing was changed.");
            return CliApp.ExitUsageError;
        }

        if (link is not null && McpClientSetup.Apply(link) is { Status: McpSetupStatus.LinkFailed } failed)
        {
            context.Stderr.WriteLine($"keypaste: could not {link.Verb} {link.Link.LinkPath}: {failed.Said}. Nothing else was changed.");
            return CliApp.ExitInternalError;
        }

        var succeeded = tools.Count(tool => tool.Plan is { IsNotConnected: true });

        foreach (var tool in acting)
        {
            var result = McpClientSetup.Apply(tool.Plan!, context.ProcessRunner);
            Report(context, tool.Client, result, removing);

            // Any link this run needed was made before the first tool step, so the tool's own link now starts this copy.
            if (result.Status == McpSetupStatus.Refused && tool.InstallLink is { } offered)
            {
                Offer(context, tool.Client, offered, alternative: true);
            }

            if (result.Status is McpSetupStatus.Done or McpSetupStatus.NotConnected)
            {
                succeeded++;
            }
        }

        if (succeeded == 0)
        {
            return CliApp.ExitClientsRefused;
        }

        if (!removing)
        {
            WriteNextStep(context, registrations[clients[0].Id]);
        }

        return CliApp.ExitSuccess;
    }

    private static bool TryRegister(
        CommandLine line,
        CliContext context,
        McpClientMachine machine,
        IReadOnlyList<McpClient> clients,
        out McpBridgeTarget? target,
        Dictionary<string, McpServerRegistration> registrations)
    {
        if (!McpServerLocator.TryForSetup(context.ProcessPath, context.AppImage(), out target, out var targetError))
        {
            context.Stderr.WriteLine($"keypaste: {targetError}");
            return false;
        }

        var home = context.Environment.Get(KeypasteHome.EnvironmentVariable);
        var named = VaultLocator.IsNamed(line, context.Environment) && VaultLocator.TryResolve(line, context.Environment, out var resolved, out _)
            ? resolved
            : null;

        // A client may start the bridge without KEYPASTE_HOME or KEYPASTE_APPROVER, and Gemini CLI strips both (D-0389).
        if (!McpServerLocator.TryPin(machine, home, context.Environment.Get(ApproverEndpoint.EnvironmentVariable), named, ChosenVault.Read(home), out var pins))
        {
            context.Stderr.WriteLine(
                $"keypaste: {KeypasteHome.EnvironmentVariable} is {home}, which a tool started without it does not see; "
                + "choose a vault with keypaste use <path>, or name one with --vault. Nothing was changed.");
            return false;
        }

        // The label is filled in per client; a single run wires several, and the audit log exists to tell them apart.
        IReadOnlyList<string> expose = line.Value("expose") is { Length: > 0 } globs
            ? globs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        foreach (var client in clients)
        {
            if (!McpServerRegistration.TryCreate(target.Server, pins, Label(line, client), expose, out var registration, out var invalid))
            {
                context.Stderr.WriteLine($"keypaste: {invalid}");
                return false;
            }

            registrations[client.Id] = registration;
        }

        return true;
    }

    private static Tool Compose(McpClient client, McpClientMachine machine, CliContext context, McpServerRegistration? registration)
    {
        var detection = McpClientSetup.Detect(client, machine, context.ProcessRunner);

        if (!detection.Found)
        {
            return new Tool(client, Installed: false, Plan: null, Skipped: null);
        }

        if (registration is null)
        {
            return detection.Problem is { } removalProblem
                ? new Tool(client, Installed: true, Plan: null, removalProblem)
                : new Tool(client, Installed: true, McpClientSetup.Remove(client, detection, McpClientConfig.Read(client, machine)), Skipped: null);
        }

        var offered = McpInstallLink.TryCreate(client, machine.Platform, registration, out var installLink, out _) ? installLink : null;

        if (detection.Problem is { } problem)
        {
            return new Tool(client, Installed: true, Plan: null, problem, offered);
        }

        return McpClientSetup.TryConnect(client, detection, registration, link: null, out var plan, out var refusal)
            ? new Tool(client, Installed: true, plan, Skipped: null, offered)
            : new Tool(client, Installed: true, Plan: null, refusal, offered);
    }

    /// <summary>Prints each selected tool's install link, composed from the registration a run would write, and changes nothing.</summary>
    private static int WriteLinks(CommandLine line, CliContext context, IReadOnlyList<McpClient> clients, McpClientPlatform platform)
    {
        if (line.HasFlag("remove"))
        {
            context.Stderr.WriteLine("keypaste: --link prints links that add keypaste; it does not go with --remove");
            return CliApp.ExitUsageError;
        }

        if (line.Value("client") is { Length: > 0 } && clients.FirstOrDefault(client => !TakesLink(client)) is { } unlinked)
        {
            context.Stderr.WriteLine($"keypaste: {unlinked.Id} takes no install link; {LinkedClientIds(platform)} do");
            return CliApp.ExitUsageError;
        }

        var linked = clients.Where(TakesLink).ToList();
        var machine = McpClientMachine.From(context.Environment.Get, platform);
        Dictionary<string, McpServerRegistration> registrations = new(StringComparer.Ordinal);

        if (!TryRegister(line, context, machine, linked, out var target, registrations))
        {
            return CliApp.ExitUsageError;
        }

        // Inside an AppImage the registration starts a link that only a run of setup makes.
        if (target!.Link is { } bridge)
        {
            context.Stderr.WriteLine($"keypaste: {bridge.Link.LinkPath} does not start this copy yet, so a link would start nothing.");
            context.Stderr.WriteLine("keypaste: run keypaste setup, which makes it. Nothing was changed.");
            return CliApp.ExitUsageError;
        }

        WriteHeader(context, registrations[linked[0].Id], target.Note, ChosenVault.Read(context.Environment.Get(KeypasteHome.EnvironmentVariable)));

        var offered = 0;

        foreach (var client in linked)
        {
            if (McpInstallLink.TryCreate(client, platform, registrations[client.Id], out var link, out var error))
            {
                Offer(context, client, link, alternative: false);
                offered++;
            }
            else
            {
                Line(context, client.Id, "no link");
                context.Stderr.WriteLine($"keypaste: {error}");
            }
        }

        if (offered == 0)
        {
            return CliApp.ExitClientsRefused;
        }

        WriteNextStep(context, registrations[linked[0].Id]);
        return CliApp.ExitSuccess;
    }

    /// <summary>Prints the link that has the tool add keypaste itself, alone on its line so it is copied whole.</summary>
    private static void Offer(CliContext context, McpClient client, string link, bool alternative)
    {
        Line(context, client.Id, (alternative ? "or open" : "open") + $" this link to have {client.DisplayName} add keypaste:");
        context.Stdout.WriteLine(link);
    }

    private static bool TakesLink(McpClient client) => client.InstallLink != McpInstallLinkFormat.None;

    private static string LinkedClientIds(McpClientPlatform platform) =>
        string.Join(" and ", McpClientCatalog.For(platform).Where(TakesLink).Select(client => client.Id));

    private static void WriteSteps(CliContext context, IReadOnlyList<Tool> tools, McpBridgeLink? link, bool dryRun, bool offersLinks)
    {
        if (link is not null)
        {
            Line(context, McpServerLocator.FileName, link.Display);
        }

        foreach (var tool in tools)
        {
            if (!tool.Installed)
            {
                Line(context, tool.Client.Id, "not installed on this machine");
                continue;
            }

            if (tool.Skipped is { } why)
            {
                Line(context, tool.Client.Id, "skipped");
                context.Stderr.WriteLine($"keypaste: {why}");

                if (offersLinks && tool.InstallLink is { } offered)
                {
                    Offer(context, tool.Client, offered, alternative: true);
                }

                continue;
            }

            context.Stdout.WriteLine(string.Join('\n', tool.Plan!.Display.Split('\n').Select(step => Formatted(tool.Client.Id, step))));

            if (dryRun)
            {
                foreach (var command in tool.Plan.Steps.OfType<McpClientCommand>())
                {
                    Line(context, tool.Client.Id, "starts: " + command.StartedDisplay);
                }
            }
        }
    }

    private static string Question(bool removing, McpBridgeLink? link, int count)
    {
        var these = count == 1 ? "this tool" : $"these {count} tools";

        return removing ? $"Remove keypaste from {these}? [y/N] "
            : link is { Change: McpLinkChange.Replace } ? $"Replace {link.Link.LinkPath} and connect keypaste to {these}? [y/N] "
            : $"Connect keypaste to {these}? [y/N] ";
    }

    private static bool Confirmed(CliContext context, string question) =>
        context.Prompt.ReadLine(question)?.Trim() is { Length: > 0 } answer && answer[0] is 'y' or 'Y';

    private static void Report(CliContext context, McpClient client, McpSetupResult result, bool removing)
    {
        switch (result.Status)
        {
            case McpSetupStatus.Done:
                Line(context, client.Id, (removing ? "removed" : "configured") + Notes(client, result, removing));
                return;

            case McpSetupStatus.NotConnected:
                Line(context, client.Id, McpSetupPlan.NothingToRemove);
                return;

            case McpSetupStatus.NotInstalled:
                Line(context, client.Id, $"{client.DisplayName} could not be started");
                return;
        }

        // The client ran and refused, or keypaste left its file alone. The client's own message is the useful one:
        // keypaste does not know what that client's scopes or config are, and paraphrasing would only lose detail.
        Line(context, client.Id, $"{client.DisplayName} refused");

        if (result.Said is { } said)
        {
            context.Stderr.WriteLine(client.Wiring == McpWiring.ConfigFile
                ? $"keypaste: {said}"
                : $"keypaste: {client.DisplayName} said: {said}");
        }
    }

    private static string Notes(McpClient client, McpSetupResult result, bool removing)
    {
        var notes = string.Empty;

        if (result.Backup is { } backup)
        {
            notes += $"; the original is kept as {backup}";
        }

        if (!removing && client.StartsOnlyInTrustedFolders)
        {
            notes += $"; {client.DisplayName} starts keypaste only in folders you have trusted";
        }

        if (client.ReadsAtLaunch)
        {
            notes += $"; restart {client.DisplayName} to load it";
        }

        return notes;
    }

    private static bool TrySelectClients(
        string? requested,
        McpClientPlatform platform,
        out IReadOnlyList<McpClient> clients,
        out string unknown)
    {
        unknown = string.Empty;
        var known = McpClientCatalog.For(platform);

        if (requested is not { Length: > 0 })
        {
            clients = known;
            return true;
        }

        List<McpClient> chosen = [];
        foreach (var id in requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var client = known.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (client is null)
            {
                unknown = id;
                clients = [];
                return false;
            }

            chosen.Add(client);
        }

        if (chosen.Count == 0)
        {
            unknown = requested;
            clients = [];
            return false;
        }

        clients = chosen;
        return true;
    }

    private static void WriteHeader(CliContext context, McpServerRegistration registration, string? note, string? chosen)
    {
        context.Stdout.WriteLine($"keypaste mcp   {registration.Server.Path}");
        context.Stdout.WriteLine(registration.VaultPath is { } pinned
            ? $"vault          {pinned}"
            : $"vault          {chosen ?? "none chosen yet"} (the chosen vault; the tool's configuration names none)");

        if (registration.AuditLogPath is { } auditLog)
        {
            context.Stdout.WriteLine($"audit log      {auditLog}");
        }

        if (registration.ClientsPath is { } ceilings)
        {
            context.Stdout.WriteLine($"ceilings       {ceilings}");
        }

        if (registration.ApproverName is { } approver)
        {
            context.Stdout.WriteLine($"approver       {approver}");
        }

        context.Stdout.WriteLine(registration.Expose is [McpServerRegistration.WideExposure]
            ? $"can see        {McpServerRegistration.WideExposure} (each agent sees what its settings allow: every project's variables until you change them)"
            : $"can see        {string.Join(", ", registration.Expose)} (no agent these tools start sees beyond these, whatever its settings)");
        context.Stdout.WriteLine("run            offered; each agent runs nothing until you turn run on in its settings");

        if (note is not null)
        {
            context.Stdout.WriteLine(note);
        }

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
        context.Stdout.WriteLine("Nothing is allowed yet. keypaste mcp holds no vault and decides nothing.");
        context.Stdout.WriteLine("Unlock the vault in the keypaste app, or start the process that decides in a terminal:");
        context.Stdout.WriteLine();
        context.Stdout.WriteLine(registration.VaultPath is { } pinned ? $"  keypaste approve --vault {pinned}" : "  keypaste approve");
    }

    private static void Line(CliContext context, string id, string text) => context.Stdout.WriteLine(Formatted(id, text));

    private static string Formatted(string id, string text) => $"  {id,-16} {text}";

    /// <summary>What the audit log will call this client: <c>--label</c>, else the client's own id.</summary>
    private static string Label(CommandLine line, McpClient client) =>
        line.Value("label") is { Length: > 0 } explicitLabel ? explicitLabel : client.Id;

    private static string KnownClientIds(McpClientPlatform platform) =>
        string.Join(", ", McpClientCatalog.For(platform).Select(client => client.Id));

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste setup [--vault <path>] [--client <a,b>] [--label <name>]");
        writer.WriteLine("                      [--expose <globs>] [--dry-run] [--remove] [--yes] [--link]");
        writer.WriteLine();
        writer.WriteLine("Finds the AI tools on this machine and points them at your vault.");
        writer.WriteLine();
        writer.WriteLine("  --vault <path>      pin the AI tools to this vault instead of the chosen one");
        writer.WriteLine("  --client <a,b>      only these, from: " + KnownClientIds(McpClientMachine.CurrentPlatform));
        writer.WriteLine("  --label <name>      what the audit log calls the agent (default: its id)");
        writer.WriteLine("  --expose <globs>    narrow what agents may see through these tools; by default their settings decide");
        writer.WriteLine("  --dry-run           print every step and change nothing");
        writer.WriteLine("  --remove            take keypaste out again, leaving everything else alone");
        writer.WriteLine("  --yes               change without asking; required when stdin is not a terminal");
        writer.WriteLine("  --link              print links that have Cursor and VS Code add keypaste");
        writer.WriteLine();
        writer.WriteLine("An AI tool that ships its own command is configured with it. For one that does not,");
        writer.WriteLine("keypaste changes only its own part of the tool's configuration file and keeps");
        writer.WriteLine("the first original beside it.");
    }

    /// <summary>One selected client: whether it is installed, the plan for it or why it is skipped, and the link that has it add keypaste itself.</summary>
    private sealed record Tool(McpClient Client, bool Installed, McpSetupPlan? Plan, string? Skipped, string? InstallLink = null);
}
