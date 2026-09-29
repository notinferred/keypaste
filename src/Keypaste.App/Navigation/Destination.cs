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

    /// <summary>The whole audit log, under Settings › Advanced.</summary>
    Log = 3,

    /// <summary>Settings.</summary>
    Settings = 4,

    /// <summary>The recycle bin, and the way back out of it.</summary>
    Trash = 5,

    /// <summary>The share links made from this vault, under Settings › Advanced.</summary>
    Sharing = 6,

    /// <summary>What agents and tokens asked for, under Agents.</summary>
    AgentHistory = 7,

    /// <summary>The vault's scoped tokens, minting and revoking one, under Settings › Advanced.</summary>
    Tokens = 8,

    /// <summary>The paths and version this app uses, under Settings › Advanced.</summary>
    Diagnostics = 9,
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
/// <param name="Title">What the sidebar, or the Back button of a screen under it, shows.</param>
/// <param name="Icon">The Lucide icon's name, drawn from <c>Icon.&lt;name&gt;</c> in Theme/Icons.axaml.</param>
/// <param name="Placement">Main list or footer, for a place.</param>
/// <param name="OwnsHeader">
/// Whether the screen draws its own title and padding. Until a screen is restyled the shell draws an
/// h1 with its title above it; a restyled screen that lays out edge to edge sets this.
/// </param>
/// <param name="Parent">
/// The place a screen sits under, reached from it and left by Back, or null for one of the four places
/// the sidebar lists (D-0374).
/// </param>
internal sealed record Destination(
    DestinationKind Kind,
    string Title,
    string Icon,
    DestinationPlacement Placement = DestinationPlacement.Main,
    bool OwnsHeader = false,
    DestinationKind? Parent = null)
{
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
        new(DestinationKind.Entries, "Items", "vault", OwnsHeader: true),
        new(DestinationKind.AgentActivity, "Agents", "bot", OwnsHeader: true),
        new(DestinationKind.Trash, "Trash", "trash-2", DestinationPlacement.Footer, OwnsHeader: true),
        new(DestinationKind.Settings, "Settings", "settings", DestinationPlacement.Footer, OwnsHeader: true),
        new(DestinationKind.EnvSets, "Env profiles", "layers", OwnsHeader: true, Parent: DestinationKind.Entries),
        new(DestinationKind.AgentHistory, "History", "history", OwnsHeader: true, Parent: DestinationKind.AgentActivity),
        new(DestinationKind.Log, "Activity log", "activity", OwnsHeader: true, Parent: DestinationKind.Settings),
        new(DestinationKind.Sharing, "Share links", "link", OwnsHeader: true, Parent: DestinationKind.Settings),
        new(DestinationKind.Tokens, "Scoped tokens", "ticket", OwnsHeader: true, Parent: DestinationKind.Settings),
        new(DestinationKind.Diagnostics, "Diagnostics", "bug", OwnsHeader: true, Parent: DestinationKind.Settings),
    ];

    /// <summary>The four places, in the order the sidebar reads and the digits count.</summary>
    internal static IReadOnlyList<Destination> Places { get; } = [.. All.Where(d => d.IsPlace)];

    internal static IReadOnlyList<Destination> Main { get; } =
        [.. Places.Where(d => d.Placement == DestinationPlacement.Main)];

    internal static IReadOnlyList<Destination> Footer { get; } =
        [.. Places.Where(d => d.Placement == DestinationPlacement.Footer)];

    internal static Destination Of(DestinationKind kind) => All.Single(d => d.Kind == kind);

    /// <summary>The place a screen is under, or the screen itself when it is a place.</summary>
    internal static Destination PlaceOf(Destination destination) =>
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
