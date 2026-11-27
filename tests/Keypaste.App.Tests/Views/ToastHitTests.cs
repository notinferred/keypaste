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

        Assert.False(toast.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ToastOffer").IsEffectivelyVisible);
        var close = toast.GetVisualDescendants().OfType<Button>().Single(button => button.Name != "ToastOffer");
        Assert.Equal("Close this message", AutomationProperties.GetName(close));
        close.Command!.Execute(null);
        RenderedShell.Drain();

        Assert.Empty(shell.Shell.Toasts.Shown);
    });

    [Fact]
    public Task A_toasts_offer_takes_the_press_does_what_it_offers_and_closes_the_toast() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-toast-offer-");
        var pressed = 0;
        shell.Shell.Toasts.Show("Run command copied.", ToastTone.Information, new ToastAction("Link the keypaste command…", () => pressed++));
        RenderedShell.Drain();
        shell.Frame();

        var offer = shell.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ToastOffer");
        Assert.True(offer.IsEffectivelyVisible);
        Assert.Equal("Link the keypaste command…", offer.Content);
        var hit = Hit(shell.Window, offer);
        Assert.True(hit is Visual visual && (visual == offer || offer.IsVisualAncestorOf(visual)), $"the offer did not take the press: {hit}");

        offer.Command!.Execute(null);
        RenderedShell.Drain();

        Assert.Equal(1, pressed);
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
