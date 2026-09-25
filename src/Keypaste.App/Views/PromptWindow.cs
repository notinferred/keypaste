using Avalonia.Controls;
using Avalonia.Input;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// A prompt window whose whole client area is the dialog card: no system chrome, moved by dragging
/// the card, focused on Deny when it opens and answered with a denial however it closes.
/// </summary>
internal abstract class PromptWindow : Window
{
    protected PromptWindow()
    {
        // Focus starts on Deny, so a keystroke meant for another window can only refuse.
        Opened += (_, _) => this.FindControl<Button>("Deny")?.Focus();
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
