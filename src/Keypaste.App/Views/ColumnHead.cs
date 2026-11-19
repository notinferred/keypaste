using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Keypaste.App.Views;

/// <summary>A table's head: its column's name in capitals, a plain label.</summary>
internal sealed class ColumnHead : TextBlock
{
    public static readonly StyledProperty<string> ColumnProperty = AvaloniaProperty.Register<ColumnHead, string>(nameof(Column), string.Empty);

    public ColumnHead()
    {
        Classes.Add("th");
        TextTrimming = TextTrimming.CharacterEllipsis;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string Column
    {
        get => GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Label();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TableColumns.TableProperty || change.Property == ColumnProperty)
        {
            Label();
        }
    }

    private void Label()
    {
        if (TableColumns.GetTable(this)?.Columns.FirstOrDefault(column => column.Key == Column) is { } column)
        {
            Text = column.Name.ToUpperInvariant();
            AutomationProperties.SetName(this, column.Name);
        }
    }
}
