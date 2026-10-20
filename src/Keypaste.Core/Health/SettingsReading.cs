using System.Globalization;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
using Keypaste.Core.Projects;
using Keypaste.Core.Recent;
using Keypaste.Core.Recommendations;
using Keypaste.Core.Settings;

namespace Keypaste.Core.Health;

/// <summary>One of keypaste's settings files that exists, and whether it parses.</summary>
/// <param name="Name">Its file name, such as <c>app.toml</c>.</param>
/// <param name="Path">Its full path.</param>
/// <param name="Finding">What doctor says of it, or null when it parses.</param>
public sealed record SettingsReading(string Name, string Path, HealthFinding? Finding)
{
    private const string _subject = "settings";

    /// <summary>Reads each settings file under keypaste's home that exists, as the program that uses it reads it.</summary>
    /// <param name="home"><c>KEYPASTE_HOME</c>, or null for the default home.</param>
    /// <returns>One reading per file that exists, in a fixed order.</returns>
    public static IReadOnlyList<SettingsReading> ReadAll(string? home)
    {
        (string Name, string Path, Func<string, string, HealthFinding?> Check)[] files =
        [
            (KeypasteHome.SettingsFileName, KeypasteHome.SettingsPath(home), AppToml),
            (KeypasteHome.ClientsFileName, KeypasteHome.ClientsPath(home), ClientsToml),
            (KeypasteHome.RecentFileName, KeypasteHome.RecentPath(home), RecentToml),
            (KeypasteHome.ProjectsFileName, KeypasteHome.ProjectsPath(home), ProjectsJson),
            (KeypasteHome.RecommendationsFileName, KeypasteHome.RecommendationsPath(home), RecommendationsJson),
            (KeypasteHome.PolicyFileName, KeypasteHome.PolicyPath(home), RetiredPolicy),
        ];

        return [.. files.Where(file => File.Exists(file.Path)).Select(file => new SettingsReading(file.Name, file.Path, file.Check(file.Name, file.Path)))];
    }

    /// <summary>What doctor says of the settings files: one line naming those that parse, and one per file that does not.</summary>
    /// <param name="files">From <see cref="ReadAll"/>.</param>
    /// <returns>The findings.</returns>
    public static IReadOnlyList<HealthFinding> Findings(IReadOnlyList<SettingsReading> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return [new HealthFinding(HealthArea.Settings, _subject, HealthStatus.Ok, "no settings files yet")];
        }

        List<string> parsed = [.. files.Where(file => file.Finding is null).Select(file => file.Name)];
        List<HealthFinding> findings = [.. files.Select(file => file.Finding).OfType<HealthFinding>()];

        if (parsed.Count > 0)
        {
            var says = HealthCheck.Joined(parsed) + (parsed.Count == 1 ? " parses" : " parse");
            findings.Insert(0, new HealthFinding(HealthArea.Settings, _subject, HealthStatus.Ok, says));
        }

        return findings;
    }

    private static HealthFinding? AppToml(string name, string path)
    {
        if (AppSettings.TryLoad(path, out _, out var problem))
        {
            return null;
        }

        var fix = LineOf(problem) is { } line
            ? $"fix {line}, or delete it and run `keypaste use <path>`; deleting also resets the app's preferences"
            : "fix or delete it, then run `keypaste use <path>`; deleting also resets the app's preferences";

        return Unparsed(name, problem, path, fix);
    }

    // Deleting the file would lift this computer's limits on every agent, so the fix never offers it.
    private static HealthFinding? ClientsToml(string name, string path)
    {
        if (AccessCaps.Probe(path, out var problem) == CapsFile.Usable)
        {
            return null;
        }

        var place = LineOf(problem) is { } line ? $"{line} of {path}" : path;
        return Unparsed(name, $"{problem}; agents are refused until it is fixed", path, $"fix {place}, and keep the file: it holds this computer's limits on every agent");
    }

    private static HealthFinding? RecentToml(string name, string path) =>
        RecentVaults.TryLoad(path, out _, out var problem)
            ? null
            : Unparsed(name, problem, path, "delete it; the app forgets only its list of recent vaults");

    private static HealthFinding? ProjectsJson(string name, string path) =>
        ProjectMappings.TryLoad(path, out _)
            ? null
            : Unparsed(name, "could not be read", path, "fix or delete it; deleting forgets which folder each project runs in");

    private static HealthFinding? RecommendationsJson(string name, string path) =>
        RecommendationDismissals.TryLoad(path, out _)
            ? null
            : Unparsed(name, "could not be read", path, "delete it; recommendations you dismissed show again");

    private static HealthFinding? RetiredPolicy(string name, string path) =>
        new(HealthArea.Settings, name, HealthStatus.Note, "keypaste no longer reads it; agents' settings are kept in the vault") { Where = path };

    private static HealthFinding Unparsed(string name, string says, string path, string fix) =>
        new(HealthArea.Settings, name, HealthStatus.Fix, says) { Where = path, Fix = fix };

    // The "line N" a parser's problem starts with, or null when it names no line.
    private static string? LineOf(string problem)
    {
        const string Prefix = "line ";
        var colon = problem.IndexOf(':', StringComparison.Ordinal);

        return colon > Prefix.Length
            && problem.StartsWith(Prefix, StringComparison.Ordinal)
            && int.TryParse(problem.AsSpan(Prefix.Length, colon - Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? problem[..colon]
            : null;
    }
}
