using System.Globalization;
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
    [NotifyPropertyChangedFor(nameof(DotWords))]
    internal partial bool DotLive { get; set; }

    /// <summary>Whether the dot is red: this app holds the vault and refuses every agent request (D-0494).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotWords))]
    internal partial bool DotDanger { get; set; }

    /// <summary>Whether the row folds the rows under it: Items does, over the group tree.</summary>
    internal bool IsExpandable { get; init; }

    /// <summary>Whether the rows under it are showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron), nameof(FoldLabel))]
    internal partial bool IsExpanded { get; set; } = true;

    /// <summary>The fold's chevron: down while open, right while closed.</summary>
    internal string Chevron => IsExpanded ? "chevron-down" : "chevron-right";

    /// <summary>What the fold button will do.</summary>
    internal string FoldLabel => IsExpanded ? "Hide the groups" : "Show the groups";

    /// <summary>The dot's state in words, beside its colour.</summary>
    internal string DotWords => DotDanger ? "refusing" : DotLive ? "live" : string.Empty;

    /// <summary>The row's tooltip and automation help text, or null.</summary>
    [ObservableProperty]
    internal partial string? Detail { get; set; }

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => string.Join(", ", new[] { Title, IsExpandable ? "level 1" : string.Empty, Count, DotWords }.Where(part => part.Length > 0));
}

/// <summary>A project in the sidebar, beneath Items, with how many entries are tagged into it.</summary>
internal sealed record ProjectRow(string Name, int Count)
{
    /// <summary>The row's name for a screen reader.</summary>
    internal string Title => Name;

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name}, {Count}");
}
