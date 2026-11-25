using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>What tells items of one name apart: only what the list already draws, never a password, notes or a custom field.</summary>
internal sealed class SameNameTells
{
    private readonly Dictionary<(string Uuid, EntryName Name), string> _tells = [];

    private SameNameTells()
    {
    }

    /// <summary>Reads the tells of every item another item shares a name with.</summary>
    /// <param name="entries">The vault's items.</param>
    /// <returns>The tells.</returns>
    internal static SameNameTells Of(IReadOnlyCollection<VaultEntry> entries)
    {
        SameNameTells tells = new();

        foreach (var named in entries.GroupBy(EntryName.Of).Where(named => named.Count() > 1))
        {
            var usernames = CountsOf(named, item => item.Username);
            var urls = CountsOf(named, item => item.Url);

            foreach (var entry in named)
            {
                var tell = OnlyThisOne(usernames, entry.Username) ?? OnlyThisOne(urls, entry.Url) ?? string.Empty;
                tells._tells[(entry.Uuid!, named.Key)] = DisplayTextSanitizer.Sanitize(tell, DisplayTextSanitizer.MaximumLength).Text;
            }
        }

        return tells;
    }

    /// <summary>The username or URL only this item of its name has, empty when none, or null when no other item has its name.</summary>
    /// <param name="item">The item, by a name that carries its UUID.</param>
    /// <returns>The tell.</returns>
    internal string? Of(EntryName item) =>
        item.Uuid is { } uuid && _tells.TryGetValue((uuid, item), out var tell) ? tell : null;

    /// <summary>The tell of the one item holding this UUID, empty when none, or null when no other item has its name or two items hold the UUID.</summary>
    /// <param name="uuid">The UUID as hex.</param>
    /// <returns>The tell.</returns>
    internal string? OfUuid(string uuid)
    {
        var held = _tells.Where(tell => string.Equals(tell.Key.Uuid, uuid, StringComparison.Ordinal)).Take(2).ToList();

        return held.Count == 1 ? held[0].Value : null;
    }

    internal static string WithTell(string path, string? tell) =>
        string.IsNullOrEmpty(tell) ? path : $"{path} ({tell})";

    private static Dictionary<string, int> CountsOf(IEnumerable<VaultEntry> named, Func<VaultEntry, string> value)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        foreach (var held in named.Select(value))
        {
            counts[held] = counts.GetValueOrDefault(held) + 1;
        }

        return counts;
    }

    private static string? OnlyThisOne(Dictionary<string, int> counts, string value) =>
        value.Length > 0 && counts[value] == 1 ? value : null;
}
