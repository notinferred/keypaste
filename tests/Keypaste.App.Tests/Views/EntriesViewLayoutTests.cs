using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// With an entry open, the Entries screen keeps its toolbar, its panes and their dividers whole and apart at
/// the main window's default and minimum sizes (V-F.22).
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

    /// <summary>Room for a short title beside its group, the least that still reads as a list.</summary>
    private const double _listMinimum = 100d;

    public static TheoryData<double, double, bool, bool> Sizes => new()
    {
        { 1000, 680, false, false },
        { 960, 520, false, false },
        { 1000, 680, false, true },
        { 960, 520, false, true },
        { 1000, 680, true, false },
        { 960, 520, true, false },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public Task An_open_entry_leaves_the_toolbar_panes_and_dividers_whole_and_apart(double width, double height, bool comparing, bool tree) =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell("keypaste-entries-layout-");
            shell.Window.Width = width;
            shell.Window.Height = height;
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();

            // The tree starts folded at these sizes, and opening it must not crowd anything out.
            var groups = shell.Named<ListBox>("Groups");
            Assert.False(groups.IsEffectivelyVisible, $"the group tree starts open at {width}×{height}");

            if (tree)
            {
                shell.Named<ToggleButton>("GroupsToggle").IsChecked = true;
                RenderedShell.Drain();
                Assert.True(groups.IsEffectivelyVisible);
            }

            if (comparing)
            {
                var history = entries.Detail!.History;
                history.ToggleCommand.Execute(null);
                RenderedShell.Drain();
                history.Selected = history.Rows[0];
                RenderedShell.Drain();
            }

            shell.Frame();
            var at = $"{width}×{height}{(comparing ? ", comparing" : string.Empty)}{(tree ? ", tree open" : string.Empty)}";

            var search = Bounds(shell, shell.Named<TextBox>("Search"));
            var controls = new Dictionary<string, Rect>
            {
                ["Search"] = search,
                ["New"] = Bounds(shell, shell.Named<Button>("AddEntry")),
                ["the entry's title"] = Bounds(shell, shell.Named<TextBlock>("EntryTitle")),
                ["Copy"] = Bounds(shell, shell.Named<Button>("CopyPassword")),
                ["Rotate"] = Bounds(shell, shell.Named<Button>("RotateFromPane")),
                ["the entry's menu"] = Bounds(shell, shell.Named<ToggleButton>("EntryMenu")),
            };
            var panes = new Dictionary<string, Rect>
            {
                ["the list"] = Bounds(shell, shell.Named<ListBox>("Rows")),
                ["the pane's divider"] = Bounds(shell, shell.Named<Rectangle>("PaneDivider")),
                ["the entry's pane"] = Bounds(shell, shell.Named<ContentControl>("EntryPane")),
            };

            if (tree)
            {
                panes["the group tree"] = Bounds(shell, shell.Named<ListBox>("Groups"));
                panes["the tree's divider"] = Bounds(shell, shell.Named<Rectangle>("TreeDivider"));
            }
            var content = Bounds(shell, shell.Root);

            var list = shell.Named<ListBox>("Rows").Bounds.Width;
            Assert.Equal(width, shell.Window.Bounds.Width);
            Assert.True(search.Width >= _searchMinimum, $"the search box is {search.Width:0} px wide at {at}");
            Assert.True(list >= _listMinimum, $"the list is {list:0} px wide at {at}");

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
