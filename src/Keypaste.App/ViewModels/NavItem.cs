using Keypaste.App.Navigation;

namespace Keypaste.App.ViewModels;

/// <summary>A sidebar row: a place, the count beside it and, for Agents, a status dot.</summary>
internal sealed partial class NavItem : ObservableObject
{
    internal NavItem(Destination destination)
    {
        Destination = destination;
    }

    internal Destination Destination { get; }

    internal string Title => Destination.Title;

    internal string Icon => Destination.Icon;

    /// <summary>The number at the right of the row, or empty. Always muted: a live signal is the dot's.</summary>
    [ObservableProperty]
    internal partial string Count { get; set; } = string.Empty;

    /// <summary>Whether the row draws a status dot: Agents does, for whether agents can reach this vault.</summary>
    internal bool HasDot { get; init; }

    /// <summary>Whether the dot is green: this app is answering agents for the vault.</summary>
    [ObservableProperty]
    internal partial bool DotLive { get; set; }

    /// <summary>Whether the dot is red: this app holds the vault and refuses every agent request (D-0494).</summary>
    [ObservableProperty]
    internal partial bool DotDanger { get; set; }

    /// <summary>Whether the row folds the rows under it: Items does, over the group tree.</summary>
    internal bool IsExpandable { get; init; }

    /// <summary>Whether the rows under it are showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    internal partial bool IsExpanded { get; set; } = true;

    /// <summary>The fold's chevron: down while open, right while closed.</summary>
    internal string Chevron => IsExpanded ? "chevron-down" : "chevron-right";

    /// <summary>The row's tooltip and automation help text, or null.</summary>
    [ObservableProperty]
    internal partial string? Detail { get; set; }
}

/// <summary>A project in the sidebar, beneath Items, with how many entries are tagged into it.</summary>
internal sealed record ProjectRow(string Name, int Count)
{
    /// <summary>The row's name for a screen reader.</summary>
    internal string Title => Name;
}
