using Keypaste.App.Resources;
namespace Keypaste.App.Navigation;

/// <summary>The screens the shell can show.</summary>
internal enum DestinationKind
{
    /// <summary>Every entry in the vault: logins, keys, notes. The sidebar calls it Items.</summary>
    Entries = 0,

    /// <summary>A project's variables, reached from its sidebar row or Items' "+" menu.</summary>
    EnvSets = 1,

    /// <summary>What agents are asking for and have been granted. The sidebar calls it Agents.</summary>
    AgentActivity = 2,

    /// <summary>The audit log, under Agents.</summary>
    Log = 3,

    /// <summary>Settings.</summary>
    Settings = 4,

    /// <summary>The recycle bin, and the way back out of it.</summary>
    Trash = 5,

    /// <summary>The share links made from this vault, under Settings › Advanced.</summary>
    Sharing = 6,

    /// <summary>The vault's scoped tokens, minting and revoking one, under Settings › Advanced.</summary>
    Tokens = 8,

    /// <summary>The paths and version this app uses, under Settings › Advanced.</summary>
    Diagnostics = 9,

    /// <summary>The way to the screens the everyday ones leave out, under Settings.</summary>
    Advanced = 10,

    /// <summary>Keys left in notes, dropped project tags and weak passwords to review, under Settings.</summary>
    Recommendations = 11,
}

/// <summary>Where a place sits in the sidebar.</summary>
internal enum DestinationPlacement
{
    /// <summary>The main list under the wordmark.</summary>
    Main = 0,

    /// <summary>The quieter rows at the bottom.</summary>
    Footer = 1,
}

/// <summary>
/// One screen, and everything the sidebar and the keyboard need to know about it.
/// </summary>
/// <param name="Kind">Which screen.</param>
/// <param name="Says">What the sidebar, or the Back button of a screen under it, shows.</param>
/// <param name="Icon">The Lucide icon's name, drawn from <c>Icon.&lt;name&gt;</c> in Theme/Icons.axaml.</param>
/// <param name="Placement">Main list or footer, for a place.</param>
/// <param name="OwnsHeader">
/// Whether the screen draws its own title and padding. Until a screen is restyled the shell draws an
/// h1 with its title above it; a restyled screen that lays out edge to edge sets this.
/// </param>
/// <param name="Parent">
/// The screen a screen sits under, reached from it and left by Back, or null for one of the four places
/// the sidebar lists (D-0374).
/// </param>
internal sealed record Destination(
    DestinationKind Kind,
    Func<string> Says,
    string Icon,
    DestinationPlacement Placement = DestinationPlacement.Main,
    bool OwnsHeader = false,
    DestinationKind? Parent = null)
{
    internal string Title => Says();

    /// <summary>The digit that reaches it with the platform's command modifier, or 0 for a screen under a place.</summary>
    internal int Shortcut => Destinations.ShortcutOf(this);

    /// <summary>Whether the sidebar lists it.</summary>
    internal bool IsPlace => Parent is null;
}

/// <summary>The screens: the four places in sidebar order, then the screens under them.</summary>
internal static class Destinations
{
    /// <summary>Every screen the app has.</summary>
    internal static IReadOnlyList<Destination> All { get; } =
    [
        new(DestinationKind.Entries, () => Strings.Items, "key-round", OwnsHeader: true),
        new(DestinationKind.AgentActivity, () => Strings.Agents, "bot", OwnsHeader: true),
        new(DestinationKind.Trash, () => Strings.Trash, "trash-2", DestinationPlacement.Footer, OwnsHeader: true),
        new(DestinationKind.Settings, () => Strings.Settings, "settings", DestinationPlacement.Footer, OwnsHeader: true),
        new(DestinationKind.EnvSets, () => Strings.Projects, "layers", OwnsHeader: true, Parent: DestinationKind.Entries),
        new(DestinationKind.Advanced, () => Strings.Advanced, "layers", OwnsHeader: true, Parent: DestinationKind.Settings),
        new(DestinationKind.Recommendations, () => Strings.Recommendations, "clipboard-check", OwnsHeader: true, Parent: DestinationKind.Entries),
        new(DestinationKind.Log, () => Strings.Activity, "activity", OwnsHeader: true, Parent: DestinationKind.AgentActivity),
        new(DestinationKind.Sharing, () => Strings.ShareLinks, "link", OwnsHeader: true, Parent: DestinationKind.Advanced),
        new(DestinationKind.Tokens, () => Strings.ScopedTokens, "ticket", OwnsHeader: true, Parent: DestinationKind.Advanced),
        new(DestinationKind.Diagnostics, () => Strings.Diagnostics, "bug", OwnsHeader: true, Parent: DestinationKind.Advanced),
    ];

    /// <summary>The four places, in the order the sidebar reads and the digits count.</summary>
    internal static IReadOnlyList<Destination> Places { get; } = [.. All.Where(d => d.IsPlace)];

    internal static IReadOnlyList<Destination> Main { get; } =
        [.. Places.Where(d => d.Placement == DestinationPlacement.Main)];

    internal static IReadOnlyList<Destination> Footer { get; } =
        [.. Places.Where(d => d.Placement == DestinationPlacement.Footer)];

    internal static Destination Of(DestinationKind kind) => All.Single(d => d.Kind == kind);

    /// <summary>The place a screen is under, however deep, or the screen itself when it is a place.</summary>
    internal static Destination PlaceOf(Destination destination) =>
        destination.Parent is { } parent ? PlaceOf(Of(parent)) : destination;

    /// <summary>The screen Back goes to: the one a screen sits under, or the screen itself when it is a place.</summary>
    internal static Destination UpOf(Destination destination) =>
        destination.Parent is { } parent ? Of(parent) : destination;

    internal static int ShortcutOf(Destination destination)
    {
        var position = 0;

        foreach (var place in Places)
        {
            position++;

            if (place.Kind == destination.Kind)
            {
                return position;
            }
        }

        return 0;
    }
}
