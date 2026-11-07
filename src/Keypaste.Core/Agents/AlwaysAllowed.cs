
namespace Keypaste.Core.Agents;

/// <summary>What a person chose Always allow on for one agent: items, by UUID.</summary>
/// <remarks>
/// A row is a policy the person wrote, and only their confirmation of the widening writes one. It never
/// reaches a protected item, an agent turned off from values, or past what the agent sees:
/// those are checked before a row is read.
/// </remarks>
public sealed record AlwaysAllowed
{
    /// <summary>The most items a record keeps: a longer list is not understood, and no change makes one.</summary>
    public const int MaximumItems = 256;

    /// <summary>Nothing is always allowed.</summary>
    public static AlwaysAllowed None { get; } = new();

    /// <summary>Each item, by its UUID as hex.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Whether nothing is always allowed.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>The short name a change removes an item by.</summary>
    /// <param name="item">The item's UUID as hex.</param>
    /// <returns>Eight lowercase hex characters.</returns>
    public static string ItemId(string item) => IdOf($"item\0{item}");

    /// <summary>Whether text has the shape of a row's short name.</summary>
    /// <param name="text">What was given.</param>
    /// <returns><see langword="true"/> for exactly eight lowercase hex characters.</returns>
    public static bool IsId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length == 8 && text.All(char.IsAsciiHexDigitLower);
    }

    /// <summary>Whether a row has this short name.</summary>
    /// <param name="id">The short name.</param>
    /// <returns><see langword="true"/> when an item has it.</returns>
    public bool Holds(string id) =>
        Items.Any(item => string.Equals(ItemId(item), id, StringComparison.Ordinal));

    /// <summary>Whether another holds the same rows, in the same order.</summary>
    /// <param name="other">The other list.</param>
    /// <returns><see langword="true"/> when every row matches.</returns>
    public bool Equals(AlwaysAllowed? other) =>
        other is not null
        && Items.SequenceEqual(other.Items, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => Items.Count;

    /// <summary>The list with the row a short name names gone.</summary>
    /// <param name="id">The short name.</param>
    /// <returns>The list, the same when no row has that name.</returns>
    internal AlwaysAllowed Without(string id) => this with
    {
        Items = [.. Items.Where(item => !string.Equals(ItemId(item), id, StringComparison.Ordinal))],
    };

    private static string IdOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
}
