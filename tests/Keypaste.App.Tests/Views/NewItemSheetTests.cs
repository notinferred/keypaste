using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>New item is a sheet over the whole window, whichever way it is opened, holding the kind, the title and the open item's rows, with Add field… at its foot.</summary>
public sealed class NewItemSheetTests
{
    [Fact]
    public Task Every_way_to_new_item_opens_the_sheet_over_the_window_with_its_kind_chosen() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-new-item-sheet-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries;
        var newItem = file.OfType<AppCommand>().Single(command => command.Header == Strings.NewItemLabel);
        var byKind = file.OfType<AppSubmenu>().Single(submenu => submenu.Header == Strings.NewItemByType).Entries().OfType<AppCommand>().ToList();

        (Action Open, ItemTemplate Kind)[] ways =
        [
            (() =>
            {
                shell.Named<ToggleButton>("AddEntry").IsChecked = true;
                RenderedShell.Drain();
                shell.Tap(shell.Named<Border>("AddMenuPanel").GetVisualDescendants().OfType<Button>().Single(button => Equals(button.CommandParameter, ItemTemplate.Database)));
            }, ItemTemplate.Database),
            (newItem.Run, ItemTemplate.Login),
            (() => shell.Commands.ForChord(newItem.Keys[0].Key, newItem.Keys[0].KeyModifiers)!.Run(), ItemTemplate.Login),
            (byKind[^1].Run, TemplateChoice.All[^1].Template),
            (() =>
            {
                shell.Shell.OpenGeneratorCommand.Execute(null);
                RenderedShell.Drain();
                shell.Tap(shell.Named<Button>("GeneratedNewItem"));
            }, ItemTemplate.Login),
        ];

        foreach (var (open, kind) in ways)
        {
            open();
            RenderedShell.Drain();

            var sheet = shell.Named<Border>("NewItemDialog");
            Assert.True(sheet.IsEffectivelyVisible);
            Assert.Equal(kind, items.NewItem!.Template);
            Assert.Equal(kind, Assert.IsType<TemplateChoice>(shell.Named<ListBox>("NewItemTemplate").SelectedItem).Template);
            Assert.Null(shell.Shell.GeneratorDialog);

            // The backdrop takes the whole window, the sidebar included, as every other sheet's does.
            var backdrop = sheet.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("backdrop"));
            Assert.Equal(shell.Window.GetVisualDescendants().OfType<ShellView>().Single().Bounds.Size, backdrop.Bounds.Size);

            items.CancelAddCommand.Execute(null);
            RenderedShell.Drain();
            Assert.False(sheet.IsEffectivelyVisible);
        }
    });

    [Fact]
    public Task The_sheets_rows_are_as_tall_as_the_open_items_and_add_field_sits_at_its_foot() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-new-item-rows-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Selected = items.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();
        items.Detail!.EditCommand.Execute(null);
        RenderedShell.Drain();
        var editing = shell.Named<UsernameBox>("DraftUsername").Bounds.Height;
        items.Detail.CancelCommand.Execute(null);
        RenderedShell.Drain();

        items.BeginAddCommand.Execute(null);
        RenderedShell.Drain();

        Assert.Equal(editing, shell.Named<UsernameBox>("NewEntryUsername").Bounds.Height);
        var addField = shell.Named<ToggleButton>("AddFieldMenu");
        var create = shell.Named<Button>("CreateItem");
        Assert.Equal(Strings.AddAField, Avalonia.Automation.AutomationProperties.GetName(addField));
        Assert.True(Math.Abs(Centre(addField, shell) - Centre(create, shell)) < 4, "Add field… and Create share the sheet's foot");

        addField.IsChecked = true;
        RenderedShell.Drain();
        var menu = shell.Named<Border>("AddFieldMenuPanel");
        Assert.True(menu.IsEffectivelyVisible);
        var option = items.NewItem!.FieldOptions[0].Field;
        shell.Tap(menu.GetVisualDescendants().OfType<Button>().First(button => Equals(button.CommandParameter, option)));
        RenderedShell.Drain();

        Assert.False(addField.IsChecked);
        Assert.DoesNotContain(items.NewItem.FieldOptions, offered => offered.Field == option);
    });

    private static double Centre(Visual control, RenderedShell shell) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height / 2), shell.Window)!.Value.Y;
}
