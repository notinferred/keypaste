using Keypaste.Core.Clients;
using Keypaste.Core.Linking;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Health;

/// <summary>The <c>keypaste</c> a terminal runs, found on PATH as a tool's command is found (D-0436).</summary>
/// <param name="Found">The first <c>keypaste</c> on PATH's full-path entries, or null when there is none.</param>
/// <param name="ThisCopy">Whether it starts this keypaste, itself or through the Linux link.</param>
/// <param name="Version">What its <c>--version</c> printed, this keypaste's version for this copy, or null when nothing was started.</param>
/// <param name="Finding">What doctor says of it.</param>
public sealed record PathReading(string? Found, bool ThisCopy, string? Version, HealthFinding Finding)
{
    private const string _subject = "PATH";

    /// <summary>Finds the first <c>keypaste</c> on PATH, and starts its <c>--version</c> when it is another copy.</summary>
    /// <param name="scope">What to look at.</param>
    /// <param name="runner">Starts another copy's <c>--version</c>; null starts nothing.</param>
    /// <returns>What a terminal runs.</returns>
    /// <remarks>An entry that is not a full path is skipped, because a shell would read it against whatever directory it is in.</remarks>
    public static PathReading Read(HealthScope scope, IProcessRunner? runner)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (FirstOnPath(scope.Machine.PathVariable) is not { } found)
        {
            return new PathReading(Found: null, ThisCopy: false, Version: null, Said(HealthStatus.Fix, "a terminal finds no keypaste on PATH", where: null, NotFound(scope)));
        }

        if (scope.IsThisCopy(found))
        {
            return new PathReading(found, ThisCopy: true, CoreInfo.Version, Said(HealthStatus.Ok, "a terminal runs this keypaste", found, fix: null));
        }

        if (runner is null)
        {
            return new PathReading(found, ThisCopy: false, Version: null, Said(HealthStatus.Ok, "a terminal runs another keypaste", found, fix: null));
        }

        var printed = HealthCheck.Start(runner, found, ["--version"]);

        return printed.Succeeded
            ? new PathReading(found, ThisCopy: false, HealthCheck.FirstLine(printed.StandardOutput) ?? string.Empty, Said(HealthStatus.Ok, "a terminal runs another keypaste", found, fix: null))
            : new PathReading(
                found,
                ThisCopy: false,
                Version: null,
                Said(HealthStatus.Fix, HealthCheck.Failed("a terminal runs another keypaste, which could not be started", printed), found, "a terminal runs that one: update or remove it"));
    }

    private static string? FirstOnPath(string? pathVariable)
    {
        foreach (var entry in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Path.IsPathFullyQualified(entry)
                && Path.Combine(entry, McpServerLocator.ExecutableName) is var candidate
                && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    // How keypaste reaches PATH where this copy came from: the app's Link, the Linux link's folder, or the installer's change.
    private static string NotFound(HealthScope scope)
    {
        var platform = scope.Machine.Platform;
        var self = scope.ProcessPath;

        if (platform == McpClientPlatform.Linux && scope.Machine.Home is { } home && File.Exists(LinuxCliLink.LinkPathFor(home)))
        {
            return "add ~/.local/bin to PATH in your shell's startup file";
        }

        if (scope.AppImage is not null
            || (platform == McpClientPlatform.MacOS && Path.GetDirectoryName(self) is { Length: > 0 } directory && CliLinks.BundledProgram(directory) is not null))
        {
            return "in the keypaste app, open Settings and press Link";
        }

        if (platform == McpClientPlatform.Windows && self is not null && scope.Machine.LocalAppData is { } local && IsUnder(self, Path.Combine(local, "Programs", "keypaste")))
        {
            return "open a new terminal; the installer's PATH change reaches only terminals opened after it";
        }

        return "put the folder holding keypaste on PATH";
    }

    private static bool IsUnder(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static HealthFinding Said(HealthStatus status, string says, string? where, string? fix) =>
        new(HealthArea.OnPath, _subject, status, says) { Where = where, Fix = fix };
}
