using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>An empty place says it is empty in one line, and draws a helper line and a next step only when it has them.</summary>
public sealed class EmptyStateTests
{
    private static (bool Helper, bool Action) Shown(EmptyState empty)
    {
        var window = new Window { Width = 600, Height = 400, Content = empty };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var parts = empty.GetVisualDescendants().OfType<Control>().Where(part => part.TemplatedParent == empty).ToList();
        var shown = (parts.Single(part => part.Name == "PART_Helper").IsVisible, parts.Single(part => part.Name == "PART_ContentPresenter").IsVisible);
        Assert.Equal(empty.Text, parts.OfType<TextBlock>().Single(part => part.Name == "PART_Text").Text);

        window.Close();
        return shown;
    }

    [Fact]
    public Task A_line_alone_draws_no_helper_and_no_step() => HeadlessSession.On(() =>
        Assert.Equal((false, false), Shown(new EmptyState { Text = "No items yet" })));

    [Fact]
    public Task A_helper_and_a_step_are_drawn_under_the_line() => HeadlessSession.On(() =>
        Assert.Equal((true, true), Shown(new EmptyState { Text = "No projects yet", Helper = "A project is the items one app runs with.", Content = new Button { Content = "New project…" } })));
}
