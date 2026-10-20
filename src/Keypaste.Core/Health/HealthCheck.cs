using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Health;

/// <summary>How a finding of <c>keypaste doctor</c> reads.</summary>
public enum HealthStatus
{
    /// <summary>It works.</summary>
    Ok,

    /// <summary>Something stands in the way, and the finding says what to do.</summary>
    Fix,

    /// <summary>Worth knowing, but nothing is broken: the vault is locked, or a tool is installed and not connected.</summary>
    Note,
}

/// <summary>What a finding is about, in the order <see cref="HealthCheck.RunAsync"/> reports them.</summary>
public enum HealthArea
{
    /// <summary>Whether this keypaste, the one holding the vault, each AI tool's and PATH's are one version.</summary>
    Versions,

    /// <summary>Whether the vault keypaste would use is there and is a KeePass database.</summary>
    Vault,

    /// <summary>Whether an AI tool's registration starts keypaste and survives upgrades.</summary>
    Tool,

    /// <summary>Whether the keypaste app or <c>keypaste agent</c> holds the vault and answers.</summary>
    Owner,

    /// <summary>Whether keypaste's settings files parse.</summary>
    Settings,

    /// <summary>Whether a terminal finds a <c>keypaste</c> on PATH, and which.</summary>
    OnPath,
}

/// <summary>One thing <c>keypaste doctor</c> says.</summary>
/// <param name="Area">What it is about.</param>
/// <param name="Subject">What it names, such as <c>vault</c> or an AI tool's id.</param>
/// <param name="Status">Whether it works, needs fixing, or is worth knowing.</param>
/// <param name="Says">The sentence a person reads. A part another program wrote is shown as it was read, for the reader to sanitize.</param>
public sealed record HealthFinding(HealthArea Area, string Subject, HealthStatus Status, string Says)
{
    /// <summary>Gets the file the finding is about, or null.</summary>
    public string? Where { get; init; }

    /// <summary>Gets what to do; every <see cref="HealthStatus.Fix"/> has one, and a note may.</summary>
    public string? Fix { get; init; }
}

/// <summary>What <c>keypaste doctor</c> looks at.</summary>
/// <param name="Machine">Where the AI tools keep their files, and PATH.</param>
/// <param name="Environment">This process's environment, read for <c>KEYPASTE_HOME</c>, <c>KEYPASTE_VAULT</c> and <c>KEYPASTE_APPROVER</c>.</param>
/// <param name="NamedVault">The vault named with <c>--vault</c>, or null.</param>
/// <param name="ProcessPath">This keypaste's own file, or null when it is not known.</param>
/// <param name="AppImage">The AppImage this keypaste runs from on Linux, or null.</param>
public sealed record HealthScope(
    McpClientMachine Machine,
    Func<string, string?> Environment,
    string? NamedVault,
    string? ProcessPath,
    McpAppImage? AppImage)
{
    /// <summary>Gets <c>KEYPASTE_HOME</c>, or null when it is unset.</summary>
    public string? Home => Environment(KeypasteHome.EnvironmentVariable) is { Length: > 0 } home ? home : null;

    /// <summary>Whether <paramref name="path"/> starts this keypaste: it is this file, or a Linux link naming this file or this AppImage.</summary>
    internal bool IsThisCopy(string path)
    {
        if (ProcessPath is not { Length: > 0 } self)
        {
            return false;
        }

        return Same(path, self)
            || (HealthCheck.LinkTarget(path, Machine.Platform) is { Program: var named }
                && (Same(named, self) || (AppImage is { Image: var image } && Same(named, image))));
    }

    private static bool Same(string left, string right) =>
        string.Equals(PathIdentity.Canonical(left), PathIdentity.Canonical(right), PathIdentity.Comparison);
}

/// <summary>
/// The checks <c>keypaste doctor</c> runs: the versions, the vault, each AI tool's registration, who holds the vault,
/// the settings files and PATH, each saying what to fix.
/// </summary>
/// <remarks>
/// It only reads (D-0467). No vault is opened and no password asked for; nothing is written, created or started but
/// the <c>keypaste</c> each tool registers and the one PATH finds, with <c>--version</c>, and <c>mcp --help</c> with the
/// registration's arguments; the owner is attached to only to learn whether it answers and its version.
/// </remarks>
public static class HealthCheck
{
    private static readonly Encoding _utf8 = new UTF8Encoding(false);

    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Runs every check.</summary>
    /// <param name="scope">What to look at.</param>
    /// <param name="runner">Starts the registered keypaste and PATH's, and nothing else.</param>
    /// <param name="cancellationToken">Ends the wait for the owner.</param>
    /// <returns>The findings: versions, vault, AI tools, owner, settings, then PATH.</returns>
    public static async Task<IReadOnlyList<HealthFinding>> RunAsync(HealthScope scope, IProcessRunner runner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(runner);

        var vault = VaultReading.Read(scope);
        var asking = OwnerReading.AskAsync(scope, vault.Path, cancellationToken);
        var tools = ToolReading.ReadAll(scope, runner);
        var path = PathReading.Read(scope, runner);
        var settings = SettingsReading.ReadAll(scope.Home);
        var owner = await asking.ConfigureAwait(false);

        List<HealthFinding> findings = [.. Versions(CoreInfo.Version, owner, tools, path), .. vault.Findings];

        if (tools.Count == 0)
        {
            findings.Add(new HealthFinding(HealthArea.Tool, "AI tools", HealthStatus.Note, "no AI tool keypaste connects is installed here"));
        }

        findings.AddRange(tools.Select(tool => tool.Finding));
        findings.Add(owner.Finding);
        findings.AddRange(SettingsReading.Findings(settings));
        findings.Add(path.Finding);
        return findings;
    }

    /// <summary>Compares this keypaste's version with the owner's, each registration's and PATH's.</summary>
    /// <param name="here">This keypaste's version.</param>
    /// <param name="owner">Who holds the vault.</param>
    /// <param name="tools">Each AI tool's registration.</param>
    /// <param name="path">PATH's keypaste.</param>
    /// <returns>One fix per copy that differs, or one finding naming everything that matched.</returns>
    internal static List<HealthFinding> Versions(string here, OwnerReading owner, IReadOnlyList<ToolReading> tools, PathReading path)
    {
        const string Subject = "versions";
        const string Update = "update this keypaste, or run the one the app installed";
        List<string> matched = [$"{here} here"];

        if (!SemanticVersion.TryParse(here, out var mine))
        {
            return [Versioned(HealthStatus.Ok, matched[0], fix: null)];
        }

        List<HealthFinding> fixes = [];

        if (owner.Answer is OwnerAnswer.Unlocked or OwnerAnswer.Locked)
        {
            var holding = OwnerReading.Holding(owner.Kind);
            var restart = OwnerReading.Restart(owner.Kind);

            switch (Order(owner.Version, mine))
            {
                case 0:
                    matched.Add(owner.Kind switch
                    {
                        OwnerKind.TerminalAgent => "in keypaste agent",
                        OwnerKind.DesktopApp => "in the keypaste app",
                        _ => "in the keypaste process holding the vault",
                    });
                    break;
                case > 0:
                    fixes.Add(Versioned(HealthStatus.Fix, $"{holding} is {owner.Version}, this is {here}", Update));
                    break;
                case < 0:
                    fixes.Add(Versioned(HealthStatus.Fix, $"{holding} is {owner.Version}, this is {here}", restart));
                    break;
                default:
                    var says = owner.Version is null ? $"{holding} is older than this keypaste" : $"{holding} gives a version keypaste cannot read";
                    fixes.Add(Versioned(HealthStatus.Fix, says, restart));
                    break;
            }
        }

        var compared = 0;

        foreach (var tool in tools.Where(reading => reading.Version is not null))
        {
            var name = tool.Tool.DisplayName;
            var kept = $"run `keypaste setup --client {tool.Tool.Id}` from the keypaste you keep";

            switch (Order(tool.Version, mine))
            {
                case 0:
                    compared++;
                    break;
                case > 0:
                    fixes.Add(Versioned(HealthStatus.Fix, $"{name} starts keypaste {tool.Version}, this is {here}", Update));
                    break;
                case < 0:
                    fixes.Add(Versioned(HealthStatus.Fix, $"{name} starts keypaste {tool.Version}, this is {here}", kept));
                    break;
                default:
                    fixes.Add(Versioned(HealthStatus.Fix, $"{name} starts a keypaste whose version keypaste cannot read", kept));
                    break;
            }
        }

        if (compared > 0)
        {
            matched.Add("in each AI tool");
        }

        if (path is { ThisCopy: false, Found: { } found, Version: { } version })
        {
            const string Other = "a terminal runs that one: update or remove it";

            switch (Order(version, mine))
            {
                case 0:
                    matched.Add("on PATH");
                    break;
                case > 0:
                    fixes.Add(Versioned(HealthStatus.Fix, $"a terminal runs keypaste {version}, this is {here}", Update) with { Where = found });
                    break;
                case < 0:
                    fixes.Add(Versioned(HealthStatus.Fix, $"a terminal runs keypaste {version}, this is {here}", Other) with { Where = found });
                    break;
                default:
                    fixes.Add(Versioned(HealthStatus.Fix, "a terminal runs a keypaste whose version keypaste cannot read", Other) with { Where = found });
                    break;
            }
        }

        return fixes.Count > 0 ? fixes : [Versioned(HealthStatus.Ok, Joined(matched), fix: null)];

        static HealthFinding Versioned(HealthStatus status, string says, string? fix) =>
            new(HealthArea.Versions, Subject, status, says) { Fix = fix };
    }

    /// <summary>Starts <paramref name="program"/> with <paramref name="arguments"/>, without a shell, for at most the probe timeout.</summary>
    internal static ProcessResult Start(IProcessRunner runner, string program, IReadOnlyList<string> arguments) =>
        runner.Run(program, arguments, stdin: null, _utf8, McpClientSetup.ProbeTimeout);

    /// <summary>The first line of <paramref name="text"/> that holds anything, trimmed; null when none does.</summary>
    internal static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);

    /// <summary><paramref name="says"/>, followed by the first line a program that failed wrote, when it wrote one.</summary>
    internal static string Failed(string says, ProcessResult result) =>
        (FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput)) is { } said ? $"{says}: {said}" : says;

    /// <summary>The program a Linux link keypaste wrote starts, or null when <paramref name="path"/> is not one.</summary>
    internal static CliTarget? LinkTarget(string path, McpClientPlatform platform)
    {
        if (platform != McpClientPlatform.Linux)
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);

            return file.Exists
                && file.Length <= CliLinkScript.MaximumLength
                && CliLinkScript.TryRead(_strict.GetString(File.ReadAllBytes(path)), out var target, out _)
                ? target
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>The parts as a sentence lists them: <c>a, b and c</c>.</summary>
    internal static string Joined(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    // Null when either version is one keypaste cannot read.
    private static int? Order(string? version, SemanticVersion mine) =>
        SemanticVersion.TryParse(version, out var theirs) ? Math.Sign(theirs.CompareTo(mine)) : null;
}
