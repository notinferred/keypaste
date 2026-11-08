using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Agents;

/// <summary>Where a vault's owner reads the rules every agent's request is decided by.</summary>
public interface IAgentRules
{
    /// <summary>Rules that are always <see cref="AgentRules.Defaults"/>, kept nowhere.</summary>
    static IAgentRules Defaults { get; } = new Fixed(AgentRules.Defaults);

    /// <summary>The rules now.</summary>
    /// <param name="rules">The rules, when they could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>False when the file holds what keypaste cannot read, which refuses every agent request.</returns>
    bool TryRead([NotNullWhen(true)] out AgentRules? rules, out string problem);

    /// <summary>Rules that never change, for a test or a process that keeps none.</summary>
    /// <param name="rules">The rules.</param>
    /// <returns>A source answering them.</returns>
    static IAgentRules Of(AgentRules rules) => new Fixed(rules ?? throw new ArgumentNullException(nameof(rules)));

    /// <summary>Why the rules on file are ignored and the defaults hold, or null when nothing is ignored.</summary>
    string? Ignored => null;

    /// <summary>Changes the rules as the vault's owner does, sealing what it writes.</summary>
    /// <param name="change">The new rules from those that hold now.</param>
    /// <param name="problem">Why nothing changed, otherwise empty.</param>
    /// <returns>Whether the rules now hold the change.</returns>
    bool TryChange(Func<AgentRules, AgentRules> change, out string problem)
    {
        problem = "this keypaste process keeps no agent rules";
        return false;
    }

    private sealed class Fixed(AgentRules rules) : IAgentRules
    {
        public bool TryRead([NotNullWhen(true)] out AgentRules? read, out string problem)
        {
            read = rules;
            problem = string.Empty;
            return true;
        }
    }
}

/// <summary>What <c>agents.toml</c> says, before its seal is checked.</summary>
/// <param name="Rules">The rules the file says, <see cref="AgentRules.Defaults"/> when there is no file.</param>
/// <param name="Seal">The seal the file carries, or null.</param>
/// <param name="Present">Whether there is a file at all.</param>
public sealed record RulesFileRead(AgentRules Rules, string? Seal, bool Present);

/// <summary>
/// <c>agents.toml</c> under the keypaste home: whether usernames are shown, each category's rule and each known
/// agent's run switch, and the seal its owner wrote, which <see cref="SealedAgentRules"/> checks; beside it,
/// <c>agents.id</c>, this home's random identity, which the seal covers so a file copied from another home is not valid here.
/// </summary>
/// <remarks>
/// A file that cannot be read or parsed refuses every agent request and is never rewritten, so a rule a person wrote is
/// not silently lost or lifted.
/// </remarks>
/// <param name="path">From <see cref="Audit.KeypasteHome.AgentsPath"/>.</param>
public sealed class AgentRulesFile(string path)
{
    private const string _sealTable = "seal";
    private const string _macKey = "mac";
    private const string _rulesTable = "rules";
    private const string _agentTable = "agent";
    private const string _usernameKey = "username";
    private const string _nameKey = "name";
    private const string _runKey = "run";
    private const string _alwaysTable = "always";
    private const string _pickTable = "pick";
    private const string _agentKey = "agent";
    private const string _itemKey = "item";
    private const string _fieldKey = "field";
    private const string _ruleKey = "rule";

    // Room for every agent and every remembered answer, each naming a field or an attachment and a name up to a request's length.
    private static readonly TomlLimits _limits = TomlLimits.Policy with
    {
        Tables = AgentRules.MaximumAgents + AgentRules.MaximumRemembered + 2,
        Lines = 2048,
        Bytes = 256 * 1024,
        LineLength = 1024,
        StringLength = Approval.CredentialRequestRules.MaximumEntryLength + 128,
    };

    /// <summary>The file.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>This home's identity, beside the file.</summary>
    public string HomeIdPath => System.IO.Path.ChangeExtension(Path, ".id");

    /// <summary>The rules the file says, unverified: what <c>keypaste clients</c> lists.</summary>
    /// <param name="rules">The rules, when the file could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>False when the file holds what keypaste cannot read.</returns>
    public bool TryRead([NotNullWhen(true)] out AgentRules? rules, out string problem)
    {
        rules = TryReadFile(out var read, out problem) ? read.Rules : null;
        return rules is not null;
    }

    /// <summary>The rules the file says and the seal it carries.</summary>
    /// <param name="read">What it says, when the file could be read.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>False when the file holds what keypaste cannot read.</returns>
    public bool TryReadFile([NotNullWhen(true)] out RulesFileRead? read, out string problem)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(Path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            read = new RulesFileRead(AgentRules.Defaults, null, Present: false);
            problem = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            read = null;
            problem = $"{Path} could not be read: {ex.Message}";
            return false;
        }

        if (!Toml.TryDecode(bytes, _limits, out var text, out var error)
            || !Toml.TryParse(text, _limits, out var document, out error)
            || !TryParse(document, out var rules, out var seal, out error))
        {
            read = null;
            problem = $"{Path}: {error}";
            return false;
        }

        read = new RulesFileRead(rules, seal, Present: true);
        problem = string.Empty;
        return true;
    }

    /// <summary>This home's identity, or null when there is none yet or it is not one keypaste wrote.</summary>
    /// <returns>The identity.</returns>
    public string? HomeId()
    {
        try
        {
            var id = File.ReadAllText(HomeIdPath).Trim();
            return id.Length == 32 && id.All(char.IsAsciiHexDigitLower) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>This home's identity, made at random and written beside the file when there is none.</summary>
    /// <param name="id">The identity, when there is one.</param>
    /// <param name="problem">Why not, otherwise empty.</param>
    /// <returns>Whether there is one.</returns>
    public bool TryEnsureHomeId([NotNullWhen(true)] out string? id, out string problem)
    {
        id = HomeId();
        problem = string.Empty;

        if (id is not null)
        {
            return true;
        }

        try
        {
            var made = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            AtomicFile.Write(HomeIdPath, Encoding.UTF8.GetBytes(made + "\n"));
            id = made;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"{HomeIdPath} could not be written: {ex.Message}";
            return false;
        }
    }

    /// <summary>The rules as the seal covers them: the file's sections, without its comments or its seal.</summary>
    /// <param name="rules">The rules.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentException">An agent's name cannot be written.</exception>
    public static string Canonical(AgentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return Render(new TomlWriter(_limits), rules).ToString();
    }

    /// <summary>Writes the rules in place of what the file holds, in one rename.</summary>
    /// <param name="rules">The rules.</param>
    /// <param name="seal">The seal the owner computed for them, or null to write them unsealed, which no owner then follows.</param>
    /// <param name="problem">Why it was not written, otherwise empty.</param>
    /// <returns>Whether the file holds them.</returns>
    /// <remarks>A file that cannot be read now is left as it is, so a hand edit part-way through is not lost.</remarks>
    public bool TryWrite(AgentRules rules, string? seal, out string problem)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (File.Exists(Path) && !TryReadFile(out _, out problem))
        {
            problem = $"{problem}; keypaste left it as it is";
            return false;
        }

        if (rules.Agents.FirstOrDefault(agent => !CanWrite(agent.Name)) is { } unwritable)
        {
            problem = $"an agent's name cannot be written: {unwritable.Name}";
            return false;
        }

        if (rules.Answers.Any(answer => !CanWrite(answer.Field)) || rules.Picks.Any(pick => !CanWrite(pick.Name) || !CanWrite(pick.Field)))
        {
            problem = "a remembered answer names a field or an item that cannot be written";
            return false;
        }

        var writer = new TomlWriter(_limits)
            .Comment(" What every AI agent gets on this computer. keypaste writes this file and seals it with a key the vault holds;")
            .Comment(" a change made anywhere else is ignored, and every value is asked about, until it is set again in keypaste.");
        Render(writer, rules);

        if (seal is not null)
        {
            writer.BlankLine().Table(_sealTable).Text(_macKey, seal);
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            AtomicFile.Write(Path, Encoding.UTF8.GetBytes(writer.ToString()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"{Path} could not be written: {ex.Message}";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static TomlWriter Render(TomlWriter writer, AgentRules rules)
    {
        writer.Table(_rulesTable).Text(_usernameKey, rules.ShowsUsername ? "shown" : "hidden");

        foreach (var category in FieldCategories.Ruled)
        {
            writer.Text(FieldCategories.Key(category), Word(rules.RuleFor(category)));
        }

        foreach (var agent in rules.Agents)
        {
            writer.BlankLine().Table(_agentTable).Text(_nameKey, agent.Name).Number(_runKey, agent.Run ? 1 : 0, "1 lets it ask to run commands, each run asked");

            foreach (var category in FieldCategories.Ruled)
            {
                if (rules.RuleFor(agent.Name, category) is { } own)
                {
                    writer.Text(FieldCategories.Key(category), Word(own));
                }
            }
        }

        foreach (var answer in rules.Answers)
        {
            writer.BlankLine().Table(_alwaysTable).Text(_agentKey, answer.Agent).Text(_itemKey, answer.Item).Text(_fieldKey, answer.Field).Text(_ruleKey, Word(answer.Rule));
        }

        foreach (var pick in rules.Picks)
        {
            writer.BlankLine().Table(_pickTable).Text(_agentKey, pick.Agent).Text(_nameKey, pick.Name).Text(_fieldKey, pick.Field).Text(_itemKey, pick.Item);
        }

        return writer;
    }

    private static bool CanWrite(string value) => new TomlWriter(_limits).CanWrite(_nameKey, value);

    private static bool TryParse(TomlDocument document, [NotNullWhen(true)] out AgentRules? rules, out string? seal, out string error)
    {
        rules = null;
        seal = null;
        var parsed = AgentRules.Defaults;
        List<KnownAgent> agents = [];
        List<AgentCategoryRule> agentCategories = [];
        List<RememberedAnswer> answers = [];
        List<RememberedPick> picks = [];
        var sawRules = false;

        foreach (var table in document.Tables)
        {
            switch (table.Name)
            {
                case _sealTable when seal is not null:
                    error = $"{At(table.Line)}: only one [[{_sealTable}]] section is read";
                    return false;
                case _sealTable:
                    if (table.Pairs is not [{ Key: _macKey, Value: { Kind: TomlValueKind.Text, Text: var mac } }])
                    {
                        error = $"{At(table.Line)}: [[{_sealTable}]] holds one mac";
                        return false;
                    }

                    seal = mac;
                    break;
                case _rulesTable when sawRules:
                    error = $"{At(table.Line)}: only one [[{_rulesTable}]] section is read";
                    return false;
                case _rulesTable:
                    sawRules = true;

                    if (!TryRules(table, parsed, out parsed, out error))
                    {
                        return false;
                    }

                    break;
                case _agentTable:
                    if (!TryAgent(table, out var agent, agentCategories, out error))
                    {
                        return false;
                    }

                    agents.Add(agent);
                    break;
                case _alwaysTable:
                    if (!TryAnswer(table, out var answer, out error))
                    {
                        return false;
                    }

                    answers.Add(answer);
                    break;
                case _pickTable:
                    if (!TryPick(table, out var pick, out error))
                    {
                        return false;
                    }

                    picks.Add(pick);
                    break;
                default:
                    error = $"{At(table.Line)}: [[{table.Name}]] is not a section keypaste reads here";
                    return false;
            }
        }

        if (agents.Select(agent => agent.Name).Distinct(StringComparer.Ordinal).Count() != agents.Count)
        {
            error = "two [[agent]] sections name the same agent";
            return false;
        }

        if (answers.Count + picks.Count > AgentRules.MaximumRemembered)
        {
            error = $"keypaste reads at most {AgentRules.MaximumRemembered} [[{_alwaysTable}]] and [[{_pickTable}]] sections";
            return false;
        }

        if (answers.GroupBy(answer => (answer.Agent, answer.Item, answer.Field)).Any(same => same.Count() > 1)
            || picks.GroupBy(pick => (pick.Agent, pick.Name, pick.Field)).Any(same => same.Count() > 1))
        {
            error = "two sections remember an answer for the same agent and field";
            return false;
        }

        rules = (parsed with { Agents = agents }).Read(agentCategories, answers, picks);
        error = string.Empty;
        return true;
    }

    private static bool TryRules(TomlTable table, AgentRules start, out AgentRules rules, out string error)
    {
        rules = start;

        foreach (var pair in table.Pairs)
        {
            if (pair.Value.Kind != TomlValueKind.Text)
            {
                error = $"{At(pair.Line)}: {pair.Key} is a quoted word";
                return false;
            }

            if (string.Equals(pair.Key, _usernameKey, StringComparison.Ordinal))
            {
                switch (pair.Value.Text)
                {
                    case "shown":
                        rules = rules with { ShowsUsername = true };
                        continue;
                    case "hidden":
                        rules = rules with { ShowsUsername = false };
                        continue;
                    default:
                        error = $"{At(pair.Line)}: {_usernameKey} is \"shown\" or \"hidden\"";
                        return false;
                }
            }

            var category = FieldCategories.Ruled.FirstOrDefault(ruled => string.Equals(FieldCategories.Key(ruled), pair.Key, StringComparison.Ordinal));

            // The username is no ruled category, so it is what no key found.
            if (category == FieldCategory.Username || !TryWord(pair.Value.Text, out var rule))
            {
                error = $"{At(pair.Line)}: {pair.Key} = \"{pair.Value.Text}\" is not a rule keypaste reads; each is ask, allow or block";
                return false;
            }

            rules = rule == CategoryRule.Allow ? rules.Allowing(category, confirmed: true) : rules.With(category, rule);
        }

        error = string.Empty;
        return true;
    }

    private static bool TryAgent(TomlTable table, [NotNullWhen(true)] out KnownAgent? agent, List<AgentCategoryRule> agentCategories, out string error)
    {
        agent = null;

        if (!table.TryGet(_nameKey, out var name) || name.Value.Kind != TomlValueKind.Text || !AgentNames.IsNamed(name.Value.Text))
        {
            error = $"{At(table.Line)}: an [[agent]] section needs a name, its bridge's --client-label";
            return false;
        }

        var run = false;

        if (table.TryGet(_runKey, out var runs))
        {
            if (runs.Value is not { Kind: TomlValueKind.Number, Number: 0 or 1 })
            {
                error = $"{At(runs.Line)}: run is 0 or 1";
                return false;
            }

            run = runs.Value.Number == 1;
        }

        foreach (var pair in table.Pairs.Where(pair => pair.Key is not (_nameKey or _runKey)))
        {
            var category = FieldCategories.Ruled.FirstOrDefault(ruled => string.Equals(FieldCategories.Key(ruled), pair.Key, StringComparison.Ordinal));

            if (category == FieldCategory.Username)
            {
                error = $"{At(pair.Line)}: {pair.Key} is not a key keypaste reads in [[agent]]";
                return false;
            }

            if (pair.Value.Kind != TomlValueKind.Text || !TryWord(pair.Value.Text, out var rule))
            {
                error = $"{At(pair.Line)}: {pair.Key} is ask, allow or block";
                return false;
            }

            agentCategories.Add(new AgentCategoryRule(name.Value.Text, category, rule));
        }

        agent = new KnownAgent(name.Value.Text, run);
        error = string.Empty;
        return true;
    }

    private static bool TryAnswer(TomlTable table, [NotNullWhen(true)] out RememberedAnswer? answer, out string error)
    {
        answer = null;

        if (!TryTexts(table, [_agentKey, _itemKey, _fieldKey, _ruleKey], out var texts, out error))
        {
            return false;
        }

        if (!AgentNames.IsNamed(texts[0]) || !AgentRules.IsItem(texts[1]) || texts[2].Length == 0 || !TryWord(texts[3], out var rule) || rule == CategoryRule.Ask)
        {
            error = $"{At(table.Line)}: an [[{_alwaysTable}]] section names an agent, an item's UUID and a field, and its rule is allow or block";
            return false;
        }

        answer = new RememberedAnswer(texts[0], texts[1].ToLowerInvariant(), texts[2], rule);
        return true;
    }

    private static bool TryPick(TomlTable table, [NotNullWhen(true)] out RememberedPick? pick, out string error)
    {
        pick = null;

        if (!TryTexts(table, [_agentKey, _nameKey, _fieldKey, _itemKey], out var texts, out error))
        {
            return false;
        }

        if (!AgentNames.IsNamed(texts[0]) || texts[1].Length == 0 || texts[2].Length == 0 || !AgentRules.IsItem(texts[3]))
        {
            error = $"{At(table.Line)}: a [[{_pickTable}]] section names an agent, the name it asked for, a field and an item's UUID";
            return false;
        }

        pick = new RememberedPick(texts[0], texts[1], texts[2], texts[3].ToLowerInvariant());
        return true;
    }

    /// <summary>A section's quoted words, exactly these keys and no other.</summary>
    private static bool TryTexts(TomlTable table, string[] keys, [NotNullWhen(true)] out string[]? texts, out string error)
    {
        texts = null;

        if (table.Pairs.Count != keys.Length || keys.Any(key => !table.TryGet(key, out var pair) || pair.Value.Kind != TomlValueKind.Text))
        {
            error = $"{At(table.Line)}: [[{table.Name}]] holds {string.Join(", ", keys)}, each a quoted word";
            return false;
        }

        texts = [.. keys.Select(key => table.TryGet(key, out var pair) ? pair.Value.Text : string.Empty)];
        error = string.Empty;
        return true;
    }

    private static string At(int line) => string.Create(CultureInfo.InvariantCulture, $"line {line}");

    private static string Word(CategoryRule rule) => rule switch
    {
        CategoryRule.Allow => "allow",
        CategoryRule.Block => "block",
        _ => "ask",
    };

    private static bool TryWord(string word, out CategoryRule rule)
    {
        (var known, rule) = word switch
        {
            "ask" => (true, CategoryRule.Ask),
            "allow" => (true, CategoryRule.Allow),
            "block" => (true, CategoryRule.Block),
            _ => (false, CategoryRule.Ask),
        };

        return known;
    }
}
