using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// A prompt window whose whole client area is the dialog card and its shadow: no system chrome,
/// moved by dragging the card, focused on Deny when it opens, is brought forward or is activated, drawn
/// inactive while another window has focus, and answered with a denial however it closes.
/// </summary>
internal abstract class PromptWindow : Window
{
    /// <summary>The transparent band around the card that its shadow is drawn in.</summary>
    private const double _shadowMargin = 24;

    /// <summary>What a style selects a prompt by, since every window's style key is <see cref="Window"/>.</summary>
    internal const string Class = "prompt-window";

    private const string _inactive = "inactive";

    protected PromptWindow()
    {
        OpensOn = ScreenItOpensOn;
        StartMove = BeginMoveDrag;
        Classes.Add(Class);

        // A security prompt keeps its layout under every culture, as ReadingDirection also holds for its text; its copy stays in English.
        FlowDirection = FlowDirection.LeftToRight;

        Opened += (_, _) =>
        {
            // A platform that cannot draw transparency would paint the shadow band black, so the
            // card fills the window instead.
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Classes.Add("opaque");
            }

            (Content as PromptFrame)?.Hold();

            // A window that opens without focus raises no Deactivated, and a press or a bring-forward then activates it.
            Classes.Set(_inactive, !IsActive);

            // Focus starts on Deny, so a keystroke meant for another window can only refuse.
            FocusDeny();
        };

        // A person coming back to a prompt starts on Deny too, wherever focus was when they left.
        Activated += (_, _) =>
        {
            Classes.Set(_inactive, false);
            FocusDeny();
        };
        Deactivated += (_, _) => Classes.Set(_inactive, true);
    }

    /// <summary>The working area the prompt must fit: the screen it opens on, as the platform centers it.</summary>
    internal Func<ScreenArea?> OpensOn { get; set; }

    /// <summary>Hands the press that starts a move to the platform's window manager.</summary>
    internal Action<PointerPressedEventArgs> StartMove { get; set; }

    /// <summary>Called once its XAML has loaded. The window's width there is the design's, shadow band included; from here the card holds it and the window follows the card's scale.</summary>
    protected void FrameCard()
    {
        if (Content is Control card)
        {
            card.Width = Width - (2 * _shadowMargin);
            Width = double.NaN;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = null;
            Content = new PromptFrame(card, () => OpensOn());
        }
    }

    private ScreenArea? ScreenItOpensOn() =>
        Screens is { ScreenCount: > 0 } screens && (screens.ScreenFromPoint(Position) ?? screens.Primary) is { } screen ? ScreenArea.Of(screen) : null;

    /// <summary>Raises the window over the others with focus on Deny, as it opened.</summary>
    internal void BringForward()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        FocusDeny();
    }

    private void FocusDeny() => this.FindControl<Button>("Deny")?.Focus();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnButton(e.GetPosition(this)))
        {
            StartMove(e);
        }
    }

    // A disabled button is skipped by the press's own hit test, which lands on the card behind it; a move started there leaves a window manager's grab to swallow the next press.
    private bool IsOnButton(Point point) =>
        this.InputHitTest(point, enabledElementsOnly: false) is Visual top && top.GetSelfAndVisualAncestors().OfType<Button>().Any();

    /// <summary>However the window closes, a request still open is refused.</summary>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as PromptViewModel)?.Closed();
        base.OnClosed(e);
    }
}
