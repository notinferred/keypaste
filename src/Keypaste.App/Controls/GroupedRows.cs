using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// The rows of a group, each below the first drawing the hairline above it; the group's surface is its parent, a <c>Border.group</c>.
/// </summary>
/// <remarks>Works as an <see cref="ItemsControl"/>'s panel too; a row that hides takes its hairline with it.</remarks>
internal sealed class GroupedRows : StackPanel
{
    /// <summary>Inherited, so a <see cref="FieldRow"/> inside a list's item container still learns it follows another row.</summary>
    internal static readonly AttachedProperty<bool> FollowsARowProperty =
        AvaloniaProperty.RegisterAttached<GroupedRows, Control, bool>("FollowsARow", inherits: true);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shownOne = false;

        foreach (var child in Children)
        {
            child.SetValue(FollowsARowProperty, shownOne);
            shownOne |= child.IsVisible;
        }

        return base.ArrangeOverride(finalSize);
    }
}
