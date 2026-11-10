using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>What a screen no longer says twice (Q.19 stage 2), each drawn in the real shell.</summary>
public sealed class EachFactOnceTests
{
    private const string _github = "github";

    private static IEnumerable<T> Shown<T>(RenderedShell shell)
        where T : Control =>
        shell.Window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);

    private static Button FieldMenu(RenderedShell shell, string field) =>
        Shown<Button>(shell).Single(button => AutomationProperties.GetName(button) == $"More actions for {field}");

    private static EntriesViewModel OpenGithub(RenderedShell shell)
    {
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == _github);
        RenderedShell.Drain();
        return entries;
    }

    private static MenuFlyout PressMenu(Button menuButton)
    {
        menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RenderedShell.Drain();
        var menu = Assert.IsType<MenuFlyout>(FlyoutBase.GetAttachedFlyout(menuButton));
        Assert.True(menu.IsOpen);
        return menu;
    }

    private static List<string> Headers(MenuFlyout menu) =>
        [.. menu.Items.Select(item => item is MenuItem { Header: string header } ? header : "-")];

    private static void Choose(MenuFlyout menu, string header)
    {
        menu.Items.OfType<MenuItem>().Single(item => item.Header as string == header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide();
        RenderedShell.Drain();
    }

    /// <summary>A field keeps Reveal and Copy on its row; the rest is its ⋯ menu, on while each command can run.</summary>
    [Fact]
    public Task A_fields_row_keeps_Reveal_and_Copy_and_its_menu_holds_the_rest() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-field-row-");
        OpenGithub(shell);

        var row = FieldMenu(shell, "API_TOKEN").GetVisualAncestors().OfType<StackPanel>().First();
        Assert.Equal(
            ["Reveal API_TOKEN", "More actions for API_TOKEN"],
            row.GetVisualChildren().OfType<Control>().Where(control => control.IsVisible).Select(AutomationProperties.GetName));
        Assert.Equal("Copy API_TOKEN", AutomationProperties.GetName(FieldMenu(shell, "API_TOKEN").GetVisualAncestors().OfType<FieldRow>().First().GetVisualDescendants().OfType<PressToCopy>().Single()));

        var menu = PressMenu(FieldMenu(shell, "API_TOKEN"));
        Assert.Equal(["Copy value", "Replace value…", "Generate value…", "Rename field…", "Stop protecting field", "-", "Remove field"], Headers(menu));
        Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.True(item.IsEffectivelyEnabled, $"{item.Header} is off"));
        menu.Hide();
        RenderedShell.Drain();

        menu = PressMenu(FieldMenu(shell, "Region"));
        Assert.Equal(["Copy value", "Replace value…", "Generate value…", "Rename field…", "Protect field", "-", "Remove field"], Headers(menu));
        Assert.False(menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Generate value…").IsEffectivelyEnabled, "a plain field offers Generate");
        menu.Hide();
    });

    [Fact]
    public Task A_fields_menu_acts_on_its_row_and_takes_the_keyboard_to_its_form() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-field-act-");
        var entries = OpenGithub(shell);
        var detail = entries.Detail!;

        Choose(PressMenu(FieldMenu(shell, "Region")), "Replace value…");
        Assert.True(detail.IsReplacingField);
        Assert.True(shell.Named<MaskedInput>("ReplacementFieldValue").IsKeyboardFocusWithin, "the form did not take the keyboard");
        detail.CancelReplaceFieldCommand.Press();
        RenderedShell.Drain();

        Choose(PressMenu(FieldMenu(shell, "Region")), "Protect field");
        Assert.True(entries.Detail!.Fields.Single(field => field.Name == "Region").IsProtected);

        Choose(PressMenu(FieldMenu(shell, "API_TOKEN")), "Remove field");
        Assert.True(entries.Detail!.IsRemovingField);
        Assert.True(shell.Named<Button>("KeepField").IsFocused, "Cancel does not hold the keyboard over a removal");
    });

    /// <summary>The Item menu holds the same commands, on while the keyboard is in a field's row and off elsewhere.</summary>
    [Fact]
    public Task The_Item_menu_acts_on_the_field_holding_the_keyboard() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-field-bar-");
        var entries = OpenGithub(shell);
        var field = shell.Commands.Menus.Single(menu => menu.Header == "Item").Entries.OfType<AppSubmenu>().Single(submenu => submenu.Header == "Field");
        AppCommand Command(string header) => field.Entries().OfType<AppCommand>().Single(command => command.Header == header);

        Assert.False(Command("Replace value…").CanRun(), "Replace is on with no field in hand");

        FieldMenu(shell, "Region").Focus(NavigationMethod.Tab);
        RenderedShell.Drain();

        Assert.True(Command("Replace value…").CanRun());
        Assert.False(Command("Generate value…").CanRun());
        Assert.Equal("Protect field", Command("Protect field").HeaderNow);

        Command("Rename field…").Run();
        RenderedShell.Drain();
        Assert.True(entries.Detail!.IsRenamingField);
    });

    /// <summary>Whichever menu opens, nothing stays revealed under it; and neither the menu nor its button carries a value.</summary>
    [Fact]
    public Task Opening_a_row_or_pane_menu_hides_a_value_and_no_name_holds_one() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-field-hide-");
        OpenGithub(shell);
        var value = Shown<RevealedValue>(shell).First(cell => AutomationProperties.GetName(cell) == "API_TOKEN");

        value.BeginReveal();
        Assert.True(value.IsRevealed);
        var menu = PressMenu(FieldMenu(shell, "API_TOKEN"));
        Assert.False(value.IsRevealed, "a value stayed shown under the row's menu");

        foreach (var header in Headers(menu))
        {
            Assert.DoesNotContain(RenderedShell.FieldValue, header, StringComparison.Ordinal);
        }

        menu.Hide();
        RenderedShell.Drain();

        value.BeginReveal();
        Assert.True(value.IsRevealed);
        shell.Named<ToggleButton>("EntryMenu").IsChecked = true;
        RenderedShell.Drain();
        Assert.False(value.IsRevealed, "a value stayed shown under the pane's menu");
        shell.Named<ToggleButton>("EntryMenu").IsChecked = false;
        RenderedShell.Drain();

        AutomationSurface.AssertNothingExposes(shell.Window, RenderedShell.FieldValue);
    });

    /// <summary>An item's empty rows are not drawn, nor a Copy or Reveal of what it does not hold, and Edit fields… still adds them.</summary>
    [Fact]
    public Task An_items_empty_rows_are_not_drawn() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(
            "keypaste-empty-rows-",
            seed: vault =>
            {
                vault.AddEntry(new VaultEntry { Title = "bare" });
                vault.SetFields(new EntryName(string.Empty, "bare"), [new FieldWrite("KEY", RenderedShell.FieldValue)]);
            });
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "bare");
        RenderedShell.Drain();

        var labels = Shown<TextBlock>(shell).Where(block => block.Classes.Contains("label")).Select(block => block.Text).ToList();
        Assert.DoesNotContain("Username", labels);
        Assert.DoesNotContain("Password", labels);
        Assert.DoesNotContain("URL", labels);
        Assert.DoesNotContain(Shown<RevealedValue>(shell), value => value.Name == "CurrentPassword");
        Assert.False(shell.Named<Button>("CopyPassword").IsEffectivelyVisible);
        Assert.DoesNotContain(Shown<Control>(shell), control => AutomationProperties.GetName(control) == "Reveal the password");

        entries.Detail!.EditCommand.Press();
        RenderedShell.Drain();
        Assert.True(entries.Detail!.IsEditing);
    });

    [Fact]
    public Task The_pane_header_says_what_its_Copy_copies() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-copy-password-");
        OpenGithub(shell);

        Assert.Contains(
            shell.Named<Button>("CopyPassword").GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text == "Copy password");
    });

    /// <summary>The header names a chosen group, so sorting by it goes, and the pane names the subgroup a row is in instead.</summary>
    [Fact]
    public Task The_Group_column_gives_way_to_the_header_naming_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(
            "keypaste-group-column-",
            seed: vault =>
            {
                vault.AddEntry(new VaultEntry { GroupPath = "work", Title = "direct" });
                vault.AddEntry(new VaultEntry { GroupPath = "work/deep", Title = "nested" });
            });
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        Assert.Contains(entries.SortChoices, choice => choice.Column == TableStates.Group);
        entries.Table.Press(TableStates.Group);
        Assert.Equal(TableStates.Group, entries.Table.Order?.Column);

        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "work");
        RenderedShell.Drain();

        Assert.DoesNotContain(entries.SortChoices, choice => choice.Column == TableStates.Group);
        Assert.NotEqual(TableStates.Group, entries.Table.Order?.Column);

        entries.Selected = entries.Rows.Single(row => row.Title == "direct");
        Assert.False(entries.PaneNamesGroup, "the pane repeats the header");
        entries.Selected = entries.Rows.Single(row => row.Title == "nested");
        Assert.True(entries.PaneNamesGroup, "nothing names the subgroup");

        entries.ShowEveryItem();
        RenderedShell.Drain();

        Assert.Contains(entries.SortChoices, choice => choice.Column == TableStates.Group);
        Assert.Equal(TableStates.Group, entries.Table.Order?.Column);
    });

    /// <summary>A row says its kind by its icon, named for a tooltip and a screen reader, and has no Kind column.</summary>
    [Fact]
    public Task A_row_says_its_kind_once() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-kind-once-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        Assert.DoesNotContain(TableStates.ItemColumns, column => column.Key == "kind");
        var row = shell.Named<ListBox>("Rows").ContainerFromIndex(0)!;
        var mark = row.GetVisualDescendants().OfType<Panel>().First(panel => panel.Name == "KindMark");
        Assert.Equal(entries.Rows[0].KindLabel, AutomationProperties.GetName(mark));
        Assert.Equal(entries.Rows[0].KindLabel, ToolTip.GetTip(mark));

        // The tip answers anywhere in the mark, not only on the icon's strokes.
        var corner = mark.TranslatePoint(new Point(1, 1), shell.Window)!.Value;
        Assert.Contains(mark, shell.Window.GetInputElementsAt(corner));
    });

    /// <summary>Settings opens on settings: one line, gone when nothing was found, and the sidebar's count stays what it says.</summary>
    [Fact]
    public Task Settings_says_recommendations_in_one_line_opening_their_screen_with_a_Back() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell(
            "keypaste-recommendations-line-",
            seed: vault => vault.AddEntry(new VaultEntry { GroupPath = "services", Title = "Stripe", Password = "correct-horse-battery-staple-9", Notes = "STRIPE_SECRET_KEY=abc" }));
        await shell.Shell.Recommendations.Checking.ConfigureAwait(true);
        RenderedShell.Drain();
        var recommendations = shell.Shell.Recommendations;
        var count = recommendations.NeedsReview;
        Assert.True(count > 0);

        Assert.DoesNotContain(Shown<TextBlock>(shell), block => block.Text == "STRIPE_SECRET_KEY");
        Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), shell.Shell.RecommendationsRow.Count);
        Assert.Contains(shell.Shell.RecommendationsRow, shell.Shell.SidebarRows);

        shell.Shell.SelectedSidebarRow = shell.Shell.RecommendationsRow;
        RenderedShell.Drain();

        Assert.Same(recommendations, shell.Shell.Content);
        Assert.True(shell.Shell.HasBack);
        Assert.Contains(Shown<TextBlock>(shell), block => block.Text == "STRIPE_SECRET_KEY");
        Assert.DoesNotContain(Shown<TextBlock>(shell), block => block.Text == "Needs review");

        // Leaving and coming back keeps the list, which the shell owns for the whole unlock.
        shell.Shell.BackCommand.Press();
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.Equal(count, recommendations.NeedsReview);

        recommendations.Dismiss(Assert.Single(recommendations.Rows));

        foreach (var password in recommendations.Passwords.ToList())
        {
            recommendations.Dismiss(password);
        }

        RenderedShell.Drain();
        Assert.Equal(string.Empty, shell.Shell.RecommendationsRow.Count);
        Assert.Contains(shell.Shell.RecommendationsRow, shell.Shell.SidebarRows);
    });

    [Fact]
    public Task The_sidebar_lists_no_recommendations_when_nothing_was_found() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell("keypaste-recommendations-none-");
        await shell.Shell.Recommendations.Checking.ConfigureAwait(true);
        RenderedShell.Drain();
        Assert.True(shell.Shell.Recommendations.IsEmpty);

        Assert.DoesNotContain(shell.Shell.RecommendationsRow, shell.Shell.SidebarRows);
    });

    /// <summary>A column's usual item is the one holding most of its keys, a tie going to the first path in ordinal order.</summary>
    [Fact]
    public void A_columns_usual_item_is_decided_the_same_way_every_time()
    {
        Assert.Equal("b", EnvProfileCell.UsualOf([["a"], ["b"], ["b"]]));
        Assert.Equal("a", EnvProfileCell.UsualOf([["b"], ["b"], ["a"], ["a"]]));
        Assert.Null(EnvProfileCell.UsualOf([["b"], ["a"]]));
        Assert.Null(EnvProfileCell.UsualOf([["a"], ["b", "c"], ["b", "c"]]));
        Assert.Null(EnvProfileCell.UsualOf([[], ["a", "b"]]));

        var usual = new EnvProfileCell("prod", EnvCellState.Set, null, [], null, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { })) { Sources = ["a"], UsualSource = "a", Also = ["dev"] };
        Assert.Equal("item also in dev", usual.SourceNote);
    }

    /// <summary>Then and Now show what changed, with the rest in one line; a custom field's value is never read to say so.</summary>
    [Fact]
    public Task History_compares_only_what_changed() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-history-compare-");
        shell.Open("revision");
        var history = Assert.IsType<EntriesViewModel>(shell.Shell.Content).Detail!.History;
        var revision = history.Rows[0];
        history.Selected = revision;
        RenderedShell.Drain();

        Assert.True(revision.PasswordDiffers);
        Assert.False(revision.UsernameDiffers);
        Assert.False(revision.TitleDiffers);
        Assert.Equal("Unchanged: username, URL, notes, custom field names and tags. Custom field values aren't compared here.", shell.Named<TextBlock>("RevisionUnchanged").Text);
        Assert.False(shell.Named<TextBlock>("RevisionFields").IsEffectivelyVisible);
        Assert.True(shell.Named<RevealedValue>("RevisionPassword").IsEffectivelyVisible);
    });

    /// <summary>A revision differing only in what drawing leaves out still counts as changed, and one differing in a field's value says values are not compared.</summary>
    [Fact]
    public Task History_compares_what_is_stored_and_says_what_it_does_not_compare() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-history-stored-");
        var entries = OpenGithub(shell);
        var detail = entries.Detail!;
        detail.History.ToggleCommand.Press();

        detail.EditCommand.Press();
        detail.DraftNotes = "\u200B";
        detail.SaveCommand.Press();
        RenderedShell.Drain();

        Assert.True(detail.History.Rows[0].NotesDiffer, "an invisible character read as unchanged");

        detail.BeginReplaceField(detail.Fields.Single(field => field.Name == "Region"));
        detail.ReplacementFieldValue.Type('x');
        detail.ConfirmReplaceFieldCommand.Press();
        RenderedShell.Drain();

        var replaced = detail.History.Rows[0];
        Assert.False(replaced.PasswordDiffers);
        Assert.False(replaced.NotesDiffer);
        Assert.False(replaced.FieldNamesDiffer);
        Assert.EndsWith("Custom field values aren't compared here.", replaced.Unchanged, StringComparison.Ordinal);
    });

    /// <summary>What was typed in one field form is not lost to another opened from a row's menu.</summary>
    [Fact]
    public Task A_rows_menu_asks_before_losing_what_was_typed() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-field-draft-");
        var entries = OpenGithub(shell);
        var detail = entries.Detail!;
        var region = detail.Fields.Single(field => field.Name == "Region");
        var token = detail.Fields.Single(field => field.Name == "API_TOKEN");

        detail.BeginReplaceField(region);
        detail.ReplacementFieldValue.Type('t');
        RenderedShell.Drain();

        foreach (var (name, press) in new (string, Action)[]
        {
            ("Rename", () => token.RenameCommand.Press()),
            ("Remove", () => token.RemoveCommand.Press()),
            ("Replace", () => token.ReplaceCommand.Press()),
            ("Generate", () => token.GenerateCommand.Press()),
        })
        {
            press();
            RenderedShell.Drain();

            Assert.True(shell.Shell.HasDiscardPrompt, $"{name} did not ask");
            Assert.Same(region, detail.ReplacingField);
            Assert.True(detail.ReplacementFieldValue.HasValue, $"{name} lost the value");
            shell.Shell.KeepDraftCommand.Press();
            RenderedShell.Drain();
        }

        token.RenameCommand.Press();
        RenderedShell.Drain();
        shell.Shell.ConfirmDiscardCommand.Press();
        RenderedShell.Drain();

        Assert.Same(token, detail.RenamingField);
        Assert.False(detail.IsReplacingField);
    });

    /// <summary>Protection rebuilds the rows, and the keyboard stays on the field it acted on.</summary>
    [Fact]
    public Task Protecting_a_field_keeps_the_keyboard_on_its_row() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-field-protect-focus-");
        var entries = OpenGithub(shell);
        var field = shell.Commands.Menus.Single(menu => menu.Header == "Item").Entries.OfType<AppSubmenu>().Single(submenu => submenu.Header == "Field");

        Choose(PressMenu(FieldMenu(shell, "Region")), "Protect field");
        RenderedShell.Drain();

        Assert.True(FieldMenu(shell, "Region").IsFocused, "the keyboard went nowhere");
        Assert.True(field.Entries().OfType<AppCommand>().Single(command => command.Header == "Replace value…").CanRun());
        Assert.True(entries.Detail!.Fields.Single(field => field.Name == "Region").IsProtected);
    });

    /// <summary>An item with no password offers neither Copy password nor Rotate, which would replace it.</summary>
    [Fact]
    public Task An_item_with_no_password_offers_no_way_to_replace_one() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-no-password-actions-", seed: vault => vault.AddEntry(new VaultEntry { Title = "no password", Username = "me" }));
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "no password");
        RenderedShell.Drain();
        var detail = entries.Detail!;

        Assert.False(detail.RotateCommand.CanExecute(null));
        Assert.False(shell.Named<Button>("RotateFromPane").IsEffectivelyVisible);
        Assert.False(shell.Named<Button>("CopyPassword").IsEffectivelyVisible);
    });

    /// <summary>The buttons a selection adds leave Trash's note and table where they were.</summary>
    [Fact]
    public Task Choosing_a_Trash_row_moves_nothing() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-trash-still-");
        var entries = OpenGithub(shell);
        entries.DeleteCommand.Press();
        var trash = shell.Show<TrashViewModel>(DestinationKind.Trash);
        trash.Selected = null;
        RenderedShell.Drain();
        var rows = Shown<ListBox>(shell).Single(list => list.Classes.Contains("table-body"));
        var before = rows.TranslatePoint(default, shell.Window);

        trash.Selected = Assert.Single(trash.Rows);
        RenderedShell.Drain();

        Assert.True(shell.Named<Button>("RestoreButton").IsEffectivelyVisible);
        Assert.Equal(before, rows.TranslatePoint(default, shell.Window));
    });

    /// <summary>Trash explains itself once: its two actions appear with a selection, and no footnote or hint repeats it.</summary>
    [Fact]
    public Task Trash_says_its_explanation_once() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-trash-once-");
        var entries = OpenGithub(shell);
        entries.DeleteCommand.Press();
        var trash = shell.Show<TrashViewModel>(DestinationKind.Trash);
        trash.Selected = null;
        RenderedShell.Drain();

        Assert.False(shell.Named<Button>("RestoreButton").IsEffectivelyVisible);
        Assert.DoesNotContain(Shown<TextBlock>(shell), block => block.Text?.Contains("format 4.1", StringComparison.Ordinal) == true);

        trash.Selected = Assert.Single(trash.Rows);
        RenderedShell.Drain();
        Assert.True(shell.Named<Button>("RestoreButton").IsEffectivelyVisible);
    });

    /// <summary>The process and the unlock leave Agents and are in Diagnostics, for a support request.</summary>
    [Fact]
    public Task Diagnostics_says_nothing_of_a_process_when_nothing_answers_agents()
    {
        return HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-diagnostics-answering-");

            Assert.Equal("no", new DiagnosticsViewModel(shell.Session, shell.Home).AnsweringAgents);
        });
    }
}
