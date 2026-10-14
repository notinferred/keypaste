namespace Keypaste.Core.Agents;

/// <summary>One project row of an agent's settings: the mode, and optionally a limit, for a project or one environment of it.</summary>
public sealed record ProjectAccess
{
    /// <summary>Builds a row.</summary>
    /// <param name="scope">The project or environment it covers.</param>
    /// <param name="mode">Its mode.</param>
    /// <param name="limit">Its limit, or null for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a mode.</exception>
    public ProjectAccess(ProjectScope scope, AgentMode mode, AccessLimit? limit = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Mode = AgentModes.Checked(mode, nameof(mode));
        Limit = limit ?? AccessLimit.None;
    }

    /// <summary>The project or environment it covers.</summary>
    public ProjectScope Scope { get; }

    /// <summary>Its mode, which beats the agent's default for what it covers.</summary>
    public AgentMode Mode { get; }

    /// <summary>Its limit on Automatic.</summary>
    public AccessLimit Limit { get; }
}

/// <summary>
/// One agent's settings: what it can see, whether it is given values, whether it may run commands,
/// its mode, and project rows with their own modes and limits.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Defaults"/> is what keypaste did before these settings existed, and what an agent with
/// no settings gets: every project's variables, values when a person allows them, no commands, and
/// Ask, allow for a while.
/// </para>
/// <para>
/// A row beats the default for the items it covers, and an environment's row beats its project's.
/// Settings say only what an agent may be given; what one request gets is <see cref="EffectiveAccess"/>.
/// </para>
/// </remarks>
public sealed record AgentAccess
{
    /// <summary>The most project rows a record keeps: a longer list is not understood, and no change makes one.</summary>
    public const int MaximumProjects = 256;

    private readonly EntryExposure _see = EntryExposure.Default;
    private readonly AgentMode _mode = AgentMode.AllowForAWhile;
    private readonly AccessLimit _limit = AccessLimit.None;
    private readonly IReadOnlyList<ProjectAccess> _projects = [];

    /// <summary>Today's behaviour: every project's variables, values when allowed, no commands, Ask, allow for a while.</summary>
    public static AgentAccess Defaults { get; } = new();

    /// <summary>What it can see, in <c>--expose</c>'s selectors.</summary>
    public EntryExposure See
    {
        get => _see;
        init => _see = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Whether it is given values; when not, values reach only commands it runs.</summary>
    public bool Values { get; init; } = true;

    /// <summary>Whether it may run commands, each of which still asks.</summary>
    public bool Run { get; init; }

    /// <summary>Its mode where no row covers an item.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a mode.</exception>
    public AgentMode Mode
    {
        get => _mode;
        init => _mode = AgentModes.Checked(value, nameof(value));
    }

    /// <summary>The limit on Automatic where no row covers an item.</summary>
    public AccessLimit Limit
    {
        get => _limit;
        init => _limit = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Its project rows, each covering a different project or environment.</summary>
    /// <remarks>
    /// Not bounded here, so a change can be judged, and held on this computer, even when the record
    /// cannot take its row; the record's bound is kept where records are read and written.
    /// </remarks>
    /// <exception cref="ArgumentException">Two rows cover the same scope.</exception>
    public IReadOnlyList<ProjectAccess> Projects
    {
        get => _projects;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Select(row => row.Scope).Distinct().Count() != value.Count)
            {
                throw new ArgumentException("two project rows cover the same project or environment", nameof(value));
            }

            _projects = [.. value];
        }
    }

    /// <summary>These settings with one row set, replacing the row for the same scope.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The new settings.</returns>
    public AgentAccess WithRow(ProjectAccess row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return this with { Projects = [.. Projects.Where(existing => existing.Scope != row.Scope), row] };
    }

    /// <summary>These settings without the row for a scope, whose items then take the default.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The new settings.</returns>
    public AgentAccess WithoutRow(ProjectScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return this with { Projects = [.. Projects.Where(existing => existing.Scope != scope)] };
    }

    /// <summary>Whether another holds the same settings, rows in the same order.</summary>
    /// <param name="other">The other settings.</param>
    /// <returns><see langword="true"/> when every setting matches.</returns>
    public bool Equals(AgentAccess? other) =>
        other is not null
        && See.Globs.SequenceEqual(other.See.Globs, StringComparer.Ordinal)
        && Values == other.Values
        && Run == other.Run
        && Mode == other.Mode
        && Limit == other.Limit
        && Projects.SequenceEqual(other.Projects);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Values, Run, Mode, Limit, Projects.Count);

    /// <summary>The mode and limit for a project's environment: its row, else its project's row, else the default.</summary>
    /// <param name="project">The project, or null for an item in no project.</param>
    /// <param name="environment">The environment, or null for one no row names.</param>
    /// <returns>The mode, the row that decided it (empty for the default) and that row's limit.</returns>
    internal (AgentMode Mode, string Row, AccessLimit Limit) Decide(string? project, string? environment)
    {
        if (project is not null)
        {
            var decided = Projects.FirstOrDefault(row => row.Scope.Environment is not null && Same(row.Scope, project, environment))
                ?? Projects.FirstOrDefault(row => row.Scope.Environment is null && Same(row.Scope, project, null));

            if (decided is not null)
            {
                return (decided.Mode, decided.Scope.Name, decided.Limit);
            }
        }

        return (Mode, string.Empty, Limit);
    }

    private static bool Same(ProjectScope scope, string project, string? environment) =>
        string.Equals(scope.Project, project, StringComparison.Ordinal)
        && string.Equals(scope.Environment, environment, StringComparison.Ordinal);
}
