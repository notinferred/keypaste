using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The "+" beside the list's title makes an item where the list is: its place's kind at once, or a choice where the place holds every kind.</summary>
public sealed class ItemAddMenuTests
{
    [Fact]
    public Task Where_every_kind_is_listed_the_plus_names_the_vault_and_lists_the_common_templates_then_more() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        Assert.False(shell.Named<Button>("AddInPlace").IsEffectivelyVisible);
        var panel = Open(shell);

        Assert.Equal($"New in {items.VaultTitle}", panel.GetVisualDescendants().OfType<TextBlock>().First().Text);
        Assert.Equal(
            TemplateChoice.Common.Select(kind => kind.MenuLabel).Append(Strings.MoreTemplatesLabel),
            panel.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.GetVisualDescendants().OfType<TextBlock>().Single().Text));

        shell.Tap(shell.Named<Button>("MoreTemplates"));
        RenderedShell.Drain();

        Assert.True(shell.Named<Border>("TemplateSheet").IsEffectivelyVisible);
        Assert.False(shell.Named<ToggleButton>("AddEntry").IsChecked);
    });

    [Fact]
    public Task Choosing_each_common_template_opens_its_form() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-kind-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        foreach (var kind in TemplateChoice.Common)
        {
            shell.Tap(Open(shell).GetVisualDescendants().OfType<Button>().Single(entry => Equals(entry.CommandParameter, kind.Template)));
            RenderedShell.Drain();

            Assert.True(items.IsAdding);
            Assert.Equal(kind.Template, items.NewItem!.Template);
            Assert.True(shell.Named<Border>("NewItemDialog").IsEffectivelyVisible);
            Assert.False(shell.Named<ToggleButton>("AddEntry").IsChecked);

            items.CancelAddCommand.Execute(null);
            RenderedShell.Drain();
        }
    });

    [Fact]
    public Task In_a_place_of_one_kind_the_plus_and_the_chord_open_its_form_in_the_vault_in_view() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-item-add-place-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Place = ItemPlace.ApiKeys;
        RenderedShell.Drain();

        var plus = shell.Named<Button>("AddInPlace");
        Assert.True(plus.IsEffectivelyVisible);
        Assert.False(shell.Named<ToggleButton>("AddEntry").IsEffectivelyVisible);
        Assert.Equal($"New API key in {items.VaultTitle}", ToolTip.GetTip(plus));
        Assert.Equal($"New API key in {items.VaultTitle}", AutomationProperties.GetName(plus));

        shell.Tap(plus);
        RenderedShell.Drain();
        Assert.Equal(ItemTemplate.ApiKey, items.NewItem!.Template);
        Assert.Same(items.NewItem.Places[0], items.NewItem.SaveIn);
        items.CancelAddCommand.Execute(null);

        var newItem = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries.OfType<AppCommand>().Single(command => command.Header == Strings.NewItemLabel);
        shell.Commands.ForChord(newItem.Keys[0].Key, newItem.Keys[0].KeyModifiers)!.Run();
        Assert.Equal(ItemTemplate.ApiKey, items.NewItem!.Template);
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

    private static Border Open(RenderedShell shell)
    {
        shell.Named<ToggleButton>("AddEntry").IsChecked = true;
        RenderedShell.Drain();
        var panel = shell.Named<Border>("AddMenuPanel");
        Assert.True(panel.IsEffectivelyVisible);
        return panel;
    }
}
