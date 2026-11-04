using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// A toast can be pointed at: it holds while the pointer is on it and closes with its ×, and the
/// clipboard toast keeps its Clear now.
/// </summary>
public sealed class ToastHitTests
{
    [Fact]
    public Task A_toast_waits_while_the_pointer_is_on_it_and_closes_with_its_button() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-toast-hit-");
        shell.Shell.ShowToast("Saved billing.", ToastTone.Success);
        RenderedShell.Drain();
        shell.Frame();

        var toast = shell.Window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("toast") && border.DataContext is Toast);
        Assert.True(toast.IsEffectivelyVisible);
        var hit = Hit(shell.Window, toast);
        Assert.True(hit is Visual visual && (visual == toast || toast.IsVisualAncestorOf(visual)), $"the toast did not take the pointer: {hit}");

        WindowInput.Hover(shell.Window, toast);
        Assert.True(toast.IsPointerOver);

        var close = toast.GetVisualDescendants().OfType<Button>().Single();
        Assert.Equal("Close this message", AutomationProperties.GetName(close));
        close.Command!.Execute(null);
        RenderedShell.Drain();

        Assert.Empty(shell.Shell.Toasts.Shown);
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
