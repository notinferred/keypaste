using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The one claim about the activity view that is only true of a real visual tree: its XAML parses,
/// its theme resources resolve, and the block a person copies from holds the model's lines.
/// </summary>
/// <remarks>
/// A compiled binding to a property that does not exist is already a build error, so this is not
/// here to check binding paths. It is here because a missing resource key or a control that does not
/// exist is a runtime failure, and the only other place it would surface is in front of a user.
/// </remarks>
public sealed class LogViewTests
{
    [Fact]
    public Task The_view_shows_the_lines_the_core_rendered() => HeadlessSession.On(() =>
    {
        using var home = new TempAuditHome();
        home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(home.Home);
        var window = new Window { Content = new LogView { DataContext = model } };

        window.Show();

        var shown = window.GetVisualDescendants()
            .OfType<SelectableTextBlock>()
            .Select(block => block.Text)
            .ToList();

        Assert.Contains(model.Text, shown);
        Assert.Contains("env/dev/STRIPE_KEY", model.Text, StringComparison.Ordinal);
    });
}
