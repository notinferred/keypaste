using System.Buffers;

namespace Keypaste.Core;

/// <summary>The items a value's KeePass field references name by UUID, <c>{REF:&lt;field&gt;@I:&lt;32 hex&gt;}</c>, which keypaste never fills in or follows.</summary>
/// <remarks>Any other search a reference makes, by title, username or text, names no one item, so it links nothing.</remarks>
internal static class FieldReferences
{
    private const string _opening = "{REF:";
    private const string _wantedFields = "TUPANIOtupanio";
    private const int _uuidLength = 32;

    private static readonly SearchValues<char> _hex = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Adds the UUID, as upper-case hex, of every item a value references by UUID.</summary>
    /// <param name="value">The value, read where it is held and cleared by the caller.</param>
    /// <param name="uuids">Where each UUID is added.</param>
    internal static void Collect(ReadOnlySpan<char> value, ISet<string> uuids)
    {
        for (var start = value.IndexOf(_opening, StringComparison.OrdinalIgnoreCase); start >= 0; start = Next(value, start))
        {
            var rest = value[(start + _opening.Length)..];

            if (rest.Length >= _uuidLength + 5
                && _wantedFields.AsSpan().Contains(rest[0])
                && rest[1] == '@'
                && rest[2] is 'I' or 'i'
                && rest[3] == ':'
                && rest[_uuidLength + 4] == '}'
                && !rest.Slice(4, _uuidLength).ContainsAnyExcept(_hex))
            {
                uuids.Add(Convert.ToHexString(Convert.FromHexString(rest.Slice(4, _uuidLength))));
            }
        }
    }

    private static int Next(ReadOnlySpan<char> value, int start)
    {
        var next = value[(start + 1)..].IndexOf(_opening, StringComparison.OrdinalIgnoreCase);
        return next < 0 ? -1 : start + 1 + next;
    }
}
