namespace Keypaste.Core.Agents;

/// <summary>How an agent's requests for values are answered, ordered by what they give.</summary>
public enum AgentMode
{
    /// <summary>Ask every time: a person answers each request, with Allow once only.</summary>
    Ask = 0,

    /// <summary>Ask, allow for a while: a person answers, and may allow for the timed grant the prompt offers.</summary>
    AllowForAWhile = 1,

    /// <summary>Automatic: what the agent can see is released without asking and recorded in history. Commands still ask.</summary>
    Automatic = 2,
}

/// <summary>The words an ACAF record, <c>clients.toml</c> and the CLI use for a mode.</summary>
public static class AgentModes
{
    /// <summary>A mode's word.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns><c>ask</c>, <c>allow-for-a-while</c> or <c>automatic</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a mode.</exception>
    public static string Name(AgentMode mode) => mode switch
    {
        AgentMode.Ask => "ask",
        AgentMode.AllowForAWhile => "allow-for-a-while",
        AgentMode.Automatic => "automatic",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Reads a mode's word.</summary>
    /// <param name="text">The word, exactly as <see cref="Name"/> spells it.</param>
    /// <param name="mode">The mode, when the word is one.</param>
    /// <returns>Whether it names a mode.</returns>
    public static bool TryParse(string text, out AgentMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);

        (var known, mode) = text switch
        {
            "ask" => (true, AgentMode.Ask),
            "allow-for-a-while" => (true, AgentMode.AllowForAWhile),
            "automatic" => (true, AgentMode.Automatic),
            _ => (false, AgentMode.Ask),
        };

        return known;
    }

    /// <summary>The mode that gives less.</summary>
    internal static AgentMode Least(AgentMode first, AgentMode second) => first < second ? first : second;

    /// <summary>A mode, refusing a value that names none.</summary>
    internal static AgentMode Checked(AgentMode mode, string parameter) =>
        Enum.IsDefined(mode) ? mode : throw new ArgumentOutOfRangeException(parameter);
}
