using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>Generate password…: the dialog over the screen it was opened from, which Escape cancels wherever focus sits.</summary>
internal sealed partial class GeneratePasswordView : UserControl
{
    private TopLevel? _root;

    public GeneratePasswordView() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        _root?.AddHandler(KeyDownEvent, OnRootEscape, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _root?.RemoveHandler(KeyDownEvent, OnRootEscape);
        _root = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRootEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || DataContext is not GeneratePasswordViewModel model)
        {
            return;
        }

        model.CancelCommand.Execute(null);
        e.Handled = true;
    }
}
