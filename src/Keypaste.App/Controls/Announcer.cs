using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Keypaste.App.Controls;

/// <summary>
/// Says a line once through its window's hidden live text, so what a control draws, such as a countdown, stays quiet.
/// Never give it a secret: the line reaches the automation tree.
/// </summary>
internal sealed class Announcer : AvaloniaObject
{
    public static readonly AttachedProperty<string?> SaysProperty =
        AvaloniaProperty.RegisterAttached<Announcer, Control, string?>("Says");

    private static readonly AttachedProperty<bool> _isQueuedProperty =
        AvaloniaProperty.RegisterAttached<Announcer, Control, bool>("IsQueued");

    static Announcer()
    {
        SaysProperty.Changed.AddClassHandler<Control>((control, _) => Queue(control));
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is true)
            {
                Queue(control);
            }
        });
    }

    private Announcer()
    {
    }

    public static string? GetSays(Control control) => control.GetValue(SaysProperty);

    public static void SetSays(Control control, string? value) => control.SetValue(SaysProperty, value);

    // Later than layout: a row's binding and its window's opening both arrive before its window has live text to change.
    private static void Queue(Control control)
    {
        if (string.IsNullOrWhiteSpace(GetSays(control)) || control.GetValue(_isQueuedProperty))
        {
            return;
        }

        control.SetValue(_isQueuedProperty, true);
        Dispatcher.UIThread.Post(() => Say(control), DispatcherPriority.Loaded);
    }

    private static void Say(Control control)
    {
        control.SetValue(_isQueuedProperty, false);
        var line = GetSays(control);

        if (string.IsNullOrWhiteSpace(line) || !control.IsEffectivelyVisible || TopLevel.GetTopLevel(control)?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text => text.Classes.Contains("live")) is not { } live)
        {
            return;
        }

        // A screen reader hears a change, so the same refusal twice takes a change between.
        if (live.Text == line)
        {
            live.Text = string.Empty;
        }

        live.Text = line;
    }
}
