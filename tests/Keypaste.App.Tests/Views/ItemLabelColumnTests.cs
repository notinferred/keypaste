using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The item pane's label column: dragged wider or narrower within its bounds, and kept for the next item.</summary>
public sealed class ItemLabelColumnTests
{
    [Fact]
    public Task Dragging_a_labels_edge_resizes_every_rows_label_and_the_next_item_keeps_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-label-column-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();
        shell.Frame();

        var grip = Grips(shell).First();
        var from = grip.TranslatePoint(new Point(4, grip.Bounds.Height / 2), shell.Window)!.Value;
        shell.Window.MouseDown(from, MouseButton.Left);
        shell.Window.MouseMove(from + new Point(50, 0));
        shell.Window.MouseUp(from + new Point(50, 0), MouseButton.Left);
        RenderedShell.Drain();
        shell.Frame();

        Assert.Equal(LabelColumn.Default + 50, entries.PaneLabels.Width, 0.5);
        Assert.All(Columns(shell), columns => Assert.Equal(LabelColumn.Default + 50, columns.ColumnDefinitions[0].Width.Value, 0.5));

        entries.PaneLabels.Width = 1000;
        Assert.Equal(FieldRow.MostLabelWidth, entries.PaneLabels.Width);

        entries.Selected = entries.Rows.First(row => row.Title != "github");
        RenderedShell.Drain();
        Assert.Equal(FieldRow.MostLabelWidth, entries.PaneLabels.Width);
    });

    private static List<Border> Grips(RenderedShell shell) =>
        [.. shell.Named<ContentControl>("EntryPane").GetVisualDescendants().OfType<Border>().Where(border => border.Name == "LabelGrip" && border.IsEffectivelyVisible)];

    private static List<Grid> Columns(RenderedShell shell) =>
        [.. shell.Named<ContentControl>("EntryPane").GetVisualDescendants().OfType<FieldRow>()
            .Where(row => row.IsEffectivelyVisible)
            .Select(row => row.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "Columns"))];
}
