using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The item list's add opens New item by kind, through New item's own kind choice, with the generator below.</summary>
public sealed class ItemAddMenuTests
{
    [Fact]
    public Task The_item_add_lists_each_kind_then_the_generator() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var panel = Open(shell);

        Assert.Equal(
            TemplateChoice.All.Select(kind => kind.MenuLabel).Append(Strings.GeneratePasswordXf22f07),
            panel.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.GetVisualDescendants().OfType<TextBlock>().Single().Text));
    });

    [Fact]
    public Task Choosing_each_kind_opens_new_item_with_that_kind_chosen() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-kind-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        foreach (var kind in TemplateChoice.All)
        {
            shell.Tap(Open(shell).GetVisualDescendants().OfType<Button>().Single(entry => Equals(entry.CommandParameter, kind.Template)));
            RenderedShell.Drain();

            Assert.True(items.IsAdding);
            Assert.Equal(kind.Template, items.NewItem!.Template);
            Assert.Same(kind, shell.Named<ListBox>("NewItemTemplate").SelectedItem);
            Assert.False(shell.Named<ToggleButton>("AddEntry").IsChecked);

            items.CancelAddCommand.Execute(null);
            RenderedShell.Drain();
        }
    });

    [Fact]
    public Task The_file_menu_reaches_each_kind_and_the_chord_still_opens_new_item() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-menu-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries;
        var kinds = file.OfType<AppSubmenu>().Single(submenu => submenu.Header == Strings.NewItemByType).Entries().OfType<AppCommand>().ToList();

        Assert.Equal(TemplateChoice.All.Select(kind => kind.MenuLabel), kinds.Select(command => command.Header));

        foreach (var (command, kind) in kinds.Zip(TemplateChoice.All))
        {
            command.Run();
            RenderedShell.Drain();
            Assert.Equal(kind.Template, items.NewItem!.Template);
            items.CancelAddCommand.Execute(null);
        }

        var newItem = file.OfType<AppCommand>().Single(command => command.Header == Strings.NewItemLabel);
        Assert.Same(newItem, shell.Commands.ForChord(newItem.Keys[0].Key, newItem.Keys[0].KeyModifiers));
        newItem.Run();
        Assert.Equal(ItemTemplate.Login, items.NewItem!.Template);
    });

    private static Border Open(RenderedShell shell)
    {
        shell.Named<ToggleButton>("AddEntry").IsChecked = true;
        RenderedShell.Drain();
        var panel = shell.Named<Border>("AddMenuPanel");
        Assert.True(panel.IsEffectivelyVisible);
        return panel;
    }
}
