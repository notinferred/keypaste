using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The one claim about the activity view that is only true of a real visual tree: its XAML parses, its theme
/// resources resolve, and it draws the rows the core's reader parsed and the core's sentence about the file.
/// </summary>
/// <remarks>
/// A compiled binding to a property that does not exist is already a build error, so this is not here to check
/// binding paths. A missing resource key or control is a runtime failure, and this is where it surfaces first.
/// </remarks>
public sealed class LogViewTests
{
    [Fact]
    public Task The_view_shows_the_rows_the_core_read() => HeadlessSession.On(() =>
    {
        using var home = new TempAuditHome();
        home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(home.Home);
        var window = new Window { Content = new LogView { DataContext = model } };

        window.Show();

        var shown = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .ToList();

        Assert.Contains("STRIPE_KEY", shown);
        Assert.Contains(model.Summary, shown);
        Assert.Contains(model.ShortPath, shown);
        Assert.Contains(model.Rows[0].Result, shown);
    });
}
