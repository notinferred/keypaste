using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The Agents screen; everything it reads and does is <see cref="ViewModels.AgentActivityViewModel"/>'s.</summary>
internal sealed partial class AgentActivityView : UserControl
{
    private readonly ListDetail _split;
    private readonly ListBox _rows;

    public AgentActivityView()
    {
        AvaloniaXamlLoader.Load(this);

        _split = this.FindControl<ListDetail>("Split")!;
        _split.PropertyChanged += OnSplitChanged;

        _rows = this.FindControl<ListBox>("AgentRows")!;
        _rows.AddHandler(TappedEvent, OnRowTapped, RoutingStrategies.Bubble);
        _rows.AddHandler(KeyDownEvent, OnRowKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>Where the detail takes the list's place, the keyboard goes with it, and back to the list when it returns.</summary>
    private void OnSplitChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ListDetail.ShowsBackProperty)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (TopLevel.GetTopLevel(this) is Window { IsVisible: true })
                {
                    Control? next = _split.ShowsBack ? this.FindControl<Button>("BackToList") : _rows;
                    next?.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>A row already chosen does not change the selection, so pressing it opens its detail here.</summary>
    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is AgentRow row)
        {
            Model?.Open(row);
        }
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && _rows.SelectedItem is AgentRow row)
        {
            Model?.Open(row);
            e.Handled = true;
        }
    }

    private AgentActivityViewModel? Model => DataContext as AgentActivityViewModel;
}
