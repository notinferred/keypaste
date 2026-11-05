using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

/// <summary>Items' list header: its one search, what it names, and a pane that shows only what the list does (Q.18).</summary>
public sealed class SearchByTheListTests
{
    private const string _longGroup = "a group with a very long name indeed that keeps on going/and a subgroup under it";

    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

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
    public Task Typing_in_the_header_filters_the_list_and_the_titlebar_holds_no_search() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-header-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var search = shell.Named<TextBox>("ShellSearch");

        Assert.Equal("Search", search.PlaceholderText);
        Assert.Equal("Search", AutomationProperties.GetName(search));
        Assert.DoesNotContain(shell.Named<Border>("TitleBar").GetVisualDescendants(), visual => visual is TextBox);
        Assert.True(Bounds(shell, shell.Named<TextBlock>("ListTitle")).Right <= Bounds(shell, search).X);
        Assert.True(Bounds(shell, search).Right <= Bounds(shell, shell.Named<ToggleButton>("AddEntry")).X);

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
        Assert.Equal("vault.kdbx · saved", shell.Named<TextBlock>("VaultStatusLine").Text);
        Assert.Contains(shell.Session.VaultPath!, shell.Shell.VaultStatusDetail, StringComparison.Ordinal);
        Assert.Single(shell.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text?.Contains("vault.kdbx", StringComparison.Ordinal) == true);

        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
        RenderedShell.Drain();

        Assert.Equal("servers", title.Text);
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
        Assert.True(shell.Named<TextBox>("ShellSearch").IsFocused);
    });

    [Fact]
    public Task Find_closes_a_sidebar_lying_over_the_field() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-overlay-", Seed);
        shell.Window.Width = 720;
        RenderedShell.Drain();
        shell.Shell.ToggleSidebar();
        RenderedShell.Drain();
        Assert.True(shell.Shell.SidebarOverlays);

        Find(shell);

        Assert.False(shell.Shell.SidebarOverlays);
        Assert.True(shell.Named<TextBox>("ShellSearch").IsFocused);
    });

    [Fact]
    public Task The_keycap_shows_a_chord_Find_answers() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-keycap-", Seed);
        var keycap = shell.Named<TextBox>("ShellSearch").GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text?.EndsWith('F') == true);

        Assert.Equal(OperatingSystem.IsMacOS() ? "⌘F" : "Ctrl F", keycap.Text);
    });

    [Fact]
    public Task A_search_or_a_group_that_hides_the_selected_item_clears_the_pane() => HeadlessSession.On(() =>
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

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
        Assert.Equal("box", entries.Selected?.Title);

        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "other");
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

        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
        AssertListSelects("box", "changing group under a search");
        shell.Shell.Search = string.Empty;
        AssertListSelects("box", "clearing the search in a group");
        entries.ShowEveryItem();
        AssertListSelects("box", "back to every item");

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
        var search = shell.Named<TextBox>("ShellSearch");
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

        shell.Named<ListBox>("Nav").SelectedItem = shell.Shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "other");
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
        Assert.True(shell.Named<TextBox>("ShellSearch").IsFocused);
    });

    [Fact]
    public Task A_search_leaves_a_form_or_a_comparison_that_holds_nothing_for_the_list() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-whole-view-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        entries.Detail!.EditCommand.Press();
        Assert.False(entries.ShowsList);
        shell.Shell.Search = "box";
        Assert.True(entries.ShowsList);
        Assert.False(entries.Detail!.IsEditing);
        shell.Shell.Search = string.Empty;

        entries.BeginAddCommand.Press();
        Assert.False(entries.ShowsList);
        shell.Shell.Search = "box";
        Assert.True(entries.ShowsList);
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
    public Task An_empty_result_in_a_group_offers_Search_all_items_and_it_keeps_the_query() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-empty-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        shell.Shell.Search = "box";
        Assert.False(entries.ListEmptyOffersSearchAll);

        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "other");
        Assert.True(entries.ShowsListEmpty);
        Assert.True(entries.ListEmptyOffersSearchAll);

        entries.SearchAllItemsCommand.Press();

        Assert.Equal("All items", entries.ListTitle);
        Assert.Equal("box", shell.Shell.Search);
        Assert.Equal(["box"], entries.Rows.Select(row => row.Title));
    });

    [Theory]
    [InlineData(1000)]
    [InlineData(720)]
    public Task The_pane_names_the_group_only_while_the_list_and_its_column_are_away(double width) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-location-", Seed);
        shell.Window.Width = width;
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        RenderedShell.Drain();
        var location = shell.Named<TextBlock>("PaneLocation");

        Assert.False(location.IsEffectivelyVisible);

        entries.Detail!.EditCommand.Press();
        RenderedShell.Drain();

        Assert.True(location.IsEffectivelyVisible);
        Assert.Equal("servers", location.Text);

        entries.Detail.CancelCommand.Press();
        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "servers");
        entries.Detail.EditCommand.Press();
        RenderedShell.Drain();

        Assert.False(entries.ShowsList);
        Assert.False(location.IsEffectivelyVisible, "the header already names the group");
    });

    [Theory]
    [InlineData(720)]
    [InlineData(840)]
    public Task A_long_group_name_leaves_the_header_whole(double width) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-long-", vault =>
        {
            Seed(vault);
            vault.AddEntry(new VaultEntry { Title = "deep", GroupPath = _longGroup });
        });
        shell.Window.Width = width;
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.SelectedGroup = entries.Groups.Single(group => group.Path == _longGroup);
        RenderedShell.Drain();
        shell.Frame();

        var title = Bounds(shell, shell.Named<TextBlock>("ListTitle"));
        var rename = Bounds(shell, shell.Named<Button>("RenameGroup"));
        var search = Bounds(shell, shell.Named<TextBox>("ShellSearch"));
        var add = Bounds(shell, shell.Named<ToggleButton>("AddEntry"));

        Assert.True(title.Right <= rename.X, $"the title {title} runs into Rename {rename}");
        Assert.True(rename.Right <= search.X, $"Rename {rename} runs into the search {search}");
        Assert.True(search.Right <= add.X, $"the search {search} runs into New {add}");
        Assert.True(search.Width >= 160, $"the search is {search.Width:0} wide");
        Assert.True(add.Right <= width, $"New {add} is past the window's {width}");
    });

    [Fact]
    public Task A_long_file_name_gives_way_before_Lock_and_the_save_state() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-long-file-", Seed, new string('n', 150) + ".kdbx");
        shell.Window.Width = 720;
        RenderedShell.Drain();
        shell.Frame();

        var line = shell.Named<TextBlock>("VaultStatusLine");
        var titleBar = shell.Named<Border>("TitleBar");
        var lockNow = titleBar.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Lock now");

        Assert.EndsWith(" · saved", line.Text, StringComparison.Ordinal);
        Assert.True(line.TextLayout.TextLines[0].HasCollapsed);
        Assert.True(Bounds(shell, lockNow).Right <= 720);
        Assert.True(Bounds(shell, line).X >= Bounds(shell, shell.Named<Button>("ShowSidebar")).Right);
    });

    [Fact]
    public Task The_sidebars_button_shows_only_while_the_sidebar_is_away_and_opens_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-sidebar-", Seed);
        var button = shell.Named<Button>("ShowSidebar");
        var lockButton = shell.Named<Border>("TitleBar").GetVisualDescendants().OfType<Button>().Single(candidate => AutomationProperties.GetName(candidate) == "Lock now");
        shell.Frame();

        Assert.False(button.IsEffectivelyVisible);
        var lockedAt = Bounds(shell, lockButton);

        shell.Shell.ToggleSidebar();
        shell.Frame();
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Show sidebar", AutomationProperties.GetName(button));
        Assert.Equal("Show sidebar", ToolTip.GetTip(button));
        Assert.Equal(lockedAt, Bounds(shell, lockButton));
        Assert.True(Bounds(shell, button).X >= WindowChrome.LeadingInset(InterfaceZoom.Factor), "the button lies under the traffic lights or the menu button");

        shell.Press(button);
        shell.Release(button);
        RenderedShell.Drain();
        Assert.False(button.IsEffectivelyVisible);
        Assert.True(shell.Named<Border>("Sidebar").IsEffectivelyVisible);

        shell.Window.Width = 720;
        shell.Frame();
        Assert.True(button.IsEffectivelyVisible);
        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.True(Bounds(shell, button).X >= WindowChrome.LeadingInset(InterfaceZoom.Factor));

        shell.Press(button);
        shell.Release(button);
        RenderedShell.Drain();
        Assert.True(shell.Shell.SidebarOverlays);
        Assert.False(button.IsEffectivelyVisible);

        AmberElements.AssertAtMostOne(shell.Window, "the sidebar over the content");

        shell.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        RenderedShell.Drain();
        Assert.False(shell.Shell.SidebarOverlays);
        Assert.True(button.IsFocused);
    });

    [Fact]
    public Task The_focused_search_is_the_views_one_amber() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-search-amber-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var add = shell.Named<ToggleButton>("AddEntry");
        shell.Frame();
        AmberElements.AssertOne(shell.Window, add, "Items at rest");
        var fieldAtRest = Bounds(shell, shell.Named<TextBox>("ShellSearch"));

        Find(shell);
        Assert.True(entries.SearchHasFocus);
        shell.Frame();
        Assert.DoesNotContain("primary", add.Classes);
        AmberElements.AssertOne(shell.Window, null, "the search focused beside New");
        Assert.Equal(fieldAtRest, Bounds(shell, shell.Named<TextBox>("ShellSearch")));

        entries.Selected = entries.Rows.Single(row => row.Title == "box");
        shell.Named<ListBox>("Rows").ContainerFromIndex(0)!.Focus();
        RenderedShell.Drain();
        shell.Frame();
        Assert.False(entries.SearchHasFocus);
        Assert.Contains("primary", add.Classes);
    });
}
