using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// With an entry open, Items keeps its toolbar, the list, the divider and the entry's preview whole and
/// apart at the main window's default size and at its former minimum (V-F.22), and a comparison of two
/// revisions takes the whole view (D-0376).
/// </summary>
/// <remarks>
/// Observed on Linux in F.2b3b: in the default window the toolbar was wider than the list's column,
/// so the search box shrank under Add and the entry's title met Delete. Organize and Delete now live
/// in the entry's ⋯ menu, so the header's Copy, Rotate and ⋯ are what the title must stay clear of. Bounds are read from the
/// window's own layout after a frame is drawn, as <see cref="RenderedShell"/> lays out any screen.
/// </remarks>
public sealed class EntriesViewLayoutTests
{
    /// <summary>Room for a few words of a search, the least that still reads as a search box.</summary>
    private const double _searchMinimum = 160d;

    /// <summary>Room for two rows and the column heads, the least that still reads as a list.</summary>
    private const double _listMinimum = 100d;

    /// <summary>Room for a short username or the start of a web address.</summary>
    private const double _columnMinimum = 72d;

    public static TheoryData<double, double, bool> Sizes => new()
    {
        { 1000, 680, false },
        { 960, 520, false },
        { 840, 520, false },
        { 1000, 680, true },
        { 960, 520, true },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public Task An_open_entry_leaves_the_toolbar_list_and_preview_whole_and_apart(double width, double height, bool comparing) =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-entries-layout-");
            shell.Window.Width = width;
            shell.Window.Height = height;
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();

            if (comparing)
            {
                var history = entries.Detail!.History;
                history.ToggleCommand.Press();
                RenderedShell.Drain();
                history.Selected = history.Rows[0];
                RenderedShell.Drain();
            }

            shell.Frame();
            var at = $"{width}×{height}{(comparing ? ", comparing" : string.Empty)}";

            // The search in the list header is the only one, under the name it has always had.
            Assert.DoesNotContain(shell.Window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "Search");
            var search = Bounds(shell, shell.Named<TextBox>("ShellSearch"));
            var controls = new Dictionary<string, Rect>
            {
                ["Search"] = search,
                ["New"] = Bounds(shell, shell.Named<ToggleButton>("AddEntry")),
                ["the entry's title"] = Bounds(shell, shell.Named<TextBlock>("EntryTitle")),
                ["Copy"] = Bounds(shell, shell.Named<Button>("CopyPassword")),
                ["Rotate"] = Bounds(shell, shell.Named<Button>("RotateFromPane")),
                ["the entry's menu"] = Bounds(shell, shell.Named<ToggleButton>("EntryMenu")),
            };
            var rows = shell.Named<ListBox>("Rows");
            var pane = Bounds(shell, shell.Named<ContentControl>("EntryPane"));
            var content = Bounds(shell, shell.Root);

            Assert.Equal(width, shell.Window.Bounds.Width);
            Assert.True(search.Width >= _searchMinimum, $"the search box is {search.Width:0} px wide at {at}");
            Assert.True(content.Contains(pane), $"the entry's pane {pane} leaves the window's content {content} at {at}");

            var panes = new Dictionary<string, Rect> { ["the entry's pane"] = pane };

            if (comparing)
            {
                // Two sets of fields side by side need the whole view: the list steps away.
                Assert.False(rows.IsEffectivelyVisible, $"the list shows beside a comparison at {at}");
            }
            else
            {
                var list = Bounds(shell, rows);
                panes["the list"] = list;
                panes["the divider"] = Bounds(shell, shell.Named<GridSplitter>("PaneDivider"));
                Assert.True(list.Height >= _listMinimum, $"the list is {list.Height:0} px tall at {at}");
                Assert.True(list.Bottom <= pane.Top, $"the list {list} is not above the entry's preview {pane} at {at}");

                // The username and URL columns (D-0488) keep room for several characters each, side by side.
                var username = Bounds(shell, shell.Named<TextBlock>("UsernameHead"));
                var url = Bounds(shell, shell.Named<TextBlock>("UrlHead"));
                Assert.True(username.Width >= _columnMinimum, $"the username column is {username.Width:0} px wide at {at}");
                Assert.True(url.Width >= _columnMinimum, $"the URL column is {url.Width:0} px wide at {at}");
                Assert.True(username.Right <= url.Left, $"the username column {username} runs into the URL column {url} at {at}");
                Assert.True(content.Contains(url), $"the URL column {url} leaves the window's content {content} at {at}");

                // The web address's link and its own Copy fit the preview's width, apart (N.5).
                var link = Bounds(shell, shell.Named<Button>("OpenUrl"));
                var copy = Bounds(shell, shell.Named<Button>("CopyUrl"));
                Assert.True(link.Right <= copy.Left, $"the web address {link} runs into its Copy {copy} at {at}");
                Assert.True(pane.Left <= link.Left && copy.Right <= pane.Right, $"the web address row leaves the preview {pane} at {at}");
            }

            foreach (var (name, bounds) in controls)
            {
                Assert.True(content.Contains(bounds), $"{name} at {bounds} leaves the window's content {content} at {at}");
            }

            AssertApart(controls, at);
            AssertApart(panes, at);
        });

    /// <summary>At the minimum width the sidebar collapses to the rail and Username and URL go; what is left of the table, the toolbar and the preview stays whole (Q.10).</summary>
    [Theory]
    [InlineData(720, 520)]
    [InlineData(720, 680)]
    [InlineData(839, 520)]
    public Task A_narrow_window_lays_Items_out_without_the_sidebar_or_the_two_columns(double width, double height) =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-entries-narrow-");
            shell.Window.Width = width;
            shell.Window.Height = height;
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();
            shell.Frame();
            var at = $"{width}×{height}";

            Assert.Equal(width, shell.Window.Bounds.Width);
            Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible, $"the sidebar shows at {at}");
            Assert.False(shell.Named<TextBlock>("UsernameHead").IsEffectivelyVisible, $"the username column shows at {at}");
            Assert.False(shell.Named<TextBlock>("UrlHead").IsEffectivelyVisible, $"the URL column shows at {at}");

            var search = Bounds(shell, shell.Named<TextBox>("ShellSearch"));
            var controls = new Dictionary<string, Rect>
            {
                ["Search"] = search,
                ["New"] = Bounds(shell, shell.Named<ToggleButton>("AddEntry")),
                ["the entry's title"] = Bounds(shell, shell.Named<TextBlock>("EntryTitle")),
                ["Copy"] = Bounds(shell, shell.Named<Button>("CopyPassword")),
                ["Rotate"] = Bounds(shell, shell.Named<Button>("RotateFromPane")),
                ["the entry's menu"] = Bounds(shell, shell.Named<ToggleButton>("EntryMenu")),
            };
            var list = Bounds(shell, shell.Named<ListBox>("Rows"));
            var pane = Bounds(shell, shell.Named<ContentControl>("EntryPane"));
            var content = Bounds(shell, shell.Root);

            Assert.True(search.Width >= _searchMinimum, $"the search box is {search.Width:0} px wide at {at}");
            Assert.True(list.Height >= _listMinimum, $"the list is {list.Height:0} px tall at {at}");
            Assert.Equal(width - WindowChrome.RailWidth, list.Width, 0.5);
            Assert.True(list.Bottom <= pane.Top, $"the list {list} is not above the entry's preview {pane} at {at}");
            Assert.True(content.Contains(pane), $"the entry's pane {pane} leaves the window's content {content} at {at}");

            // Each row keeps its title, kind and group, side by side and inside the list.
            var row = shell.Named<ListBox>("Rows").ContainerFromIndex(0)!.GetVisualDescendants().OfType<EntryColumns>().Single();
            var title = row.Children[1];
            var username = row.Children[2];
            var url = row.Children[3];
            Assert.False(username.IsEffectivelyVisible, $"a row's username shows at {at}");
            Assert.False(url.IsEffectivelyVisible, $"a row's URL shows at {at}");
            Assert.True(title.Bounds.Width >= _columnMinimum, $"a row's title is {title.Bounds.Width:0} px wide at {at}");

            var link = Bounds(shell, shell.Named<Button>("OpenUrl"));
            var copy = Bounds(shell, shell.Named<Button>("CopyUrl"));
            Assert.True(link.Right <= copy.Left, $"the web address {link} runs into its Copy {copy} at {at}");
            Assert.True(pane.Left <= link.Left && copy.Right <= pane.Right, $"the web address row leaves the preview {pane} at {at}");

            foreach (var (name, bounds) in controls)
            {
                Assert.True(content.Contains(bounds), $"{name} at {bounds} leaves the window's content {content} at {at}");
            }

            AssertApart(controls, at);
        });

    private static void AssertApart(Dictionary<string, Rect> placed, string at)
    {
        foreach (var (first, a) in placed)
        {
            foreach (var (second, b) in placed.Where(pair => string.CompareOrdinal(pair.Key, first) > 0))
            {
                Assert.False(a.Intersects(b), $"{first} {a} overlaps {second} {b} at {at}");
            }
        }
    }

    private static Rect Bounds(RenderedShell shell, Control control) =>
        new(control.TranslatePoint(default, shell.Window)!.Value, control.Bounds.Size);
}
