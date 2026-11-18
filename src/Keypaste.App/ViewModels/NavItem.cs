using System.Globalization;
using Keypaste.App.Navigation;

namespace Keypaste.App.ViewModels;

/// <summary>A place on the rail: its icon, the count on its badge and, for Agents, a status dot.</summary>
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
    [NotifyPropertyChangedFor(nameof(HasBadge), nameof(RailName))]
    internal partial string Count { get; set; } = string.Empty;

    /// <summary>Whether the row draws a status dot: Agents does, for whether agents can reach this vault.</summary>
    internal bool HasDot { get; init; }

    /// <summary>Whether the mark is a green dot: this app is answering agents for the vault.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotWords), nameof(RailName), nameof(DotStopped))]
    internal partial bool DotLive { get; set; }

    /// <summary>Whether the mark is a red diamond: this app holds the vault and refuses every agent request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DotWords), nameof(RailName), nameof(DotStopped))]
    internal partial bool DotDanger { get; set; }

    /// <summary>Whether the mark is a ring: this app is not answering agents for the vault.</summary>
    internal bool DotStopped => !DotLive && !DotDanger;

    /// <summary>Whether the slot is kept for later: dimmed, never chosen, and named so in its tooltip.</summary>
    internal bool IsLater => Destination.Kind == DestinationKind.Access;

    /// <summary>Whether the badge shows: Security's count of what needs review.</summary>
    internal bool HasBadge => Count.Length > 0;

    /// <summary>The dot's state in words, beside its colour.</summary>
    internal string DotWords => DotDanger ? "refusing" : DotLive ? "live" : string.Empty;

    /// <summary>The row's tooltip and automation help text, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailTip))]
    internal partial string? Detail { get; set; }

    /// <summary>How many requests are waiting for an answer; the rail's accessible name says it, since the rail has no count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailName))]
    internal partial int Waiting { get; set; }

    /// <summary>The rail's row for a screen reader: the rail draws no word, so its name says what the dot's colour and the row's count do.</summary>
    internal string RailName => string.Join(", ", new[] { Title, DotWords, Waiting > 0 ? string.Create(CultureInfo.InvariantCulture, $"{Waiting} waiting") : string.Empty, HasDot ? string.Empty : Count }.Where(part => part.Length > 0));

    /// <summary>The rail's tooltip: the place, and what the row would say beside it if there were room.</summary>
    internal string RailTip => string.IsNullOrEmpty(Detail) ? Title : $"{Title} · {Detail}";

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => string.Join(", ", new[] { Title, Count, DotWords }.Where(part => part.Length > 0));
}

/// <summary>A project in the sidebar, beneath its vault's Environments while that is in view, with how many entries are tagged into it.</summary>
internal sealed record ProjectRow(string Name, int Count)
{
    /// <summary>The row's name for a screen reader.</summary>
    internal string Title => Name;

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name}, {Count}");
}
