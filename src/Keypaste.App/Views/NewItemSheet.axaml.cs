using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>New item's sheet over the window, whose Add field… menu opens and closes as the list's menus do.</summary>
internal sealed partial class NewItemSheet : UserControl
{
    public NewItemSheet()
    {
        AvaloniaXamlLoader.Load(this);
        ToggleMenus.Attach(this, ("AddFieldMenu", "AddFieldMenuPanel"));
    }
}
