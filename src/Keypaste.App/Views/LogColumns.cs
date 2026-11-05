using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Views;

/// <summary>The columns the log's header and every row share, which give up Where under the rail.</summary>
internal sealed class LogColumns : Grid
{
    public static readonly StyledProperty<bool> ShowsWhereProperty = AvaloniaProperty.Register<LogColumns, bool>(nameof(ShowsWhere), defaultValue: true);

    private const int _where = 4;

    public LogColumns()
    {
        ColumnDefinitions = new ColumnDefinitions("64,110,100,1.6*,*,100");
        ColumnSpacing = 12;
    }

    public bool ShowsWhere
    {
        get => GetValue(ShowsWhereProperty);
        set => SetValue(ShowsWhereProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowsWhereProperty)
        {
            ColumnDefinitions[_where].Width = change.GetNewValue<bool>() ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }
    }
}
