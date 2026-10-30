using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Clients;

/// <summary>The AppImage keypaste runs from on Linux, and the link a client is registered through instead of it.</summary>
/// <param name="Image">The AppImage file.</param>
/// <param name="Link">G.5's <c>~/.local/bin/keypaste</c>, which starts <paramref name="Image"/> with <c>cli</c>.</param>
public sealed record McpAppImage(string Image, ICliLink Link)
{
    /// <summary>The AppImage this process runs from, or null when it does not run from one.</summary>
    /// <remarks>
    /// <see cref="CliLinks.TargetFor"/> decides, so <c>APPIMAGE</c> and <c>APPDIR</c> must agree with where keypaste
    /// runs before either is believed.
    /// </remarks>
    public static McpAppImage? ForThisProcess()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return home is { Length: > 0 }
            && SemanticVersion.TryParse(CoreInfo.Version, out var version)
            && CliLinks.TargetFor(home, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("APPIMAGE"), Environment.GetEnvironmentVariable("APPDIR"))
                is { ThroughAppImage: true } target
            ? new McpAppImage(target.Program, new LinuxCliLink(home, target, version, Environment.GetEnvironmentVariable("PATH")))
            : null;
    }
}

/// <summary>What a client is told to start, and what must happen before it can.</summary>
/// <param name="Server">The bridge's path and the argument that selects it.</param>
/// <param name="Link">The link to make first, inside an AppImage when the link is absent, stale or another program; otherwise null.</param>
/// <param name="Note">What the person should know about the link left as it is, or null.</param>
public sealed record McpBridgeTarget(McpServerCommand Server, McpBridgeLink? Link, string? Note);

/// <summary>Finds the <c>keypaste</c> a client is told to start as <c>keypaste mcp</c>, at a path that outlives this run.</summary>
/// <remarks>
/// <para>
/// Windows and macOS install keypaste at a fixed path. Inside a Linux AppImage, whose mount moves on every launch and
/// whose file an upgrade replaces, a client is registered through G.5's <c>~/.local/bin/keypaste</c>, which a newer image
/// re-points when it starts (D-0334).
/// </para>
/// <para>
/// The bridge reads its vault, its audit log and its approver from <c>KEYPASTE_HOME</c> and <c>KEYPASTE_APPROVER</c>,
/// which a client need not pass on and Gemini CLI strips. <see cref="TryPin"/> writes them as flags when they differ
/// from what a bridge started without them would find (D-0389).
/// </para>
/// </remarks>
public static class McpServerLocator
{
    /// <summary>The CLI binary's file name, without the platform's extension.</summary>
    public const string FileName = "keypaste";

    /// <summary>What selects the bridge: <c>keypaste mcp</c>.</summary>
    public const string BridgeArgument = "mcp";

    private const string _volumes = "/Volumes/";

    private const string _moveFirst = "move keypaste to Applications, then connect";

    /// <summary>The CLI binary's file name on this platform.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? FileName + ".exe" : FileName;

    /// <summary>Finds <c>keypaste</c> beside <paramref name="besideDirectory"/>, then on PATH.</summary>
    /// <param name="besideDirectory">The running program's directory.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    /// <returns>The bridge, or null when neither place has it.</returns>
    public static McpServerCommand? Find(string? besideDirectory, string? pathVariable)
    {
        if (besideDirectory is { Length: > 0 }
            && Path.Combine(besideDirectory, ExecutableName) is var beside
            && File.Exists(beside))
        {
            return new McpServerCommand(Path.GetFullPath(beside), [BridgeArgument]);
        }

        foreach (var directory in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), ExecutableName);
            if (File.Exists(candidate))
            {
                return new McpServerCommand(Path.GetFullPath(candidate), [BridgeArgument]);
            }
        }

        return null;
    }

    /// <summary>Where <see cref="Find"/> looked, for a message that says so.</summary>
    public static string Places(string? besideDirectory) =>
        besideDirectory is { Length: > 0 } ? $"in {besideDirectory} and on PATH" : "on PATH";

    /// <summary>The bridge <c>keypaste setup</c> registers: the <c>keypaste</c> running it, or the AppImage's link.</summary>
    /// <param name="processPath">The running program.</param>
    /// <param name="appImage">The AppImage keypaste runs from, or null.</param>
    /// <param name="target">The bridge, when there is one.</param>
    /// <param name="error">Why not.</param>
    /// <returns>Whether there is a bridge to register.</returns>
    /// <remarks>The client starts this binary, so setup and the bridge it registers are always one version.</remarks>
    public static bool TryForSetup(
        string? processPath,
        McpAppImage? appImage,
        [NotNullWhen(true)] out McpBridgeTarget? target,
        out string error) =>
        TryForSetup(processPath, appImage, McpClientMachine.CurrentPlatform, out target, out error);

    /// <summary>The bridge the desktop registers: the <c>keypaste</c> beside it, else on PATH, or the AppImage's link.</summary>
    /// <param name="appDirectory">The desktop's own directory.</param>
    /// <param name="appImage">The AppImage the desktop runs from, or null.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    /// <param name="target">The bridge, when there is one.</param>
    /// <param name="error">Why not.</param>
    /// <returns>Whether there is a bridge to register.</returns>
    /// <remarks>
    /// Beside the app first: the packages carry the two together, and a mismatched pair is a class of bug nobody would
    /// enjoy diagnosing.
    /// </remarks>
    public static bool TryForDesktop(
        string appDirectory,
        McpAppImage? appImage,
        string? pathVariable,
        [NotNullWhen(true)] out McpBridgeTarget? target,
        out string error) =>
        TryForDesktop(appDirectory, appImage, pathVariable, McpClientMachine.CurrentPlatform, out target, out error);

    /// <summary>What a registration pins with the bridge's own flags.</summary>
    /// <param name="machine">The machine, whose home names the default <c>.keypaste</c>.</param>
    /// <param name="home"><c>KEYPASTE_HOME</c>, or the app's home; null or empty when unset.</param>
    /// <param name="approverOverride"><c>KEYPASTE_APPROVER</c>; null or empty when unset.</param>
    /// <param name="namedVault">The vault named for this run (<c>--vault</c>, or the vault the app has unlocked), or null.</param>
    /// <param name="chosenVault">The vault chosen with <c>keypaste use</c>, or null.</param>
    /// <param name="pins">The flags, when they can be decided.</param>
    /// <returns>False only for a home other than the default with no vault named or chosen, which no flag can stand in for.</returns>
    /// <remarks>
    /// <para>
    /// Under the default home a bridge started without either variable finds the same vault, log and owner, so only a
    /// named vault other than the chosen one is pinned, and a client keeps following <c>keypaste use</c>. A set
    /// <c>KEYPASTE_APPROVER</c> adds <c>--approver</c>.
    /// </para>
    /// <para>
    /// Under any other home all four are pinned: the vault named, else the chosen one; that home's log; that home's
    /// ceilings, so an agent's own narrowing lowers the file that home's owner reads; and the approver, else the pipe
    /// that home's owner of that vault listens on. The machine's home is read from the machine
    /// rather than the real profile, so a test never compares against a developer's own.
    /// </para>
    /// </remarks>
    public static bool TryPin(
        McpClientMachine machine,
        string? home,
        string? approverOverride,
        string? namedVault,
        string? chosenVault,
        [NotNullWhen(true)] out McpServerPins? pins)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var approver = approverOverride is { Length: > 0 } ? approverOverride : null;

        if (home is not { Length: > 0 } || IsDefaultHome(machine, home))
        {
            var vault = namedVault is not null && !ChosenVault.Same(chosenVault, namedVault) ? namedVault : null;
            pins = new McpServerPins(vault, AuditLogPath: null, approver);
            return true;
        }

        if ((namedVault ?? chosenVault) is not { } pinned)
        {
            pins = null;
            return false;
        }

        pins = new McpServerPins(
            pinned,
            KeypasteHome.AuditPath(home),
            approver ?? ApproverEndpoint.Resolve(null, null, VaultIdentity.Of(KeypasteHome.Resolve(home), pinned)),
            KeypasteHome.ClientsPath(home));
        return true;
    }

    internal static bool TryForSetup(
        string? processPath,
        McpAppImage? appImage,
        McpClientPlatform platform,
        [NotNullWhen(true)] out McpBridgeTarget? target,
        out string error)
    {
        if (appImage is not null)
        {
            return TryThrough(appImage, out target, out error);
        }

        if (processPath is null || !string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            target = null;
            error = $"setup registers the {FileName} that runs it, and this is {processPath ?? "an unknown program"}; "
                + $"run {FileName} itself rather than through dotnet";
            return false;
        }

        return TryStable(processPath, platform, out target, out error);
    }

    internal static bool TryForDesktop(
        string appDirectory,
        McpAppImage? appImage,
        string? pathVariable,
        McpClientPlatform platform,
        [NotNullWhen(true)] out McpBridgeTarget? target,
        out string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(appDirectory);

        if (appImage is not null)
        {
            return TryThrough(appImage, out target, out error);
        }

        if (Find(appDirectory, pathVariable) is not { } found)
        {
            target = null;
            error = $"{FileName} was not found {Places(appDirectory)}, so there is nothing an AI tool could start";
            return false;
        }

        return TryStable(found.Path, platform, out target, out error);
    }

    /// <summary>Whether a copy at <paramref name="canonical"/> is gone once its disk image is ejected or its translocated app quits.</summary>
    /// <param name="canonical">The copy's path with every link resolved.</param>
    /// <param name="platform">The platform it runs on; only macOS has either.</param>
    internal static bool IsTransient(string canonical, McpClientPlatform platform) =>
        platform == McpClientPlatform.MacOS && (canonical.StartsWith(_volumes, StringComparison.Ordinal) || MacCliLink.IsTranslocated(canonical));

    // A macOS path reached through a link, G.5's /usr/local/bin/keypaste among them, is registered as the bundle it names.
    private static bool TryStable(string path, McpClientPlatform platform, [NotNullWhen(true)] out McpBridgeTarget? target, out string error)
    {
        var stable = platform == McpClientPlatform.MacOS ? PathIdentity.Canonical(path) : Path.GetFullPath(path);

        if (IsTransient(stable, platform))
        {
            target = null;
            error = _moveFirst;
            return false;
        }

        target = new McpBridgeTarget(new McpServerCommand(stable, [BridgeArgument]), Link: null, Note: null);
        error = string.Empty;
        return true;
    }

    // Registered as the link in every state, so a client never names the image file an upgrade replaces.
    private static bool TryThrough(McpAppImage appImage, [NotNullWhen(true)] out McpBridgeTarget? target, out string error)
    {
        var link = appImage.Link;
        var status = link.Read();
        var server = new McpServerCommand(link.LinkPath, [BridgeArgument]);
        error = string.Empty;

        // A replacement keeps what is there as the backup, and the backup a person already has is theirs for good.
        if (status is { State: CliLinkState.Foreign, BackupExists: true })
        {
            target = null;
            error = $"{link.LinkPath} is {Foreign(status)}, and {link.BackupPath} already exists, which keypaste never overwrites. "
                + $"Move or delete it to replace {link.LinkPath}. Nothing was changed.";
            return false;
        }

        target = status switch
        {
            { State: CliLinkState.Absent } => new(server, new McpBridgeLink(link, appImage.Image, McpLinkChange.Create, Was: null), Note: null),
            { State: CliLinkState.OtherCopy, Stale: true, LinkedTo: var named } =>
                new(server, new McpBridgeLink(link, appImage.Image, McpLinkChange.Repoint, named is not null && File.Exists(named) ? named : $"the missing {named}"), Note: null),
            { State: CliLinkState.OtherCopy } => new(server, Link: null, $"{link.LinkPath} runs keypaste from {status.LinkedTo}"),
            { State: CliLinkState.Foreign } => new(server, new McpBridgeLink(link, appImage.Image, McpLinkChange.Replace, Foreign(status)), Note: null),
            _ => new(server, Link: null, Note: null),
        };
        return true;
    }

    private static string Foreign(CliLinkStatus status) => status.LinkedTo is { } linkedTo ? $"a link to {linkedTo}" : "another program";

    private static bool IsDefaultHome(McpClientMachine machine, string home) =>
        machine.Home is { } profile
        && string.Equals(
            PathIdentity.Canonical(KeypasteHome.Resolve(home)),
            PathIdentity.Canonical(Path.Combine(profile, KeypasteHome.DirectoryName)),
            PathIdentity.Comparison);
}
