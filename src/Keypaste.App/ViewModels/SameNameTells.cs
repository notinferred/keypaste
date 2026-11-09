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
            foreach (var entry in named)
            {
                var tell = OnlyThisOne(named, entry, item => item.Username) ?? OnlyThisOne(named, entry, item => item.Url) ?? string.Empty;
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

    /// <summary>The path as a sentence names this item, with its tell when another item has the path too.</summary>
    /// <param name="item">The item.</param>
    /// <returns>What to show, sanitized.</returns>
    internal string Named(EntryName item) => WithTell(RecommendationsViewModel.Shown(item), Of(item));

    internal static string WithTell(string path, string? tell) =>
        string.IsNullOrEmpty(tell) ? path : $"{path} ({tell})";

    private static string? OnlyThisOne(IEnumerable<VaultEntry> named, VaultEntry entry, Func<VaultEntry, string> value) =>
        value(entry).Length > 0 && named.Count(other => value(other) == value(entry)) == 1 ? value(entry) : null;
}
