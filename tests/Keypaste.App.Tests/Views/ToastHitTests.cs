using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Tests.Rendering;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// A plain toast says something and takes no press, so a click where it is drawn reaches the control
/// under it; the clipboard toast keeps its Clear now (C.4).
/// </summary>
public sealed class ToastHitTests
{
    [Fact]
    public Task A_plain_toast_lets_a_press_reach_what_it_covers() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-toast-hit-");
        shell.Shell.ShowToast("Saved billing.");
        RenderedShell.Drain();
        shell.Frame();

        var toast = shell.Named<Border>("Toast");
        Assert.True(toast.IsEffectivelyVisible);
        var hit = Hit(shell.Window, toast);

        Assert.False(hit is Visual visual && (visual == toast || toast.IsVisualAncestorOf(visual)), $"the toast took the press: {hit}");
    });

    [Fact]
    public Task The_clipboard_toast_keeps_Clear_now_pressable() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell("keypaste-toast-clear-");
        await shell.Shell.Clipboard.CopyAsync(RenderedShell.Current, "Password");
        RenderedShell.Drain();
        shell.Frame();

        var clear = shell.Named<Border>("ClipboardToast").GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Clear now");
        Assert.True(clear.IsEffectivelyVisible);
        var hit = Hit(shell.Window, clear);

        Assert.True(hit is Visual visual && (visual == clear || clear.IsVisualAncestorOf(visual)), $"Clear now did not take the press: {hit}");
    });

    private static IInputElement? Hit(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) is { } centre
            ? window.InputHitTest(centre)
            : null;
}
