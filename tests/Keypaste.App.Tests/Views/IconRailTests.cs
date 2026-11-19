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

/// <summary>The rail of places, Outlook's way, at every width: Items, Agents, Security, Access kept for later and Settings; under 840 design pixels the sidebar gives way and opens over the content from the title bar.</summary>
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

    [Theory]
    [InlineData(720)]
    [InlineData(1000)]
    public Task The_rail_is_52_wide_at_every_width_keeps_the_places_and_names_each(int width) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-rail-places-");
        shell.Window.Width = width;
        shell.Frame();
        var rail = shell.Named<Border>("Rail");

        Assert.Equal(WindowChrome.RailWidth, rail.Bounds.Width);

        foreach (var (kind, name, icon) in new[]
        {
            (DestinationKind.Entries, "Items", "inbox"),
            (DestinationKind.AgentActivity, "Agents", "bot"),
            (DestinationKind.Recommendations, "Security", "shield"),
            (DestinationKind.Access, "Access, later", "network"),
            (DestinationKind.Settings, "Settings", "settings"),
        })
        {
            var row = Row(shell, kind);
            Assert.StartsWith(name, AutomationProperties.GetName(row), StringComparison.Ordinal);
            Assert.StartsWith(name, Assert.IsType<string>(ToolTip.GetTip(row)), StringComparison.Ordinal);
            var drawn = row.GetVisualDescendants().OfType<KpIcon>().Single();
            Assert.Equal(icon, drawn.Icon);
            Assert.Equal(20, drawn.Size);
            Assert.True(row.Bounds.Width >= 20 && row.Bounds.Height >= 20);
        }

        // Access waits for later, dimmed and never chosen; Settings sits at the rail's foot.
        Assert.False(Row(shell, DestinationKind.Access).IsEnabled);
        Assert.True(ToolTip.GetShowOnDisabled(Row(shell, DestinationKind.Access)));
        Assert.True(Bounds(shell, Row(shell, DestinationKind.Settings)).Bottom > shell.Root.Bounds.Height - 24);
        Assert.True(Bounds(shell, Row(shell, DestinationKind.Access)).Bottom < Bounds(shell, Row(shell, DestinationKind.Settings)).Y);
    });

    [Fact]
    public Task The_rail_starts_under_the_titlebar_so_the_traffic_lights_and_menu_button_stay_clear() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-top-");

        Assert.Equal(WindowChrome.TitleBarHeight, Bounds(shell, shell.Named<Border>("Rail")).Y);
    });

    [Fact]
    public Task The_selected_place_stays_selected_and_the_arrow_keys_walk_the_places_past_Access() => HeadlessSession.On(() =>
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
        Assert.Equal(DestinationKind.Recommendations, shell.Shell.Current.Kind);
        Key(shell, PhysicalKey.ArrowDown);
        Assert.Equal(DestinationKind.Settings, shell.Shell.Current.Kind);
        Assert.True(Row(shell, DestinationKind.Settings).IsSelected);
        Assert.False(Row(shell, DestinationKind.Entries).IsSelected);
        Key(shell, PhysicalKey.Home);
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);

        // A screen under a place keeps the place selected, and so do Environments and Trash on Items.
        shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens);
        RenderedShell.Drain();
        Assert.True(Row(shell, DestinationKind.Settings).IsSelected);

        shell.Show<TrashViewModel>(DestinationKind.Trash);
        RenderedShell.Drain();
        Assert.True(Row(shell, DestinationKind.Entries).IsSelected);

        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();
        Assert.True(Row(shell, DestinationKind.Entries).IsSelected);
    });

    [Fact]
    public Task A_press_on_a_place_goes_there_and_on_Items_shows_every_item() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-press-");
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        shell.Tap(Row(shell, DestinationKind.AgentActivity));
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.AgentActivity, shell.Shell.Current.Kind);

        shell.Tap(Row(shell, DestinationKind.Entries));
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.Equal(ItemPlace.All, ((EntriesViewModel)shell.Shell.Content!).Place);

        shell.Tap(Row(shell, DestinationKind.Access));
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);

        shell.Tap(Row(shell, DestinationKind.Settings));
        shell.Frame();
        Assert.Equal(DestinationKind.Settings, shell.Shell.Current.Kind);
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
    public Task A_press_on_the_chosen_Items_leaves_a_project_for_every_item() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-repress-project-");
        shell.Shell.SelectedSidebarRow = shell.Shell.ProjectRowNamed("billing");
        RenderedShell.Drain();
        Assert.Equal(DestinationKind.EnvSets, shell.Shell.Current.Kind);

        shell.Tap(Row(shell, DestinationKind.Entries));
        RenderedShell.Drain();

        Assert.Equal(DestinationKind.Entries, shell.Shell.Current.Kind);
        Assert.Equal(ItemPlace.All, ((EntriesViewModel)shell.Shell.Content!).Place);
    });

    [Theory]
    [InlineData(true, false, 2, "ok", "Agents, Answering agents, 2 waiting", "Agents · Answering agents · 2 waiting · 1 connected")]
    [InlineData(true, false, 0, "ok", "Agents, Answering agents", "Agents · Answering agents · 2 waiting · 1 connected")]
    [InlineData(false, true, 0, "danger", "Agents, Refusing agents", "Agents · Refusing agents · 2 waiting · 1 connected")]
    public Task Agents_keeps_its_signal_and_says_it_in_words(bool live, bool refusing, int waiting, string dotClass, string name, string tip) => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-agents-");
        var agents = Place(shell, DestinationKind.AgentActivity);

        agents.DotLive = live;
        agents.DotDanger = refusing;
        agents.Waiting = waiting;
        agents.Detail = "2 waiting · 1 connected";
        RenderedShell.Drain();

        var row = Row(shell, DestinationKind.AgentActivity);
        var dot = row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailAgentsDot");
        Assert.True(dot.IsEffectivelyVisible);
        Assert.Contains(dotClass, dot.Classes);
        Assert.Equal(name, AutomationProperties.GetName(row));
        Assert.Equal(tip, ToolTip.GetTip(row));
        Assert.Equal("2 waiting · 1 connected", AutomationProperties.GetHelpText(row));
        Assert.False(Row(shell, DestinationKind.Settings).GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailAgentsDot").IsEffectivelyVisible);
    });

    [Fact]
    public Task Security_counts_what_needs_review_on_a_badge_and_says_it() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-security-");
        var security = Place(shell, DestinationKind.Recommendations);

        security.Count = "3";
        RenderedShell.Drain();

        var badge = Row(shell, DestinationKind.Recommendations).GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailBadge");
        Assert.True(badge.IsEffectivelyVisible);
        Assert.Equal("3", Assert.IsType<TextBlock>(badge.Child).Text);
        Assert.Equal("Security, 3", AutomationProperties.GetName(Row(shell, DestinationKind.Recommendations)));

        security.Count = string.Empty;
        RenderedShell.Drain();
        Assert.False(badge.IsEffectivelyVisible);
    });

    [Theory]
    [InlineData(720, PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference)]
    [InlineData(720, PlatformThemeVariant.Light, ColorContrastPreference.NoPreference)]
    [InlineData(720, PlatformThemeVariant.Dark, ColorContrastPreference.High)]
    [InlineData(720, PlatformThemeVariant.Light, ColorContrastPreference.High)]
    [InlineData(1100, PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference)]
    [InlineData(1100, PlatformThemeVariant.Light, ColorContrastPreference.NoPreference)]
    public Task Agents_draws_a_different_shape_for_each_state_so_it_reads_without_colour(int width, PlatformThemeVariant variant, ColorContrastPreference contrast) => HeadlessSession.On(() =>
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
            var mark = shell.Root.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RailAgentsDot" && border.IsEffectivelyVisible);
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
    public Task The_sidebar_opens_over_a_narrow_window_from_the_menu() => HeadlessSession.On(() =>
    {
        using var shell = Narrow("keypaste-rail-overlay-");
        var rail = shell.Named<Border>("Rail");
        var menu = Command(shell, "Hide sidebar");
        var content = shell.Named<ContentControl>("EntryPane").Bounds.Width;

        menu.Run();
        RenderedShell.Drain();

        Assert.True(shell.Shell.SidebarOverlays);
        Assert.True(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.True(rail.IsEffectivelyVisible, "the rail stays beside the sidebar over the content");
        Assert.Equal(content, shell.Named<ContentControl>("EntryPane").Bounds.Width);
        Assert.True(shell.Named<ListBox>("Nav").ContainerFromItem(shell.Shell.SelectedSidebarRow!)!.IsFocused);
        AccentElements.AssertAtMostOne(shell.Window, "the sidebar opened over a narrow window");

        Key(shell, PhysicalKey.Escape);
        RenderedShell.Drain();
        Assert.False(shell.Shell.SidebarOverlays);
        Assert.True(ChosenRailPlace(shell).IsFocused);

        menu.Run();
        RenderedShell.Drain();
        Assert.True(shell.Shell.SidebarOverlays);
        Assert.False(shell.Shell.SidebarHidden, "opening it over a narrow window is not the person's choice to hide it");
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
        var field = shell.Window.GetVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible && box.Name != "ListSearch");

        shell.Tap(field);
        RenderedShell.Drain();

        Assert.False(shell.Shell.SidebarOverlays);
        Assert.True(field.IsFocused);
    });

    [Fact]
    public Task Hide_sidebar_keeps_the_rail_says_the_way_back_and_holds_at_every_wide_width() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-rail-hide-");
        shell.Window.Width = 1000;
        shell.Frame();
        var rail = shell.Named<Border>("Rail");
        var hide = Command(shell, "Hide sidebar");
        shell.Named<ListBox>("Nav").ContainerFromItem(shell.Shell.SelectedSidebarRow!)!.Focus(NavigationMethod.Tab);
        RenderedShell.Drain();

        hide.Run();
        shell.Frame();
        RenderedShell.Drain();

        Assert.Equal("Show sidebar", hide.HeaderNow);
        Assert.True(rail.IsEffectivelyVisible);
        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.Equal(0, shell.Named<Grid>("BodyColumns").ColumnDefinitions[1].Width.Value);
        Assert.True(ChosenRailPlace(shell).IsFocused, "the keyboard goes to the rail, the sidebar's way back");

        shell.Window.Width = 1200;
        shell.Frame();
        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible, "the choice holds at every wide width");

        hide.Run();
        shell.Frame();
        RenderedShell.Drain();

        Assert.True(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.False(shell.Shell.SidebarHidden);
        Assert.Equal(WindowChrome.SidebarWidth, shell.Named<Grid>("BodyColumns").ColumnDefinitions[1].Width.Value);
    });

    [Fact]
    public Task Keyboard_inside_the_sidebar_moves_to_the_rail_as_the_window_narrows() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-rail-narrowing-");
        shell.Window.Width = 1000;
        shell.Frame();
        shell.Named<ListBox>("Nav").ContainerFromItem(shell.Shell.SelectedSidebarRow!)!.Focus();
        RenderedShell.Drain();

        shell.Window.Width = 720;
        shell.Frame();
        RenderedShell.Drain();

        Assert.True(ChosenRailPlace(shell).IsFocused);
    });

    [Fact]
    public Task Agents_Security_and_Settings_have_no_sidebar_and_the_content_takes_its_width() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-rail-wide-");
        shell.Window.Width = 1000;
        shell.Frame();
        Assert.True(shell.Named<Border>("Sidebar").IsEffectivelyVisible);

        foreach (var kind in new[] { DestinationKind.AgentActivity, DestinationKind.Recommendations, DestinationKind.Settings })
        {
            shell.Shell.Current = Destinations.Of(kind);
            shell.Frame();
            Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
            Assert.Equal(0, shell.Named<Grid>("BodyColumns").ColumnDefinitions[1].Width.Value);
            Assert.True(shell.Named<Border>("Rail").IsEffectivelyVisible);
        }
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
        shell.Shell.SelectedSidebarRow = shell.Shell.ProjectRowNamed("billing");
        shell.Frame();

        var note = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(block => block.IsEffectivelyVisible && block.Text == "Same as staging");
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

        Assert.True(shell.Shell.IsNarrow);
        Assert.Equal(WindowChrome.RailWidth, shell.Named<Border>("Rail").Bounds.Width);
        Assert.Equal(20, Row(shell, DestinationKind.Entries).GetVisualDescendants().OfType<KpIcon>().Single().Size);

        shell.Window.Width = 1800;
        shell.Frame();
        Assert.False(shell.Shell.IsNarrow);
        Assert.True(shell.Shell.ShowsSidebar);
    });

    private static Control ChosenRailPlace(RenderedShell shell)
    {
        var rail = shell.Named<ListBox>("RailNav");
        return rail.ContainerFromItem(rail.SelectedItem!)!;
    }
}
