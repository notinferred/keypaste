using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The generator's sheet, which Escape cancels wherever focus sits and which gives the keyboard back to where it was when it closes.</summary>
internal sealed partial class GeneratePasswordView : UserControl
{
    private TopLevel? _root;
    private IInputElement? _returnTo;

    public GeneratePasswordView() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        IsVisible = DataContext is GeneratePasswordViewModel;

        if (IsVisible)
        {
            _returnTo = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }
        else
        {
            if (_returnTo is InputElement { IsEffectivelyVisible: true } returnTo)
            {
                returnTo.Focus(NavigationMethod.Tab);
            }

            _returnTo = null;
        }
    }

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
