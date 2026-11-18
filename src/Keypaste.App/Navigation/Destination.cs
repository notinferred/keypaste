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

    /// <summary>Security: keys left in notes, dropped project tags, weak passwords and reminders to review.</summary>
    Recommendations = 11,

    /// <summary>Access, the rail's slot kept for later, which opens no screen.</summary>
    Access = 12,
}

/// <summary>
/// One screen, and everything the sidebar and the keyboard need to know about it.
/// </summary>
/// <param name="Kind">Which screen.</param>
/// <param name="Says">What the sidebar, or the Back button of a screen under it, shows.</param>
/// <param name="Icon">The Lucide icon's name, drawn from <c>Icon.&lt;name&gt;</c> in Theme/Icons.axaml.</param>
/// <param name="OwnsHeader">
/// Whether the screen draws its own title and padding. Until a screen is restyled the shell draws an
/// h1 with its title above it; a restyled screen that lays out edge to edge sets this.
/// </param>
/// <param name="Parent">The screen a screen sits under, or null for one of the four places the rail lists.</param>
/// <param name="InSidebar">Whether the screen is one of a vault's places in the sidebar, reached from there rather than by Back.</param>
internal sealed record Destination(
    DestinationKind Kind,
    Func<string> Says,
    string Icon,
    bool OwnsHeader = false,
    DestinationKind? Parent = null,
    bool InSidebar = false)
{
    internal string Title => Says();

    /// <summary>The digit that reaches it with the platform's command modifier, or 0 for a screen under a place.</summary>
    internal int Shortcut => Destinations.ShortcutOf(this);

    /// <summary>Whether the rail lists it.</summary>
    internal bool IsPlace => Parent is null;

    /// <summary>Whether Back leaves it: a screen under a place that the sidebar does not list.</summary>
    internal bool HasBack => !IsPlace && !InSidebar;
}

/// <summary>The screens: the four places in rail order, then the screens under them.</summary>
internal static class Destinations
{
    /// <summary>Every screen the app has.</summary>
    internal static IReadOnlyList<Destination> All { get; } =
    [
        new(DestinationKind.Entries, () => Strings.Items, "inbox", OwnsHeader: true),
        new(DestinationKind.AgentActivity, () => Strings.Agents, "bot", OwnsHeader: true),
        new(DestinationKind.Recommendations, () => Strings.Security, "shield", OwnsHeader: true),
        new(DestinationKind.Settings, () => Strings.Settings, "settings", OwnsHeader: true),
        new(DestinationKind.EnvSets, () => Strings.Environments, "braces", OwnsHeader: true, Parent: DestinationKind.Entries, InSidebar: true),
        new(DestinationKind.Trash, () => Strings.Trash, "trash", OwnsHeader: true, Parent: DestinationKind.Entries, InSidebar: true),
        new(DestinationKind.Advanced, () => Strings.Advanced, "layers", OwnsHeader: true, Parent: DestinationKind.Settings),
        new(DestinationKind.Log, () => Strings.Activity, "activity", OwnsHeader: true, Parent: DestinationKind.AgentActivity),
        new(DestinationKind.Sharing, () => Strings.ShareLinks, "link", OwnsHeader: true, Parent: DestinationKind.Advanced),
        new(DestinationKind.Tokens, () => Strings.ScopedTokens, "ticket", OwnsHeader: true, Parent: DestinationKind.Advanced),
        new(DestinationKind.Diagnostics, () => Strings.Diagnostics, "bug", OwnsHeader: true, Parent: DestinationKind.Advanced),
    ];

    /// <summary>The four places, in the order the rail reads and the digits count.</summary>
    internal static IReadOnlyList<Destination> Places { get; } = [.. All.Where(d => d.IsPlace)];

    /// <summary>The rail's dimmed slot for Access, which no screen answers yet.</summary>
    internal static Destination Access { get; } = new(DestinationKind.Access, () => Strings.AccessLater, "network");

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
