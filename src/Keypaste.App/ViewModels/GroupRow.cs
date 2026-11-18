using System.Globalization;

namespace Keypaste.App.ViewModels;

/// <summary>A group in the sidebar's tree under a vault's Groups row, as KeePassXC's group tree draws it.</summary>
/// <param name="Path">The slash-separated path that selects it.</param>
/// <param name="Title">The group's name, scrubbed, which is also its name for a screen reader.</param>
/// <param name="Depth">How deep it sits under Items, from one.</param>
/// <param name="HasChildren">Whether it has groups inside it, so it can fold.</param>
/// <param name="IsExpanded">Whether its children are showing.</param>
/// <param name="Vault">The vault it is in, or null for the vault in view.</param>
internal sealed record GroupRow(string Path, string Title, int Depth, bool HasChildren, bool IsExpanded, VaultSection? Vault = null)
{
    /// <summary>The fold's chevron: down while open, right while closed.</summary>
    internal string Chevron => IsExpanded ? "chevron-down" : "chevron-right";

    /// <summary>What the fold button will do.</summary>
    internal string FoldLabel => $"{(IsExpanded ? "Hide" : "Show")} the groups in {Title}";

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Title}, level {Depth + 1}");
}
