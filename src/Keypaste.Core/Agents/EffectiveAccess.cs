namespace Keypaste.Core.Agents;

/// <summary>One row's hourly limit on Automatic, as a release spends it.</summary>
/// <param name="Row">The row: its project or environment as a record names it, such as <c>billing:staging</c>, or empty for the agent's default.</param>
/// <param name="PerHour">How many releases an hour it allows.</param>
public sealed record RowLimit(string Row, int PerHour);

/// <summary>What applies to one item an agent asks for.</summary>
/// <param name="Mode">How the request is answered, a limit already reached by time applied.</param>
/// <param name="Limits">The hourly limits an Automatic release spends; empty for any other mode.</param>
public sealed record ItemAccess(AgentMode Mode, IReadOnlyList<RowLimit> Limits);

/// <summary>
/// What applies to an agent's requests: the least of its records in the vault and this computer's
/// ceilings in <c>clients.toml</c>, and per request the bridge's own flags.
/// </summary>
/// <remarks>
/// <para>
/// A named agent with a record of its own is held to it. With several, which another entry a person
/// or another program made under its name leaves, it is held to the least of them. An agent with no
/// record, a bridge with no name and <see cref="AgentNames.Any"/> get <see cref="AgentAccess.Defaults"/>,
/// beneath every <see cref="AgentNames.Any"/> record, which is read as a ceiling only, so neither is
/// ever Automatic.
/// A record not wholly understood, or read while the vault differs from its file, applies only beneath
/// the defaults.
/// </para>
/// <para>
/// Per item: what is reached is the least of the bridge's <c>--expose</c>, each record's
/// <see cref="AgentAccess.See"/> and the ceiling's. The mode is, for each of the item's project tags,
/// its environment's row, else its project's row, else the default, capped, and then the strictest
/// across its tags; an item in no project takes the default. A protected environment is Ask whatever
/// any row says (D-0348, D-0371), and an Automatic limit, a record's or this computer's ceiling's, that
/// has passed its time asks for a while.
/// </para>
/// </remarks>
public sealed class EffectiveAccess
{
    private EffectiveAccess(string? label, IReadOnlyList<AgentAccess> terms, AccessCeiling caps)
    {
        Label = label;
        Terms = terms;
        Caps = caps;
        Values = caps.Values && terms.All(term => term.Values);
        Run = caps.Run && terms.All(term => term.Run);
    }

    /// <summary>What applies to an agent with no record and no ceiling.</summary>
    public static EffectiveAccess Defaults { get; } = Create(null, AgentRecords.None, AccessCaps.Empty);

    /// <summary>The agent's name, or null when the bridge's label names no one agent.</summary>
    public string? Label { get; }

    /// <summary>Whether the agent is given values; when not, values reach only commands it runs.</summary>
    public bool Values { get; }

    /// <summary>Whether the agent's settings let it run commands, each of which still asks.</summary>
    public bool Run { get; }

    /// <summary>The settings whose least applies.</summary>
    internal IReadOnlyList<AgentAccess> Terms { get; }

    /// <summary>This computer's ceilings for the agent.</summary>
    internal AccessCeiling Caps { get; }

    /// <summary>Works out what applies to an agent.</summary>
    /// <param name="label">The bridge's raw <c>--client-label</c>, or null for one started without one.</param>
    /// <param name="records">The records that bear on it, from <see cref="AgentAccessStore.Read"/>.</param>
    /// <param name="caps">This computer's ceilings.</param>
    /// <returns>What applies.</returns>
    public static EffectiveAccess Create(string? label, AgentRecords records, AccessCaps caps)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(caps);

        var named = AgentNames.IsNamed(label);
        List<AgentAccess> terms = [];

        if (named && records.Own.Count > 0)
        {
            terms.AddRange(records.Own.Select(record => record.Access));

            if (records.State != SavedRead.Current || records.Own.Any(record => !record.Understood))
            {
                terms.Add(AgentAccess.Defaults);
            }
        }
        else
        {
            terms.Add(AgentAccess.Defaults);
            terms.AddRange(records.Any.Select(record => record.Access));
        }

        return new EffectiveAccess(named ? label : null, terms, caps.For(label));
    }

    /// <summary>Whether the agent may run a command through a bridge.</summary>
    /// <param name="bridgeOffersRun">Whether the bridge was started with <c>--allow-run</c>.</param>
    /// <returns>Whether both the bridge and the settings allow it.</returns>
    public bool MayRun(bool bridgeOffersRun) => bridgeOffersRun && Run;

    /// <summary>How much of an item the agent reaches through a bridge.</summary>
    /// <param name="bridge">The bridge's <c>--expose</c>.</param>
    /// <param name="name">The item's real, unsanitized name.</param>
    /// <param name="tags">The item's own tags.</param>
    /// <returns>The least of what each reaches.</returns>
    public ExposureReach Reach(EntryExposure bridge, EntryName name, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tags);

        var reach = bridge.Reach(name, tags);

        foreach (var term in Terms)
        {
            reach = Least(reach, term.See.Reach(name, tags));
        }

        return Caps.See is { } ceiling ? Least(reach, ceiling.Reach(name, tags)) : reach;

        static ExposureReach Least(ExposureReach first, ExposureReach second) => first < second ? first : second;
    }

    /// <summary>How a request for an item is answered.</summary>
    /// <param name="tags">The item's own tags.</param>
    /// <param name="now">What an Automatic limit's time is judged against.</param>
    /// <returns>The mode, and the hourly limits an Automatic release spends.</returns>
    public ItemAccess Item(IReadOnlyList<string> tags, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (EnvProfileNames.RequiresLiveApproval(tags))
        {
            return new ItemAccess(AgentMode.Ask, []);
        }

        var members = tags.Select(ProjectTag.Read).Where(tag => tag.Kind == ProjectTagKind.Member).ToList();
        List<(string? Project, string? Environment)> classes = members.Count == 0
            ? [(null, null)]
            : [.. members.Select(member => (member.Project, member.Environment))];

        return Decide(classes, now);
    }

    /// <summary>Whether a request for an item in one place is answered without asking now, an hourly limit aside.</summary>
    /// <param name="scope">A project, in an environment no row names, or one environment of it; null for an item in no project.</param>
    /// <param name="now">What an Automatic limit's time is judged against.</param>
    /// <returns>Whether the mode there is Automatic, which a protected environment never is.</returns>
    internal bool IsAutomaticIn(ProjectScope? scope, DateTimeOffset now) =>
        !(scope?.Environment is { } environment && EnvProfileNames.IsProtected(environment))
        && Decide([(scope?.Project, scope?.Environment)], now).Mode == AgentMode.Automatic;

    private ItemAccess Decide(IReadOnlyList<(string? Project, string? Environment)> classes, DateTimeOffset now)
    {
        var mode = AgentMode.Automatic;
        List<(string Row, AccessLimit Limit)> deciding = [];

        foreach (var (project, environment) in classes)
        {
            mode = AgentModes.Least(mode, Caps.ModeFor(project, environment));

            if (Caps.LimitFor(project, environment) is { Limit.IsNone: false } held)
            {
                deciding.Add(held);
            }

            foreach (var term in Terms)
            {
                var decided = term.Decide(project, environment);
                mode = AgentModes.Least(mode, decided.Mode);
                deciding.Add((decided.Row, decided.Limit));
            }
        }

        if (mode != AgentMode.Automatic)
        {
            return new ItemAccess(mode, []);
        }

        if (deciding.Any(row => row.Limit.Until is { } until && until <= now))
        {
            return new ItemAccess(AgentMode.AllowForAWhile, []);
        }

        return new ItemAccess(
            AgentMode.Automatic,
            [.. deciding
                .Where(row => row.Limit.PerHour is not null)
                .GroupBy(row => row.Row, StringComparer.Ordinal)
                .Select(group => new RowLimit(group.Key, group.Min(row => row.Limit.PerHour!.Value)))]);
    }

    /// <summary>Whether an item is released without asking, spending its hourly limits if it is.</summary>
    /// <param name="item">What applies to the item, from <see cref="Item"/>.</param>
    /// <param name="limits">The owner's hourly limits.</param>
    /// <returns>
    /// Whether it is Automatic with every hourly limit unspent. Otherwise the request is asked about,
    /// with a timed grant offered where the item's mode is Automatic.
    /// </returns>
    public bool TryAutomatic(ItemAccess item, HourlyLimit limits)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(limits);

        return item.Mode == AgentMode.Automatic && Label is not null && limits.TryUse(Label, item.Limits);
    }
}
