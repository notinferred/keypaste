using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>New item is a sheet over the whole window: the grid of templates, whose tile opens its form, and the form's rows, custom fields and Save.</summary>
public sealed class NewItemSheetTests
{
    [Fact]
    public Task The_sidebars_New_and_the_shifted_chord_open_the_grid_and_a_tile_opens_its_form_in_the_same_sheet() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-new-item-grid-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var file = shell.Commands.Menus.Single(group => group.Header == Strings.FileMenu).Entries;
        var fromTemplate = file.OfType<AppCommand>().Single(command => command.Header == Strings.NewItemFromTemplateLabel);

        Assert.Equal(new KeyGesture(Key.N, (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift), fromTemplate.Keys[0]);

        foreach (var open in new Action[] { () => shell.Shell.NewItemCommand.Execute(null), () => shell.Commands.ForChord(fromTemplate.Keys[0].Key, fromTemplate.Keys[0].KeyModifiers)!.Run() })
        {
            open();
            RenderedShell.Drain();

            var grid = shell.Named<Border>("TemplateSheet");
            Assert.True(grid.IsEffectivelyVisible);
            Assert.False(shell.Named<Border>("NewItemDialog").IsEffectivelyVisible);
            var tiles = shell.Named<ItemsControl>("TemplateTiles").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(TemplateChoice.All.Select(choice => choice.Label), tiles.Select(Avalonia.Automation.AutomationProperties.GetName));
            Assert.Same(tiles[0], shell.Window.FocusManager?.GetFocusedElement());
            Assert.Equal(26, tiles[0].GetVisualDescendants().OfType<KpIcon>().Single().Size);

            var backdrop = grid.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("backdrop"));
            Assert.Equal(shell.Window.GetVisualDescendants().OfType<ShellView>().Single().Bounds.Size, backdrop.Bounds.Size);

            shell.Tap(tiles.Single(tile => Equals(tile.CommandParameter, ItemTemplate.ApiKey)));
            RenderedShell.Drain();

            Assert.False(grid.IsEffectivelyVisible);
            Assert.True(shell.Named<Border>("NewItemDialog").IsEffectivelyVisible);
            Assert.Equal(ItemTemplate.ApiKey, items.NewItem!.Template);
            Assert.True(shell.Named<TextBox>("NewItemKeyName").IsEffectivelyVisible);

            shell.Tap(shell.Named<Button>("CancelNewItem"));
            RenderedShell.Drain();
            Assert.Null(items.NewItem);
        }

        shell.Shell.NewItemCommand.Execute(null);
        RenderedShell.Drain();
        shell.Tap(shell.Named<Button>("CancelTemplate"));
        RenderedShell.Drain();
        Assert.False(items.IsAdding);
    });

    [Fact]
    public Task Every_direct_way_to_a_form_opens_it_over_the_window_with_its_template() => HeadlessSession.On(() =>
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
                var (menu, _) = shell.OpenMenu("SidebarNew");
                Menus.Choose(menu, menu.Items.OfType<MenuItem>().Single(item => Equals(item.CommandParameter, ItemTemplate.Card)));
            }, ItemTemplate.Card),
            (newItem.Run, ItemTemplate.Login),
            (() => shell.Commands.ForChord(newItem.Keys[0].Key, newItem.Keys[0].KeyModifiers)!.Run(), ItemTemplate.Login),
            (byKind[^1].Run, TemplateChoice.All[^1].Template),
        ];

        foreach (var (open, kind) in ways)
        {
            open();
            RenderedShell.Drain();

            var sheet = shell.Named<Border>("NewItemDialog");
            Assert.True(sheet.IsEffectivelyVisible);
            Assert.Equal(kind, items.NewItem!.Template);
            Assert.Equal(TemplateChoice.Of(kind).NewTitle, shell.Named<TextBox>("NewItemTitle").Text);
            Assert.Null(shell.Shell.GeneratorDialog);

            items.CancelAddCommand.Execute(null);
            RenderedShell.Drain();
            Assert.False(sheet.IsEffectivelyVisible);
        }
    });

    [Fact]
    public Task A_login_form_has_its_rows_custom_fields_with_add_field_and_the_browser_footnote() => HeadlessSession.On(() =>
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
        Assert.Same(shell.Named<TextBox>("NewItemTitle"), shell.Window.FocusManager?.GetFocusedElement());
        Assert.Equal(Strings.Save, shell.Named<Button>("CreateItem").Content);
        Assert.True(shell.Named<StackPanel>("FillsInBrowserNote").IsEffectivelyVisible);
        Assert.False(shell.Named<ComboBox>("NewItemSaveIn").IsEffectivelyVisible, "Save in shows with one vault open");

        var addField = shell.Named<Button>("AddFieldMenu");
        Assert.Equal(Strings.AddAField, Avalonia.Automation.AutomationProperties.GetName(addField));
        var (menu, _) = shell.OpenMenu("AddFieldMenu");
        Menus.Choose(menu, Menus.Rows(menu).Single(item => Equals(item.CommandParameter, NewItemField.CustomField)));

        Assert.False(menu.IsOpen);
        Assert.Single(items.NewItem!.CustomFields);
        Assert.True(shell.Named<ItemsControl>("NewItemCustomFields").IsEffectivelyVisible);

        items.NewItem.RemoveFieldCommand.Execute(NewItemField.Url);
        RenderedShell.Drain();
        Assert.False(shell.Named<StackPanel>("FillsInBrowserNote").IsEffectivelyVisible);
    });

    [Fact]
    public Task A_rows_label_is_at_its_left_and_goes_above_while_the_form_is_too_narrow_for_both() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-new-item-labels-");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.BeginAddCommand.Execute(null);
        RenderedShell.Drain();

        var field = shell.Named<UsernameBox>("NewEntryUsername");
        var row = field.FindAncestorOfType<FieldRow>()!;
        var label = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "Label");

        Assert.True(Left(label, shell) + label.Bounds.Width <= Left(field, shell), "the label stands left of its field");
        Assert.True(Math.Abs(Top(label, shell) - Top(field, shell)) < field.Bounds.Height, "the label is on its field's line");
        Assert.Empty(LabelledFields.Problems(shell.Window));

        shell.Named<Border>("NewItemDialog").Width = FieldRow.NarrowBelow;
        RenderedShell.Drain();

        Assert.True(Top(label, shell) + label.Bounds.Height <= Top(field, shell), "a narrow row puts the label above its field");
        Assert.Empty(LabelledFields.Problems(shell.Window));
    });

    private static double Left(Visual control, RenderedShell shell) => control.TranslatePoint(default, shell.Window)!.Value.X;

    private static double Top(Visual control, RenderedShell shell) => control.TranslatePoint(default, shell.Window)!.Value.Y;

    [Fact]
    public Task Save_in_shows_at_the_foot_only_while_another_vault_is_open() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-new-item-save-in-", secondVault: "acme");
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.BeginAddCommand.Execute(null);
        RenderedShell.Drain();

        var saveIn = shell.Named<ComboBox>("NewItemSaveIn");
        Assert.True(saveIn.IsEffectivelyVisible);
        Assert.True(
            saveIn.TranslatePoint(default, shell.Window)!.Value.Y > shell.Named<ScrollViewer>("NewItemRows").TranslatePoint(default, shell.Window)!.Value.Y,
            "Save in is not under the rows");
    });
}
