using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Under 840 design pixels the sidebar collapses to a rail of the four places, and View › Hide sidebar still hides everything.</summary>
public sealed class IconRailTests
{
    private static RenderedShell Narrow(string name)
    {
        var shell = new RenderedShell(name);
        shell.Window.Width = 720;
        shell.Frame();
        return shell;
    }

    private static Rect Bounds(RenderedShell shell, Control control) =>
        new(control.TranslatePoint(default, shell.Root)!.Value, control.Bounds.Size);

    private static NavItem Place(RenderedShell shell, DestinationKind kind) =>
        shell.Shell.RailPlaces.Single(place => place.Destination.Kind == kind);

    private static ListBoxItem Row(RenderedShell shell, DestinationKind kind) =>
        (ListBoxItem)shell.Named<ListBox>("RailNav").ContainerFromItem(Place(shell, kind))!;

    private static void Key(RenderedShell shell, PhysicalKey key)
    {
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }

    private static AppCommand Command(RenderedShell shell, string header) =>
        shell.Commands.All.Single(command => command.Header == header);

    [Fact]
    public Task The_rail_is_56_wide_keeps_the_four_places_and_names_each() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-places-");
        var rail = shell.Named<Border>("Rail");

        Assert.Equal(WindowChrome.RailWidth, rail.Bounds.Width);
        Assert.False(shell.Named<Button>("ShowSidebar").IsEffectivelyVisible);
        Assert.Equal(720 - WindowChrome.RailWidth, shell.Named<ContentControl>("EntryPane").GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("content")).Bounds.Width);

        foreach (var (kind, name, icon) in new[]
        {
            (DestinationKind.Entries, "Items", "key-round"),
            (DestinationKind.AgentActivity, "Agents", "bot"),
            (DestinationKind.Trash, "Trash", "trash-2"),
            (DestinationKind.Settings, "Settings", "settings"),
        })
        {
            var row = Row(shell, kind);
            Assert.StartsWith(name, AutomationProperties.GetName(row), StringComparison.Ordinal);
            Assert.StartsWith(name, Assert.IsType<string>(ToolTip.GetTip(row)), StringComparison.Ordinal);
            var drawn = row.GetVisualDescendants().OfType<KpIcon>().Single();
            Assert.Equal(icon, drawn.Icon);
            Assert.Equal(20, drawn.Size);
            Assert.DoesNotContain(row.GetVisualDescendants(), visual => visual is TextBlock { IsEffectivelyVisible: true });
            Assert.True(row.Bounds.Width >= 20 && row.Bounds.Height >= 20);
        }

        // The sidebar keeps Trash and Settings at its foot, so the rail does too.
        Assert.True(Bounds(shell, Row(shell, DestinationKind.Settings)).Bottom <= Bounds(shell, shell.Named<Button>("RailLock")).Y);
        Assert.True(Bounds(shell, shell.Named<Button>("RailLock")).Bottom > shell.Root.Bounds.Height - 16);
        Assert.True(Bounds(shell, Row(shell, DestinationKind.Trash)).Y > Bounds(shell, Row(shell, DestinationKind.AgentActivity)).Bottom);
    });

    [Fact]
    public Task The_rail_starts_under_the_titlebar_so_the_traffic_lights_and_menu_button_stay_clear() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-top-");

        Assert.Equal(WindowChrome.TitleBarHeight, Bounds(shell, shell.Named<Border>("Rail")).Y);
    });

    [Fact]
    public Task The_selected_place_stays_selected_and_the_arrow_keys_walk_the_places() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-keys-");
        var list = shell.Named<ListBox>("RailNav");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();
        Assert.Same(Place(shell, DestinationKind.Entries), list.SelectedItem);

        Row(shell, DestinationKind.Entries).Focus(NavigationMethod.Directional);
        Key(shell, PhysicalKey.ArrowDown);
        Assert.Equal(DestinationKind.AgentActivity, shell.Shell.Current.Kind);
        Key(shell, PhysicalKey.ArrowDown);
        Assert.Equal(DestinationKind.Trash, shell.Shell.Current.Kind);
        Key(shell, PhysicalKey.ArrowDown);
        Assert.Equal(DestinationKind.Settings, shell.Shell.Current.Kind);
        Assert.True(Row(shell, DestinationKind.Settings).IsSelected);
        Assert.False(Row(shell, DestinationKind.Entries).IsSelected);
        Key(shell, PhysicalKey.Home);
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);

        // A screen under a place keeps the place selected, and so does a group on Items.
        shell.Show<AdvancedViewModel>(DestinationKind.Advanced);
        RenderedShell.Drain();
        Assert.True(Row(shell, DestinationKind.Settings).IsSelected);

        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.SelectedGroup = entries.Groups.First(group => !group.IsEverything);
        RenderedShell.Drain();
        Assert.True(Row(shell, DestinationKind.Entries).IsSelected);
    });

    [Fact]
    public Task A_press_on_a_place_goes_there_and_on_Items_shows_every_item() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-press-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.SelectedGroup = entries.Groups.First(group => !group.IsEverything);
        RenderedShell.Drain();

        shell.Tap(Row(shell, DestinationKind.AgentActivity));
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.AgentActivity, shell.Shell.Current.Kind);

        shell.Tap(Row(shell, DestinationKind.Entries));
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.True(((EntriesViewModel)shell.Shell.Content!).SelectedGroup!.IsEverything);

        shell.Tap(Row(shell, DestinationKind.Settings));
        shell.Frame();
        Assert.Equal(DestinationKind.Settings, shell.Shell.Current.Kind);
        Assert.All(shell.Named<Border>("Rail").GetVisualAncestors().OfType<Grid>().First().GetVisualDescendants().OfType<ScrollViewer>().Where(view => view.IsEffectivelyVisible), view => Assert.Equal(0, view.Offset.Y));
    });

    [Fact]
    public Task A_draft_kept_from_the_rail_keeps_the_rails_place() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-draft-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        entries.Detail!.EditCommand.Press();
        entries.Detail.DraftUsername = "half typed";
        RenderedShell.Drain();

        shell.Tap(Row(shell, DestinationKind.Settings));
        RenderedShell.Drain();
        Assert.True(shell.Shell.HasDiscardPrompt);

        shell.Shell.KeepDraftCommand.Press();
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.True(Row(shell, DestinationKind.Entries).IsSelected);
        Assert.False(Row(shell, DestinationKind.Settings).IsSelected);
    });

    [Fact]
    public Task A_press_on_the_chosen_Items_leaves_a_group_for_every_item() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-repress-group-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.SelectedGroup = entries.Groups.First(group => !group.IsEverything);
        RenderedShell.Drain();

        shell.Tap(Row(shell, DestinationKind.Entries));
        RenderedShell.Drain();

        Assert.True(((EntriesViewModel)shell.Shell.Content!).SelectedGroup!.IsEverything);
    });

    [Fact]
    public Task A_press_on_the_chosen_Items_leaves_a_project_for_every_item() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-repress-project-");
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "billing");
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.EnvSets, shell.Shell.Current.Kind);

        shell.Tap(Row(shell, DestinationKind.Entries));
        RenderedShell.Drain();

        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.True(((EntriesViewModel)shell.Shell.Content!).SelectedGroup!.IsEverything);
    });

    [Theory]
    [InlineData(true, false, 2, "ok", "Agents, live, 2 waiting")]
    [InlineData(true, false, 0, "ok", "Agents, live")]
    [InlineData(false, true, 0, "danger", "Agents, refusing")]
    public Task Agents_keeps_its_signal_and_says_it_in_words(bool live, bool refusing, int waiting, string dotClass, string name) => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-agents-");
        var agents = Place(shell, DestinationKind.AgentActivity);

        agents.DotLive = live;
        agents.DotDanger = refusing;
        agents.Waiting = waiting;
        agents.Detail = "Answering agents · 2 waiting · 1 connected";
        RenderedShell.Drain();

        var row = Row(shell, DestinationKind.AgentActivity);
        var dot = row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailAgentsDot");
        Assert.True(dot.IsEffectivelyVisible);
        Assert.Contains(dotClass, dot.Classes);
        Assert.Equal(name, AutomationProperties.GetName(row));
        Assert.Equal("Agents · Answering agents · 2 waiting · 1 connected", ToolTip.GetTip(row));
        Assert.Equal("Answering agents · 2 waiting · 1 connected", AutomationProperties.GetHelpText(row));
        Assert.False(Row(shell, DestinationKind.Settings).GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailAgentsDot").IsEffectivelyVisible);
    });

    [Theory]
    [InlineData(720, "RailAgentsDot", PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference)]
    [InlineData(720, "RailAgentsDot", PlatformThemeVariant.Light, ColorContrastPreference.NoPreference)]
    [InlineData(720, "RailAgentsDot", PlatformThemeVariant.Dark, ColorContrastPreference.High)]
    [InlineData(720, "RailAgentsDot", PlatformThemeVariant.Light, ColorContrastPreference.High)]
    [InlineData(1100, "AgentsDot", PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference)]
    [InlineData(1100, "AgentsDot", PlatformThemeVariant.Light, ColorContrastPreference.NoPreference)]
    public Task Agents_draws_a_different_shape_for_each_state_so_it_reads_without_colour(int width, string markName, PlatformThemeVariant variant, ColorContrastPreference contrast) => HeadlessSession.On(() =>
    {
        PlatformTheme.Set(variant, contrast);
        using var shell = new RenderedShell("keypaste-agents-marks-");
        shell.Window.Width = width;
        shell.Frame();
        var agents = Place(shell, DestinationKind.AgentActivity);

        bool[] ShapeWhile(bool live, bool refusing)
        {
            agents.DotLive = live;
            agents.DotDanger = refusing;
            RenderedShell.Drain();
            var mark = shell.Root.GetVisualDescendants().OfType<Border>().Single(border => border.Name == markName && border.IsEffectivelyVisible);
            var frame = shell.Frame();
            return frame.Shape(frame.Of(mark, shell.Window));
        }

        var running = ShapeWhile(live: true, refusing: false);
        var refusing = ShapeWhile(live: false, refusing: true);
        var stopped = ShapeWhile(live: false, refusing: false);

        // One shape drawn three times differs by 0 pixels; the ring's hole is the smallest real difference.
        const int leastDifferentPixels = 3;
        int Difference(bool[] one, bool[] other) => one.Zip(other).Count(pair => pair.First != pair.Second);
        Assert.True(Difference(running, stopped) >= leastDifferentPixels, $"running and stopped differ by {Difference(running, stopped)} pixels of shape");
        Assert.True(Difference(running, refusing) >= leastDifferentPixels, $"running and refusing differ by {Difference(running, refusing)} pixels of shape");
        Assert.True(Difference(stopped, refusing) >= leastDifferentPixels, $"stopped and refusing differ by {Difference(stopped, refusing)} pixels of shape");
    });

    [Fact]
    public Task The_groups_and_projects_open_from_the_rail_and_the_menu() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-overlay-");
        var open = shell.Named<Button>("RailGroups");
        var rail = shell.Named<Border>("Rail");
        var column = shell.Named<Grid>("BodyColumns").ColumnDefinitions[0];
        var menu = Command(shell, "Show groups and projects");
        var content = shell.Named<ContentControl>("EntryPane").Bounds.Width;
        Assert.Equal("Show groups and projects", AutomationProperties.GetName(open));
        Assert.Equal("Show groups and projects", ToolTip.GetTip(open));

        shell.Press(open);
        shell.Release(open);
        RenderedShell.Drain();

        Assert.True(shell.Shell.SidebarOverlays);
        Assert.True(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.False(rail.IsEffectivelyVisible, "the overlay's own rows stand for the places while it is open");
        Assert.Equal(WindowChrome.RailWidth, column.Width.Value);
        Assert.Equal(content, shell.Named<ContentControl>("EntryPane").Bounds.Width);
        Assert.False(menu.CanRun());
        AmberElements.AssertAtMostOne(shell.Window, "the sidebar opened from the rail");

        Key(shell, PhysicalKey.Escape);
        Assert.True(rail.IsEffectivelyVisible);
        Assert.True(open.IsFocused);
        Assert.True(menu.CanRun());

        menu.Run();
        RenderedShell.Drain();
        Assert.True(shell.Shell.SidebarOverlays);
    });

    [Theory]
    [InlineData(nameof(DestinationKind.Trash), "Trash")]
    [InlineData(nameof(DestinationKind.Settings), "Settings")]
    [InlineData(nameof(DestinationKind.Advanced), "Settings")]
    public Task The_overlay_from_a_footer_place_gives_the_keyboard_to_its_row_and_Escape_takes_it_back(string screen, string row) => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-overlay-footer-");
        shell.Shell.Current = Destinations.Of(Enum.Parse<DestinationKind>(screen));
        RenderedShell.Drain();
        var open = shell.Named<Button>("RailGroups");

        shell.Press(open);
        shell.Release(open);
        RenderedShell.Drain();

        var footer = shell.Named<ListBox>("FooterNav");
        var chosen = footer.ContainerFromItem(footer.Items.OfType<NavItem>().Single(item => item.Title == row))!;
        Assert.True(chosen.IsFocused);

        Key(shell, PhysicalKey.Escape);
        Assert.True(shell.Named<Border>("Rail").IsEffectivelyVisible);
        Assert.True(open.IsFocused);
    });

    [Fact]
    public Task A_press_on_a_field_outside_the_overlay_keeps_the_keyboard_in_the_field() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-overlay-press-field-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows[0];
        entries.Detail!.EditCommand.Press();
        RenderedShell.Drain();
        shell.Shell.OpenSidebarOverlay();
        shell.Frame();
        RenderedShell.Drain();
        var field = shell.Window.GetVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible && box.Name != "ShellSearch");

        shell.Tap(field);
        RenderedShell.Drain();

        Assert.False(shell.Shell.SidebarOverlays);
        Assert.True(field.IsFocused);
    });

    [Fact]
    public Task Showing_the_rail_again_by_the_menu_gives_the_keyboard_to_its_chosen_place() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-show-menu-");
        var hide = Command(shell, "Hide sidebar");
        Row(shell, DestinationKind.Entries).Focus(NavigationMethod.Directional);
        RenderedShell.Drain();

        hide.Run();
        RenderedShell.Drain();
        Assert.True(shell.Named<Button>("ShowSidebar").IsFocused);

        hide.Run();
        shell.Frame();
        RenderedShell.Drain();
        Assert.True(Row(shell, DestinationKind.Entries).IsFocused);
    });

    [Fact]
    public Task Hide_sidebar_hides_the_rail_says_the_way_back_and_keeps_it_reachable() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-hide-");
        var rail = shell.Named<Border>("Rail");
        var button = shell.Named<Button>("ShowSidebar");
        var hide = Command(shell, "Hide sidebar");
        shell.Named<Button>("RailGroups").Focus(NavigationMethod.Tab);
        RenderedShell.Drain();

        hide.Run();
        shell.Frame();
        RenderedShell.Drain();

        Assert.Equal("Show sidebar", hide.HeaderNow);
        Assert.False(rail.IsEffectivelyVisible);
        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.Equal(0, shell.Named<Grid>("BodyColumns").ColumnDefinitions[0].Width.Value);
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsFocused, "the keyboard follows the sidebar's way back");
        Assert.Equal("Show sidebar", AutomationProperties.GetName(button));
        Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text is { Text: "Show sidebar", IsEffectivelyVisible: true });
        var tip = Assert.IsType<string>(ToolTip.GetTip(button));
        Assert.Contains(OperatingSystem.IsMacOS() ? "⌃⌘S" : "Ctrl+B", tip, StringComparison.Ordinal);
        Assert.Contains("View menu", tip, StringComparison.Ordinal);
        Assert.False(Command(shell, "Show groups and projects").CanRun());
        Assert.True(Bounds(shell, button).X >= WindowChrome.LeadingInset(InterfaceZoom.Factor));

        shell.Window.Width = 1000;
        shell.Frame();
        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible, "the choice holds at every width");
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Show sidebar", ToolTip.GetTip(button));
        shell.Window.Width = 720;
        shell.Frame();

        shell.Press(button);
        shell.Release(button);
        RenderedShell.Drain();

        Assert.True(rail.IsEffectivelyVisible);
        Assert.False(button.IsEffectivelyVisible);
        Assert.False(shell.Shell.SidebarHidden);
        Assert.True(Row(shell, shell.Shell.SelectedRailPlace!.Destination.Kind).IsFocused);
    });

    [Fact]
    public Task Hide_sidebar_from_the_open_overlay_hides_everything() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-hide-overlay-");
        shell.Shell.OpenSidebarOverlay();
        RenderedShell.Drain();

        Command(shell, "Hide sidebar").Run();
        RenderedShell.Drain();

        Assert.True(shell.Shell.SidebarHidden);
        Assert.False(shell.Shell.SidebarOverlays);
        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.False(shell.Named<Border>("Rail").IsEffectivelyVisible);
    });

    [Fact]
    public Task Keyboard_inside_the_sidebar_moves_to_the_rail_as_the_window_narrows() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-rail-narrowing-");
        shell.Window.Width = 1000;
        shell.Frame();
        shell.Named<ListBox>("Nav").ContainerFromIndex(0)!.Focus();
        RenderedShell.Drain();

        shell.Window.Width = 720;
        shell.Frame();
        RenderedShell.Drain();

        Assert.True(shell.Named<Button>("RailGroups").IsFocused);
    });

    [Theory]
    [InlineData(nameof(DestinationKind.Entries), "Nav")]
    [InlineData(nameof(DestinationKind.Settings), "FooterNav")]
    public Task Keyboard_on_the_rail_moves_to_the_sidebar_as_the_window_widens(string screen, string list) => HeadlessSession.On(() =>
    {
        var kind = Enum.Parse<DestinationKind>(screen);
        using var shell = Narrow("keypaste-rail-widening-");
        shell.Shell.Current = Destinations.Of(kind);
        RenderedShell.Drain();
        var place = Place(shell, kind);
        Row(shell, kind).Focus(NavigationMethod.Directional);
        RenderedShell.Drain();

        shell.Window.Width = 1000;
        shell.Frame();
        RenderedShell.Drain();

        var row = shell.Named<ListBox>(list).ContainerFromItem(place)!;
        Assert.True(row.IsFocused);
    });

    [Fact]
    public Task At_840_and_wider_there_is_no_rail_and_the_content_keeps_its_width() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-rail-wide-");
        shell.Window.Width = 840;
        shell.Frame();

        Assert.False(shell.Named<Border>("Rail").IsEffectivelyVisible);
        Assert.Equal(WindowChrome.SidebarWidth, shell.Named<Grid>("BodyColumns").ColumnDefinitions[0].Width.Value);
        Assert.False(Command(shell, "Show groups and projects").CanRun());

        shell.Shell.ToggleSidebar();
        shell.Frame();
        Assert.False(shell.Named<Border>("Rail").IsEffectivelyVisible, "hidden at a wide width is hidden, with no rail in its place");
        Assert.Equal(0, shell.Named<Grid>("BodyColumns").ColumnDefinitions[0].Width.Value);
        var button = shell.Named<Button>("ShowSidebar");
        Assert.Equal("Show sidebar", ToolTip.GetTip(button));
        Assert.Null(AutomationProperties.GetHelpText(button));
        Assert.DoesNotContain(button.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible);
        Assert.Contains("icon", button.Classes);
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public Task The_activitys_Result_reads_whole_beside_the_rail(int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-rail-log-");
        using var home = new TempAuditHome();
        home.Append("env/dev/STRIPE_KEY");
        Directory.CreateDirectory(Path.GetDirectoryName(KeypasteHome.AuditPath(shell.Home))!);
        File.Copy(home.LogPath, KeypasteHome.AuditPath(shell.Home), overwrite: true);
        shell.Window.Width = 720 * percent / 100d;
        var log = shell.Show<LogViewModel>(DestinationKind.Log);
        log.RefreshCommand.Press();
        shell.Frame();

        var scroll = shell.Named<ScrollViewer>("TableScroll");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width, "the table scrolls sideways to its Result");
        var row = Assert.Single(shell.Window.GetVisualDescendants().OfType<LogColumns>(), columns => columns.Parent is Border { Classes: var classes } && classes.Contains("table-row"));
        var result = row.GetVisualDescendants().OfType<TextBlock>().Last(block => block.IsEffectivelyVisible);
        Assert.All(result.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"{result.Text} is cut"));
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<TextBlock>(), block => block.Classes.Contains("where") && block.IsEffectivelyVisible);
        Assert.True(row.DrawnWidths[3] > 100, $"Secrets is {row.DrawnWidths[3]} wide with Where's room to take");
        Assert.Equal(log.Rows.First(entry => entry.Where.Length > 0).SecretsAndWhere, ToolTip.GetTip(row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Classes.Contains("secrets"))));

        shell.Window.Width = 1000 * percent / 100d;
        shell.Frame();
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), block => block.Classes.Contains("where") && block.IsEffectivelyVisible);
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public Task An_environments_note_keeps_the_name_it_points_to_beside_the_rail(int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-rail-env-note-", vault =>
        {
            ProjectVariables.Set(vault, "billing", "dev", "SHARED", "value");
            ProjectVariables.Set(vault, "billing", "staging", "SHARED", "value");
        });
        shell.Window.Width = 720 * percent / 100d;
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "billing");
        shell.Frame();

        var note = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(block => block.IsEffectivelyVisible && block.Text == "same as staging");
        Assert.All(note.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, "the note is cut"));
        var overprints = ReachableControls.Problems(shell.Window).Where(problem => problem.Contains("overprints", StringComparison.Ordinal)).ToList();
        Assert.True(overprints.Count == 0, string.Join(Environment.NewLine, overprints));
    });

    [Fact]
    public Task Under_zoom_the_rail_reads_design_pixels() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        using var shell = new RenderedShell("keypaste-rail-zoom-");
        shell.Window.Width = 1440;
        shell.Window.Height = 1100;
        shell.Frame();

        Assert.True(shell.Shell.ShowsRail);
        Assert.Equal(WindowChrome.RailWidth, shell.Named<Border>("Rail").Bounds.Width);
        Assert.Equal(20, Row(shell, DestinationKind.Entries).GetVisualDescendants().OfType<KpIcon>().Single().Size);

        shell.Window.Width = 1680;
        shell.Frame();
        Assert.False(shell.Shell.ShowsRail);
    });
}
