using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Keypaste.Core.Linking;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Clients;

/// <summary>One thing a plan does, shown to the person before it runs.</summary>
public abstract record McpSetupStep
{
    /// <summary>Gets the step as the person reads it before confirming.</summary>
    public abstract string Display { get; }
}

/// <summary>One command a client's own program is asked to run.</summary>
/// <param name="Program">How the client's command is started.</param>
/// <param name="Shown">The client's command as a person types it, such as <c>claude</c>.</param>
/// <param name="Arguments">Its arguments, passed without a shell.</param>
/// <param name="MayFail">
/// Whether a refusal is expected and ignored: the removal that clears an earlier entry before an
/// add, which finds nothing to remove on a first run.
/// </param>
public sealed record McpClientCommand(McpClientProgram Program, string Shown, IReadOnlyList<string> Arguments, bool MayFail) : McpSetupStep
{
    /// <inheritdoc/>
    /// <remarks>The command as a person would type it, each argument that needs it quoted.</remarks>
    public override string Display => Quoted([Shown, .. Arguments]);

    /// <summary>Gets the whole argv the runner is given, the resolved program first.</summary>
    public IReadOnlyList<string> Started => [Program.FileName, .. Program.Prefix, .. Arguments];

    /// <summary>Gets <see cref="Started"/> quoted as <see cref="Display"/> is.</summary>
    public string StartedDisplay => Quoted(Started);

    private static string Quoted(IEnumerable<string> words) => string.Join(' ', words.Select(Quote));

    private static string Quote(string value) =>
        value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')
            ? value
            : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>How a plan changes the Linux link a client is registered through.</summary>
public enum McpLinkChange
{
    /// <summary>Nothing is at the link path, so the link is written.</summary>
    Create,

    /// <summary>The link names a copy that is gone or older, so it is rewritten for this one.</summary>
    Repoint,

    /// <summary>A file keypaste did not write is at the link path; it is kept as the backup and the link written.</summary>
    Replace,
}

/// <summary>The Linux link a client registered inside an AppImage starts, made before any client is told about it.</summary>
/// <param name="Link">The link, G.5's <c>~/.local/bin/keypaste</c>.</param>
/// <param name="Image">The AppImage the link will start.</param>
/// <param name="Change">What happens to the link.</param>
/// <param name="Was">What the link path holds now, as the person reads it; null for <see cref="McpLinkChange.Create"/>.</param>
public sealed record McpBridgeLink(ICliLink Link, string Image, McpLinkChange Change, string? Was) : McpSetupStep
{
    /// <inheritdoc/>
    public override string Display => Change switch
    {
        McpLinkChange.Create => $"create {Link.LinkPath}, a script that starts {Image} cli",
        McpLinkChange.Repoint => $"re-point {Link.LinkPath} from {Was} to {Image}",
        _ => $"replace {Link.LinkPath}, which is {Was}, with a script that starts {Image} cli; the old file is kept as {Link.BackupPath}",
    };

    /// <summary>Gets the verb a failure names: create, re-point or replace.</summary>
    public string Verb => Change switch
    {
        McpLinkChange.Create => "create",
        McpLinkChange.Repoint => "re-point",
        _ => "replace",
    };
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
/// The same object is shown and then run, so what a person confirmed and what happened cannot differ. A removal with no
/// steps is for a client whose configuration holds no keypaste entry: it is not connected, and nothing runs.
/// </remarks>
/// <param name="Client">The client this plan is for.</param>
/// <param name="Action">Connect or remove.</param>
/// <param name="Steps">What runs, in order: a link first when there is one, then the client's commands or the edit of its file.</param>
public sealed record McpSetupPlan(McpClient Client, McpSetupAction Action, IReadOnlyList<McpSetupStep> Steps)
{
    /// <summary>What a removal with nothing to remove says.</summary>
    public const string NothingToRemove = "not connected; nothing to remove";

    /// <summary>Gets whether this is a removal from a client that holds no keypaste entry.</summary>
    public bool IsNotConnected => Action == McpSetupAction.Remove && Steps.Count == 0;

    /// <summary>Gets the plan as the person reads it before confirming: one line per step.</summary>
    public string Display => IsNotConnected ? NothingToRemove : string.Join('\n', Steps.Select(step => step.Display));
}

/// <summary>What looking for a client on a machine found.</summary>
/// <param name="Platform">The machine's platform.</param>
/// <param name="Found">Whether the client is installed.</param>
/// <param name="Program">How to start its own command, for a client that has one and can be started.</param>
/// <param name="ConfigPath">The file keypaste edits, for a client configured by file.</param>
/// <param name="Problem">Why a client that is installed cannot be connected or removed, naming what stands in the way.</param>
public sealed record McpClientDetection(
    McpClientPlatform Platform,
    bool Found,
    McpClientProgram? Program,
    string? ConfigPath,
    string? Problem);

/// <summary>What running a plan did.</summary>
public enum McpSetupStatus
{
    /// <summary>Every step ran and succeeded.</summary>
    Done,

    /// <summary>A removal found no keypaste entry, so nothing ran or changed.</summary>
    NotConnected,

    /// <summary>The client said no, or keypaste left its file alone; <see cref="McpSetupResult.Said"/> has why.</summary>
    Refused,

    /// <summary>The client's command could not be started at all.</summary>
    NotInstalled,

    /// <summary>The link a client is registered through was not made, so no client step ran.</summary>
    LinkFailed,
}

/// <summary>The outcome of running a plan.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Said">The client's first line when it refused, or keypaste's reason for a refused edit or a failed link.</param>
/// <param name="Backup">The copy of the first original this run made, or null when it made none.</param>
public sealed record McpSetupResult(McpSetupStatus Status, string? Said, string? Backup);

/// <summary>
/// Connects keypaste to a client and removes it again, for <c>keypaste setup</c> and the desktop.
/// </summary>
/// <remarks>
/// Where a client ships its own command it is called; otherwise keypaste edits only its own entry in the client's file
/// (<see cref="McpWiring"/>). Before an add, any earlier keypaste entry is removed, because the clients disagree about
/// adding twice: Codex overwrites, Claude Code refuses with "already exists". Running it again is the ordinary case — the
/// vault moved, or the binary did — so it has to be the same plan either way.
/// </remarks>
public static class McpClientSetup
{
    /// <summary>How long a client's own command may take before keypaste gives up on it.</summary>
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A shorter budget for "are you installed", which must not stall a whole report.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private const string _applications = "Applications";

    /// <summary>Whether <paramref name="client"/> is installed on <paramref name="machine"/>, and how to reach it.</summary>
    /// <remarks>
    /// A client with a command of its own is installed when <see cref="McpClientProgram.TryLocate"/> finds it and its
    /// <c>--version</c> starts: <c>--version</c> rather than <c>mcp list</c>, because listing makes a client check every
    /// server it already has, which takes long enough to look hung. A client configured by file is installed when the
    /// folder of its file exists, its launcher is on <c>PATH</c>, or, for the macOS app, its bundle is in
    /// <c>Applications</c>; nothing of it is started.
    /// </remarks>
    public static McpClientDetection Detect(McpClient client, McpClientMachine machine, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(runner);

        var absent = new McpClientDetection(machine.Platform, Found: false, Program: null, ConfigPath: null, Problem: null);

        if (client.Wiring == McpWiring.OwnCli)
        {
            if (!McpClientProgram.TryLocate(client, machine, out var program, out var problem))
            {
                return problem is null ? absent : absent with { Found = true, Problem = problem };
            }

            return program.Run(runner, ["--version"], ProbeTimeout).ToolFound ? absent with { Found = true, Program = program } : absent;
        }

        if (client.Configuration is not { } location || !IsInstalled(client, location, machine))
        {
            return absent;
        }

        var moved = location.MovedOn(machine);
        var path = location.PathOn(machine);
        var cannot = moved is not null
            ? $"{moved} is set, so {client.DisplayName} may keep its configuration where keypaste does not look"
            : path is null ? $"keypaste cannot tell where {client.DisplayName} keeps its configuration" : null;

        return cannot is null ? absent with { Found = true, ConfigPath = path } : absent with { Found = true, Problem = cannot };
    }

    /// <summary>The plan that registers <paramref name="registration"/> with a client <see cref="Detect"/> found.</summary>
    /// <param name="client">The client.</param>
    /// <param name="detection">What <see cref="Detect"/> found.</param>
    /// <param name="registration">What the client is told to start.</param>
    /// <param name="link">The Linux link the registration starts, when it must first be made; it runs first.</param>
    /// <param name="plan">The plan, when there is one.</param>
    /// <param name="error">Why not: the client cannot be reached, or it would rewrite part of the registration.</param>
    /// <returns>Whether there is a plan.</returns>
    public static bool TryConnect(
        McpClient client,
        McpClientDetection detection,
        McpServerRegistration registration,
        McpBridgeLink? link,
        [NotNullWhen(true)] out McpSetupPlan? plan,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(registration);

        plan = null;

        if (Unreachable(client, detection) is { } unreachable)
        {
            error = unreachable;
            return false;
        }

        foreach (var marker in client.ExpansionMarkers(detection.Platform))
        {
            if (registration.CommandLine().FirstOrDefault(word => word.Contains(marker, StringComparison.Ordinal)) is { } rewritten)
            {
                error = $"{client.DisplayName} would rewrite {marker} in {rewritten}, so it would not start what keypaste registers";
                return false;
            }
        }

        List<McpSetupStep> steps = link is null ? [] : [link];

        if (detection.Program is { } program)
        {
            steps.Add(new McpClientCommand(program, client.Executable!, client.RemoveArguments(), MayFail: true));
            steps.Add(new McpClientCommand(program, client.Executable!, client.AddArguments(registration), MayFail: false));
        }
        else
        {
            steps.Add(Edit(client, detection.ConfigPath!, registration.FileEntry(client.TypedEntry)));
        }

        plan = new McpSetupPlan(client, McpSetupAction.Connect, steps);
        error = string.Empty;
        return true;
    }

    /// <summary>The plan that removes keypaste from a client <see cref="Detect"/> found and could reach.</summary>
    /// <param name="client">The client.</param>
    /// <param name="detection">What <see cref="Detect"/> found, with no problem.</param>
    /// <param name="reading">What <see cref="McpClientConfig.Read"/> found in the client's configuration.</param>
    /// <returns>No steps when the configuration holds no keypaste entry; otherwise the client's removal or the edit of its file.</returns>
    public static McpSetupPlan Remove(McpClient client, McpClientDetection detection, McpEntryReading reading)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(reading);

        if (Unreachable(client, detection) is { } unreachable)
        {
            throw new ArgumentException(unreachable, nameof(detection));
        }

        if (reading.Presence == McpEntryPresence.Absent)
        {
            return new McpSetupPlan(client, McpSetupAction.Remove, []);
        }

        McpSetupStep step = detection.Program is { } program
            ? new McpClientCommand(program, client.Executable!, client.RemoveArguments(), MayFail: false)
            : Edit(client, detection.ConfigPath!, entry: null);

        return new McpSetupPlan(client, McpSetupAction.Remove, [step]);
    }

    /// <summary>Runs exactly the plan's steps, in order, stopping at the first that fails.</summary>
    /// <remarks>A link that is not made stops the run before any client step, so nothing is registered through it.</remarks>
    public static McpSetupResult Apply(McpSetupPlan plan, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runner);

        if (plan.IsNotConnected)
        {
            return new McpSetupResult(McpSetupStatus.NotConnected, Said: null, Backup: null);
        }

        string? backup = null;

        foreach (var step in plan.Steps)
        {
            switch (step)
            {
                case McpBridgeLink link:
                    if (Apply(link) is { Status: not McpSetupStatus.Done } failed)
                    {
                        return failed;
                    }

                    break;

                case McpClientCommand command:
                    var result = command.Program.Run(runner, command.Arguments, ClientTimeout);

                    if (!result.ToolFound)
                    {
                        return new McpSetupResult(McpSetupStatus.NotInstalled, Said: null, backup);
                    }

                    if (!result.Succeeded && !command.MayFail)
                    {
                        return new McpSetupResult(McpSetupStatus.Refused, FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput), backup);
                    }

                    break;

                case McpConfigEdit edit:
                    var outcome = McpConfigFile.Apply(edit);
                    backup ??= outcome.Backup;

                    if (outcome.Kind == McpConfigOutcomeKind.Refused)
                    {
                        return new McpSetupResult(McpSetupStatus.Refused, outcome.Problem, backup);
                    }

                    if (outcome.Kind == McpConfigOutcomeKind.Unchanged && edit.Entry is null)
                    {
                        return new McpSetupResult(McpSetupStatus.NotConnected, Said: null, backup);
                    }

                    break;
            }
        }

        return new McpSetupResult(McpSetupStatus.Done, Said: null, backup);
    }

    /// <summary>Makes the link a client is registered through, and checks it starts this copy afterwards.</summary>
    /// <returns><see cref="McpSetupStatus.Done"/>, or <see cref="McpSetupStatus.LinkFailed"/> with keypaste's reason.</returns>
    public static McpSetupResult Apply(McpBridgeLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var result = link.Change == McpLinkChange.Replace ? link.Link.Replace() : link.Link.Link();

        if (result.Outcome is CliLinkOutcome.Linked or CliLinkOutcome.Replaced && link.Link.Read().State == CliLinkState.ThisCopy)
        {
            return new McpSetupResult(McpSetupStatus.Done, Said: null, Backup: null);
        }

        return new McpSetupResult(McpSetupStatus.LinkFailed, LinkFailure(link.Link, result), Backup: null);
    }

    private static string? Unreachable(McpClient client, McpClientDetection detection) => detection switch
    {
        { Found: false } => $"{client.DisplayName} is not installed on this machine",
        { Problem: { } problem } => problem,
        { Program: null, ConfigPath: null } => $"keypaste cannot tell how to reach {client.DisplayName}",
        _ => null,
    };

    private static McpConfigEdit Edit(McpClient client, string path, JsonObject? entry)
    {
        var backup = McpConfigFile.BackupPath(path);

        return new McpConfigEdit(path, client.Configuration!.ServersKey, entry)
        {
            Backup = File.Exists(path) && !File.Exists(backup) ? backup : null,
        };
    }

    private static bool IsInstalled(McpClient client, McpConfigLocation location, McpClientMachine machine)
    {
        if (location.Folders(machine).Any(Directory.Exists))
        {
            return true;
        }

        if (client.Launcher is { } launcher && McpClientProgram.Find(launcher, client, machine) is not null)
        {
            return true;
        }

        return machine is { LooksInInstallFolders: true, Platform: McpClientPlatform.MacOS }
            && client.MacApp is { } app
            && (Directory.Exists(Path.Combine("/", _applications, app))
                || (machine.Home is { } home && Directory.Exists(Path.Combine(home, _applications, app))));
    }

    private static string LinkFailure(ICliLink link, CliLinkResult result) => result switch
    {
        { Outcome: CliLinkOutcome.Linked or CliLinkOutcome.Replaced } => "it does not start this copy afterwards",
        { Outcome: CliLinkOutcome.Cancelled } => "the system's dialog was dismissed",
        { Refusal: CliLinkRefusal.BackupExists } => $"{link.BackupPath} already exists, and keypaste never overwrites it",
        { Refusal: CliLinkRefusal.NotKeypaste } => "it changed before keypaste linked it",
        { Refusal: CliLinkRefusal.Translocated } => "macOS runs this copy from a temporary location",
        { Refusal: CliLinkRefusal.UnsafeProgram } => "this copy's path holds a line break",
        _ => result.Reason ?? "the link was not made",
    };

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
}
