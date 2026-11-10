using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// With an entry open, Items keeps its list and the entry side by side and whole at the main window's default size and its minimum, a comparison of two revisions
/// takes the whole view, and where both cannot fit the entry replaces the list with a way back.
/// </summary>
/// <remarks>
/// Observed on Linux in F.2b3b: in the default window the toolbar was wider than the list's column,
/// so the search box shrank under Add and the entry's title met Delete. Bounds are read from the window's own layout after a frame is drawn, as <see cref="RenderedShell"/> lays out any screen.
/// </remarks>
public sealed class EntriesViewLayoutTests
{
    /// <summary>Room for a few words of a search, the least that still reads as a search box.</summary>
    private const double _searchMinimum = 160d;

    /// <summary>Room for two rows, the least that still reads as a list.</summary>
    private const double _listMinimum = 100d;

    public static TheoryData<double, double, bool> Sizes => new()
    {
        { 1000, 680, false },
        { 960, 520, false },
        { 1000, 680, true },
        { 960, 520, true },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public Task An_open_entry_leaves_the_list_and_the_entry_whole_and_apart(double width, double height, bool comparing) =>
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
            var controls = new Dictionary<string, Rect>
            {
                ["the entry's title"] = Bounds(shell, shell.Named<TextBlock>("EntryTitle")),
                ["Copy"] = Bounds(shell, shell.Named<Button>("CopyPassword")),
                ["Edit"] = Bounds(shell, shell.Named<Button>("EditItem")),
                ["the entry's menu"] = Bounds(shell, shell.Named<ToggleButton>("EntryMenu")),
            };
            var rows = shell.Named<ListBox>("Rows");
            var pane = Bounds(shell, shell.Named<ContentControl>("EntryPane"));
            var content = Bounds(shell, shell.Root);

            Assert.Equal(width, shell.Window.Bounds.Width);
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
                var search = Bounds(shell, shell.Named<TextBox>("ShellSearch"));
                panes["the list"] = list;
                controls["Search"] = search;
                controls["New"] = Bounds(shell, shell.Named<ToggleButton>("AddEntry"));
                Assert.True(search.Width >= _searchMinimum, $"the search box is {search.Width:0} px wide at {at}");
                Assert.Equal(ListDetail.ListWidth, shell.Named<Border>("ListColumn").Bounds.Width, 0.5);
                Assert.True(list.Height >= _listMinimum, $"the list is {list.Height:0} px tall at {at}");
                Assert.True(list.Right <= pane.Left, $"the list {list} is not left of the entry {pane} at {at}");

                // The web address's Copy and Open fit the entry's width, apart.
                var copy = Bounds(shell, shell.Named<Button>("CopyUrl"));
                var open = Bounds(shell, shell.Named<Button>("OpenUrl"));
                Assert.True(copy.Right <= open.Left, $"the web address {copy} runs into its Open {open} at {at}");
                Assert.True(pane.Left <= copy.Left && open.Right <= pane.Right, $"the web address row leaves the entry {pane} at {at}");

                Assert.False(shell.Named<Button>("BackToList").IsEffectivelyVisible, $"a Back shows beside the list at {at}");
            }

            foreach (var (name, bounds) in controls)
            {
                Assert.True(content.Contains(bounds), $"{name} at {bounds} leaves the window's content {content} at {at}");
            }

            AssertApart(controls, at);
            AssertApart(panes, at);
        });

    /// <summary>Where both cannot fit, the list stands alone until an item is chosen, the item replaces it with a Back, and Back returns.</summary>
    [Theory]
    [InlineData(720, 520)]
    [InlineData(840, 680)]
    public Task Where_both_cannot_fit_the_entry_replaces_the_list_with_a_Back(double width, double height) =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-entries-single-");
            shell.Window.Width = width;
            shell.Window.Height = height;
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            shell.Frame();
            var rows = shell.Named<ListBox>("Rows");
            var back = shell.Named<Button>("BackToList");
            var at = $"{width}×{height}";

            Assert.Equal(width, shell.Window.Bounds.Width);
            Assert.True(rows.IsEffectivelyVisible, $"the list is away with nothing chosen at {at}");
            Assert.False(back.IsEffectivelyVisible);

            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();
            shell.Frame();

            Assert.False(rows.IsEffectivelyVisible, $"the list stayed beside an entry with no room at {at}");
            Assert.True(shell.Named<ContentControl>("EntryPane").IsEffectivelyVisible);
            Assert.True(back.IsEffectivelyVisible, $"no way back at {at}");

            var content = Bounds(shell, shell.Root);
            Assert.True(content.Contains(Bounds(shell, shell.Named<Button>("CopyPassword"))), $"Copy password leaves the window at {at}");

            shell.Tap(back);
            RenderedShell.Drain();
            shell.Frame();

            Assert.Null(entries.Detail);
            Assert.True(rows.IsEffectivelyVisible);
            Assert.False(back.IsEffectivelyVisible);
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
