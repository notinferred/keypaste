using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;

namespace Keypaste.Core.Tests;

/// <summary>This computer's agent rules as a test sets them, or a file that cannot be read.</summary>
internal sealed class FakeAgentRules : IAgentRules
{
    internal AgentRules Rules { get; set; } = AgentRules.Defaults;

    /// <summary>When set, the rules cannot be read, for this reason.</summary>
    internal string? Unreadable { get; set; }

    public bool TryChange(Func<AgentRules, AgentRules> change, out string problem)
    {
        Rules = change(Rules);
        problem = string.Empty;
        return true;
    }

    public bool TryRead([NotNullWhen(true)] out AgentRules? rules, out string problem)
    {
        rules = Unreadable is null ? Rules : null;
        problem = Unreadable ?? string.Empty;
        return rules is not null;
    }
}
