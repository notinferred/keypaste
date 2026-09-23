using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Mouse and keyboard delivered to a drawn window, which the platform routes to a control by
/// hit-testing and focus rather than a test raising events on the control it wants.
/// </summary>
internal static class WindowInput
{
    internal static void Drain() => Dispatcher.UIThread.RunJobs();

    /// <summary>Scrolls <paramref name="control"/> into the window's view and draws, so it can be hit.</summary>
    internal static void Reveal(TopLevel window, Control control)
    {
        control.BringIntoView();
        Drain();
        DrawnFrame.Capture(window);
    }

    internal static void Hover(TopLevel window, Visual control)
    {
        DrawnFrame.Capture(window);
        window.MouseMove(Centre(window, control));
        Drain();
    }

    internal static void Press(TopLevel window, Visual control)
    {
        DrawnFrame.Capture(window);
        window.MouseDown(Centre(window, control), MouseButton.Left);
        Drain();
    }

    internal static void Release(TopLevel window, Visual control)
    {
        window.MouseUp(Centre(window, control), MouseButton.Left);
        Drain();
    }

    /// <summary>Clicks <paramref name="field"/> to focus it.</summary>
    internal static void Click(TopLevel window, Control field)
    {
        Reveal(window, field);
        Press(window, field);
        Release(window, field);
        Assert.True(field.IsFocused, $"{field.Name} did not take focus from a click");
    }

    /// <summary>Types into whatever has focus, one keystroke per character.</summary>
    internal static void Type(TopLevel window, string text)
    {
        foreach (var c in text)
        {
            window.KeyTextInput(c.ToString());
        }

        Drain();
    }

    internal static void Escape(TopLevel window)
    {
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Drain();
    }

    private static Point Centre(TopLevel window, Visual control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the control is not in the window");
}
