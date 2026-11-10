using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Keypaste.App.Controls;

/// <summary>
/// A list on the left and the chosen thing on the right, or only one of the two where both cannot fit.
/// </summary>
/// <remarks>
/// The first child is the list, the second the detail. Both show side by side while the width holds
/// <see cref="ListWidth"/> and <see cref="DetailLeast"/>; under that, the detail replaces the list while
/// <see cref="ShowsDetail"/> says something is chosen, and <see cref="ShowsBack"/> tells the screen to offer the way back.
/// With <see cref="ShowsList"/> off the detail takes the whole view, as a comparison of two revisions does.
/// </remarks>
internal sealed class ListDetail : Panel
{
    /// <summary>The list's width beside the detail.</summary>
    internal const double ListWidth = 262;

    /// <summary>The least the detail keeps beside the list, its title and actions still readable.</summary>
    internal const double DetailLeast = 420;

    internal static readonly StyledProperty<bool> ShowsListProperty =
        AvaloniaProperty.Register<ListDetail, bool>(nameof(ShowsList), defaultValue: true);

    internal static readonly StyledProperty<bool> ShowsDetailProperty =
        AvaloniaProperty.Register<ListDetail, bool>(nameof(ShowsDetail));

    internal static readonly DirectProperty<ListDetail, bool> ShowsBackProperty =
        AvaloniaProperty.RegisterDirect<ListDetail, bool>(nameof(ShowsBack), panel => panel.ShowsBack);

    private bool _showsBack;

    /// <summary>Which of the two panes a width or a choice just hid while the keyboard was in it.</summary>
    internal enum Pane
    {
        List,
        Detail,
    }

    /// <summary>The keyboard was in a pane that has just hidden, so the screen decides where it goes.</summary>
    internal event Action<Pane>? HidKeyboardPane;

    static ListDetail() => AffectsMeasure<ListDetail>(ShowsListProperty, ShowsDetailProperty);

    /// <summary>Whether the list is in the layout at all; off, the detail takes the whole width.</summary>
    internal bool ShowsList
    {
        get => GetValue(ShowsListProperty);
        set => SetValue(ShowsListProperty, value);
    }

    /// <summary>Whether something is chosen, so a width that cannot hold both shows the detail rather than the list.</summary>
    internal bool ShowsDetail
    {
        get => GetValue(ShowsDetailProperty);
        set => SetValue(ShowsDetailProperty, value);
    }

    /// <summary>Whether the detail stands in the list's place, so the screen offers a way back to it.</summary>
    internal bool ShowsBack
    {
        get => _showsBack;
        private set => SetAndRaise(ShowsBackProperty, ref _showsBack, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == ShowsListProperty || change.Property == ShowsDetailProperty)
        {
            ShowWhatFits();
        }
    }

    // Posted, not done in this layout pass: hiding a child that holds the keyboard moves focus, and an adorner layer changed during layout loses its place.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Dispatcher.UIThread.Post(ShowWhatFits, DispatcherPriority.Loaded);
    }

    /// <summary>Whether the list shows and the width cannot also hold the detail.</summary>
    internal bool ListStandsAlone => ShowsList && !Fits(Bounds.Width);

    private static bool Fits(double width) => width >= ListWidth + DetailLeast || double.IsInfinity(width);

    private void ShowWhatFits()
    {
        if (Children is not [var list, var detail] || Bounds.Width <= 0 || TopLevel.GetTopLevel(this) is not Window { IsVisible: true })
        {
            return;
        }

        var focused = (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual)?.GetSelfAndVisualAncestors().ToList();
        var listHadKeyboard = list.IsVisible && focused?.Contains(list) == true;
        var detailHadKeyboard = detail.IsVisible && focused?.Contains(detail) == true;

        var side = Fits(Bounds.Width);
        list.IsVisible = ShowsList && (side || !ShowsDetail);
        detail.IsVisible = !ShowsList || side || ShowsDetail;
        ShowsBack = ShowsList && !side && ShowsDetail;

        if (listHadKeyboard && !list.IsVisible)
        {
            HidKeyboardPane?.Invoke(Pane.List);
        }
        else if (detailHadKeyboard && !detail.IsVisible)
        {
            HidKeyboardPane?.Invoke(Pane.Detail);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var list, var detail])
        {
            return default;
        }

        var side = Fits(availableSize.Width);
        var listOn = ShowsList && (side || !ShowsDetail);
        var detailOn = !ShowsList || side || ShowsDetail;

        if (listOn && detailOn)
        {
            list.Measure(availableSize.WithWidth(ListWidth));
            detail.Measure(availableSize.WithWidth(availableSize.Width - ListWidth));
        }
        else
        {
            (listOn ? list : detail).Measure(availableSize);
        }

        var drawn = listOn ? list : detail;
        return new Size(availableSize.Width, drawn.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var list, var detail])
        {
            return finalSize;
        }

        var side = Fits(finalSize.Width);
        var listOn = ShowsList && (side || !ShowsDetail);
        var detailOn = !ShowsList || side || ShowsDetail;
        var both = listOn && detailOn;

        list.Arrange(listOn ? new Rect(0, 0, both ? ListWidth : finalSize.Width, finalSize.Height) : default);
        detail.Arrange(detailOn ? new Rect(both ? ListWidth : 0, 0, finalSize.Width - (both ? ListWidth : 0), finalSize.Height) : default);

        return finalSize;
    }
}
