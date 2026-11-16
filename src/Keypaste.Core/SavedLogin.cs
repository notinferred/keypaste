namespace Keypaste.Core;

/// <summary>An item's username and password as they leave keypaste for a browser, each with its own item's placeholders filled in.</summary>
/// <param name="Entry">The item as the vault holds it.</param>
/// <param name="Username">Its username filled in, or why it cannot leave.</param>
/// <param name="Password">Its password filled in, or why it cannot leave.</param>
/// <remarks><see cref="ToString"/> names the item's UUID and never a value.</remarks>
internal sealed record SavedLogin(VaultEntry Entry, PlaceholderFill Username, PlaceholderFill Password)
{
    /// <summary>The item's UUID.</summary>
    /// <returns>Never a value.</returns>
    public override string ToString() => Entry.Uuid ?? string.Empty;
}
