using System.Globalization;

namespace Keypaste.App.ViewModels;

/// <summary>What a row holds in a sorted column: words, a time, or nothing, which sorts last in either direction.</summary>
internal readonly record struct SortValue(string? Text, long? Time = null)
{
    internal bool IsBlank => string.IsNullOrEmpty(Text) && Time is null;

    internal static SortValue At(long? time) => new(null, time);
}

/// <summary>How every table compares what it sorts, in the one place the culture reaches it.</summary>
internal static class TableSorting
{
    /// <summary>Text as the current culture orders it, ignoring case. <c>InvariantGlobalization</c> makes every culture invariant, so this is by code point until Q.16 lifts it.</summary>
    internal static int CompareText(string left, string right) =>
        CultureInfo.CurrentCulture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase);

    /// <summary>The rows under <paramref name="order"/>, or as they arrive with none.</summary>
    /// <param name="rows">What the search, the group or the filter kept.</param>
    /// <param name="order">The order in force.</param>
    /// <param name="value">What a row holds in a column.</param>
    /// <param name="tie">Decides between rows the column does not.</param>
    internal static IEnumerable<T> Sorted<T>(IEnumerable<T> rows, TableOrder? order, Func<T, string, SortValue> value, Comparison<T> tie)
    {
        if (order is not { AsRowsArrive: false } by)
        {
            return rows;
        }

        return rows
            .Select(row => (Row: row, Value: value(row, by.Column)))
            .Order(Comparer<(T Row, SortValue Value)>.Create((left, right) =>
            {
                var compared = Compare(left.Value, right.Value, by.Descending);
                return compared != 0 ? compared : tie(left.Row, right.Row);
            }))
            .Select(sorted => sorted.Row);
    }

    private static int Compare(SortValue left, SortValue right, bool descending)
    {
        if (left.IsBlank || right.IsBlank)
        {
            return left.IsBlank.CompareTo(right.IsBlank);
        }

        var compared = left.Text is { } text && right.Text is { } other
            ? CompareText(text, other)
            : Nullable.Compare(left.Time, right.Time);

        return descending ? -compared : compared;
    }
}
