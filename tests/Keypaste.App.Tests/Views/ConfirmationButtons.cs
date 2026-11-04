using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The order DESIGN gives every confirmation, read from what a window draws.</summary>
internal static class ConfirmationButtons
{
    /// <summary>Asserts a Cancel is drawn beside an action, and that each drawn Cancel comes before the buttons beside it.</summary>
    internal static void AssertCancelComesFirst(Window window)
    {
        var drawn = window.GetVisualDescendants().OfType<Button>().Where(button => button is not ToggleButton && button.IsEffectivelyVisible).ToList();

        Assert.DoesNotContain(drawn, button => button.Content is string label && label.StartsWith("Keep ", StringComparison.Ordinal));

        var cancels = drawn.Where(button => button.Content is "Cancel").ToList();
        Assert.NotEmpty(cancels);

        foreach (var cancel in cancels)
        {
            var row = drawn.Where(button => ReferenceEquals(button.GetVisualParent(), cancel.GetVisualParent())).ToList();

            Assert.True(row.Count > 1, "Cancel is drawn with no action beside it");
            Assert.All(row.Where(button => !ReferenceEquals(button, cancel)), action => Assert.True(
                X(action, window) > X(cancel, window),
                $"{action.Content} is drawn before Cancel"));
        }
    }

    private static double X(Button button, Window window) => button.TranslatePoint(default, window)!.Value.X;
}
