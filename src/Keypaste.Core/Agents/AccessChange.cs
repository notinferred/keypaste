namespace Keypaste.Core.Agents;

/// <summary>What a change to an agent's settings can give it.</summary>
public enum AccessDimension
{
    /// <summary>More it can see.</summary>
    See = 0,

    /// <summary>Values given to it.</summary>
    Values = 1,

    /// <summary>Commands it may run.</summary>
    Run = 2,

    /// <summary>A mode that asks less.</summary>
    Mode = 3,

    /// <summary>A limit on Automatic that allows more, or none.</summary>
    Limit = 4,
}

/// <summary>One thing a change gives an agent that it does not have now.</summary>
/// <param name="Dimension">What it gains.</param>
/// <param name="Scope">The project or environment a mode or limit is gained in, or null for the agent's default or the whole agent.</param>
public sealed record AccessGain(AccessDimension Dimension, ProjectScope? Scope);

/// <summary>A requested change to an agent's settings, classified against what applies to it now.</summary>
/// <remarks>
/// <para>
/// A change is a narrowing only when it is provably no wider in every dimension than what applies now,
/// this computer's ceilings included: a selector set is narrower only when each new selector equals or
/// lies under one in force, and a mode or limit is compared for every project and environment any of
/// the settings name. Anything else, a reset that gives more included, is a widening, which takes effect
/// only after the person confirms it.
/// </para>
/// <para>
/// A change is judged by what the agent could do before and after it. While the record it writes still
/// holds parts keypaste does not understand, that record applies only beneath the defaults, so what it
/// already said stays inactive and is no gain: only what the change adds beyond what the record says,
/// and what the agent would get beneath the defaults once the person confirms, counts. A later change
/// that leaves the record wholly understood gives everything it then says, and counts all of it.
/// </para>
/// </remarks>
public sealed class AccessChange
{
    private AccessChange(AgentAccess proposed, IReadOnlyList<AccessGain> gains, EffectiveAccess? said = null)
    {
        Proposed = proposed;
        Gains = gains;
        Said = said;
    }

    /// <summary>The settings the change would leave the agent with.</summary>
    public AgentAccess Proposed { get; }

    /// <summary>
    /// What the agent's records say now, each at its word with no ceiling, when the change was judged
    /// against them because its record would still not be wholly understood; otherwise null.
    /// </summary>
    public EffectiveAccess? Said { get; }

    /// <summary>Everything the change gives that does not apply now, in a fixed order; empty for a narrowing.</summary>
    public IReadOnlyList<AccessGain> Gains { get; }

    /// <summary>Whether the change gives nothing that does not apply now, so it may apply at once.</summary>
    public bool IsNarrowing => Gains.Count == 0;

    /// <summary>
    /// This change without each gain it shares with leaving the record as it is, what this computer's
    /// ceilings hold back now, unless it names what that gain is in.
    /// </summary>
    /// <param name="heldBack">What the agent's record gives now beyond what applies, its ceilings held back.</param>
    /// <param name="named">Whether the change names what a gain is in (<see cref="AccessEdit.Names"/>).</param>
    /// <returns>The change, whose gains a confirmation shows and lifts.</returns>
    /// <remarks>
    /// A change naming one thing judges what it does to the agent and leaves another ceiling as it is, so
    /// commands turned off after values were kept back here is a narrowing, not a request for the values.
    /// </remarks>
    public AccessChange Without(IReadOnlyCollection<AccessGain> heldBack, Func<AccessGain, bool> named)
    {
        ArgumentNullException.ThrowIfNull(heldBack);
        ArgumentNullException.ThrowIfNull(named);

        return new AccessChange(Proposed, [.. Gains.Where(gain => !heldBack.Contains(gain) || named(gain))], Said);
    }

    /// <summary>Classifies proposed settings against what applies now.</summary>
    /// <param name="current">What applies to the agent now, its records and this computer's ceilings.</param>
    /// <param name="proposed">The settings the change would leave it with.</param>
    /// <returns>The change, with everything it would give.</returns>
    public static AccessChange Classify(EffectiveAccess current, AgentAccess proposed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);

        List<AccessGain> gains = [];

        if (proposed.Values && !current.Values)
        {
            gains.Add(new AccessGain(AccessDimension.Values, null));
        }

        if (proposed.Run && !current.Run)
        {
            gains.Add(new AccessGain(AccessDimension.Run, null));
        }

        if (!Sees(current, proposed.See))
        {
            gains.Add(new AccessGain(AccessDimension.See, null));
        }

        foreach (var (scope, project, environment) in Classes(current, proposed))
        {
            var now = ModeNow(current, project, environment);
            var next = proposed.Decide(project, environment);

            if (next.Mode > now)
            {
                gains.Add(new AccessGain(AccessDimension.Mode, scope));
            }
            else if (next.Mode == AgentMode.Automatic && !IsWithin(next.Row, next.Limit, InForce(current, project, environment)))
            {
                gains.Add(new AccessGain(AccessDimension.Limit, scope));
            }
        }

        return new AccessChange(proposed, gains);
    }

    /// <summary>Classifies a change to an agent's record by what the record would let the agent do once written.</summary>
    /// <param name="current">What applies to the agent now, its records and this computer's ceilings.</param>
    /// <param name="records">The records <paramref name="current"/> was worked out from.</param>
    /// <param name="written">What the agent's record would read as once the change is written, which every record under its name is then given.</param>
    /// <returns>
    /// The change. With <paramref name="written"/> wholly understood, it gives everything that record
    /// gives beyond what applies now. Otherwise it gives what the record would say beyond what the
    /// records say now, and what the agent would get beneath the defaults beyond what applies now.
    /// </returns>
    public static AccessChange Classify(EffectiveAccess current, AgentRecords records, AcafRead written)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(written);

        if (written.Understood)
        {
            return Classify(current, written.Access);
        }

        var said = EffectiveAccess.Create(
            current.Label,
            new AgentRecords(SavedRead.Current, [.. records.Own.Select(AtItsWord)], [.. records.Any.Select(AtItsWord)]),
            AccessCaps.Empty);
        var added = Classify(said, written.Said).Gains;
        var given = Classify(current, written.Access).Gains.Where(gain => IsGivenBeneathTheDefaults(current, gain));

        return new AccessChange(written.Said, [.. added.Union(given)], said);

        static AcafRead AtItsWord(AcafRead record) => new(record.Said, true, record.Rewritable);
    }

    /// <summary>
    /// Whether the agent gets a gain once the person confirms it while its record applies only beneath
    /// the defaults, this computer's ceilings lifted as far as the change goes.
    /// </summary>
    /// <remarks>
    /// Run and a limit on Automatic never are, since the defaults give neither. What the record sees is
    /// bounded by what the defaults see, so it gives nothing more where what applies now covers that.
    /// </remarks>
    private static bool IsGivenBeneathTheDefaults(EffectiveAccess current, AccessGain gain) => gain.Dimension switch
    {
        AccessDimension.Values => AgentAccess.Defaults.Values,
        AccessDimension.Run => AgentAccess.Defaults.Run,
        AccessDimension.See => !Sees(current, AgentAccess.Defaults.See),
        AccessDimension.Mode => AgentAccess.Defaults.Mode > ModeNow(current, gain.Scope?.Project, gain.Scope?.Environment),
        _ => false,
    };

    /// <summary>Whether what applies now, every term and the ceiling, reaches all that a selector set does.</summary>
    private static bool Sees(EffectiveAccess current, EntryExposure see) =>
        current.Terms.All(term => term.See.Covers(see)) && (current.Caps.See is not { } ceiling || ceiling.Covers(see));

    /// <summary>The mode an item of a project's environment gets now, before any limit: the least of every term and the ceiling.</summary>
    /// <param name="current">What applies to the agent now.</param>
    /// <param name="project">The project, or null for an item in no project.</param>
    /// <param name="environment">The environment, or null for one no row names.</param>
    /// <returns>The mode.</returns>
    internal static AgentMode ModeNow(EffectiveAccess current, string? project, string? environment) =>
        current.Terms.Aggregate(
            current.Caps.ModeFor(project, environment),
            (mode, term) => AgentModes.Least(mode, term.Decide(project, environment).Mode));

    /// <summary>
    /// The agent's default, each project any of the settings name in an environment none names, and each
    /// environment one names: every item's mode and limit is decided as one of these is.
    /// </summary>
    internal static IEnumerable<(ProjectScope? Scope, string? Project, string? Environment)> Classes(EffectiveAccess current, AgentAccess proposed)
    {
        var scopes = current.Terms.SelectMany(term => term.Projects.Select(row => row.Scope))
            .Concat(proposed.Projects.Select(row => row.Scope))
            .Concat(current.Caps.Rows.Select(row => row.Project).OfType<ProjectScope>())
            .Distinct()
            .ToList();

        yield return (null, null, null);

        foreach (var project in scopes.Select(scope => scope.WholeProject).Distinct())
        {
            yield return (project, project.Project, null);
        }

        foreach (var environment in scopes.Where(scope => scope.Environment is not null))
        {
            yield return (environment, environment.Project, environment.Environment);
        }
    }

    /// <summary>Each limit in force for a project's environment: every term's, and this computer's ceiling's.</summary>
    private static IEnumerable<(AgentMode Mode, string Row, AccessLimit Limit)> InForce(EffectiveAccess current, string? project, string? environment)
    {
        var (row, limit) = current.Caps.LimitFor(project, environment);

        return current.Terms
            .Select(term => term.Decide(project, environment))
            .Append((current.Caps.ModeFor(project, environment), row, limit));
    }

    /// <summary>Whether a limit allows no more than every limit in force where each decided Automatic.</summary>
    /// <remarks>
    /// An earlier or equal time is within; an hourly limit is within one in force only on the same row,
    /// whose count it shares, and at no more an hour.
    /// </remarks>
    private static bool IsWithin(string row, AccessLimit limit, IEnumerable<(AgentMode Mode, string Row, AccessLimit Limit)> current)
    {
        foreach (var decided in current)
        {
            if (decided.Limit.Until is { } until && (limit.Until is not { } mine || mine > until))
            {
                return false;
            }

            if (decided.Limit.PerHour is { } perHour
                && (!string.Equals(decided.Row, row, StringComparison.Ordinal) || limit.PerHour is not { } count || count > perHour))
            {
                return false;
            }
        }

        return true;
    }
}
