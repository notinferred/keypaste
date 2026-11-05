using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>The rail's list: a stack with its last two rows at its foot, where the sidebar keeps Trash and Settings. A <see cref="StackPanel"/> still, because a list's arrow keys walk only a navigable panel.</summary>
internal sealed class RailPanel : StackPanel
{
    private const int _rowsAtFoot = 2;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = Children.Where(row => row.IsVisible).ToList();
        var firstAtFoot = rows.Count - _rowsAtFoot;
        var footHeight = rows.Skip(firstAtFoot).Sum(row => row.DesiredSize.Height);
        var top = 0.0;

        for (var index = 0; index < rows.Count; index++)
        {
            if (index == firstAtFoot)
            {
                top = Math.Max(top, finalSize.Height - footHeight);
            }

            rows[index].Arrange(new Rect(0, top, finalSize.Width, rows[index].DesiredSize.Height));
            top += rows[index].DesiredSize.Height;
        }

        return finalSize;
    }
}
