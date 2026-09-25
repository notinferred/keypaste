using Keypaste.App.Navigation;

namespace Keypaste.App.ViewModels;

/// <summary>A sidebar row: a destination and the count beside it.</summary>
internal sealed class NavItem(Destination destination) : ObservableObject
{
    private string _count = string.Empty;
    private bool _isLive;

    internal Destination Destination { get; } = destination;

    internal string Title => Destination.Title;

    internal string Icon => Destination.Icon;

    /// <summary>The number at the right of the row, or empty.</summary>
    internal string Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    /// <summary>Whether the count is something live that needs a person, drawn in amber.</summary>
    internal bool IsLive
    {
        get => _isLive;
        set => Set(ref _isLive, value);
    }
}

/// <summary>An env project in the sidebar, with how many variables it holds.</summary>
internal sealed record ProjectRow(string Name, int Count);
