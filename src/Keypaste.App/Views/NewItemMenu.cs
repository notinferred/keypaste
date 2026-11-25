using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Keypaste.App.Controls;
using Keypaste.App.Resources;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>New's menu: the vault it adds to, the common templates and the way to every template, pointed at the shell as it opens.</summary>
internal static class NewItemMenu
{
    public static readonly AttachedProperty<bool> OffersTemplatesProperty =
        AvaloniaProperty.RegisterAttached<MenuFlyout, bool>("OffersTemplates", typeof(NewItemMenu));

    static NewItemMenu() =>
        OffersTemplatesProperty.Changed.AddClassHandler<MenuFlyout>((menu, change) =>
        {
            if (change.NewValue is true)
            {
                Build(menu);
                menu.Opening += (_, _) => Point(menu);
            }
        });

    public static bool GetOffersTemplates(MenuFlyout menu) => menu.GetValue(OffersTemplatesProperty);

    public static void SetOffersTemplates(MenuFlyout menu, bool value) => menu.SetValue(OffersTemplatesProperty, value);

    // Made before the menu first opens, as the app's other menus are: rows added while a flyout opens are never drawn.
    private static void Build(MenuFlyout menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Name = "NewItemHeading", IsEnabled = false });

        foreach (var kind in TemplateChoice.Common)
        {
            var item = new MenuItem
            {
                Header = kind.MenuLabel,
                Icon = new KpIcon { Icon = kind.Icon, Size = 14 },
                CommandParameter = kind.Template,
            };
            AutomationProperties.SetName(item, kind.Label);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Name = "MoreTemplates",
            Header = Strings.MoreTemplatesLabel,
            Icon = new KpIcon { Icon = "ellipsis", Size = 14 },
        });
    }

    /// <summary>Points the rows at the shell the menu opens over: its vault's heading and its commands.</summary>
    private static void Point(MenuFlyout menu)
    {
        if (menu.Target?.DataContext is not ShellViewModel shell)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Name)
            {
                case "NewItemHeading":
                    item.Header = shell.NewItemHeading;
                    break;
                case "MoreTemplates":
                    item.Command = shell.NewItemCommand;
                    break;
                default:
                    item.Command = shell.NewItemOfKindCommand;
                    break;
            }
        }
    }
}
