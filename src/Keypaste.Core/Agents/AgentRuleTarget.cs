namespace Keypaste.Core.Agents;

/// <summary>An agent's rule for one category as a terminal names it, <c>category=rule</c>, such as <c>password=block</c>.</summary>
public static class AgentRuleTarget
{
    /// <summary>The words a rule is named by: <c>ask</c>, <c>block</c>, or <c>settings</c> for what Settings sets.</summary>
    public const string Rules = "ask|block|settings";

    /// <summary>Reads a target.</summary>
    /// <param name="target">The text.</param>
    /// <param name="category">The ruled category it names.</param>
    /// <param name="rule">Ask, Block, or null for what Settings sets.</param>
    /// <returns>Whether the text names a ruled category and one of <see cref="Rules"/>.</returns>
    public static bool TryParse(string target, out FieldCategory category, out CategoryRule? rule)
    {
        ArgumentNullException.ThrowIfNull(target);

        rule = null;
        var equals = target.IndexOf('=', StringComparison.Ordinal);

        if (equals < 0 || !FieldCategories.TryParseKey(target[..equals], out category))
        {
            category = default;
            return false;
        }

        (var known, rule) = target[(equals + 1)..] switch
        {
            "ask" => (true, CategoryRule.Ask),
            "block" => (true, CategoryRule.Block),
            "settings" => (true, (CategoryRule?)null),
            _ => (false, null),
        };

        return known;
    }

    /// <summary>The rules with a target applied by <see cref="AgentRules.Narrowing"/>.</summary>
    /// <param name="rules">The rules that hold.</param>
    /// <param name="agent">The agent.</param>
    /// <param name="target">The target.</param>
    /// <returns>The new rules.</returns>
    /// <exception cref="ArgumentException">The target is not one, or the change would release more without asking.</exception>
    public static AgentRules Apply(AgentRules rules, string agent, string target)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return TryParse(target, out var category, out var rule)
            ? rules.Narrowing(agent, category, rule)
            : throw new ArgumentException($"a rule is a kind of value and {Rules}, such as password=block");
    }
}
