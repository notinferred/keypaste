namespace Keypaste.App.ViewModels;

/// <summary>Removes one row of an agent's Always allowed list.</summary>
/// <param name="Id">The row's short name.</param>
/// <param name="Shown">What it covers, for the button's accessible name.</param>
/// <param name="Settings">The agent's settings it is listed in.</param>
internal sealed record AlwaysRemoval(string Id, string Shown, AgentSettingsViewModel Settings)
{
    /// <summary>Whether the settings can take a change now.</summary>
    internal bool CanRemove => Settings.CanChange;

    /// <summary>Removes the row.</summary>
    internal void Run() => _ = Settings.RemoveAlwaysAsync(Id);
}

/// <summary>One thing an agent is always allowed, as its settings list it. It holds names, never a value.</summary>
/// <param name="What">The item as shown, or the command line.</param>
/// <param name="Detail">What kind it is, and why it does not apply now if it does not.</param>
/// <param name="Remove">The way to remove it.</param>
internal sealed record AlwaysRow(string What, string Detail, AlwaysRemoval Remove)
{
    /// <summary>What Remove is announced as.</summary>
    internal string RemoveName => $"Remove always allowed {What}";

    /// <summary>Removes the row, as its button does.</summary>
    internal RelayCommand RemoveCommand { get; } = new(Remove.Run, () => Remove.CanRemove);
}
