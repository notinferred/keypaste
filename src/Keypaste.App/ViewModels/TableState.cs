using System.Globalization;
using Keypaste.App.Resources;

namespace Keypaste.App.ViewModels;

/// <summary>One column a table sorts and sizes.</summary>
/// <param name="Key">How the column is named in code and in <c>app.toml</c>.</param>
/// <param name="Says">How a person calls it, in sentence case; its head draws it in capitals.</param>
/// <param name="Width">Its width until the table is resized: a share of the room, or design pixels.</param>
/// <param name="IsShare">Whether <paramref name="Width"/> is a share.</param>
/// <param name="Least">The narrowest it goes, in design pixels, so its head and its text stay reachable.</param>
/// <param name="IsTime">Whether it holds a time, whose first press shows the newest first.</param>
internal sealed record TableColumn(string Key, Func<string> Says, double Width, bool IsShare, double Least, bool IsTime = false)
{
    internal string Name => Says();
}


/// <summary>A column and the direction its rows run in.</summary>
/// <param name="Column">The column a head draws its arrow on.</param>
/// <param name="Descending">Which way the arrow points.</param>
/// <param name="AsRowsArrive">Whether the rows already run this way and are not compared, which only a table's own order can be.</param>
internal readonly record struct TableOrder(string Column, bool Descending, bool AsRowsArrive = false);

/// <summary>How one table is sorted and sized. Presentation only: nothing here reaches a vault or the audit log.</summary>
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

    /// <summary>Raised when <see cref="Widths"/> changes.</summary>
    internal event Action? WidthsChanged;

    internal string Id => id;

    internal IReadOnlyList<TableColumn> Columns => columns;

    /// <summary>The order in force, or null for the order the rows arrive in.</summary>
    internal TableOrder? Order => _chosen is { } chosen && !_dropped.Contains(chosen.Column) ? chosen : ownOrder;

    /// <summary>Each column's share of the room once the table has been resized, or null until then, when each column keeps its own width.</summary>
    internal IReadOnlyList<double>? Widths { get; private set; }

    /// <summary>Whether nothing has been chosen, so the table has nothing to keep or reset.</summary>
    internal bool IsOwnWay => _chosen is null && Widths is null;

    internal string? SortText => _chosen is { } chosen
        ? $"{id}:{chosen.Column}:{(chosen.Descending ? "desc" : "asc")}"
        : null;

    internal string? WidthsText => Widths is { } widths
        ? $"{id}:{string.Join(',', widths.Select(width => Math.Max(1, Math.Round(width)).ToString(CultureInfo.InvariantCulture)))}"
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

    /// <summary>How wide a column is and how far its edge lets it go, given the next column showing keeps its own least.</summary>
    /// <param name="column">The column left of the edge.</param>
    /// <param name="drawn">Each column's drawn width, in design pixels, zero where the window dropped it.</param>
    internal (double Now, double Least, double Most) Reach(string column, IReadOnlyList<double> drawn)
    {
        var left = IndexOf(column);
        var right = NextShowing(left, drawn);

        return right == 0
            ? (drawn[left], drawn[left], drawn[left])
            : (drawn[left], Math.Min(columns[left].Least, drawn[left]), drawn[left] + Math.Max(0, drawn[right] - columns[right].Least));
    }

    /// <summary>Moves the edge between a column and the next one showing, each staying at its least or wider.</summary>
    /// <param name="column">The column left of the edge.</param>
    /// <param name="by">How far, in design pixels, from where <paramref name="drawn"/> had it.</param>
    /// <param name="drawn">Each column's drawn width, in design pixels, zero where the window dropped it.</param>
    internal void Resize(string column, double by, IReadOnlyList<double> drawn)
    {
        var left = IndexOf(column);
        var right = NextShowing(left, drawn);

        if (right == 0 || drawn[left] <= 0)
        {
            return;
        }

        var (now, least, most) = Reach(column, drawn);
        var moved = Math.Clamp(by, least - now, most - now);

        if (moved == 0 && Widths is null)
        {
            return;
        }

        var widths = Scaled(drawn);
        widths[left] = drawn[left] + moved;
        widths[right] = drawn[right] - moved;
        Widths = widths;
        WidthsChanged?.Invoke();
    }

    /// <summary>Back to the table's own order and widths.</summary>
    internal void Reset()
    {
        _chosen = null;
        Widths = null;
        OrderChanged?.Invoke();
        WidthsChanged?.Invoke();
    }

    /// <summary>Takes what <c>app.toml</c> kept, leaving the table's own order or widths for anything that does not fit its columns.</summary>
    internal void Restore(string? sort, IReadOnlyList<double>? widths)
    {
        if (sort?.Split(':') is [var column, var direction and ("asc" or "desc")] && columns.Any(known => known.Key == column))
        {
            var chosen = new TableOrder(column, direction == "desc");
            _chosen = chosen == ownOrder ? null : chosen;
        }

        if (widths?.Count == columns.Count)
        {
            Widths = widths;
        }
    }

    private static string Direction(TableColumn column, bool descending) => column.IsTime
        ? descending ? "newest first" : "oldest first"
        : descending ? "descending" : "ascending";

    private TableColumn Column(string key) => columns[IndexOf(key)];

    private int NextShowing(int column, IReadOnlyList<double> drawn) =>
        Enumerable.Range(column + 1, columns.Count - column - 1).FirstOrDefault(index => drawn[index] > 0);

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

    /// <summary>The drawn widths as shares, with a dropped column at the share it would draw at, so it comes back in proportion.</summary>
    private double[] Scaled(IReadOnlyList<double> drawn)
    {
        var showing = Enumerable.Range(0, columns.Count).Where(index => drawn[index] > 0).ToList();
        var shared = showing.Where(index => columns[index].IsShare).ToList();
        var perShare = Widths is { } kept
            ? showing.Sum(index => drawn[index]) / Math.Max(double.Epsilon, showing.Sum(index => kept[index]))
            : shared.Sum(index => drawn[index]) / Math.Max(double.Epsilon, shared.Sum(index => columns[index].Width));

        return [.. Enumerable.Range(0, columns.Count).Select(index =>
            drawn[index] > 0 ? drawn[index] : (Widths?[index] ?? columns[index].Width) * perShare)];
    }
}

/// <summary>Items', Trash's and the activity log's tables, as the shell holds them and <c>[[window]]</c> keeps them.</summary>
internal sealed class TableStates
{
    internal const string Title = "title";
    internal const string Username = "username";
    internal const string Url = "url";
    internal const string Kind = "kind";
    internal const string Group = "group";
    internal const string LastUsed = "last-used";
    internal const string DeletedFrom = "deleted-from";
    internal const string Deleted = "deleted";
    internal const string Time = "time";
    internal const string Actor = "actor";
    internal const string Action = "action";
    internal const string Secrets = "secrets";
    internal const string Where = "where";
    internal const string Result = "result";

    internal static readonly IReadOnlyList<TableColumn> ItemColumns =
    [
        new(Title, () => Strings.Title, 1.4, IsShare: true, Least: 64),
        new(Username, () => Strings.Username, 1, IsShare: true, Least: 64),
        new(Url, () => Strings.URL, 1, IsShare: true, Least: 56),
        new(Kind, () => Strings.Kind, 88, IsShare: false, Least: 56),
        new(Group, () => Strings.Group, 1, IsShare: true, Least: 56),
        new(LastUsed, () => Strings.LastUsed, 104, IsShare: false, Least: 88, IsTime: true),
    ];

    internal static readonly IReadOnlyList<TableColumn> TrashColumns =
    [
        new(Title, () => Strings.Title, 1.3, IsShare: true, Least: 96),
        new(DeletedFrom, () => Strings.DeletedFrom, 1.3, IsShare: true, Least: 96),
        new(Deleted, () => Strings.Deleted, 160, IsShare: false, Least: 124, IsTime: true),
    ];

    internal static readonly IReadOnlyList<TableColumn> LogColumns =
    [
        new(Time, () => Strings.Time, 84, IsShare: false, Least: 84, IsTime: true),
        new(Actor, () => Strings.Actor, 110, IsShare: false, Least: 56),
        new(Action, () => Strings.Action, 100, IsShare: false, Least: 56),
        new(Secrets, () => Strings.Secrets, 1.6, IsShare: true, Least: 64),
        new(Where, () => Strings.Where, 1, IsShare: true, Least: 56),
        new(Result, () => Strings.Result, 100, IsShare: false, Least: 64),
    ];

    internal TableState Items { get; } = NewItems();

    internal TableState Trash { get; } = NewTrash();

    internal TableState Log { get; } = NewLog();

    internal string? SortsText => Joined(',', All.Select(table => table.SortText));

    internal string? WidthsText => Joined(';', All.Select(table => table.WidthsText));

    private IEnumerable<TableState> All => [Items, Trash, Log];

    /// <summary>Items run in the vault's order until a head is pressed.</summary>
    internal static TableState NewItems() => new("items", ItemColumns, null);

    internal static TableState NewTrash() => new("trash", TrashColumns, new TableOrder(Deleted, Descending: true));

    /// <summary>The log runs in the file's order until a head is pressed, since only the chain vouches for it and a record's clock can be wrong.</summary>
    internal static TableState NewLog() => new("log", LogColumns, new TableOrder(Time, Descending: true, AsRowsArrive: true));

    /// <summary>Reads what <c>app.toml</c> kept; what does not read leaves its table as it is.</summary>
    internal static TableStates Read(string? sorts, string? widths)
    {
        var states = new TableStates();

        foreach (var table in states.All)
        {
            table.Restore(Part(sorts, ',', table.Id), Numbers(Part(widths, ';', table.Id)));
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

    private static double[]? Numbers(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var parts = text.Split(',');
        var numbers = new double[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is <= 0 or > 100_000)
            {
                return null;
            }

            numbers[index] = number;
        }

        return numbers;
    }
}
