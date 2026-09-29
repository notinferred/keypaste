namespace Keypaste.App.ViewModels;

/// <summary>A group in the sidebar's tree under Items, as KeePassXC's group tree draws it.</summary>
/// <param name="Path">The slash-separated path that selects it.</param>
/// <param name="Title">The group's name, scrubbed, which is also its name for a screen reader.</param>
/// <param name="Depth">How deep it sits under Items, from one.</param>
/// <param name="HasChildren">Whether it has groups inside it, so it can fold.</param>
/// <param name="IsExpanded">Whether its children are showing.</param>
internal sealed record GroupRow(string Path, string Title, int Depth, bool HasChildren, bool IsExpanded)
{
    /// <summary>The fold's chevron: down while open, right while closed.</summary>
    internal string Chevron => IsExpanded ? "chevron-down" : "chevron-right";
}

/// <summary>A label in the sidebar's list that names the rows under it and cannot be chosen.</summary>
/// <param name="Title">What it says.</param>
internal sealed record SidebarHeading(string Title)
{
    /// <summary>What the list draws: a table heading, in capitals.</summary>
    internal string Label => Title.ToUpperInvariant();
}
