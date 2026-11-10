using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

public sealed class SortAndSizeTablesTests
{
    private static Rect InWindow(Visual control, TopLevel window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    private static ColumnHead Head(RenderedShell shell, string column) =>
        shell.Window.GetVisualDescendants().OfType<ColumnHead>().Single(head => head.Column == column);

    private static ColumnGrip Grip(RenderedShell shell, string column) =>
        shell.Window.GetVisualDescendants().OfType<ColumnGrip>().Single(grip => grip.Column == column);

    private static List<string> Titles(EntriesViewModel entries) => [.. entries.Rows.Select(row => row.Title)];

    private static List<string> Titles(TrashViewModel trash) => [.. trash.Rows.Select(row => row.Title)];

    private static TrashViewModel TrashOf(RenderedShell shell, params string[] titles)
    {
        var entries = Items(shell);

        foreach (var title in titles)
        {
            entries.Selected = entries.Rows.First(row => row.Title == title);
            entries.DeleteCommand.Press();

            if (entries.IsConfirmingDelete)
            {
                entries.ConfirmDeleteCommand.Press();
            }
        }

        var trash = shell.Show<TrashViewModel>(DestinationKind.Trash);
        shell.Frame();
        return trash;
    }

    private static void Press(RenderedShell shell, Control control)
    {
        var at = InWindow(control, shell.Window).Center;
        shell.Window.MouseMove(at);
        shell.Window.MouseDown(at, MouseButton.Left);
        shell.Window.MouseUp(at, MouseButton.Left);
        shell.Frame();
    }

    private static RenderedShell ShellWith(string prefix, int width, int height, params string[] titles)
    {
        var shell = new RenderedShell(
            prefix,
            vault =>
            {
                foreach (var title in titles)
                {
                    vault.AddEntry(new VaultEntry { Title = title, Password = "a password no row holds" });
                }
            });
        shell.Window.Width = width;
        shell.Window.Height = height;
        return shell;
    }

    private static TextBlock LabelOf(ColumnHead head) => head.GetVisualDescendants().OfType<TextBlock>().Single();

    private static void DeleteOne(RenderedShell shell)
    {
        var entries = Items(shell);
        entries.Selected = entries.Rows[0];
        entries.DeleteCommand.Press();

        if (entries.IsConfirmingDelete)
        {
            entries.ConfirmDeleteCommand.Press();
        }
    }

    private static EntriesViewModel Items(RenderedShell shell)
    {
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        shell.Frame();
        return entries;
    }

    [Fact]
    public void Text_compares_without_case_and_every_row_ties_the_same_way()
    {
        Assert.Equal(0, TableSorting.CompareText("github", "GitHub"));
        Assert.True(TableSorting.CompareText("apple", "Banana") < 0);

        (string Name, int At)[] rows = [("b", 1), ("A", 2), ("a", 3), (string.Empty, 4)];
        var ascending = TableSorting.Sorted(rows, new TableOrder("name", false), static (row, _) => new SortValue(row.Name), static (left, right) => right.At.CompareTo(left.At));
        var descending = TableSorting.Sorted(rows, new TableOrder("name", true), static (row, _) => new SortValue(row.Name), static (left, right) => right.At.CompareTo(left.At));

        Assert.Equal([3, 2, 1, 4], ascending.Select(row => row.At));
        Assert.Equal([1, 3, 2, 4], descending.Select(row => row.At));
    }

    [Fact]
    public void What_has_no_time_sorts_last_in_either_direction()
    {
        (long? Ticks, int At)[] rows = [(null, 1), (5, 2), (long.MaxValue, 3)];

        Assert.Equal([3, 2, 1], TableSorting.Sorted(rows, new TableOrder("t", true), static (row, _) => SortValue.At(row.Ticks), static (_, _) => 0).Select(row => row.At));
        Assert.Equal([2, 3, 1], TableSorting.Sorted(rows, new TableOrder("t", false), static (row, _) => SortValue.At(row.Ticks), static (_, _) => 0).Select(row => row.At));
    }

    [Fact]
    public void A_press_sorts_again_reverses_and_a_time_column_starts_at_the_newest()
    {
        var table = TableStates.NewItems();

        Assert.Null(table.Order);
        Assert.True(table.IsOwnWay);
        Assert.Equal("Title, not sorted", table.HeadName(TableStates.Title));

        table.Press(TableStates.Title);
        Assert.Equal(new TableOrder(TableStates.Title, false), table.Order);
        Assert.Equal("Title, sorted ascending", table.HeadName(TableStates.Title));

        table.Press(TableStates.Title);
        Assert.Equal("Title, sorted descending", table.HeadName(TableStates.Title));
        Assert.False(table.IsOwnWay);

        table.Press(TableStates.LastUsed);
        Assert.Equal("Last used, sorted newest first", table.HeadName(TableStates.LastUsed));
        table.Press(TableStates.LastUsed);
        Assert.Equal("Last used, sorted oldest first", table.HeadName(TableStates.LastUsed));

        table.Reset();
        Assert.Null(table.Order);
        Assert.True(table.IsOwnWay);
    }

    [Fact]
    public void A_sort_by_a_column_the_window_dropped_holds_only_while_it_shows()
    {
        var table = TableStates.NewItems();
        var changes = 0;
        table.OrderChanged += () => changes++;
        table.Press(TableStates.Username);
        changes = 0;

        table.Show(TableStates.Username, shows: false);
        Assert.Null(table.Order);
        Assert.Equal(1, changes);

        table.Show(TableStates.Username, shows: true);
        Assert.Equal(new TableOrder(TableStates.Username, false), table.Order);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Resizing_moves_one_edge_and_stops_at_each_columns_least()
    {
        var table = TableStates.NewTrash();
        double[] drawn = [200, 200, 160];

        table.Resize(TableStates.Title, 0, drawn);
        Assert.Null(table.Widths);

        table.Resize(TableStates.Title, 30, drawn);
        Assert.Equal([230d, 170d, 160d], table.Widths!);

        table.Resize(TableStates.Title, -1000, drawn);
        Assert.Equal([96d, 304d, 160d], table.Widths!);

        table.Resize(TableStates.Title, 1000, drawn);
        Assert.Equal([304d, 96d, 160d], table.Widths!);
    }

    [Fact]
    public void An_edge_beside_a_dropped_column_moves_the_next_column_showing()
    {
        var table = TableStates.NewItems();
        double[] drawn = [200, 0, 0, 88, 120, 104];

        table.Resize(TableStates.Title, 20, drawn);

        Assert.Equal(220, table.Widths![0]);
        Assert.Equal(68, table.Widths![3]);
        Assert.True(table.Widths![1] > 0, "the username column comes back with a width");
    }

    [Fact]
    public void Each_table_reads_back_what_it_kept_and_leaves_anything_else_as_its_own()
    {
        var states = new TableStates();
        Assert.Null(states.SortsText);
        Assert.Null(states.WidthsText);

        states.Items.Press(TableStates.Url);
        states.Trash.Resize(TableStates.Title, 10, [200, 200, 160]);

        var read = TableStates.Read(states.SortsText, states.WidthsText);

        Assert.Equal("items:url:asc", states.SortsText);
        Assert.Equal("trash:210,190,160", states.WidthsText);
        Assert.Equal(states.Items.Order, read.Items.Order);
        Assert.Equal([210d, 190d, 160d], read.Trash.Widths!);

        var odd = TableStates.Read("items:password:asc,trash:title:up,nothing", "items:1,2;log:1,2,3,4,5,x");

        Assert.Null(odd.Items.Order);
        Assert.Null(odd.Items.Widths);
        Assert.Equal(TableStates.Deleted, odd.Trash.Order!.Value.Column);
    }

    [Fact]
    public Task A_head_is_a_named_control_of_at_least_20_by_20_and_a_press_on_its_grip_does_not_sort() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-heads-", 1280, 800, "beta", "Alpha", "gamma");
        var trash = TrashOf(shell, "beta", "Alpha", "gamma");
        var table = shell.Shell.Tables.Trash;
        var head = Head(shell, TableStates.Title);
        var grip = Grip(shell, TableStates.Title);
        var order = Titles(trash);

        Assert.Equal("Title, not sorted", AutomationProperties.GetName(head));
        Assert.Equal("Resize the Title column", AutomationProperties.GetName(grip));
        Assert.True(head.Bounds is { Width: >= 20, Height: >= 20 }, $"the head is {head.Bounds}");
        Assert.True(grip.Bounds is { Width: >= 20, Height: >= 20 }, $"the grip is {grip.Bounds}");

        Press(shell, grip);
        Assert.Equal(TableStates.Deleted, table.Order?.Column);
        Assert.Equal(order, Titles(trash));

        Press(shell, head);
        Assert.Equal("Title, sorted ascending", AutomationProperties.GetName(head));
        Assert.Equal(["Alpha", "beta", "gamma"], Titles(trash).Where(title => title is "Alpha" or "beta" or "gamma"));

        Press(shell, head);
        Assert.Equal("Title, sorted descending", AutomationProperties.GetName(head));
        Assert.Equal(["gamma", "beta", "Alpha"], Titles(trash).Where(title => title is "Alpha" or "beta" or "gamma"));
    });

    [Fact]
    public Task An_edge_says_what_it_is_to_a_screen_reader_and_a_tooltip_and_reports_its_width() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-peer-", 1280, 800, "beta");
        _ = TrashOf(shell, "beta");
        var grip = Grip(shell, TableStates.Title);
        var columns = grip.FindAncestorOfType<TrashColumns>()!;

        Assert.Equal("Resize the Title column. The left and right arrow keys change the width.", ToolTip.GetTip(grip));

        var peer = ControlAutomationPeer.CreatePeerForElement(grip);
        Assert.IsNotType<NoneAutomationPeer>(peer);
        Assert.True(peer.IsControlElement());
        Assert.True(peer.IsKeyboardFocusable());
        Assert.Equal("Resize the Title column", peer.GetName());
        Assert.Equal("The left and right arrow keys change the width.", peer.GetHelpText());

        var width = peer.GetProvider<IRangeValueProvider>()!;
        Assert.Equal(columns.DrawnWidths[0], width.Value, 1d);
        Assert.Equal(TableStates.TrashColumns[0].Least, width.Minimum);
        Assert.True(width.Maximum > width.Value);

        var reported = new List<double>();
        peer.PropertyChanged += (_, change) =>
        {
            if (change.Property == RangeValuePatternIdentifiers.ValueProperty)
            {
                reported.Add((double)change.NewValue!);
            }
        };

        var asked = width.Value + 16;
        width.SetValue(asked);
        shell.Frame();

        Assert.Equal(asked, columns.DrawnWidths[0], 1d);
        Assert.Equal(asked, Assert.Single(reported), 1d);
    });

    [Fact]
    public Task An_edge_draws_the_control_edge_and_changes_under_the_pointer() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-hover-", 1280, 800, "beta");
        _ = TrashOf(shell, "beta");
        var grip = Grip(shell, TableStates.Title);
        var line = grip.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("column-grip-line"));

        Color Resource(string key) =>
            Assert.IsAssignableFrom<ISolidColorBrush>(shell.Window.FindResource(shell.Window.ActualThemeVariant, key)).Color;
        Color Drawn() => Assert.IsAssignableFrom<ISolidColorBrush>(line.Background).Color;

        Assert.Equal(Resource("KpBorderControl"), Drawn());

        shell.Window.MouseMove(InWindow(grip, shell.Window).Center);
        shell.Frame();

        Assert.Equal(Resource("TextControlBorderBrushPointerOver"), Drawn());
    });

    [Fact]
    public Task A_head_reads_whole_with_no_sort_at_the_narrowest_sidebar_width() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-whole-", 840, 800, "beta");
        _ = TrashOf(shell, "beta");

        foreach (var head in shell.Window.GetVisualDescendants().OfType<ColumnHead>().Where(head => head.IsEffectivelyVisible))
        {
            Assert.All(LabelOf(head).TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"{head.Column} is cut"));
        }
    });

    [Fact]
    public Task At_its_least_a_sorted_heads_arrow_stays_clear_of_its_edge_and_a_target_is_left() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-least-trash-", 1280, 800, "beta");
        DeleteOne(shell);
        shell.Show<TrashViewModel>(DestinationKind.Trash);
        shell.Frame();

        var table = shell.Shell.TableInView!;

        foreach (var grip in shell.Window.GetVisualDescendants().OfType<ColumnGrip>().ToList())
        {
            var columns = grip.FindAncestorOfType<TableColumns>()!;
            var index = table.Columns.ToList().FindIndex(column => column.Key == grip.Column);

            table.Reset();
            shell.Frame();
            table.Resize(grip.Column, -5000, columns.DrawnWidths);
            shell.Frame();
            Assert.Equal(table.Columns[index].Least, columns.DrawnWidths[index], 1d);

            table.Press(grip.Column);
            shell.Frame();

            var head = Head(shell, grip.Column);
            var arrow = head.GetVisualDescendants().OfType<KpIcon>().Single();
            var edge = InWindow(grip, shell.Window).Left;
            var arrowEnds = InWindow(arrow, shell.Window).Right;
            Assert.True(arrowEnds <= edge, $"{grip.Column}'s arrow ends at {arrowEnds}, its edge starts at {edge}");
            Assert.True(edge - InWindow(head, shell.Window).Left >= 20, $"{grip.Column}'s head has less than a 20 wide target left of its edge");
        }

        table.Reset();
    });

    [Fact]
    public Task A_head_sorts_and_an_edge_moves_from_the_keyboard() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-keys-", 1280, 800, "beta", "Alpha");
        _ = TrashOf(shell, "beta", "Alpha");

        var head = Head(shell, TableStates.Title);
        head.Focus(NavigationMethod.Tab);
        WindowInput.Drain();
        var ring = Assert.Single(AdornerLayer.GetAdornerLayer(head)!.Children);
        Assert.True(ring.Clip is null || ring.Clip.Bounds.Contains(ring.Bounds), "the head's focus ring is clipped to the head");
        shell.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        shell.Frame();
        Assert.Equal(new TableOrder(TableStates.Title, false), shell.Shell.Tables.Trash.Order);

        var grip = Grip(shell, TableStates.Title);
        var before = grip.FindAncestorOfType<TrashColumns>()!.DrawnWidths[0];
        grip.Focus(NavigationMethod.Tab);
        shell.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        shell.Frame();
        Assert.Equal(before + 8, grip.FindAncestorOfType<TrashColumns>()!.DrawnWidths[0], 1d);
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public Task Dragging_an_edge_follows_the_pointer_and_every_row_stays_under_its_head(int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = ShellWith("keypaste-table-drag-", 1920, 1200, "beta", "Alpha");
        _ = TrashOf(shell, "beta", "Alpha");
        var grip = Grip(shell, TableStates.Title);
        var edge = InWindow(grip, shell.Window);
        var by = 60d;

        shell.Window.MouseMove(edge.Center);
        shell.Window.MouseDown(edge.Center, MouseButton.Left);
        WindowInput.Drain();
        shell.Window.MouseMove(edge.Center + new Point(by, 0));
        WindowInput.Drain();
        shell.Window.MouseUp(edge.Center + new Point(by, 0), MouseButton.Left);
        shell.Frame();

        Assert.Equal(edge.X + by, InWindow(grip, shell.Window).X, 1d);

        var row = shell.Window.GetVisualDescendants().OfType<TrashColumns>().First(columns => columns.Parent is Border { Classes: var classes } && classes.Contains("table-body-row"));
        Assert.Equal(InWindow(Head(shell, TableStates.DeletedFrom), shell.Window).X, InWindow(row.Children[1], shell.Window).X, 1d);
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public Task An_edge_of_a_table_with_gaps_between_its_columns_follows_the_pointer_too(int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = ShellWith("keypaste-table-gaps-", 1920, 1200);
        DeleteOne(shell);
        _ = shell.Show<TrashViewModel>(DestinationKind.Trash);
        shell.Frame();
        var head = Head(shell, TableStates.Title);
        var grip = Grip(shell, TableStates.Title);
        var columns = grip.FindAncestorOfType<TrashColumns>()!;
        Assert.Equal(head.Bounds.Width, columns.DrawnWidths[0], 1d);
        var edge = InWindow(grip, shell.Window);
        var by = -80d;

        shell.Window.MouseMove(edge.Center);
        shell.Window.MouseDown(edge.Center, MouseButton.Left);
        WindowInput.Drain();
        shell.Window.MouseMove(edge.Center + new Point(by, 0));
        WindowInput.Drain();
        shell.Window.MouseUp(edge.Center + new Point(by, 0), MouseButton.Left);
        shell.Frame();

        // Layout rounds a column to a whole design pixel.
        Assert.Equal(edge.X + by, InWindow(grip, shell.Window).X, percent / 100d + 0.01);
        Assert.Equal(head.Bounds.Width, columns.DrawnWidths[0], 1d);
        Assert.Equal(Head(shell, TableStates.Deleted).Bounds.Width, columns.DrawnWidths[2], 1d);
    });

    [Fact]
    public Task The_selected_row_stays_selected_and_in_view_and_the_pane_stays_open_through_a_sort() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-selection-", 1280, 800, [.. Enumerable.Range(0, 60).Select(number => $"item {number:00}")]);
        var entries = Items(shell);
        var rows = shell.Named<ListBox>("Rows");

        entries.Selected = entries.Rows.First(row => row.Title == "item 00");
        shell.Frame();
        var pane = entries.Detail;
        Assert.NotNull(pane);

        entries.Table.Press(TableStates.Title);
        entries.Table.Press(TableStates.Title);
        shell.Frame();

        Assert.Same(pane, entries.Detail);
        Assert.Equal("item 00", entries.Selected!.Title);
        var selected = Assert.IsType<EntryRow>(rows.SelectedItem);
        Assert.Equal("item 00", selected.Title);
        var container = rows.ContainerFromItem(selected);
        Assert.NotNull(container);
        Assert.True(InWindow(rows, shell.Window).Intersects(InWindow(container, shell.Window)), "the selected row left the list's view");
    });

    [Fact]
    public Task Sorting_applies_to_what_the_search_kept_and_never_clears_a_held_row() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-search-", 1280, 800, "needle b", "needle a", "other");
        var entries = Items(shell);

        entries.Search = "needle";
        entries.Table.Press(TableStates.Title);
        Assert.Equal(["needle a", "needle b"], Titles(entries));

        entries.Table.Press(TableStates.Title);
        Assert.Equal(["needle b", "needle a"], Titles(entries));
        Assert.Equal("needle", entries.Search);
    });

    [Fact]
    public Task A_press_shows_in_what_the_window_would_keep_and_Reset_takes_it_back() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-kept-", 1280, 800, "beta");
        var entries = Items(shell);
        var tables = shell.Commands.All.Single(command => command.Header == "Reset table order and widths");

        Assert.False(tables.CanRun());
        Assert.Null(shell.Shell.Tables.SortsText);
        Assert.Null(shell.Shell.Tables.WidthsText);

        entries.SortByCommand.Press(TableStates.Group);
        Assert.True(tables.CanRun());
        Assert.Equal("items:group:asc", shell.Shell.Tables.SortsText);

        tables.Run();
        shell.Frame();
        Assert.Null(shell.Shell.Tables.SortsText);
        Assert.Equal("Group, not sorted", entries.SortChoices.Single(choice => choice.Column == TableStates.Group).Spoken);
    });

    [Fact]
    public Task Trash_keeps_what_is_selected_through_a_sort_and_a_confirmation_stays_armed() => HeadlessSession.On(() =>
    {
        using var shell = ShellWith("keypaste-table-trash-", 1280, 800, "zeta", "Alpha");
        var entries = Items(shell);

        foreach (var title in new[] { "zeta", "Alpha" })
        {
            entries.Selected = entries.Rows.First(row => row.Title == title);
            entries.DeleteCommand.Execute(null);
        }

        var trash = shell.Show<TrashViewModel>(DestinationKind.Trash);
        trash.Load();
        shell.Frame();
        var list = shell.Window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Classes.Contains("table-body"));
        trash.Selected = trash.Rows.First(row => row.Title == "zeta");
        shell.Frame();
        trash.PurgeCommand.Execute(null);
        Assert.True(trash.IsConfirmingPurge);

        Press(shell, Head(shell, TableStates.Title));

        Assert.Equal(["Alpha", "zeta"], trash.Rows.Select(row => row.Title).Where(title => title is "Alpha" or "zeta"));
        Assert.Equal("zeta", trash.Selected!.Title);
        Assert.True(trash.IsConfirmingPurge);
        Assert.Equal("zeta", Assert.IsType<TrashRow>(list.SelectedItem).Title);
        Assert.Contains("zeta", trash.PurgePrompt, StringComparison.Ordinal);

        trash.CancelPurgeCommand.Execute(null);
    });

    [Fact]
    public void A_log_whose_clock_stepped_back_keeps_the_files_order_newest_first()
    {
        using var home = new TempAuditHome();
        home.AppendAt(AppClock.Start, "env/dev/FIRST");
        home.AppendAt(AppClock.Start.AddMinutes(60), "env/dev/SECOND");
        home.AppendAt(AppClock.Start.AddMinutes(-60), "env/dev/THIRD");

        var screen = new LogViewModel(home.Home);
        var written = screen.Rows.Select(row => row.Secrets).ToList();

        Assert.Equal(3, written.Count);
        Assert.Contains("THIRD", written[0], StringComparison.Ordinal);
        Assert.Contains("FIRST", written[2], StringComparison.Ordinal);
    }
}
