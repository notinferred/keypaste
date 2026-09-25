namespace Keypaste.App.Navigation;

/// <summary>The places the sidebar can go.</summary>
internal enum DestinationKind
{
    /// <summary>Every entry in the vault: logins, keys, notes. The sidebar calls it Secrets.</summary>
    Entries = 0,

    /// <summary>Env projects and their variables. The sidebar calls it Env profiles.</summary>
    EnvSets = 1,

    /// <summary>What agents are asking for and have been granted. The sidebar calls it Agents.</summary>
    AgentActivity = 2,

    /// <summary>The audit log. The sidebar calls it Activity.</summary>
    Log = 3,

    /// <summary>Settings.</summary>
    Settings = 4,

    /// <summary>The recycle bin, and the way back out of it.</summary>
    Trash = 5,
}

/// <summary>Where a destination sits in the sidebar.</summary>
internal enum DestinationPlacement
{
    /// <summary>The main list under the lockup.</summary>
    Main = 0,

    /// <summary>The quieter rows at the bottom.</summary>
    Footer = 1,
}

/// <summary>
/// One navigable place, and everything the sidebar and the keyboard need to know about it.
/// </summary>
/// <param name="Kind">Which destination.</param>
/// <param name="Title">What the sidebar shows.</param>
/// <param name="Icon">The Lucide icon's name, drawn from <c>Icon.&lt;name&gt;</c> in Theme/Icons.axaml.</param>
/// <param name="Placement">Main list or footer.</param>
/// <param name="OwnsHeader">
/// Whether the screen draws its own title and padding. Until a screen is restyled the shell draws an
/// h1 with its title above it; a restyled screen that lays out edge to edge sets this.
/// </param>
/// <remarks>
/// A registry rather than a hard-coded sidebar: the sidebar, the digit shortcuts and the shortcuts
/// sheet are all read from <see cref="Destinations.All"/>, so adding a place is one line there
/// (plus its view model in <c>ShellViewModel.Show</c> and its view in <c>ShellView</c>).
/// </remarks>
internal sealed record Destination(
    DestinationKind Kind,
    string Title,
    string Icon,
    DestinationPlacement Placement = DestinationPlacement.Main,
    bool OwnsHeader = false)
{
    /// <summary>The digit that reaches it with the platform's command modifier: its position, from 1.</summary>
    internal int Shortcut => Destinations.ShortcutOf(this);
}

/// <summary>The destinations, in sidebar order: the main list, then the footer.</summary>
internal static class Destinations
{
    /// <summary>Every destination the app has.</summary>
    internal static IReadOnlyList<Destination> All { get; } =
    [
        new(DestinationKind.Entries, "Secrets", "vault"),
        new(DestinationKind.AgentActivity, "Agents", "bot"),
        new(DestinationKind.Log, "Activity", "activity"),
        new(DestinationKind.EnvSets, "Env profiles", "layers"),
        new(DestinationKind.Settings, "Settings", "settings", DestinationPlacement.Footer),
        new(DestinationKind.Trash, "Trash", "trash-2", DestinationPlacement.Footer),
    ];

    internal static IReadOnlyList<Destination> Main { get; } =
        [.. All.Where(d => d.Placement == DestinationPlacement.Main)];

    internal static IReadOnlyList<Destination> Footer { get; } =
        [.. All.Where(d => d.Placement == DestinationPlacement.Footer)];

    internal static Destination Of(DestinationKind kind) => All.Single(d => d.Kind == kind);

    internal static int ShortcutOf(Destination destination)
    {
        var position = 0;

        foreach (var candidate in All)
        {
            position++;

            if (candidate.Kind == destination.Kind)
            {
                return position;
            }
        }

        return 0;
    }
}
