using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Keypaste.App.Views;

/// <summary>
/// What the main window draws everything through: its body is laid out at the design's sizes and scaled by
/// <see cref="InterfaceZoom"/>, so the layout's breakpoints read those sizes and a width in the body is never a window's.
/// </summary>
/// <remarks>The body never lays out under the window's minimum; where the screen cannot hold that minimum at this zoom, the whole body scrolls.</remarks>
internal sealed class ZoomFrame : Decorator
{
    private readonly LayoutTransformControl _scaled;
    private readonly ScrollViewer _scroller;

    internal ZoomFrame(Control body)
    {
        _scaled = new LayoutTransformControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = Scale(InterfaceZoom.Factor),

            // A plain parent, because the transform turns about its child's own corner and would leave that child's margin unscaled.
            Child = new Panel { Children = { body } },
        };
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _scaled,
        };

        // Every scroller on a focus's route asks to show it, and a second request moved pages that already had; this one acts only in OnGotFocus.
        ScrollViewer.SetBringIntoViewOnFocusChange(_scroller, false);
        Child = _scroller;

        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InterfaceZoom.Changed += OnZoomChanged;
        OnZoomChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InterfaceZoom.Changed -= OnZoomChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var factor = InterfaceZoom.Factor;
        _scaled.Width = Math.Max(availableSize.Width, WindowChrome.MinimumWidth * factor);
        _scaled.Height = Math.Max(availableSize.Height, WindowChrome.MinimumHeight * factor);
        return base.MeasureOverride(availableSize);
    }

    private static ScaleTransform Scale(double factor) => new(factor, factor);

    private void OnZoomChanged()
    {
        _scaled.LayoutTransform = Scale(InterfaceZoom.Factor);
        InvalidateMeasure();
    }

    // Only while the frame itself scrolls, so a window that fits behaves as it would without the frame. Posted, because a page's own scroller answers first and moves the control.
    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control focused && Scrolls())
        {
            Dispatcher.UIThread.Post(() => ShowInFrame(focused), DispatcherPriority.Loaded);
        }
    }

    private bool Scrolls() =>
        _scroller.Extent.Width > _scroller.Viewport.Width || _scroller.Extent.Height > _scroller.Viewport.Height;

    private void ShowInFrame(Control focused)
    {
        if (focused.TransformToVisual(_scroller) is not { } transform)
        {
            return;
        }

        var shown = new Rect(focused.Bounds.Size).TransformToAABB(transform);
        var view = _scroller.Viewport;
        _scroller.Offset = new Vector(
            _scroller.Offset.X + Into(shown.Left, shown.Right, view.Width),
            _scroller.Offset.Y + Into(shown.Top, shown.Bottom, view.Height));
    }

    /// <summary>How far to move so a span shows in a view of <paramref name="length"/>, its start winning when it does not fit.</summary>
    private static double Into(double start, double end, double length) =>
        start < 0 || end - start > length ? start : Math.Max(0, end - length);
}
