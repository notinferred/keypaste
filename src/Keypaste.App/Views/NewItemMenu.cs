using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Keypaste.App.Controls;
using Keypaste.App.Resources;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>New's menu, made as it opens: the vault it adds to, the common templates and the way to every template.</summary>
internal static class NewItemMenu
{
    public static readonly AttachedProperty<bool> OffersTemplatesProperty =
        AvaloniaProperty.RegisterAttached<MenuFlyout, bool>("OffersTemplates", typeof(NewItemMenu));

    static NewItemMenu() =>
        OffersTemplatesProperty.Changed.AddClassHandler<MenuFlyout>((menu, change) =>
        {
            if (change.NewValue is true)
            {
                menu.Opening += (_, _) => Fill(menu);
            }
        });

    public static bool GetOffersTemplates(MenuFlyout menu) => menu.GetValue(OffersTemplatesProperty);

    public static void SetOffersTemplates(MenuFlyout menu, bool value) => menu.SetValue(OffersTemplatesProperty, value);

    private static void Fill(MenuFlyout menu)
    {
        menu.Items.Clear();

        if (menu.Target?.DataContext is not ShellViewModel shell)
        {
            return;
        }

        menu.Items.Add(new MenuItem { Header = shell.NewItemHeading, IsEnabled = false });

        foreach (var kind in TemplateChoice.Common)
        {
            var item = new MenuItem
            {
                Header = kind.MenuLabel,
                Icon = new KpIcon { Icon = kind.Icon, Size = 14 },
                Command = shell.NewItemOfKindCommand,
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
            Command = shell.NewItemCommand,
        });
    }
}
