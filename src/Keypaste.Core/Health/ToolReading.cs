using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Health;

/// <summary>What one AI tool's own configuration registers for keypaste, and whether that starts.</summary>
/// <param name="Tool">The tool.</param>
/// <param name="Reading">What its configuration holds.</param>
/// <param name="Version">
/// What the registered keypaste's <c>--version</c> printed, this keypaste's version when the tool starts this keypaste, or
/// null when nothing was started.
/// </param>
/// <param name="Finding">What doctor says of it.</param>
public sealed record ToolReading(McpClient Tool, McpEntryReading Reading, string? Version, HealthFinding Finding)
{
    private const string _retired = "keypaste-mcp";

    /// <summary>Reads each AI tool keypaste connects that is installed or holds keypaste, starting only the keypaste each registers.</summary>
    /// <param name="scope">What to look at.</param>
    /// <param name="runner">
    /// Starts a registered keypaste with <c>--version</c>, once per file, and with <c>mcp --help</c> and its arguments;
    /// null starts nothing.
    /// </param>
    /// <returns>One reading per such tool, in the catalog's order; a tool neither installed nor registered is left out.</returns>
    /// <remarks>
    /// Only a registered command that is a full path to an existing file named <c>keypaste</c>, whose arguments start
    /// with <c>mcp</c> and are not <c>mcp serve</c>, <c>mcp setup</c> or <c>mcp policy</c>, is started; never the AI tool.
    /// <c>--help</c> follows <c>mcp</c> at once, so no option of the registration can take it as its value.
    /// </remarks>
    public static IReadOnlyList<ToolReading> ReadAll(HealthScope scope, IProcessRunner? runner)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var versions = new Dictionary<string, ProcessResult>(StringComparer.FromComparison(PathIdentity.Comparison));
        List<ToolReading> found = [];

        foreach (var tool in McpClientCatalog.For(scope.Machine.Platform))
        {
            var reading = McpClientConfig.Read(tool, scope.Machine);

            if (reading.Presence == McpEntryPresence.Present
                || reading is { Presence: McpEntryPresence.Unknown, Path: not null }
                || McpClientSetup.LooksInstalled(tool, scope.Machine))
            {
                found.Add(Read(tool, reading, scope, runner, versions));
            }
        }

        return found;
    }

    private static ToolReading Read(McpClient tool, McpEntryReading reading, HealthScope scope, IProcessRunner? runner, Dictionary<string, ProcessResult> versions)
    {
        var setup = $"run `keypaste setup --client {tool.Id}`";

        if (reading.Presence == McpEntryPresence.Unknown)
        {
            return Said(HealthStatus.Note, $"keypaste cannot tell whether it is connected: {reading.Reason}", reading.Path, fix: null);
        }

        if (reading.Presence == McpEntryPresence.Absent)
        {
            return Said(HealthStatus.Note, "installed but not connected", where: null, setup);
        }

        if (reading.Launch is not { Command: var command, Arguments: var arguments })
        {
            return Said(HealthStatus.Note, "keypaste is registered, but keypaste cannot read what it starts", reading.Path, setup);
        }

        if (Unstartable(command, arguments, scope.Machine.Platform, setup) is { } blocked)
        {
            return Said(HealthStatus.Fix, blocked.Says, command, blocked.Fix);
        }

        var version = scope.IsThisCopy(command) ? CoreInfo.Version : null;

        if (runner is not null)
        {
            if (version is null)
            {
                var key = PathIdentity.Canonical(command);

                if (!versions.TryGetValue(key, out var printed))
                {
                    versions[key] = printed = HealthCheck.Start(runner, command, ["--version"]);
                }

                if (!printed.Succeeded)
                {
                    return Said(HealthStatus.Fix, HealthCheck.Failed("could not be started", printed), command, setup);
                }

                version = HealthCheck.FirstLine(printed.StandardOutput) ?? string.Empty;
            }

            var help = HealthCheck.Start(runner, command, [McpServerLocator.BridgeArgument, "--help", .. arguments.Skip(1)]);

            if (!help.Succeeded)
            {
                var refused = help.ToolFound ? HealthCheck.Failed("refuses its arguments", help) : "could not be started";
                return Said(HealthStatus.Fix, refused, command, setup) with { Version = version };
            }
        }

        if (PinnedVault(arguments) is { } vault && !File.Exists(vault))
        {
            return Said(HealthStatus.Fix, "names a vault that is not there", vault, setup) with { Version = version };
        }

        var started = SemanticVersion.TryParse(version, out _) ? $"starts keypaste {version} from a place upgrades keep" : "starts keypaste from a place upgrades keep";
        return Said(HealthStatus.Ok, started, command, fix: null) with { Version = version };

        ToolReading Said(HealthStatus status, string says, string? where, string? fix) =>
            new(tool, reading, Version: null, new HealthFinding(HealthArea.Tool, tool.Id, status, says) { Where = where, Fix = fix });
    }

    // What stands in the way of starting the registration at all, or of it starting after an upgrade.
    private static (string Says, string Fix)? Unstartable(string command, IReadOnlyList<string> arguments, McpClientPlatform platform, string setup)
    {
        var name = Path.GetFileName(command);
        var comparison = platform == McpClientPlatform.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!Path.IsPathFullyQualified(command))
        {
            return ("starts a command that is not a full path", setup);
        }

        if (!File.Exists(command))
        {
            return ("starts a program that is not there", setup);
        }

        if (McpServerLocator.IsTransient(PathIdentity.Canonical(command), platform))
        {
            return ("starts keypaste from a disk image, gone once it is ejected", $"move keypaste to Applications, then {setup}");
        }

        if (platform == McpClientPlatform.Linux
            && (command.Contains("/.mount_", StringComparison.Ordinal) || command.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)))
        {
            return ("starts keypaste inside an AppImage, which an upgrade replaces", $"{setup}, which registers ~/.local/bin/keypaste");
        }

        if (!Named(name, McpServerLocator.FileName, comparison))
        {
            return (Named(name, _retired, comparison) ? $"starts {_retired}, which keypaste no longer ships" : $"starts {name}, which is not keypaste", setup);
        }

        if (arguments is not [McpServerLocator.BridgeArgument, ..] || arguments is [_, "serve" or "setup" or "policy", ..])
        {
            return ("starts keypaste, but not `keypaste mcp`", setup);
        }

        if (HealthCheck.LinkTarget(command, platform) is { Program: var program } && !File.Exists(program))
        {
            return ($"{command} names {program}, which is gone", "start the keypaste app you kept; it re-points ~/.local/bin/keypaste");
        }

        return null;
    }

    private static bool Named(string name, string program, StringComparison comparison) =>
        string.Equals(name, program, comparison) || string.Equals(name, program + ".exe", comparison);

    private static string? PinnedVault(IReadOnlyList<string> arguments)
    {
        const string Option = "--vault";

        for (var i = 1; i < arguments.Count; i++)
        {
            if (arguments[i].StartsWith(Option + "=", StringComparison.Ordinal))
            {
                return arguments[i][(Option.Length + 1)..];
            }

            if (string.Equals(arguments[i], Option, StringComparison.Ordinal))
            {
                return i + 1 < arguments.Count ? arguments[i + 1] : null;
            }
        }

        return null;
    }
}
