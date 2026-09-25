using Avalonia.Controls;
using Avalonia.Input;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// A prompt window whose whole client area is the dialog card and its shadow: no system chrome,
/// moved by dragging the card, focused on Deny when it opens and answered with a denial however it
/// closes.
/// </summary>
internal abstract class PromptWindow : Window
{
    /// <summary>The transparent band around the card that its shadow is drawn in.</summary>
    private const double ShadowMargin = 24;

    protected PromptWindow()
    {
        Opened += (_, _) =>
        {
            // A platform that cannot draw transparency would paint the shadow band black, so the
            // card fills the window instead.
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Classes.Add("opaque");
                Width -= 2 * ShadowMargin;
            }

            // Focus starts on Deny, so a keystroke meant for another window can only refuse.
            this.FindControl<Button>("Deny")?.Focus();
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>However the window closes, a request still open is refused.</summary>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as PromptViewModel)?.Closed();
        base.OnClosed(e);
    }
}
