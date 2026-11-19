using Avalonia.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The sidebar's New is the one add control: a menu naming the vault, the common templates and the way to every template; ⌘N opens the place's kind.</summary>
public sealed class ItemAddMenuTests
{
    [Fact]
    public Task New_names_the_vault_and_lists_the_common_templates_then_more() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        var (menu, _) = shell.OpenMenu("SidebarNew");
        var rows = menu.Items.OfType<MenuItem>().ToList();

        Assert.Equal($"New in {items.VaultTitle}", rows[0].Header);
        Assert.False(rows[0].IsEnabled);
        Assert.Equal(TemplateChoice.Common.Select(kind => kind.MenuLabel).Append(Strings.MoreTemplatesLabel), rows.Skip(1).Select(row => row.Header));

        Menus.Choose(menu, Menus.Item(menu, "MoreTemplates"));

        Assert.True(shell.Named<Border>("TemplateSheet").IsEffectivelyVisible);
        Assert.False(menu.IsOpen);
    });

    [Fact]
    public Task Choosing_each_common_template_opens_its_form() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-kind-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        foreach (var kind in TemplateChoice.Common)
        {
            var (menu, _) = shell.OpenMenu("SidebarNew");
            Menus.Choose(menu, menu.Items.OfType<MenuItem>().Single(entry => Equals(entry.CommandParameter, kind.Template)));

            Assert.True(items.IsAdding);
            Assert.Equal(kind.Template, items.NewItem!.Template);
            Assert.True(shell.Named<Border>("NewItemDialog").IsEffectivelyVisible);
            Assert.False(menu.IsOpen);

            items.CancelAddCommand.Execute(null);
            RenderedShell.Drain();
        }
    });

    [Fact]
    public Task In_a_place_of_one_kind_the_chord_opens_its_form_in_the_vault_in_view() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-place-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Place = ItemPlace.ApiKeys;
        RenderedShell.Drain();

        var newItem = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries.OfType<AppCommand>().Single(command => command.Header == Strings.NewItemLabel);
        shell.Commands.ForChord(newItem.Keys[0].Key, newItem.Keys[0].KeyModifiers)!.Run();
        Assert.Equal(ItemTemplate.ApiKey, items.NewItem!.Template);
        Assert.Same(items.NewItem.Places[0], items.NewItem.SaveIn);
    });

    [Fact]
    public Task The_file_menu_reaches_every_template_and_change_kind_every_kind_kept() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-menu-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries;
        var kinds = file.OfType<AppSubmenu>().Single(submenu => submenu.Header == Strings.NewItemByType).Entries().OfType<AppCommand>().ToList();

        Assert.Equal(TemplateChoice.All.Select(kind => kind.MenuLabel), kinds.Select(command => command.Header));
        Assert.DoesNotContain(TemplateChoice.Kept, kind => kind.Template == ItemTemplate.Blank);

        foreach (var (command, kind) in kinds.Zip(TemplateChoice.All))
        {
            command.Run();
            RenderedShell.Drain();
            Assert.Equal(kind.Template, items.NewItem!.Template);
            items.CancelAddCommand.Execute(null);
        }
    });
}
