using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Views;

/// <summary>The columns Items' header and every row share, which give up Username and URL in a narrow window.</summary>
internal sealed class EntryColumns : Grid
{
    /// <summary>Whether the Username and URL columns show; inherited, so the screen sets it once for its header and rows.</summary>
    public static readonly AttachedProperty<bool> ShowsContactProperty =
        AvaloniaProperty.RegisterAttached<EntryColumns, Control, bool>("ShowsContact", defaultValue: true, inherits: true);

    private const int _username = 2;
    private const int _url = 3;

    public EntryColumns() => ColumnDefinitions = new ColumnDefinitions("28,1.4*,*,*,88,*,104");

    protected override Type StyleKeyOverride => typeof(Grid);

    public static bool GetShowsContact(Control control) => control.GetValue(ShowsContactProperty);

    public static void SetShowsContact(Control control, bool value) => control.SetValue(ShowsContactProperty, value);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowsContactProperty)
        {
            var width = change.GetNewValue<bool>() ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ColumnDefinitions[_username].Width = width;
            ColumnDefinitions[_url].Width = width;
        }
    }
}
