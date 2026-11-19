using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;

namespace Keypaste.App.Views;

/// <summary>
/// The Trash screen.
/// </summary>
/// <remarks>
/// There is no secret control here: a trash row holds a name, where it came from and when it went, so nothing on
/// this screen needs masking or a hold. What is here is the keyboard's place when a narrow width swaps the list for the detail.
/// </remarks>
internal sealed partial class TrashView : UserControl
{
    public TrashView()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<ListDetail>("Split")!.PropertyChanged += (_, e) =>
        {
            if (e.Property == ListDetail.ShowsBackProperty)
            {
                var split = this.FindControl<ListDetail>("Split")!;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } window && KeyboardIsHere(window))
                        {
                            Control? next = split.ShowsBack ? this.FindControl<Button>("BackToList") : this.FindControl<ListBox>("TrashRows");
                            next?.Focus(NavigationMethod.Tab);
                        }
                    },
                    DispatcherPriority.Loaded);
            }
        };
    }

    private bool KeyboardIsHere(Window window) => window.FocusManager?.GetFocusedElement() is not Visual focused || this.IsVisualAncestorOf(focused);
}
