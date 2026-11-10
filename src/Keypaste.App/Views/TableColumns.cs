using Avalonia;
using Avalonia.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The columns a table's head and every row share, so they line up however the table is sized.</summary>
internal abstract class TableColumns : Grid
{
    /// <summary>The table a view draws; inherited, so the screen sets it once for its head and its rows.</summary>
    public static readonly AttachedProperty<TableState?> TableProperty =
        AvaloniaProperty.RegisterAttached<TableColumns, Control, TableState?>("Table", inherits: true);

    private readonly IReadOnlyList<TableColumn> _columns;
    private readonly int _leading;
    private TableState? _followed;

    /// <param name="columns">The table's columns.</param>
    /// <param name="leading">A fixed column before them, in pixels, or none.</param>
    /// <param name="spacing">The gap between columns.</param>
    protected TableColumns(IReadOnlyList<TableColumn> columns, double? leading = null, double spacing = 0)
    {
        _columns = columns;
        ColumnSpacing = spacing;
        _leading = leading is null ? 0 : 1;

        if (leading is { } width)
        {
            ColumnDefinitions.Add(new ColumnDefinition(new GridLength(width)));
        }

        foreach (var _ in columns)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
        }

        ApplyWidths();
    }

    public static TableState? GetTable(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(TableProperty);
    }

    public static void SetTable(Control control, TableState? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(TableProperty, value);
    }

    /// <summary>Each column's drawn width in design pixels, zero where the window dropped it.</summary>
    /// <remarks>Grid adds its spacing to each definition's size, so a width, a least and a share all mean what is drawn, and only the actual width carries the spacing.</remarks>
    internal IReadOnlyList<double> DrawnWidths =>
        [.. _columns.Select((column, index) => Shows(column.Key) ? ColumnDefinitions[index + _leading].ActualWidth - ColumnSpacing : 0)];

    /// <summary>Whether the window has room for a column.</summary>
    protected virtual bool Shows(string column) => true;

    protected void ApplyWidths()
    {
        var kept = _followed?.Widths;

        for (var index = 0; index < _columns.Count; index++)
        {
            var column = _columns[index];
            var definition = ColumnDefinitions[index + _leading];
            var shows = Shows(column.Key);

            definition.MinWidth = shows ? column.Least : 0;
            definition.Width = !shows
                ? new GridLength(0)
                : kept is not null
                    ? new GridLength(kept[index], GridUnitType.Star)
                    : new GridLength(column.Width, column.IsShare ? GridUnitType.Star : GridUnitType.Pixel);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow(GetTable(this));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Follow(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TableProperty && IsAttachedToVisualTree())
        {
            Follow(change.GetNewValue<TableState?>());
        }
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;

    private void Follow(TableState? table)
    {
        if (ReferenceEquals(_followed, table))
        {
            return;
        }

        if (_followed is not null)
        {
            _followed.WidthsChanged -= ApplyWidths;
        }

        _followed = table;

        if (_followed is not null)
        {
            _followed.WidthsChanged += ApplyWidths;
        }

        ApplyWidths();
    }
}
