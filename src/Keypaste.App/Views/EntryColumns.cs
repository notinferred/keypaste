using Avalonia;
using Avalonia.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The columns Items' header and every row share, which give up Username and URL in a narrow window.</summary>
internal sealed class EntryColumns : TableColumns
{
    /// <summary>Whether the Username and URL columns show; inherited, so the screen sets it once for its header and rows.</summary>
    public static readonly AttachedProperty<bool> ShowsContactProperty =
        AvaloniaProperty.RegisterAttached<EntryColumns, Control, bool>("ShowsContact", defaultValue: true, inherits: true);

    private const double _kindIcon = 28;

    public EntryColumns()
        : base(TableStates.ItemColumns, leading: _kindIcon)
    {
    }

    protected override Type StyleKeyOverride => typeof(Grid);

    public static bool GetShowsContact(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ShowsContactProperty);
    }

    public static void SetShowsContact(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ShowsContactProperty, value);
    }

    protected override bool Shows(string column) =>
        column is not (TableStates.Username or TableStates.Url) || GetValue(ShowsContactProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowsContactProperty)
        {
            ApplyWidths();
        }
    }
}
