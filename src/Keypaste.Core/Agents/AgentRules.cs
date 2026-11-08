namespace Keypaste.Core.Agents;

/// <summary>One agent this computer knows by its bridge's <c>--client-label</c>, and whether it may run commands.</summary>
/// <param name="Name">The label.</param>
/// <param name="Run">Whether its <c>run</c> tool may ask to start a command, each run still asked.</param>
public sealed record KnownAgent(string Name, bool Run);

/// <summary>One agent's own rule for a category, in place of the one Settings sets for every agent.</summary>
/// <param name="Agent">The agent's name.</param>
/// <param name="Category">One of <see cref="FieldCategories.Ruled"/>.</param>
/// <param name="Rule">Its rule for that agent.</param>
public sealed record AgentCategoryRule(string Agent, FieldCategory Category, CategoryRule Rule);

/// <summary>A person's Always allow or Always block for one agent and one item's field, kept once they confirmed it.</summary>
/// <param name="Agent">The agent's name.</param>
/// <param name="Item">The item's UUID as hex.</param>
/// <param name="Field">What a request names within the item: the field, or <c>attachment:</c> and the attachment's name.</param>
/// <param name="Rule">Allow or Block.</param>
public sealed record RememberedAnswer(string Agent, string Item, string Field, CategoryRule Rule);

/// <summary>The item a person picked with Always allow when more than one answered to a name, released to that agent for that name and field without asking.</summary>
/// <param name="Agent">The agent's name.</param>
/// <param name="Name">The name exactly as the agent's request gave it.</param>
/// <param name="Field">What the request named within the item, as <see cref="RememberedAnswer.Field"/>.</param>
/// <param name="Item">The picked item's UUID as hex.</param>
public sealed record RememberedPick(string Agent, string Name, string Field, string Item);

/// <summary>Which answer decided a request, from the most specific.</summary>
public enum RuleLevel
{
    /// <summary>What Settings sets for every agent, or a category's fixed rule.</summary>
    Settings = 0,

    /// <summary>The agent's own rule for the category.</summary>
    Agent = 1,

    /// <summary>A person's remembered answer for this agent and this item's field.</summary>
    Item = 2,
}

/// <summary>The rule a request is decided by and where it was set.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Level">Which answer set it.</param>
public readonly record struct DecidedRule(CategoryRule Rule, RuleLevel Level);

/// <summary>
/// What agents get on this computer: whether usernames are shown, each category's rule for every agent, each agent's own
/// rules, and the answers a person told keypaste to remember, kept in <c>agents.toml</c> under the keypaste home and never
/// in a vault, so a synced or edited vault releases nothing more.
/// </summary>
/// <remarks>
/// Every category is Ask until the person sets it. An Allow, for every agent or for one, and every remembered answer are
/// taken only with the person's confirmation. A passkey's fields are Blocked whatever is set. The most specific answer
/// decides: an item's field, then the agent's category, then Settings.
/// </remarks>
public sealed record AgentRules
{
    /// <summary>The most agents the file keeps.</summary>
    public const int MaximumAgents = 64;

    /// <summary>The most remembered answers and picks the file keeps, together.</summary>
    public const int MaximumRemembered = 128;

    private Dictionary<FieldCategory, CategoryRule> Rules { get; init; } = [];
    private readonly IReadOnlyList<KnownAgent> _agents = [];

    /// <summary>The username shown, every category asked, no agent running commands.</summary>
    public static AgentRules Defaults { get; } = new();

    /// <summary>Whether agents see each item's username without asking; its title, group, tags and URL they always see.</summary>
    public bool ShowsUsername { get; init; } = true;

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

    /// <summary>Each agent's own rules, every agent among <see cref="Agents"/>.</summary>
    public IReadOnlyList<AgentCategoryRule> AgentCategories { get; private init; } = [];

    /// <summary>The Always allow and Always block answers kept, in the order they were given.</summary>
    public IReadOnlyList<RememberedAnswer> Answers { get; private init; } = [];

    /// <summary>The picks kept, in the order they were given.</summary>
    public IReadOnlyList<RememberedPick> Picks { get; private init; } = [];

    /// <summary>Whether another answer or pick can be remembered.</summary>
    public bool HasRoom => Answers.Count + Picks.Count < MaximumRemembered;

    /// <summary>The rule Settings gives every agent for a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>Block for a passkey, Allow for a URL, the username by <see cref="ShowsUsername"/>, otherwise what is set, Ask by default.</returns>
    public CategoryRule RuleFor(FieldCategory category) => category switch
    {
        FieldCategory.Passkey => CategoryRule.Block,
        FieldCategory.Url => CategoryRule.Allow,
        FieldCategory.Username => ShowsUsername ? CategoryRule.Allow : CategoryRule.Ask,
        _ => Rules.TryGetValue(category, out var rule) ? rule : CategoryRule.Ask,
    };

    /// <summary>An agent's own rule for a category, or null when Settings decides it.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="category">The category.</param>
    /// <returns>The rule, or null.</returns>
    public CategoryRule? RuleFor(string agent, FieldCategory category) =>
        AgentCategories.FirstOrDefault(own => Same(own.Agent, agent) && own.Category == category)?.Rule;

    /// <summary>The rule one request is decided by, from the most specific answer that holds.</summary>
    /// <param name="agent">The bridge's raw label, or null; an unnamed agent is decided by Settings alone.</param>
    /// <param name="item">The item's UUID as hex, or null when it cannot be read.</param>
    /// <param name="field">What the request names within the item, as <see cref="RememberedAnswer.Field"/>.</param>
    /// <param name="category">The field's category.</param>
    /// <returns>Block for a passkey; otherwise the item's remembered answer, then the agent's rule, then Settings'.</returns>
    public DecidedRule Decide(string? agent, string? item, string field, FieldCategory category)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (category == FieldCategory.Passkey || !AgentNames.IsNamed(agent))
        {
            return new DecidedRule(RuleFor(category), RuleLevel.Settings);
        }

        if (item is not null && Answers.FirstOrDefault(answer => Same(answer.Agent, agent) && SameItem(answer.Item, item) && Same(answer.Field, field)) is { } remembered)
        {
            return new DecidedRule(remembered.Rule, RuleLevel.Item);
        }

        return FieldCategories.Ruled.Contains(category) && RuleFor(agent, category) is { } own
            ? new DecidedRule(own, RuleLevel.Agent)
            : new DecidedRule(RuleFor(category), RuleLevel.Settings);
    }

    /// <summary>The rule a remembered pick's item is released under: Allow, as the pick is an answer for that item, unless a passkey's or the item's own remembered answer says otherwise.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="item">The picked item's UUID as hex.</param>
    /// <param name="field">What the request names within the item.</param>
    /// <param name="category">The field's category.</param>
    /// <returns>Block for a passkey, the item's remembered answer, otherwise Allow.</returns>
    public CategoryRule DecidePicked(string agent, string item, string field, FieldCategory category) =>
        category == FieldCategory.Passkey ? CategoryRule.Block
        : Decide(agent, item, field, category) is { Level: RuleLevel.Item } remembered ? remembered.Rule
        : CategoryRule.Allow;

    /// <summary>Everything that can decide one item's field for any agent, so a change to the rules can tell whether that field's decision moved.</summary>
    /// <param name="item">The item's UUID as hex, or null.</param>
    /// <param name="field">What a request names within the item.</param>
    /// <param name="category">The field's category.</param>
    /// <returns>A text equal for two rules exactly when they decide the field alike.</returns>
    internal string Governing(string? item, string field, FieldCategory category) => string.Join(
        '\n',
        RuleFor(category).ToString(),
        string.Join(',', AgentCategories.Where(own => own.Category == category).Select(own => $"{own.Agent}={own.Rule}").Order(StringComparer.Ordinal)),
        string.Join(',', Answers.Where(answer => item is not null && SameItem(answer.Item, item) && Same(answer.Field, field)).Select(answer => $"{answer.Agent}={answer.Rule}")),
        string.Join(',', Picks.Where(pick => Same(pick.Field, field)).Select(pick => $"{pick.Agent}:{pick.Name}={pick.Item}")));

    /// <summary>The item a person always picks for an agent's request naming more than one, or null.</summary>
    /// <param name="agent">The bridge's raw label, or null.</param>
    /// <param name="name">The name as the request gave it.</param>
    /// <param name="field">What the request names within the item.</param>
    /// <returns>The picked item's UUID as hex, or null.</returns>
    public string? PickFor(string? agent, string name, string field) =>
        AgentNames.IsNamed(agent)
            ? Picks.FirstOrDefault(pick => Same(pick.Agent, agent) && Same(pick.Name, name) && Same(pick.Field, field))?.Item
            : null;

    /// <summary>These rules with one category set to Ask or Block for every agent.</summary>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="rule">Ask or Block; Allow needs <see cref="Allowing(FieldCategory, bool)"/>.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category is not ruled, or the rule is Allow.</exception>
    public AgentRules With(FieldCategory category, CategoryRule rule)
    {
        Unconfirmed(rule);
        return Set(category, rule);
    }

    /// <summary>These rules with one category set to Allow for every agent, once the person confirmed <see cref="AllowConfirmation(FieldCategory)"/>.</summary>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="confirmed">Whether the person confirmed it; without that nothing changes.</param>
    /// <returns>The new rules, or these when it was not confirmed.</returns>
    public AgentRules Allowing(FieldCategory category, bool confirmed) => confirmed ? Set(category, CategoryRule.Allow) : this;

    /// <summary>What the person confirms before a category is set to Allow for every agent.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The sentence.</returns>
    public static string AllowConfirmation(FieldCategory category) =>
        $"Every agent will receive every {FieldCategories.One(category)} value without asking.";

    /// <summary>These rules with an agent's own rule for a category set to Ask or Block, or removed so Settings decides it.</summary>
    /// <param name="agent">A named agent's label, added if it is new.</param>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="rule">Ask, Block, or null for what Settings sets; Allow needs <see cref="Allowing(string, FieldCategory, bool)"/>.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentException">The name is not one agent's.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The category is not ruled, or the rule is Allow.</exception>
    public AgentRules With(string agent, FieldCategory category, CategoryRule? rule)
    {
        if (rule is { } set)
        {
            Unconfirmed(set);
        }

        return SetFor(agent, category, rule);
    }

    /// <summary>These rules with an agent's own rule for a category set to Allow, once the person confirmed <see cref="AllowConfirmation(string, FieldCategory)"/>.</summary>
    /// <param name="agent">A named agent's label, added if it is new.</param>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="confirmed">Whether the person confirmed it; without that nothing changes.</param>
    /// <returns>The new rules, or these when it was not confirmed.</returns>
    public AgentRules Allowing(string agent, FieldCategory category, bool confirmed) =>
        confirmed ? SetFor(agent, category, CategoryRule.Allow) : this;

    /// <summary>What the person confirms before a category is set to Allow for one agent.</summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="category">The category.</param>
    /// <returns>The sentence.</returns>
    public static string AllowConfirmation(string agent, FieldCategory category) =>
        $"{agent} will receive every {FieldCategories.One(category)} value without asking.";

    /// <summary>These rules remembering a person's Always allow or Always block, once they confirmed it, in place of any earlier one for that agent and field.</summary>
    /// <param name="answer">The answer, its rule Allow or Block.</param>
    /// <param name="confirmed">Whether the person confirmed it; without that nothing changes.</param>
    /// <returns>The new rules, or these when it was not confirmed.</returns>
    /// <exception cref="ArgumentException">The answer names no agent, item or field, or its rule is Ask.</exception>
    /// <exception cref="InvalidOperationException">Nothing more can be remembered (<see cref="HasRoom"/>).</exception>
    public AgentRules Remembering(RememberedAnswer answer, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (!AgentNames.IsNamed(answer.Agent) || !IsItem(answer.Item) || answer.Field.Length == 0 || answer.Rule == CategoryRule.Ask)
        {
            throw new ArgumentException("a remembered answer names an agent, an item and a field, and allows or blocks", nameof(answer));
        }

        if (!confirmed)
        {
            return this;
        }

        List<RememberedAnswer> kept = [.. Answers.Where(held => !(Same(held.Agent, answer.Agent) && SameItem(held.Item, answer.Item) && Same(held.Field, answer.Field)))];
        Room(kept.Count + Picks.Count);

        return this with { Answers = [.. kept, answer with { Item = answer.Item.ToLowerInvariant() }] };
    }

    /// <summary>These rules remembering a person's pick, once they confirmed it, in place of any earlier one for that agent, name and field.</summary>
    /// <param name="pick">The pick.</param>
    /// <param name="confirmed">Whether the person confirmed it; without that nothing changes.</param>
    /// <returns>The new rules, or these when it was not confirmed.</returns>
    /// <exception cref="ArgumentException">The pick names no agent, name, field or item.</exception>
    /// <exception cref="InvalidOperationException">Nothing more can be remembered (<see cref="HasRoom"/>).</exception>
    public AgentRules Remembering(RememberedPick pick, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(pick);

        if (!AgentNames.IsNamed(pick.Agent) || pick.Name.Length == 0 || pick.Field.Length == 0 || !IsItem(pick.Item))
        {
            throw new ArgumentException("a remembered pick names an agent, the name it asked for, a field and an item", nameof(pick));
        }

        if (!confirmed)
        {
            return this;
        }

        List<RememberedPick> kept = [.. Picks.Where(held => !(Same(held.Agent, pick.Agent) && Same(held.Name, pick.Name) && Same(held.Field, pick.Field)))];
        Room(Answers.Count + kept.Count);

        return this with { Picks = [.. kept, pick with { Item = pick.Item.ToLowerInvariant() }] };
    }

    /// <summary>These rules without the remembered answer for an agent, item and field, so the next request is decided as if it were never given.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The new rules.</returns>
    public AgentRules Forgetting(RememberedAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return this with { Answers = [.. Answers.Where(held => !(Same(held.Agent, answer.Agent) && SameItem(held.Item, answer.Item) && Same(held.Field, answer.Field)))] };
    }

    /// <summary>These rules without the remembered pick for an agent, name and field, so the next request naming more than one item is asked again.</summary>
    /// <param name="pick">The pick.</param>
    /// <returns>The new rules.</returns>
    public AgentRules Forgetting(RememberedPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        return this with { Picks = [.. Picks.Where(held => !(Same(held.Agent, pick.Agent) && Same(held.Name, pick.Name) && Same(held.Field, pick.Field)))] };
    }

    /// <summary>The short key a terminal names a remembered answer by, the same however the answers are ordered.</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>Twelve lower-case hex characters.</returns>
    public static string KeyOf(RememberedAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return Key($"answer\n{answer.Agent}\n{answer.Item.ToLowerInvariant()}\n{answer.Field}");
    }

    /// <summary>The short key a terminal names a remembered pick by, the same however the picks are ordered.</summary>
    /// <param name="pick">The pick.</param>
    /// <returns>Twelve lower-case hex characters.</returns>
    public static string KeyOf(RememberedPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        return Key($"pick\n{pick.Agent}\n{pick.Name}\n{pick.Field}");
    }

    /// <summary>
    /// These rules with an agent's own rule for a category changed from a terminal, which proves no person agreed, so only to
    /// what releases nothing more unasked: Ask, Block, or what Settings sets unless that is an Allow the agent did not have.
    /// </summary>
    /// <param name="agent">A named agent's label, added if it is new.</param>
    /// <param name="category">One of <see cref="FieldCategories.Ruled"/>.</param>
    /// <param name="rule">Ask, Block, or null for what Settings sets.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentException">The change would release more without asking, or the name is not one agent's.</exception>
    public AgentRules Narrowing(string agent, FieldCategory category, CategoryRule? rule)
    {
        if (rule == CategoryRule.Allow || (rule is null && RuleFor(category) == CategoryRule.Allow && RuleFor(agent, category) != CategoryRule.Allow))
        {
            throw new ArgumentException(
                $"that would give {agent} {FieldCategories.Shown(category)} without asking, which only the app's agent page sets, once you confirm it");
        }

        return With(agent, category, rule);
    }

    /// <summary>
    /// These rules without the Always allow or the pick a terminal names by its key; an Always block is removed only on the
    /// agent's page, since a terminal proves no person agreed to what removing it may release.
    /// </summary>
    /// <param name="agent">The agent's name.</param>
    /// <param name="key">From <see cref="KeyOf(RememberedAnswer)"/> or <see cref="KeyOf(RememberedPick)"/>.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentException">No Always allow or pick of that agent has the key, or it names an Always block.</exception>
    public AgentRules ForgettingKey(string agent, string key)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(key);

        if (Answers.FirstOrDefault(answer => Same(answer.Agent, agent) && Same(KeyOf(answer), key)) is { } answer)
        {
            return answer.Rule == CategoryRule.Block
                ? throw new ArgumentException($"{key} is an Always block, which is removed on {agent}'s page in the app, since removing it may release more")
                : Forgetting(answer);
        }

        return Picks.FirstOrDefault(pick => Same(pick.Agent, agent) && Same(KeyOf(pick), key)) is { } pick
            ? Forgetting(pick)
            : throw new ArgumentException($"no answer kept for {agent} has the key {key}");
    }

    private static string Key(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    /// <summary>Whether an agent may ask to run commands.</summary>
    /// <param name="label">The bridge's raw label, or null.</param>
    /// <returns>Whether a named agent this computer knows has run turned on.</returns>
    public bool Runs(string? label) =>
        AgentNames.IsNamed(label) && _agents.Any(agent => agent.Run && Same(agent.Name, label));

    /// <summary>These rules with an agent's run turned on or off, adding the agent if it is new.</summary>
    /// <param name="name">A named agent's label.</param>
    /// <param name="run">Whether it may ask to run commands.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentException">The name is not one agent's, or a new one would pass <see cref="MaximumAgents"/>.</exception>
    public AgentRules WithRun(string name, bool run)
    {
        var known = Known(name);
        return known with { Agents = [.. known._agents.Select(agent => Same(agent.Name, name) ? agent with { Run = run } : agent)] };
    }

    /// <summary>These rules without an agent, its own rules and what was remembered for it.</summary>
    /// <param name="name">Its label.</param>
    /// <returns>The new rules.</returns>
    public AgentRules Without(string name) => this with
    {
        Agents = [.. _agents.Where(agent => !Same(agent.Name, name))],
        AgentCategories = [.. AgentCategories.Where(own => !Same(own.Agent, name))],
        Answers = [.. Answers.Where(answer => !Same(answer.Agent, name))],
        Picks = [.. Picks.Where(pick => !Same(pick.Agent, name))],
    };

    /// <inheritdoc/>
    public bool Equals(AgentRules? other) =>
        other is not null
        && ShowsUsername == other.ShowsUsername
        && FieldCategories.Ruled.All(category => RuleFor(category) == other.RuleFor(category))
        && _agents.SequenceEqual(other._agents)
        && AgentCategories.ToHashSet().SetEquals(other.AgentCategories)
        && Answers.SequenceEqual(other.Answers)
        && Picks.SequenceEqual(other.Picks);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ShowsUsername, _agents.Count, Answers.Count, Picks.Count);

    /// <summary>These rules as a file holds them, its sections read in turn, nothing confirmed by a person.</summary>
    internal AgentRules Read(IReadOnlyList<AgentCategoryRule> agentCategories, IReadOnlyList<RememberedAnswer> answers, IReadOnlyList<RememberedPick> picks) =>
        this with { AgentCategories = [.. agentCategories], Answers = [.. answers], Picks = [.. picks] };

    private static bool Same(string left, string? right) => string.Equals(left, right, StringComparison.Ordinal);

    private static bool SameItem(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a value is an item's UUID as hex.</summary>
    internal static bool IsItem(string item) => item.Length == 32 && item.All(char.IsAsciiHexDigit);

    private static void Unconfirmed(CategoryRule rule)
    {
        if (rule is not (CategoryRule.Ask or CategoryRule.Block))
        {
            throw new ArgumentOutOfRangeException(nameof(rule), rule, "Allow is set only with the person's confirmation");
        }
    }

    private static void Ruled(FieldCategory category)
    {
        if (!FieldCategories.Ruled.Contains(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Settings sets no rule for this category");
        }
    }

    private static void Room(int count)
    {
        if (count >= MaximumRemembered)
        {
            throw new InvalidOperationException($"keypaste remembers at most {MaximumRemembered} answers and picks");
        }
    }

    /// <summary>These rules knowing an agent, added with run off if it is new.</summary>
    private AgentRules Known(string name)
    {
        if (!AgentNames.IsNamed(name))
        {
            throw new ArgumentException("an agent's name is its bridge's --client-label", nameof(name));
        }

        return _agents.Any(agent => Same(agent.Name, name)) ? this : this with { Agents = [.. _agents, new KnownAgent(name, Run: false)] };
    }

    private AgentRules Set(FieldCategory category, CategoryRule rule)
    {
        Ruled(category);
        return this with { Rules = new Dictionary<FieldCategory, CategoryRule>(Rules) { [category] = rule } };
    }

    private AgentRules SetFor(string agent, FieldCategory category, CategoryRule? rule)
    {
        Ruled(category);
        var known = Known(agent);
        List<AgentCategoryRule> kept = [.. known.AgentCategories.Where(own => !(Same(own.Agent, agent) && own.Category == category))];

        return known with { AgentCategories = rule is { } set ? [.. kept, new AgentCategoryRule(agent, category, set)] : kept };
    }
}
