using Keypaste.App.Resources;

namespace Keypaste.App.ViewModels;

/// <summary>One column a table sorts by.</summary>
/// <param name="Key">How the column is named in code and in <c>app.toml</c>.</param>
/// <param name="Says">How a person calls it, in sentence case; its head draws it in capitals.</param>
/// <param name="IsTime">Whether it holds a time, whose first press shows the newest first.</param>
internal sealed record TableColumn(string Key, Func<string> Says, bool IsTime = false)
{
    internal string Name => Says();
}


/// <summary>A column and the direction its rows run in.</summary>
/// <param name="Column">The column a head draws its arrow on.</param>
/// <param name="Descending">Which way the arrow points.</param>
/// <param name="AsRowsArrive">Whether the rows already run this way and are not compared, which only a table's own order can be.</param>
internal readonly record struct TableOrder(string Column, bool Descending, bool AsRowsArrive = false);

/// <summary>How one table is sorted. Presentation only: nothing here reaches a vault or the audit log.</summary>
/// <remarks>
/// The table's own order holds until a head is pressed. A column the window has dropped gives its order back to the
/// table's own while it is gone, so no row is in an order the screen cannot show.
/// </remarks>
/// <param name="id">How the table is named in <c>app.toml</c>.</param>
/// <param name="columns">Its columns.</param>
/// <param name="ownOrder">Its order until one is chosen, or null for the order the rows arrive in.</param>
internal sealed class TableState(string id, IReadOnlyList<TableColumn> columns, TableOrder? ownOrder)
{
    private readonly HashSet<string> _dropped = new(StringComparer.Ordinal);
    private TableOrder? _chosen;

    /// <summary>Raised when <see cref="Order"/> changes.</summary>
    internal event Action? OrderChanged;

    internal string Id => id;

    internal IReadOnlyList<TableColumn> Columns => columns;

    /// <summary>The order in force, or null for the order the rows arrive in.</summary>
    internal TableOrder? Order => _chosen is { } chosen && !_dropped.Contains(chosen.Column) ? chosen : ownOrder;

    /// <summary>Whether nothing has been chosen, so the table has nothing to keep or reset.</summary>
    internal bool IsOwnWay => _chosen is null;

    internal string? SortText => _chosen is { } chosen
        ? $"{id}:{chosen.Column}:{(chosen.Descending ? "desc" : "asc")}"
        : null;

    /// <summary>A head's press: its column, and again to reverse.</summary>
    internal void Press(string column)
    {
        var chosen = Order is { } order && order.Column == column
            ? new TableOrder(column, !order.Descending)
            : new TableOrder(column, Column(column).IsTime);

        _chosen = chosen == ownOrder ? null : chosen;
        OrderChanged?.Invoke();
    }

    /// <summary>What a head says of itself: its column and the direction, in words.</summary>
    internal string HeadName(string column)
    {
        var named = Column(column);

        return Order is { } order && order.Column == column
            ? $"{named.Name}, sorted {Direction(named, order.Descending)}"
            : $"{named.Name}, not sorted";
    }

    /// <summary>Says whether the window shows a column, which decides whether a sort by it holds.</summary>
    internal void Show(string column, bool shows)
    {
        var before = Order;

        if (shows)
        {
            _dropped.Remove(column);
        }
        else
        {
            _dropped.Add(column);
        }

        if (before != Order)
        {
            OrderChanged?.Invoke();
        }
    }

    /// <summary>Back to the table's own order.</summary>
    internal void Reset()
    {
        _chosen = null;
        OrderChanged?.Invoke();
    }

    /// <summary>Takes what <c>app.toml</c> kept, leaving the table's own order for anything that does not fit its columns.</summary>
    internal void Restore(string? sort)
    {
        if (sort?.Split(':') is [var column, var direction and ("asc" or "desc")] && columns.Any(known => known.Key == column))
        {
            var chosen = new TableOrder(column, direction == "desc");
            _chosen = chosen == ownOrder ? null : chosen;
        }
    }

    private static string Direction(TableColumn column, bool descending) => column.IsTime
        ? descending ? "newest first" : "oldest first"
        : descending ? "descending" : "ascending";

    private TableColumn Column(string key) => columns[IndexOf(key)];

    private int IndexOf(string key)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index].Key == key)
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(key), key, "Not a column of this table.");
    }
}

/// <summary>Items' and Trash's tables, as the shell holds them and <c>[[window]]</c> keeps them.</summary>
internal sealed class TableStates
{
    internal const string Title = "title";
    internal const string Username = "username";
    internal const string Url = "url";
    internal const string Group = "group";
    internal const string LastUsed = "last-used";
    internal const string DeletedFrom = "deleted-from";
    internal const string Deleted = "deleted";

    internal static readonly IReadOnlyList<TableColumn> ItemColumns =
    [
        new(Title, () => Strings.Title),
        new(Username, () => Strings.Username),
        new(Url, () => Strings.URL),
        new(Group, () => Strings.Group),
        new(LastUsed, () => Strings.LastUsed, IsTime: true),
    ];

    internal static readonly IReadOnlyList<TableColumn> TrashColumns =
    [
        new(Title, () => Strings.Title),
        new(DeletedFrom, () => Strings.DeletedFrom),
        new(Deleted, () => Strings.Deleted, IsTime: true),
    ];

    internal TableState Items { get; } = NewItems();

    internal TableState Trash { get; } = NewTrash();

    internal string? SortsText => Joined(',', All.Select(table => table.SortText));

    private IEnumerable<TableState> All => [Items, Trash];

    /// <summary>Items run in the vault's order until a head is pressed.</summary>
    internal static TableState NewItems() => new("items", ItemColumns, null);

    internal static TableState NewTrash() => new("trash", TrashColumns, new TableOrder(Deleted, Descending: true));

    /// <summary>Reads what <c>app.toml</c> kept; what does not read leaves its table as it is.</summary>
    internal static TableStates Read(string? sorts)
    {
        var states = new TableStates();

        foreach (var table in states.All)
        {
            table.Restore(Part(sorts, ',', table.Id));
        }

        return states;
    }

    private static string? Joined(char separator, IEnumerable<string?> parts) =>
        string.Join(separator, parts.OfType<string>()) is { Length: > 0 } joined ? joined : null;

    private static string? Part(string? text, char separator, string id)
    {
        var prefix = id + ":";

        return text?.Split(separator).FirstOrDefault(part => part.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }
}
