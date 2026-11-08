namespace Keypaste.Core.Agents;

/// <summary>What saving a change to the vault came to.</summary>
public enum AgentSaveOutcome
{
    /// <summary>The vault's file holds the change.</summary>
    Saved = 0,

    /// <summary>The vault already said this, so nothing was saved.</summary>
    Unchanged = 1,

    /// <summary>The change was refused before anything changed; the reason says why.</summary>
    Refused = 2,

    /// <summary>No vault is open.</summary>
    Locked = 3,

    /// <summary>Another program saved the vault since it was opened, so nothing was written.</summary>
    ChangedOnDisk = 4,

    /// <summary>The save failed; the reason says why.</summary>
    Failed = 5,
}

/// <summary>What saving a change to the vault came to, and why when it did not.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Reason">keypaste's own words when nothing was saved, otherwise empty.</param>
public sealed record AgentSave(AgentSaveOutcome Outcome, string Reason)
{
    /// <summary>The file holds the change.</summary>
    public static AgentSave Saved { get; } = new(AgentSaveOutcome.Saved, string.Empty);

    /// <summary>Nothing needed saving.</summary>
    public static AgentSave Unchanged { get; } = new(AgentSaveOutcome.Unchanged, string.Empty);

    /// <summary>Whether the vault's file now holds what was asked.</summary>
    public bool Holds => Outcome is AgentSaveOutcome.Saved or AgentSaveOutcome.Unchanged;
}
