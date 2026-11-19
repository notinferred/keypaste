using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Keypaste.App.Tests.Rendering;

/// <summary>A button's menu, which is drawn in a window of its own beside the one that holds the button.</summary>
internal static class Menus
{
    /// <summary>Opens the button's menu as pressing it does, and gives back the menu and the window it is drawn in.</summary>
    internal static (MenuFlyout Menu, TopLevel Drawn) Open(Button button)
    {
        var menu = (MenuFlyout)button.Flyout!;
        menu.ShowAt(button);
        WindowInput.Drain();
        return (menu, Drawn(menu));
    }

    /// <summary>The open menu's rows as drawn, whether written out or made from a list.</summary>
    internal static IEnumerable<MenuItem> Rows(MenuFlyout menu) => Drawn(menu).GetVisualDescendants().OfType<MenuItem>();

    internal static MenuItem Item(MenuFlyout menu, string name) => menu.Items.OfType<MenuItem>().Single(item => item.Name == name);

    /// <summary>Chooses a row as a click does, which closes the menu.</summary>
    internal static void Choose(MenuFlyout menu, MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide();
        WindowInput.Drain();
    }

    // Avalonia keeps a flyout's popup to itself; the rows a list makes are reached only through it.
    private static TopLevel Drawn(MenuFlyout menu) =>
        TopLevel.GetTopLevel(((Popup)typeof(PopupFlyoutBase).GetProperty("Popup", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(menu)!).Child)!;
}
