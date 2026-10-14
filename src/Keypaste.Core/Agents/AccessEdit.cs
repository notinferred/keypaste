using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Agents;

/// <summary>A change to one agent's settings, as <c>keypaste mcp policy</c> names it; what it leaves out stays as it is.</summary>
/// <remarks>
/// <para>
/// A mode and a limit apply to <see cref="Project"/>'s row, or with no project to the agent's default.
/// What the agent sees, values and commands apply to the whole agent, so a change naming a project
/// holds none of them. <see cref="Reset"/> starts from the defaults, or with a project removes its row.
/// </para>
/// <para>
/// Its words are the CLI's, and they are what crosses the pipe: <see cref="TryCreate"/> reads them
/// in the CLI and again in the owner, so the owner trusts nothing it did not parse itself. A time
/// crosses in UTC, as the record holds it: the CLI, the panel and the bridge read a person's or an
/// agent's time on this computer's clock first (<see cref="UntilTime.TryRecord"/>).
/// </para>
/// </remarks>
public sealed record AccessEdit
{
    /// <summary>The word that removes a limit.</summary>
    public const string None = "none";

    private AccessEdit()
    {
    }

    /// <summary>Whether it starts from the defaults, or with a project removes that row.</summary>
    public bool Reset { get; private init; }

    /// <summary>The row a mode and a limit apply to, or null for the agent's default.</summary>
    public ProjectScope? Project { get; private init; }

    /// <summary>The mode to set, or null to leave it.</summary>
    public AgentMode? Mode { get; private init; }

    /// <summary>What the agent may see, or null to leave it.</summary>
    public EntryExposure? See { get; private init; }

    /// <summary>Whether it is given values, or null to leave it.</summary>
    public bool? Values { get; private init; }

    /// <summary>Whether it may run commands, or null to leave it.</summary>
    public bool? Run { get; private init; }

    /// <summary>Releases an hour Automatic may make, or null to leave it.</summary>
    public int? PerHour { get; private init; }

    /// <summary>Whether the hourly limit is removed.</summary>
    public bool NoPerHour { get; private init; }

    /// <summary>When Automatic stops, or null to leave it.</summary>
    public DateTimeOffset? Until { get; private init; }

    /// <summary>Whether the time limit is removed.</summary>
    public bool NoUntil { get; private init; }

    /// <summary>
    /// The change the app's "Use the vault's settings" asks for: nothing in the record changes, and once
    /// the person confirms what the vault gives beyond this computer's ceilings, they are lifted to it.
    /// </summary>
    /// <remarks>The app's own; no CLI word or agent request makes it, and nothing writes it to the vault.</remarks>
    public static AccessEdit VaultSettings { get; } = new() { UsesVault = true };

    /// <summary>Whether it is <see cref="VaultSettings"/>, which names nothing to change.</summary>
    public bool UsesVault { get; private init; }

    /// <summary>
    /// The change Always for in an approval prompt asks for: Automatic for one project environment from
    /// now on, so an end its row had, which may be why the prompt was shown, is removed, and an hourly
    /// limit stays.
    /// </summary>
    /// <param name="scope">The project environment.</param>
    /// <returns>The change.</returns>
    public static AccessEdit Automatic(ProjectScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new AccessEdit { Project = scope, Mode = AgentMode.Automatic, NoUntil = true };
    }

    /// <summary>Reads a change from the CLI's words.</summary>
    /// <param name="project"><c>&lt;project&gt;[:&lt;environment&gt;]</c>, or null.</param>
    /// <param name="mode"><c>ask</c>, <c>allow-for-a-while</c> or <c>automatic</c>, or null.</param>
    /// <param name="see">Selectors as <c>--expose</c> takes them, or null.</param>
    /// <param name="values"><c>yes</c> or <c>no</c>, or null.</param>
    /// <param name="run"><c>on</c> or <c>off</c>, or null.</param>
    /// <param name="perHour">A number from 1 to <see cref="AccessLimit.MaximumPerHour"/>, or <see cref="None"/>, or null.</param>
    /// <param name="until">A time in UTC as <see cref="UntilTime.Recorded"/> writes it, or <see cref="None"/>, or null.</param>
    /// <param name="reset">Whether it starts from the defaults.</param>
    /// <param name="edit">The change, when the words make one.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether the words make a change.</returns>
    public static bool TryCreate(
        string? project,
        string? mode,
        IReadOnlyList<string>? see,
        string? values,
        string? run,
        string? perHour,
        string? until,
        bool reset,
        [NotNullWhen(true)] out AccessEdit? edit,
        out string error)
    {
        edit = null;
        ProjectScope? scope = null;
        AgentMode? parsedMode = null;
        EntryExposure? parsedSee = null;

        if (project is not null && !ProjectScope.TryParseName(project, out scope, out error))
        {
            return false;
        }

        if (mode is not null)
        {
            if (!AgentModes.TryParse(mode, out var known))
            {
                error = "a mode is ask, allow-for-a-while or automatic";
                return false;
            }

            parsedMode = known;
        }

        if (see is not null && !EntryExposure.TryCreate(see, out parsedSee, out error))
        {
            return false;
        }

        if (!TryWord(values, "yes", "no", "values", out var parsedValues, out error)
            || !TryWord(run, "on", "off", "run", out var parsedRun, out error)
            || !TryPerHour(perHour, out var parsedPerHour, out error)
            || !TryUntil(until, out var parsedUntil, out error))
        {
            return false;
        }

        var limits = perHour is not null || until is not null;

        if (scope is not null && (see is not null || values is not null || run is not null))
        {
            error = "a project row holds a mode and a limit; what the agent sees, values and run apply to the whole agent";
            return false;
        }

        if (scope is not null && reset && (mode is not null || limits))
        {
            error = "--reset with a project removes its row, so it takes no mode or limit";
            return false;
        }

        if (!reset && mode is null && see is null && values is null && run is null && !limits)
        {
            error = "name something to change: --mode, --see, --values, --run, --per-hour, --until or --reset";
            return false;
        }

        edit = new AccessEdit
        {
            Reset = reset,
            Project = scope,
            Mode = parsedMode,
            See = parsedSee,
            Values = parsedValues,
            Run = parsedRun,
            PerHour = parsedPerHour,
            NoPerHour = string.Equals(perHour, None, StringComparison.Ordinal),
            Until = parsedUntil,
            NoUntil = string.Equals(until, None, StringComparison.Ordinal),
        };
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether this change names what a gain is in, so that confirming it may lift what this computer's
    /// ceilings already hold back there; a change naming something else leaves that held back.
    /// </summary>
    /// <param name="gain">The gain.</param>
    /// <returns>
    /// <see langword="true"/> when it turns values or commands on, names what the agent sees, or names a
    /// mode or limit for the gain's project or environment or one holding it, and for a reset of the
    /// whole agent or the vault's settings.
    /// </returns>
    public bool Names(AccessGain gain)
    {
        ArgumentNullException.ThrowIfNull(gain);

        return UsesVault || (Reset && Project is null) || gain.Dimension switch
        {
            AccessDimension.Values => Values == true,
            AccessDimension.Run => Run == true,
            AccessDimension.See => See is not null,
            _ => (Reset || Mode is not null || PerHour is not null || NoPerHour || Until is not null || NoUntil) && Holds(Project, gain.Scope),
        };

        static bool Holds(ProjectScope? named, ProjectScope? scope) =>
            named is null
            || (scope is not null
                && string.Equals(scope.Project, named.Project, StringComparison.Ordinal)
                && (named.Environment is null || string.Equals(scope.Environment, named.Environment, StringComparison.Ordinal)));
    }

    /// <summary>The settings this change leaves an agent with.</summary>
    /// <param name="current">The agent's settings now.</param>
    /// <returns>The new settings, which may hold more project rows than a record keeps.</returns>
    public AgentAccess Apply(AgentAccess current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var access = Reset && Project is null ? AgentAccess.Defaults : current;

        if (Project is { } scope)
        {
            if (Reset)
            {
                access = access.WithoutRow(scope);
            }
            else
            {
                var row = access.Projects.FirstOrDefault(existing => existing.Scope == scope);
                var mode = Mode ?? row?.Mode ?? access.Decide(scope.Project, scope.Environment).Mode;
                access = access.WithRow(new ProjectAccess(scope, mode, Limited(row?.Limit ?? AccessLimit.None)));
            }
        }
        else
        {
            access = access with { Mode = Mode ?? access.Mode, Limit = Limited(access.Limit) };
        }

        return access with
        {
            See = See ?? access.See,
            Values = Values ?? access.Values,
            Run = Run ?? access.Run,
        };
    }

    /// <summary>The change in a person's words, for a confirmation, a reply and an audit line.</summary>
    /// <returns>Each setting it names, as the CLI spells it, a character in a selector or project name that could hide or reorder text shown as an escape.</returns>
    public override string ToString()
    {
        if (UsesVault)
        {
            return "use the vault's settings";
        }

        List<string> parts = [];

        if (Reset)
        {
            parts.Add("reset");
        }

        if (Project is { } scope)
        {
            parts.Add($"project {DisplayTextSanitizer.Escaped(scope.Name)}");
        }

        if (Mode is { } mode)
        {
            parts.Add($"mode {AgentModes.Name(mode)}");
        }

        if (See is { } see)
        {
            parts.Add($"see {string.Join(' ', see.Globs.Select(DisplayTextSanitizer.Escaped))}");
        }

        if (Values is { } values)
        {
            parts.Add(values ? "values yes" : "values no");
        }

        if (Run is { } run)
        {
            parts.Add(run ? "run on" : "run off");
        }

        if (PerHour is { } perHour)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"per-hour {perHour}"));
        }
        else if (NoPerHour)
        {
            parts.Add($"per-hour {None}");
        }

        if (Until is { } time)
        {
            parts.Add($"until {UntilTime.Recorded(time)}");
        }
        else if (NoUntil)
        {
            parts.Add($"until {None}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>Words that name this change as <see cref="ToString"/> does, with its end time on this computer's clock rather than as recorded.</summary>
    /// <param name="said">Words that may name this change, such as the owner's reply.</param>
    /// <param name="zone">This computer's time zone.</param>
    /// <returns>The words for a person to read.</returns>
    public string OnThisClock(string said, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(zone);

        return Until is { } until
            ? said.Replace($"until {UntilTime.Recorded(until)}", $"until {UntilTime.Shown(until, zone)}", StringComparison.Ordinal)
            : said;
    }

    private AccessLimit Limited(AccessLimit limit) => limit with
    {
        PerHour = NoPerHour ? null : PerHour ?? limit.PerHour,
        Until = NoUntil ? null : Until ?? limit.Until,
    };

    private static bool TryWord(string? text, string yes, string no, string name, out bool? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (text is null)
        {
            return true;
        }

        if (string.Equals(text, yes, StringComparison.Ordinal) || string.Equals(text, no, StringComparison.Ordinal))
        {
            value = string.Equals(text, yes, StringComparison.Ordinal);
            return true;
        }

        error = $"{name} is {yes} or {no}";
        return false;
    }

    private static bool TryPerHour(string? text, out int? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (text is null || string.Equals(text, None, StringComparison.Ordinal))
        {
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= AccessLimit.MaximumPerHour)
        {
            value = number;
            return true;
        }

        error = string.Create(CultureInfo.InvariantCulture, $"per-hour is a whole number from 1 to {AccessLimit.MaximumPerHour}, or {None}");
        return false;
    }

    private static bool TryUntil(string? text, out DateTimeOffset? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (text is null || string.Equals(text, None, StringComparison.Ordinal))
        {
            return true;
        }

        if (UntilTime.TryParseRecorded(text, out var time))
        {
            value = time;
            return true;
        }

        error = $"until is a time in UTC such as 2026-12-31T18:00:00Z, or {None}";
        return false;
    }
}
