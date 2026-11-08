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

/// <summary>
/// <c>agents.toml</c> under the keypaste home: whether item details are shown, each category's rule and each known
/// agent's run switch, read at each request so an edit applies to the next one.
/// </summary>
/// <remarks>
/// An absent file is <see cref="AgentRules.Defaults"/>. One that cannot be read or parsed refuses every agent request
/// and is never rewritten, so a rule a person wrote is not silently lost or lifted.
/// </remarks>
/// <param name="path">From <see cref="Audit.KeypasteHome.AgentsPath"/>.</param>
public sealed class AgentRulesFile(string path) : IAgentRules
{
    private const string _rulesTable = "rules";
    private const string _agentTable = "agent";
    private const string _detailsKey = "details";
    private const string _nameKey = "name";
    private const string _runKey = "run";

    private static readonly TomlLimits _limits = TomlLimits.Policy with { Tables = AgentRules.MaximumAgents + 1 };

    /// <summary>The file.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <inheritdoc/>
    public bool TryRead([NotNullWhen(true)] out AgentRules? rules, out string problem)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(Path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            rules = AgentRules.Defaults;
            problem = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rules = null;
            problem = $"{Path} could not be read: {ex.Message}";
            return false;
        }

        if (!Toml.TryDecode(bytes, _limits, out var text, out var error)
            || !Toml.TryParse(text, _limits, out var document, out error)
            || !TryParse(document, out rules, out error))
        {
            rules = null;
            problem = $"{Path}: {error}";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    /// <summary>Writes the rules in place of what the file holds, in one rename.</summary>
    /// <param name="rules">The rules.</param>
    /// <param name="problem">Why it was not written, otherwise empty.</param>
    /// <returns>Whether the file holds them.</returns>
    /// <remarks>A file that cannot be read now is left as it is, so a hand edit part-way through is not lost.</remarks>
    public bool TryWrite(AgentRules rules, out string problem)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (File.Exists(Path) && !TryRead(out _, out problem))
        {
            problem = $"{problem}; keypaste left it as it is";
            return false;
        }

        var writer = new TomlWriter(_limits)
            .Comment(" What every AI agent gets on this computer. keypaste writes this file; a vault never holds it.")
            .Comment(" Each rule is ask, allow or block. Settings asks you to confirm an allow before writing it.")
            .Table(_rulesTable)
            .Text(_detailsKey, rules.ShowsDetails ? "shown" : "hidden");

        foreach (var category in FieldCategories.Ruled)
        {
            writer.Text(FieldCategories.Key(category), Word(rules.RuleFor(category)));
        }

        foreach (var agent in rules.Agents)
        {
            if (!writer.CanWrite(_nameKey, agent.Name))
            {
                problem = $"an agent's name cannot be written: {agent.Name}";
                return false;
            }

            writer.BlankLine().Table(_agentTable).Text(_nameKey, agent.Name).Number(_runKey, agent.Run ? 1 : 0, "1 lets it ask to run commands, each run asked");
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

    private static bool TryParse(TomlDocument document, [NotNullWhen(true)] out AgentRules? rules, out string error)
    {
        rules = null;
        var parsed = AgentRules.Defaults;
        List<KnownAgent> agents = [];
        var sawRules = false;

        foreach (var table in document.Tables)
        {
            switch (table.Name)
            {
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
                    if (!TryAgent(table, out var agent, out error))
                    {
                        return false;
                    }

                    agents.Add(agent);
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

        rules = parsed with { Agents = agents };
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

            if (string.Equals(pair.Key, _detailsKey, StringComparison.Ordinal))
            {
                switch (pair.Value.Text)
                {
                    case "shown":
                        rules = rules with { ShowsDetails = true };
                        continue;
                    case "hidden":
                        rules = rules with { ShowsDetails = false };
                        continue;
                    default:
                        error = $"{At(pair.Line)}: {_detailsKey} is \"shown\" or \"hidden\"";
                        return false;
                }
            }

            var category = FieldCategories.Ruled.FirstOrDefault(ruled => string.Equals(FieldCategories.Key(ruled), pair.Key, StringComparison.Ordinal));

            // Detail is no ruled category, so it is what no key found.
            if (category == FieldCategory.Detail || !TryWord(pair.Value.Text, out var rule))
            {
                error = $"{At(pair.Line)}: {pair.Key} = \"{pair.Value.Text}\" is not a rule keypaste reads; each is ask, allow or block";
                return false;
            }

            rules = rule == CategoryRule.Allow ? rules.Allowing(category, confirmed: true) : rules.With(category, rule);
        }

        error = string.Empty;
        return true;
    }

    private static bool TryAgent(TomlTable table, [NotNullWhen(true)] out KnownAgent? agent, out string error)
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

        if (table.Pairs.FirstOrDefault(pair => pair.Key is not (_nameKey or _runKey)) is { } unknown)
        {
            error = $"{At(unknown.Line)}: {unknown.Key} is not a key keypaste reads in [[agent]]";
            return false;
        }

        agent = new KnownAgent(name.Value.Text, run);
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
