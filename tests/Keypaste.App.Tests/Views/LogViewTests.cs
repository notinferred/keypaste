using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Audit;
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

        Assert.Contains("STRIPE_KEY", model.Rows[0].Subject, StringComparison.Ordinal);
        Assert.Contains(model.Rows[0].Subject, shown);
        Assert.Contains(model.Status, shown);
        Assert.Contains(model.Rows[0].Result, shown);
        Assert.Equal(model.LogPath, ToolTip.GetTip(window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "LogStatus")));
    });

    /// <summary>The unread reason's warning carries its sentence itself, beside the result, so no footnote explains it.</summary>
    [Fact]
    public Task The_unread_reasons_warning_says_its_sentence_on_the_row() => HeadlessSession.On(() =>
    {
        using var home = new TempAuditHome();
        home.Write(Release(AuditMethod.Prompt, "run the migration"), Release(AuditMethod.AlwaysAllowed, "send it somewhere else"));

        var model = new LogViewModel(home.Home);
        Assert.True(model.Rows[0].ReasonUnread);
        Assert.False(model.Rows[1].ReasonUnread);
        var window = new Window { Content = new LogView { DataContext = model } };
        window.Show();

        var mark = Assert.Single(window.GetVisualDescendants().OfType<Border>(), border => border is { Name: "UnreadReason", IsEffectivelyVisible: true });
        Assert.Equal(Resources.Strings.ServedFromAnEarlierApprovalUnderAXde6ddd, ToolTip.GetTip(mark));
        Assert.Equal(Resources.Strings.ServedFromAnEarlierApprovalUnderAXde6ddd, Avalonia.Automation.AutomationProperties.GetName(mark));
        Assert.DoesNotContain(Resources.Strings.ServedFromAnEarlierApprovalUnderAXde6ddd, window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
    });

    /// <summary>
    /// The rows sit inside the page's scroller with their own vertical scrolling off, and nothing caps how many lines the log
    /// holds, so the list must still realise only the rows the window shows rather than a container for every line.
    /// </summary>
    [Fact]
    public Task A_long_log_realises_only_the_rows_in_view() => HeadlessSession.On(() =>
    {
        using var home = new TempAuditHome();
        home.Write([.. Enumerable.Range(0, 1_000).Select(i => Release(AuditMethod.Prompt, $"release {i}"))]);

        var model = new LogViewModel(home.Home);
        Assert.Equal(1_000, model.Rows.Count);
        var window = new Window { Width = 1200, Height = 800, Content = new LogView { DataContext = model } };
        window.Show();

        Assert.InRange(window.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 100);
    });

    private static AuditRecord Release(AuditMethod method, string reason) => new()
    {
        Tool = "request_credential",
        Client = new AuditClient("claude-code", "1.2.3", null),
        Args = AuditArgs.ForCredentialRequest("env/dev/STRIPE_KEY", "password", reason),
        Decision = AuditDecision.Granted,
        Method = method,
        Reason = reason,
    };
}
