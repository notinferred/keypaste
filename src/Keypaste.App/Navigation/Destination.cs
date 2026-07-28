namespace Keypaste.App.Navigation;

/// <summary>The five places the sidebar can go.</summary>
internal enum DestinationKind
{
    /// <summary>Entries. 4.2 fills it.</summary>
    Entries = 0,

    /// <summary>Env sets. 4.2 fills it.</summary>
    EnvSets = 1,

    /// <summary>Agent activity. 4.3 fills it; 4.1 says one true thing on it.</summary>
    AgentActivity = 2,

    /// <summary>The audit log. Real in 4.1.</summary>
    Log = 3,

    /// <summary>Settings. Real in 4.1.</summary>
    Settings = 4,
}

/// <summary>
/// One navigable place, and everything the sidebar and the keyboard need to know about it.
/// </summary>
/// <param name="Kind">Which destination.</param>
/// <param name="Title">What the sidebar shows.</param>
/// <param name="Shortcut">The digit that reaches it, with the platform's command modifier.</param>
/// <remarks>
/// <para>
/// <b>A registry rather than a hard-coded sidebar, and that is a deliberate down-payment.</b>
/// <c>docs/IDEAS.md</c> wants a command palette for everything, and 4.1 is the wrong stage for it — with
/// five navigations and two actions a palette reaches almost nothing, and the version people
/// actually want searches entries, which is 4.2's data by definition. But building the palette later
/// against a registry that already exists costs an afternoon, whereas building it against a
/// hard-coded sidebar means writing the sidebar twice. The shortcuts sheet is generated from this
/// list too, which is how a shortcut list stays true.
/// </para>
/// </remarks>
internal sealed record Destination(DestinationKind Kind, string Title, int Shortcut);

/// <summary>The five destinations, in sidebar order.</summary>
internal static class Destinations
{
    /// <summary>Every destination the app has.</summary>
    internal static IReadOnlyList<Destination> All { get; } =
    [
        new(DestinationKind.Entries, "Entries", 1),
        new(DestinationKind.EnvSets, "Env Sets", 2),
        new(DestinationKind.AgentActivity, "Agent Activity", 3),
        new(DestinationKind.Log, "Log", 4),
        new(DestinationKind.Settings, "Settings", 5),
    ];
}
