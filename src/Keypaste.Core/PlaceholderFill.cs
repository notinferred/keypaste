using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>A value with its own item's placeholders filled in, or why it cannot leave keypaste (D-0465).</summary>
/// <remarks><see cref="ToString"/> never shows the value or anything filled into it.</remarks>
public sealed class PlaceholderFill
{
    private PlaceholderFill(string? value, string? refusal, IReadOnlyList<string> parts)
    {
        Value = value;
        Refusal = refusal;
        Parts = parts;
    }

    /// <summary>The value with every placeholder filled in, or null when it was refused.</summary>
    public string? Value { get; }

    /// <summary>
    /// Why the value was refused, completing a sentence about it, such as <c>holds the KeePass placeholder
    /// {REF:…}, which keypaste does not fill in</c>; null when it was filled. It names a placeholder, never a value.
    /// </summary>
    public string? Refusal { get; }

    /// <summary>Each distinct non-empty text filled in at any depth, so a run's output can be scrubbed of it.</summary>
    internal IReadOnlyList<string> Parts { get; }

    internal static PlaceholderFill Filled(string value, IReadOnlyList<string> parts) => new(value, null, parts);

    internal static PlaceholderFill Refused(PlaceholderRefusal refusal) => new(null, refusal.Reason, []);

    /// <summary>The refusal, or <c>filled</c>.</summary>
    /// <returns>Never the value.</returns>
    public override string ToString() => Refusal ?? "filled";
}

/// <summary>A placeholder that refuses its value wherever it would leave, and why.</summary>
/// <param name="Placeholder">How it is shown: its keyword, or a <c>{S:…}</c> field's name only when its item has that field.</param>
/// <param name="Ending">Why, completing <c>which …</c>, such as <c>keypaste does not fill in</c>.</param>
internal sealed record PlaceholderRefusal(string Placeholder, string Ending)
{
    /// <summary>The refusal as it completes a sentence about the value.</summary>
    internal string Reason => $"holds the KeePass placeholder {Placeholder}, which {Ending}";
}

/// <summary>The placeholders a value an edit writes plants, as a terminal edit's prompt names them (N.11).</summary>
/// <param name="Fills">Each placeholder that fills in from the item when the value leaves, as shown, in the order first written.</param>
/// <param name="Refused">The first one that refuses the value wherever it would leave, or null when none does.</param>
internal sealed record PlantedPlaceholders(IReadOnlyList<string> Fills, PlaceholderRefusal? Refused);

/// <summary>What a placeholder in one item's value may fill in from: its standard fields, and its custom fields keypaste releases by name (D-0388).</summary>
/// <remarks>Every custom field's name is kept, so a refusal can tell a field keypaste never releases from one the item lacks.</remarks>
internal sealed class PlaceholderFields
{
    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _names;

    private PlaceholderFields(Dictionary<string, string> values, HashSet<string> names)
    {
        _values = values;
        _names = names;
    }

    /// <summary>One item's fields.</summary>
    /// <param name="entry">Its standard fields.</param>
    /// <param name="fields">Custom fields with their values; only those <see cref="CredentialFields.IsCustom"/> accepts are kept.</param>
    /// <param name="names">The name of each of its custom fields, whatever keypaste does with it.</param>
    /// <returns>The fields.</returns>
    internal static PlaceholderFields Of(VaultEntry entry, IEnumerable<KeyValuePair<string, string>> fields, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(names);

        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["Title"] = entry.Title,
            ["UserName"] = entry.Username,
            ["Password"] = entry.Password,
            ["URL"] = entry.Url,
            ["Notes"] = entry.Notes,
        };

        foreach (var (name, value) in fields.Where(field => CredentialFields.IsCustom(field.Key)))
        {
            values[name] = value;
        }

        return new PlaceholderFields(values, [.. values.Keys, .. names]);
    }

    /// <summary>An item's fields known by name only, each read as empty, for judging a value before it is written.</summary>
    /// <param name="names">The name of each custom field the item will have.</param>
    /// <returns>The fields.</returns>
    internal static PlaceholderFields Named(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        List<string> known = [.. names];

        return Of(new VaultEntry { Title = string.Empty }, known.Select(name => KeyValuePair.Create(name, string.Empty)), known);
    }

    /// <summary>Whether the item has a field of this name, a standard one or any custom one, matched exactly.</summary>
    /// <param name="field">The field's name.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    internal bool Has(string field) => _names.Contains(field);

    /// <summary>The value a placeholder fills in from a field, matched exactly.</summary>
    /// <param name="field">The field's name.</param>
    /// <returns>The value, or null when the item has no such field or keypaste never releases it.</returns>
    internal string? Value(string field) => _values.GetValueOrDefault(field);
}
