using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>A table's head: its column in capitals, an arrow while the rows run by it, and a press to sort, again to reverse.</summary>
internal sealed class ColumnHead : Button
{
    public static readonly StyledProperty<string> ColumnProperty = AvaloniaProperty.Register<ColumnHead, string>(nameof(Column), string.Empty);

    private const double _arrowSize = 12;
    private const double _ringOutset = 4;

    private readonly TextBlock _label = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly KpIcon _arrow = new() { Size = _arrowSize, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Icon = "arrow-up" };
    private readonly Grid _face = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    private TableState? _followed;
    private double _underEdge;

    public ColumnHead()
    {
        _label.Classes.Add("th");
        Classes.Add("column-head");

        Grid.SetColumn(_arrow, 1);
        _face.Children.Add(_label);
        _face.Children.Add(_arrow);
        Content = _face;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    public string Column
    {
        get => GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        _followed?.Press(Column);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // With no gap after it, its focus ring would lie over the next head's text.
        if (Parent is TableColumns { ColumnSpacing: 0 } && HorizontalContentAlignment != HorizontalAlignment.Right)
        {
            Margin = new Thickness(0, 0, _ringOutset, 0);
        }

        _underEdge = Parent is TableColumns columns && columns.Children.OfType<ColumnGrip>().Any(grip => grip.Column == Column)
            ? ColumnGrip.Over(columns.ColumnSpacing) - Margin.Right
            : 0;

        Follow(TableColumns.GetTable(this));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Follow(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TableColumns.TableProperty && TopLevel.GetTopLevel(this) is not null)
        {
            Follow(change.GetNewValue<TableState?>());
        }
    }

    private void Follow(TableState? table)
    {
        if (_followed is not null)
        {
            _followed.OrderChanged -= Refresh;
        }

        _followed = table;

        if (_followed is not null)
        {
            _followed.OrderChanged += Refresh;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_followed?.Columns.FirstOrDefault(column => column.Key == Column) is not { } column)
        {
            return;
        }

        var name = _followed.HeadName(Column);
        var sorted = _followed.Order is { } order && order.Column == Column ? order : (TableOrder?)null;

        _label.Text = column.Name.ToUpperInvariant();
        _arrow.IsVisible = sorted is not null;

        // The arrow stays clear of the edge lying over this head's end, however narrow the column, so pressing it always sorts.
        _face.Margin = new Thickness(0, 0, sorted is null ? 0 : _underEdge, 0);
        _arrow.Icon = sorted is { Descending: true } ? "arrow-down" : "arrow-up";
        Classes.Set("sorted", sorted is not null);
        AutomationProperties.SetName(this, name);
        ToolTip.SetTip(this, name);
    }
}
