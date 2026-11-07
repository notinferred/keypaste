namespace Keypaste.Core.Agents;

/// <summary>How an agent's requests for values are answered, ordered by what they give.</summary>
public enum AgentMode
{
    /// <summary>Ask every time: a person answers each request.</summary>
    Ask = 0,

    /// <summary>Automatic: what the agent can see is released without asking and recorded in history. Commands still ask.</summary>
    Automatic = 1,
}

/// <summary>The words an ACAF record, <c>clients.toml</c> and the CLI use for a mode.</summary>
public static class AgentModes
{
    /// <summary>A mode's word.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns><c>ask</c> or <c>automatic</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a mode.</exception>
    public static string Name(AgentMode mode) => mode switch
    {
        AgentMode.Ask => "ask",
        AgentMode.Automatic => "automatic",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Reads a mode's word given as a new setting.</summary>
    /// <param name="text">The word, exactly as <see cref="Name"/> spells it.</param>
    /// <param name="mode">The mode, when the word is one.</param>
    /// <returns>Whether it names a mode.</returns>
    public static bool TryParse(string text, out AgentMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);

        (var known, mode) = text switch
        {
            "ask" => (true, AgentMode.Ask),
            "automatic" => (true, AgentMode.Automatic),
            _ => (false, AgentMode.Ask),
        };

        return known;
    }

    /// <summary>Reads a mode's word a record or <c>clients.toml</c> holds, where the word earlier versions wrote for a timed mode still reads, as Ask every time.</summary>
    /// <param name="text">The word.</param>
    /// <param name="mode">The mode, when the word is one.</param>
    /// <returns>Whether it names a mode.</returns>
    public static bool TryReadStored(string text, out AgentMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParse(text, out mode) || string.Equals(text, RetiredWord, StringComparison.Ordinal);
    }

    /// <summary>The word of the timed mode earlier versions wrote: read, and never written.</summary>
    internal const string RetiredWord = "allow-for-a-while";

    /// <summary>The mode that gives less.</summary>
    internal static AgentMode Least(AgentMode first, AgentMode second) => first < second ? first : second;

    /// <summary>A mode, refusing a value that names none.</summary>
    internal static AgentMode Checked(AgentMode mode, string parameter) =>
        Enum.IsDefined(mode) ? mode : throw new ArgumentOutOfRangeException(parameter);
}
