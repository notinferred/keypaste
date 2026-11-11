using System.Globalization;
using Keypaste.App.Resources;

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

    /// <summary>What the fold button will do.</summary>
    internal string FoldLabel => $"{(IsExpanded ? "Hide" : "Show")} the groups in {Title}";

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Title}, level {Depth + 1}");
}

/// <summary>What a sidebar heading's + makes.</summary>
internal enum SidebarAddition
{
    Group,
    Project,
}

/// <summary>A label in the sidebar's list that names the rows under it and cannot be chosen; its + makes one more of them.</summary>
/// <param name="Title">What it says.</param>
/// <param name="Adds">What its + makes.</param>
internal sealed record SidebarHeading(string Title, SidebarAddition Adds)
{
    /// <summary>What the list draws: a table heading, in capitals.</summary>
    internal string Label => Title.ToUpperInvariant();

    /// <summary>The + button's name and tooltip, the menu command it runs.</summary>
    internal string AddLabel => Adds == SidebarAddition.Group ? Strings.NewGroupLabel : Strings.NewProjectLabel;

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => Title;
}
