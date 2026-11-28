using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Keypaste.App.Resources;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// Quick search's window: a card at the interface's zoom with the search holding the keyboard. The keys act wherever focus is
/// in it, except Return and Space on a button, which press that button; it closes once another window takes focus.
/// </summary>
internal sealed partial class QuickSearchWindow : Window
{
    /// <summary>The window's title, the same whatever it shows, since system switchers show it.</summary>
    internal static readonly string FixedTitle = $"{Strings.QuickSearch} - {ShellViewModel.BareWindowTitle}";

    private readonly ListBox _results;
    private readonly TextBox _field;

    public QuickSearchWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Title = FixedTitle;
        _results = this.FindControl<ListBox>("QuickSearchResults")!;
        _field = this.FindControl<TextBox>("QuickSearchField")!;

        // A plain parent, because the transform turns about its child's own corner and would leave its margin, the shadow band, unscaled.
        if (Content is Control card)
        {
            Content = null;
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(InterfaceZoom.Factor, InterfaceZoom.Factor),
                Child = new Panel { Children = { card } },
            };
        }

        Opened += (_, _) =>
        {
            Classes.Set("opaque", ActualTransparencyLevel == WindowTransparencyLevel.None);
            _field.Focus();
        };

        // Another window taking focus is the person moving on, as from Spotlight.
        Deactivated += (_, _) => Close();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    internal QuickSearchWindow(QuickSearchViewModel model)
        : this() => DataContext = model;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickSearchViewModel model)
        {
            return;
        }

        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var onButton = e.Source is Button;

        switch (e.Key)
        {
            case Key.Down or Key.Up when e.KeyModifiers == KeyModifiers.None:
                model.MoveChoice(e.Key == Key.Down ? 1 : -1);
                ShowChosenRow(model);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && !onButton:
                _ = model.CopyPasswordAsync();
                break;
            case Key.Enter when e.KeyModifiers == command:
                model.OpenInWindow();
                break;
            case Key.C when e.KeyModifiers == (command | KeyModifiers.Shift):
                _ = model.CopyUsernameAsync();
                break;
            case Key.T when e.KeyModifiers == command:
                _ = model.CopyOneTimeCodeAsync();
                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                model.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void ShowChosenRow(QuickSearchViewModel model)
    {
        if (model.Selected is { } chosen && _results.Items.IndexOf(chosen) is >= 0 and var index)
        {
            _results.ScrollIntoView(index);
        }
    }
}
