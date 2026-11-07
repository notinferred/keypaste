namespace Keypaste.App.ViewModels;

/// <summary>Removes one row of an agent's Always allowed list.</summary>
/// <param name="Run">Removes it.</param>
/// <param name="Settings">The agent's settings it is listed in.</param>
internal sealed record AlwaysRemoval(Func<Task> Run, AgentSettingsViewModel Settings)
{
    /// <summary>Whether the settings can take a change now.</summary>
    internal bool CanRemove => Settings.CanChange;
}

/// <summary>One thing an agent is always allowed, as its settings list it. It holds names, never a value.</summary>
/// <param name="What">The item as shown, the command line, or the project row's name.</param>
/// <param name="Detail">What kind it is, and why it does not apply now if it does not.</param>
/// <param name="Identity">For a command, the rest of what makes it that command: its program, its directory and where each variable comes from; otherwise empty.</param>
/// <param name="Remove">The way to remove it.</param>
internal sealed record AlwaysRow(string What, string Detail, string Identity, AlwaysRemoval Remove)
{
    /// <summary>Whether it has more to identify it than <see cref="What"/>.</summary>
    internal bool HasIdentity => Identity.Length > 0;

    /// <summary>What Remove is announced as.</summary>
    internal string RemoveName => $"Remove always allowed {What}";

    /// <summary>Removes the row, as its button does.</summary>
    internal RelayCommand RemoveCommand { get; } = new(() => _ = Remove.Run(), () => Remove.CanRemove);
}
