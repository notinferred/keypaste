using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Items' one search, atop the list, what the list's header names, and a pane that shows only what the list does.</summary>  (the search is ListSearch in EntriesView's ListColumn, and the first test asserts it sits above ListTitle, not in the title bar)
public sealed class SearchByTheListTests
{

    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void SeedWithAPassword(Vault vault)
    {
        vault.AddEntry(new VaultEntry { Title = "box", Username = "ops", Password = "a password", GroupPath = "servers" });
    }

    private static void Seed(Vault vault)
    {
        vault.AddEntry(new VaultEntry { Title = "box", Username = "ops", GroupPath = "servers" });
        vault.AddEntry(new VaultEntry { Title = "spare", GroupPath = "other" });
    }

    private static void Find(RenderedShell shell)
    {
        shell.Window.KeyPressQwerty(PhysicalKey.F, _command);
        shell.Window.KeyReleaseQwerty(PhysicalKey.F, _command);
        RenderedShell.Drain();
    }

    private static Rect Bounds(RenderedShell shell, Control control) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(shell.Window)!.Value);

    [Fact]
    public Task Typing_in_the_search_atop_the_list_filters_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-header-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var search = shell.Named<TextBox>("ListSearch");

        Assert.Equal("Search", search.PlaceholderText);
        Assert.Equal("Search", AutomationProperties.GetName(search));
        Assert.Contains(search, shell.Named<Border>("ListColumn").GetVisualDescendants());
        Assert.True(Bounds(shell, shell.Named<TextBlock>("ListTitle")).Bottom <= Bounds(shell, search).Top);
        Assert.Equal(ShellViewModel.SearchShortcut, shell.Named<Border>("ListSearchShortcut").GetVisualDescendants().OfType<TextBlock>().Single().Text);

        Find(shell);
        WindowInput.Type(shell.Window, "box");
        RenderedShell.Drain();

        Assert.Equal("box", search.Text);
        Assert.Equal(["box"], entries.Rows.Select(row => row.Title));
    });

    [Fact]
    public Task The_header_says_All_items_or_the_group_and_only_the_status_names_the_file() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-title-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var title = shell.Named<TextBlock>("ListTitle");

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        entries.Detail!.EditCommand.Press();
        entries.Detail.CancelCommand.Press();
        RenderedShell.Drain();

        Assert.Equal("All items", title.Text);
        Assert.Equal("vault", shell.Named<TextBlock>("ListSubtitle").Text);
        Assert.Equal(entries.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), shell.Named<TextBlock>("ListCount").Text);

        // The title, its count, its vault and the sort share one line, the sort as tall as the search under them.
        var line = Bounds(shell, title).Center.Y;
        Assert.All(new Control[] { shell.Named<TextBlock>("ListCount"), shell.Named<TextBlock>("ListSubtitle"), shell.Named<Button>("SortMenu") }, control => Assert.Equal(line, Bounds(shell, control).Center.Y, 0.5));
        Assert.Equal(Bounds(shell, shell.Named<TextBox>("ListSearch")).Height, Bounds(shell, shell.Named<Button>("SortMenu")).Height, 0.5);
        Assert.False(shell.Named<TextBlock>("VaultSaveState").IsEffectivelyVisible, "a saved vault says nothing of it");
        Assert.Contains(shell.Session.VaultPath!, shell.Shell.VaultStatusDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(shell.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text?.Contains("vault.kdbx", StringComparison.Ordinal) == true);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public Task Find_reaches_the_field_from_any_place(int place) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-find-", Seed);
        shell.Shell.GoTo(place);
        RenderedShell.Drain();

        Find(shell);

        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.True(shell.Named<TextBox>("ListSearch").IsFocused);
    });

    [Fact]
    public Task Find_closes_a_sidebar_lying_over_the_field() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-overlay-", Seed);
        shell.Window.Width = 720;
        RenderedShell.Drain();
        shell.Shell.OpenSidebarOverlay();
        RenderedShell.Drain();
        Assert.True(shell.Shell.SidebarOverlays);

        Find(shell);

        Assert.False(shell.Shell.SidebarOverlays);
        Assert.True(shell.Named<TextBox>("ListSearch").IsFocused);
    });

    [Fact]
    public Task The_searchs_tooltip_names_a_chord_Find_answers() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-keycap-", Seed);

        Assert.Equal($"Search, {(OperatingSystem.IsMacOS() ? "⌘F" : "Ctrl F")}", ToolTip.GetTip(shell.Named<TextBox>("ListSearch")));
    });

    [Fact]
    public Task A_search_that_hides_the_selected_item_clears_the_pane() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-hides-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        var pane = entries.Detail;
        shell.Shell.Search = "bo";
        Assert.Same(pane, entries.Detail);
        Assert.Equal("box", entries.Selected?.Title);

        shell.Shell.Search = "spare";
        Assert.Null(entries.Detail);
        Assert.Null(entries.Selected);

        shell.Shell.Search = string.Empty;
        Assert.Null(entries.Detail);
    });

    [Fact]
    public Task The_list_keeps_a_row_selected_while_it_stays_listed() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-list-selection-", vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "box", Username = "ops", GroupPath = "servers" });
            vault.AddEntry(new VaultEntry { Title = "crate", Username = "ops", GroupPath = "servers" });
            vault.AddEntry(new VaultEntry { Title = "spare", GroupPath = "other" });
        });
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var list = shell.Named<ListBox>("Rows");

        void AssertListSelects(string title, string at)
        {
            RenderedShell.Drain();
            Assert.Equal(title, entries.Selected?.Title);
            Assert.True(ReferenceEquals(entries.Selected, list.SelectedItem), $"the list selects {(list.SelectedItem as EntryRow)?.Title ?? "nothing"} {at}, the pane {title}");
            var row = Assert.IsAssignableFrom<ListBoxItem>(list.ContainerFromItem(list.SelectedItem!));
            Assert.True(row.IsSelected, $"the row is not drawn selected {at}");
        }

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        AssertListSelects("box", "as chosen");

        shell.Shell.Search = "b";
        AssertListSelects("box", "narrowing to a title");
        shell.Shell.Search = "bo";
        AssertListSelects("box", "narrowing again");
        shell.Shell.Search = "b";
        AssertListSelects("box", "widening");
        shell.Shell.Search = string.Empty;
        AssertListSelects("box", "clearing the search");
        shell.Shell.Search = "ops";
        AssertListSelects("box", "matching another field");

        shell.Shell.Search = string.Empty;
        AssertListSelects("box", "clearing the search again");

        shell.Shell.Search = "ops";
        AssertListSelects("box", "before the keyboard");
        list.ContainerFromItem(list.SelectedItem!)!.Focus(NavigationMethod.Directional);
        shell.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        AssertListSelects("crate", "after the Down key");

        var pane = entries.Detail!;
        entries.Detail!.BeginAddFieldCommand.Press();
        entries.Detail.DraftFieldName = "half typed";
        RenderedShell.Drain();
        list.SelectedItem = entries.Rows.Single(row => row.Title == "box");
        RenderedShell.Drain();
        Assert.True(shell.Shell.HasDiscardPrompt);
        shell.Shell.KeepDraftCommand.Press();
        AssertListSelects("crate", "after Cancel on the question");
        Assert.Same(pane, entries.Detail);
        Assert.Equal("half typed", entries.Detail.DraftFieldName);
    });

    [Fact]
    public Task A_search_or_a_group_asks_before_it_loses_a_draft_in_the_pane() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-draft-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var search = shell.Named<TextBox>("ListSearch");
        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        entries.Detail!.EditCommand.Press();
        entries.Detail.DraftUsername = "half typed";
        RenderedShell.Drain();

        Find(shell);
        WindowInput.Type(shell.Window, "s");
        RenderedShell.Drain();
        Assert.True(shell.Shell.HasDiscardPrompt);
        shell.Shell.KeepDraftCommand.Press();
        RenderedShell.Drain();
        Assert.Equal("half typed", entries.Detail!.DraftUsername);
        Assert.Equal(string.Empty, search.Text);
        Assert.Same(entries, shell.Shell.Content);

        shell.Named<ListBox>("Nav").SelectedItem = shell.Shell.Place(ItemPlace.Trash);
        RenderedShell.Drain();
        Assert.True(shell.Shell.HasDiscardPrompt);
        shell.Shell.KeepDraftCommand.Press();
        RenderedShell.Drain();
        Assert.Equal("half typed", entries.Detail!.DraftUsername);

        Find(shell);
        WindowInput.Type(shell.Window, "s");
        RenderedShell.Drain();
        shell.Shell.ConfirmDiscardCommand.Press();
        RenderedShell.Drain();
        var rebuilt = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        Assert.Equal("s", rebuilt.Search);
        Assert.Null(rebuilt.Detail);
        Assert.True(shell.Named<TextBox>("ListSearch").IsFocused);
    });

    [Fact]
    public Task A_search_keeps_history_open_beside_the_list_and_closes_an_empty_new_item() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-whole-view-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        entries.Detail!.History.ToggleCommand.Press();
        shell.Shell.Search = "box";
        Assert.True(entries.Detail!.History.IsOpen);
        shell.Shell.Search = string.Empty;

        entries.BeginAddCommand.Press();
        shell.Shell.Search = "box";
        Assert.Null(entries.NewItem);
    });

    [Fact]
    public Task A_save_that_takes_the_item_out_of_the_result_keeps_it_listed_with_its_pane() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-pin-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        shell.Shell.Search = "ops";
        entries.Selected = entries.Rows.Single(row => row.Title == "box");

        entries.Detail!.EditCommand.Press();
        entries.Detail.DraftUsername = "nobody";
        entries.Detail.SaveCommand.Press();
        RenderedShell.Drain();

        Assert.Equal("box", entries.Selected?.Title);
        Assert.NotNull(entries.Detail);
        Assert.Null(entries.Error);

        shell.Shell.Search = "opsx";
        Assert.Null(entries.Detail);
    });

    [Fact]
    public Task A_long_file_name_gives_way_in_the_sidebar_and_its_path_is_the_tooltip() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-long-file-", Seed, new string('n', 150) + ".kdbx", secondVault: "acme");
        shell.Window.Width = 1000;
        shell.Frame();

        var vault = shell.Named<ListBox>("Nav").GetVisualDescendants().OfType<TextBlock>().Single(block => block.Classes.Contains("vault") && block.Text != "acme");

        Assert.True(vault.TextLayout.TextLines[0].HasCollapsed);
        Assert.True(Bounds(shell, vault).Right <= WindowChrome.RailWidth + WindowChrome.SidebarWidth);
        Assert.Equal(shell.Session.VaultPath, ToolTip.GetTip(vault.GetVisualAncestors().OfType<Grid>().First()));
        Assert.True(Bounds(shell, shell.Named<Button>("LockButton")).Right <= 1000);
    });

    [Fact]
    public Task Copy_password_holds_the_views_amber_and_New_stays_quiet() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-amber-", SeedWithAPassword);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var add = shell.Named<Button>("SidebarNew");
        shell.Frame();
        Assert.DoesNotContain("primary", add.Classes);
        AccentElements.AssertOne(shell.Window, null, "Items at rest");
        var fieldAtRest = Bounds(shell, shell.Named<TextBox>("ListSearch"));

        Find(shell);
        shell.Frame();
        AccentElements.AssertOne(shell.Window, null, "the search focused");
        Assert.Equal(fieldAtRest, Bounds(shell, shell.Named<TextBox>("ListSearch")));

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        RenderedShell.Drain();
        shell.Frame();
        var copy = shell.Named<Button>("CopyPassword");
        Assert.Contains("primary", copy.Classes);
        AccentElements.AssertOne(shell.Window, copy, "an open item");
        Assert.DoesNotContain("primary", add.Classes);
    });
}
