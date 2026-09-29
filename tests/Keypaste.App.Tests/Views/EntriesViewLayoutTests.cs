using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// With an entry open, Items keeps its toolbar, the list, the divider and the entry's preview whole and
/// apart at the main window's default and minimum sizes (V-F.22, D-0344), and a comparison of two
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

    public static TheoryData<double, double, bool> Sizes => new()
    {
        { 1000, 680, false },
        { 960, 520, false },
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
                history.ToggleCommand.Execute(null);
                RenderedShell.Drain();
                history.Selected = history.Rows[0];
                RenderedShell.Drain();
            }

            shell.Frame();
            var at = $"{width}×{height}{(comparing ? ", comparing" : string.Empty)}";

            // The titlebar's is the only search (D-0374); the list has no filter box of its own.
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
            }

            foreach (var (name, bounds) in controls)
            {
                Assert.True(content.Contains(bounds), $"{name} at {bounds} leaves the window's content {content} at {at}");
            }

            AssertApart(controls, at);
            AssertApart(panes, at);
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
