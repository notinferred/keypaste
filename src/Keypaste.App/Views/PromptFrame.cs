using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Keypaste.App.Views;

/// <summary>
/// Scales a prompt as a whole, until it opens: at the interface's zoom, or smaller where that would not fit its screen,
/// so its layout stays fixed and no part of it, Deny and the agent's reason included, starts past a screen's edge.
/// </summary>
/// <remarks>The prompt's security rules win over rule 7's 200% (DESIGN § Interaction): a scrolling prompt could hide what the person is answering. Zooming while it is open moves nothing under the pointer.</remarks>
internal sealed class PromptFrame : LayoutTransformControl
{
    private readonly Func<ScreenArea?> _screen;
    private bool _held;

    internal PromptFrame(Control card, Func<ScreenArea?> screen)
    {
        _screen = screen;
        LayoutTransform = new ScaleTransform(1, 1);

        // A plain parent, because the transform turns about its child's own corner and would leave that child's margin, the card's shadow band, unscaled.
        Child = new Panel { Children = { card } };
    }

    /// <summary>Keeps the scale from here on: the prompt has opened, and nothing may move under the pointer.</summary>
    internal void Hold() => _held = true;

    /// <summary>The scale that shows a card of <paramref name="natural"/> size at <paramref name="zoom"/>, or whole in <paramref name="room"/> when that is too small for it.</summary>
    internal static double ScaleFor(double zoom, Size natural, Size room) =>
        natural.Width <= 0 || natural.Height <= 0
            ? zoom
            : Math.Min(zoom, Math.Min(room.Width / natural.Width, room.Height / natural.Height));

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_held && Child is { } panel)
        {
            panel.Measure(Size.Infinity);

            // What the window offers is as far as the platform will size it, which can stop short of the screen.
            var screen = _screen()?.Size ?? Size.Infinity;
            var room = new Size(Math.Min(screen.Width, availableSize.Width), Math.Min(screen.Height, availableSize.Height));
            var scale = ScaleFor(InterfaceZoom.Factor, panel.DesiredSize, room);

            if (LayoutTransform is not ScaleTransform { ScaleX: var current } || current != scale)
            {
                LayoutTransform = new ScaleTransform(scale, scale);
            }
        }

        return base.MeasureOverride(availableSize);
    }
}
