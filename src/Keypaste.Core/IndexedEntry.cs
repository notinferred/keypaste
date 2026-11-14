using Keypaste.Core.Browser;

namespace Keypaste.Core;

/// <summary>An entry as a search names it to an agent: its listing, its kind, its web origin and, when Settings shows it, its username.</summary>
/// <remarks>Its origin and username are the only values it holds, and only ones an agent sees without asking (PRODUCT §2).</remarks>
/// <param name="Listing">The entry as a listing names it, with its UUID.</param>
/// <param name="Kind">What the item is for.</param>
/// <param name="Origin">The scheme, host and port of its URL, or null when it has none that parses.</param>
/// <param name="Username">Its username, or null when it holds none or Settings hides usernames from agents.</param>
public sealed record IndexedEntry(ListedEntry Listing, ItemKind Kind, WebOrigin? Origin, string? Username)
{
    /// <summary>The projects its own tags make it a member of, ordinally and each once.</summary>
    public IReadOnlyList<string> Projects =>
        [.. Listing.Tags.Select(ProjectTag.Read).Select(tag => tag.Project).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>This entry as a reach shows it: the fields the reach covers, and its origin and username only when it reaches the whole entry.</summary>
    /// <param name="reach">How much of the entry an exposure reaches.</param>
    /// <returns>The entry with only what the reach covers.</returns>
    public IndexedEntry Within(ExposureReach reach)
    {
        var whole = reach == ExposureReach.Entry;

        return this with { Listing = Listing.Within(reach), Origin = whole ? Origin : null, Username = whole ? Username : null };
    }
}
