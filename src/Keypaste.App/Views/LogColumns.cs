using Avalonia;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The columns the log's header and every row share, which give up Where where the content is tight.</summary>
internal sealed class LogColumns : TableColumns
{
    public static readonly StyledProperty<bool> ShowsWhereProperty = AvaloniaProperty.Register<LogColumns, bool>(nameof(ShowsWhere), defaultValue: true);

    public LogColumns()
        : base(TableStates.LogColumns, spacing: 12)
    {
    }

    public bool ShowsWhere
    {
        get => GetValue(ShowsWhereProperty);
        set => SetValue(ShowsWhereProperty, value);
    }

    protected override bool Shows(string column) => column != TableStates.Where || ShowsWhere;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowsWhereProperty)
        {
            ApplyWidths();
        }
    }
}
