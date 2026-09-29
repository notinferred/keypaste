using Keypaste.App.Navigation;

namespace Keypaste.App.ViewModels;

/// <summary>A sidebar row: a place, the count beside it and, for Agents, a status dot.</summary>
internal sealed class NavItem(Destination destination) : ObservableObject
{
    private string _count = string.Empty;
    private bool _dotLive;
    private string? _detail;

    internal Destination Destination { get; } = destination;

    internal string Title => Destination.Title;

    internal string Icon => Destination.Icon;

    /// <summary>The number at the right of the row, or empty. Always muted: a live signal is the dot's.</summary>
    internal string Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    /// <summary>Whether the row draws a status dot: Agents does, for whether agents can reach this vault.</summary>
    internal bool HasDot { get; init; }

    /// <summary>Whether the dot is green: this app is answering agents for the vault.</summary>
    internal bool DotLive
    {
        get => _dotLive;
        set => Set(ref _dotLive, value);
    }

    /// <summary>The row's tooltip and automation help text, or null.</summary>
    internal string? Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }
}

/// <summary>An env project in the sidebar, beneath Items, with how many variables it holds.</summary>
internal sealed record ProjectRow(string Name, int Count)
{
    /// <summary>The row's name for a screen reader.</summary>
    internal string Title => Name;
}
