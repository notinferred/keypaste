namespace Keypaste.Core.Agents;

/// <summary>One agent this computer knows by its bridge's <c>--client-label</c>, and whether it may run commands.</summary>
/// <param name="Name">The label.</param>
/// <param name="Run">Whether its <c>run</c> tool may ask to start a command, each run still asked.</param>
public sealed record KnownAgent(string Name, bool Run);

/// <summary>
/// What every agent gets on this computer: whether item details are shown and each category's rule, kept in
/// <c>agents.toml</c> under the keypaste home and never in a vault, so a synced or edited vault releases nothing more.
/// </summary>
/// <remarks>
/// Every category is Ask until the person sets it. Allow is taken only with the person's confirmation that every
/// value in the category will be released without asking. A passkey's fields are Blocked whatever is set.
/// </remarks>
public sealed record AgentRules
{
    /// <summary>The most agents the file keeps.</summary>
    public const int MaximumAgents = 64;

    private Dictionary<FieldCategory, CategoryRule> Rules { get; init; } = [];
    private readonly IReadOnlyList<KnownAgent> _agents = [];

    /// <summary>Details shown, every category asked, no agent running commands.</summary>
    public static AgentRules Defaults { get; } = new();

    /// <summary>Whether agents see each item's username, URL, tags and unprotected custom fields without asking.</summary>
    public bool ShowsDetails { get; init; } = true;

    /// <summary>The agents this computer knows, by name.</summary>
    /// <exception cref="ArgumentException">Two share a name, or there are more than <see cref="MaximumAgents"/>.</exception>
    public IReadOnlyList<KnownAgent> Agents
    {
        get => _agents;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Select(agent => agent.Name).Distinct(StringComparer.Ordinal).Count() != value.Count || value.Count > MaximumAgents)
            {
                throw new ArgumentException($"agents need distinct names, at most {MaximumAgents}", nameof(value));
            }

            _agents = [.. value];
        }
    }

    /// <summary>The rule each request for a category is answered by.</summary>
    /// <param name="category">The category.</param>
    /// <returns>Block for a passkey, details by <see cref="ShowsDetails"/>, otherwise what is set, Ask by default.</returns>
    public CategoryRule RuleFor(FieldCategory category) => category switch
    {
        FieldCategory.Passkey => CategoryRule.Block,
        FieldCategory.Detail => ShowsDetails ? CategoryRule.Allow : CategoryRule.Ask,
        _ => Rules.TryGetValue(category, out var rule) ? rule : CategoryRule.Ask,
    };

    /// <summary>These rules with one category set to Ask or Block.</summary>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="rule">Ask or Block; Allow needs <see cref="Allowing"/>.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not ruled, or the rule is Allow.</exception>
    public AgentRules With(FieldCategory category, CategoryRule rule)
    {
        if (rule is not (CategoryRule.Ask or CategoryRule.Block))
        {
            throw new ArgumentOutOfRangeException(nameof(rule), rule, "Allow is set only with the person's confirmation");
        }

        return Set(category, rule);
    }

    /// <summary>These rules with one category set to Allow, once the person confirmed <see cref="AllowConfirmation"/>.</summary>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="confirmed">Whether the person confirmed it; without that nothing changes.</param>
    /// <returns>The new rules, or these when it was not confirmed.</returns>
    public AgentRules Allowing(FieldCategory category, bool confirmed) => confirmed ? Set(category, CategoryRule.Allow) : this;

    /// <summary>What the person confirms before a category is set to Allow.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The sentence.</returns>
    public static string AllowConfirmation(FieldCategory category) =>
        $"Every agent will get {FieldCategories.Shown(category)} without asking, each release recorded in the log.";

    /// <summary>Whether an agent may ask to run commands.</summary>
    /// <param name="label">The bridge's raw label, or null.</param>
    /// <returns>Whether a named agent this computer knows has run turned on.</returns>
    public bool Runs(string? label) =>
        AgentNames.IsNamed(label) && _agents.Any(agent => agent.Run && string.Equals(agent.Name, label, StringComparison.Ordinal));

    /// <summary>These rules with an agent's run turned on or off, adding the agent if it is new.</summary>
    /// <param name="name">A named agent's label.</param>
    /// <param name="run">Whether it may ask to run commands.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentException">The name is not one agent's, or a new one would pass <see cref="MaximumAgents"/>.</exception>
    public AgentRules WithRun(string name, bool run)
    {
        if (!AgentNames.IsNamed(name))
        {
            throw new ArgumentException("an agent's name is its bridge's --client-label", nameof(name));
        }

        return this with
        {
            Agents = _agents.Any(agent => string.Equals(agent.Name, name, StringComparison.Ordinal))
                ? [.. _agents.Select(agent => string.Equals(agent.Name, name, StringComparison.Ordinal) ? agent with { Run = run } : agent)]
                : [.. _agents, new KnownAgent(name, run)],
        };
    }

    /// <summary>These rules without an agent.</summary>
    /// <param name="name">Its label.</param>
    /// <returns>The new rules.</returns>
    public AgentRules Without(string name) =>
        this with { Agents = [.. _agents.Where(agent => !string.Equals(agent.Name, name, StringComparison.Ordinal))] };

    /// <inheritdoc/>
    public bool Equals(AgentRules? other) =>
        other is not null
        && ShowsDetails == other.ShowsDetails
        && FieldCategories.Ruled.All(category => RuleFor(category) == other.RuleFor(category))
        && _agents.SequenceEqual(other._agents);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ShowsDetails, _agents.Count);

    private AgentRules Set(FieldCategory category, CategoryRule rule)
    {
        if (!FieldCategories.Ruled.Contains(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Settings sets no rule for this category");
        }

        return this with { Rules = new Dictionary<FieldCategory, CategoryRule>(Rules) { [category] = rule } };
    }
}
