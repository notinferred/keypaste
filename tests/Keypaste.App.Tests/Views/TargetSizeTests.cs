using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>A control's target is at least 20 by 20 (DESIGN rule 7, Q.8).</summary>
public sealed class TargetSizeTests
{
    private const double _minimum = 20;

    [Fact]
    public Task The_sidebars_folds_and_the_clear_scope_button_are_large_enough() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-targets-");
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.SelectedGroup = entries.Groups.First(group => !group.IsEverything);
        RenderedShell.Drain();

        var buttons = shell.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
        var folds = buttons.Where(button => button.Classes.Contains("fold")).ToList();

        Assert.NotEmpty(folds);
        Assert.All(folds, AssertLargeEnough);
        AssertLargeEnough(buttons.Single(button => button.Name == "ClearSearchScope"));
    });

    [Fact]
    public Task A_chips_remove_button_is_large_enough() => HeadlessSession.On(() =>
    {
        var remove = new Button { Classes = { "chip-remove" } };
        var window = new Window { Content = new StackPanel { Children = { remove } } };
        window.Show();
        RenderedShell.Drain();

        AssertLargeEnough(remove);
        window.Close();
    });

    private static void AssertLargeEnough(Button button) =>
        Assert.True(button.Bounds.Width >= _minimum && button.Bounds.Height >= _minimum, $"{button.Name} is {button.Bounds.Width}x{button.Bounds.Height}");
}
