using Avalonia;
using Avalonia.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The columns Items' header and every row share, which give up Username and URL in a narrow window, and Group while the header names it.</summary>
internal sealed class EntryColumns : TableColumns
{
    /// <summary>Whether the Username and URL columns show; inherited, so the screen sets it once for its header and rows.</summary>
    public static readonly AttachedProperty<bool> ShowsContactProperty =
        AvaloniaProperty.RegisterAttached<EntryColumns, Control, bool>("ShowsContact", defaultValue: true, inherits: true);

    /// <summary>Whether the Group column shows; inherited as <see cref="ShowsContactProperty"/> is.</summary>
    public static readonly AttachedProperty<bool> ShowsGroupProperty =
        AvaloniaProperty.RegisterAttached<EntryColumns, Control, bool>("ShowsGroup", defaultValue: true, inherits: true);

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

    public static bool GetShowsGroup(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ShowsGroupProperty);
    }

    public static void SetShowsGroup(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(ShowsGroupProperty, value);
    }

    protected override bool Shows(string column) => column switch
    {
        TableStates.Username or TableStates.Url => GetValue(ShowsContactProperty),
        TableStates.Group => GetValue(ShowsGroupProperty),
        _ => true,
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowsContactProperty || change.Property == ShowsGroupProperty)
        {
            ApplyWidths();
        }
    }
}
