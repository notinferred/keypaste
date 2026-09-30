namespace Keypaste.Core;

/// <summary>
/// The placeholders KeePassXC replaces in a value where it uses it, such as <c>{PASSWORD}</c>, which
/// keypaste does not resolve (P.3b1): a value holding one would reach a child as its literal text.
/// </summary>
/// <remarks>
/// Names are matched in any case, as KeePassXC matches them. Any other text in braces, such as a
/// JSON value, is not a placeholder.
/// </remarks>
public static class KeePassPlaceholders
{
    private static readonly string[] _names = ["TITLE", "USERNAME", "PASSWORD", "NOTES", "URL", "UUID", "TOTP", "DB_DIR"];
    private static readonly string[] _prefixes = ["S:", "REF:", "URL:", "T-CONV:", "T-REPLACE-RX:"];
    private const string _dateTime = "DT_";

    /// <summary>The first placeholder in a value, as a refusal may show it without the value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A bare placeholder such as <c>{PASSWORD}</c>, one taking an argument as its prefix such as <c>{S:…}</c>, or null when there is none.</returns>
    public static string? Find(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        for (var open = value.IndexOf('{', StringComparison.Ordinal); open >= 0; open = value.IndexOf('{', open + 1))
        {
            var close = value.IndexOf('}', open + 1);

            if (close < 0)
            {
                return null;
            }

            var inner = value[(open + 1)..close];

            if (_names.Any(name => string.Equals(name, inner, StringComparison.OrdinalIgnoreCase))
                || (inner.StartsWith(_dateTime, StringComparison.OrdinalIgnoreCase)
                    && inner.Length > _dateTime.Length
                    && inner.All(c => char.IsAsciiLetter(c) || c == '_')))
            {
                return "{" + inner.ToUpperInvariant() + "}";
            }

            if (_prefixes.FirstOrDefault(prefix => inner.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                return "{" + found + "…}";
            }
        }

        return null;
    }
}
