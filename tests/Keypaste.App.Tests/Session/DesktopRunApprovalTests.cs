using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An agent's run over the app's real endpoint raises the design's approval window: who asks, the
/// exact program and command line, where, each variable and its entry, and the agent's claim. Only a
/// press of Allow once or the timed allow releases anything.
/// </summary>
public sealed class DesktopRunApprovalTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheWindow_ShowsTheDesignsContent_AndNoValue() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskRun();
            var window = Assert.IsType<RunApprovalWindow>(await app.PromptAsync());

            Assert.Equal("Let claude-code run this command?", Text(window, "TitleText"));
            Assert.Equal("dev", Text(window, "ProfileText"));
            Assert.Equal("keypaste.run", Text(window, "ToolText"));
            Assert.Equal("deploy --to staging", Text(window, "CommandText"));
            Assert.False(string.IsNullOrEmpty(Text(window, "ProgramText")));
            Assert.Equal("deploy --to staging gets 1 value in its environment; keypaste removes it from its output.", Text(window, "ConsequenceText"));
            Assert.Equal("Allow only a command you would run yourself.", Text(window, "WarningText"));
            Assert.Equal(TextWrapping.Wrap, window.FindControl<TextBlock>("CommandText")!.TextWrapping);
            Assert.Equal(TextTrimming.None, window.FindControl<TextBlock>("CommandText")!.TextTrimming);
            Assert.Equal("Run once", window.FindControl<Button>("AllowOnce")!.Content);
            Assert.Null(window.FindControl<Button>("AlwaysAllow"));

            Click(window, "Deny");
            Assert.Equal(EnvOutcome.Declined, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
            await PromptedApp.WithdrawnAsync(window);
            AutomationSurface.AssertNothingExposes(window, Sentinel);
        });

    /// <summary>What the agent calls itself, where it runs and what it runs cannot move the window or its buttons.</summary>
    [Fact]
    public Task A_long_client_directory_and_command_leave_the_window_and_buttons_where_they_were() =>
        HeadlessSession.On(() =>
        {
            var (ordinary, ordinaryLayout) = Layout(Run("claude-code", "/work", "deploy"));
            var (hostile, hostileLayout) = Layout(Run(
                string.Join(' ', Enumerable.Repeat("claude-code", 60)),
                "/" + string.Join('/', Enumerable.Repeat("deep", 200)),
                "deploy " + string.Join(' ', Enumerable.Repeat("--flag=" + new string('x', 40), 90))));

            Assert.Equal(ordinaryLayout, hostileLayout);
            Assert.Equal(["Deny", "Run once"], Buttons(hostile).Select(button => button.Content as string));
            Assert.DoesNotContain(Buttons(hostile), button => button.IsDefault);

            ordinary.Close();
            hostile.Close();
        });

    /// <summary>The longest stage is drawn whole on one line beside a directory that wraps, and moves nothing.</summary>
    [Fact]
    public Task The_longest_stage_is_drawn_whole_and_moves_nothing() =>
        HeadlessSession.On(() =>
        {
            var longest = new string('w', EnvProfileNames.MaximumLength - 3) + "-eu";
            var (ordinary, ordinaryLayout) = Layout(Run("claude-code", "/work", "deploy"));
            var (window, layout) = Layout(
                Run("claude-code", "/home/alice/src/" + string.Join('/', Enumerable.Repeat("billing-api", 12)), "deploy") with { Profile = longest });

            Assert.Equal(longest, Drawn(window, "ProfileText"));
            Assert.Single(window.FindControl<TextBlock>("ProfileText")!.TextLayout.TextLines);
            Assert.Equal(TextTrimming.None, window.FindControl<TextBlock>("DirectoryText")!.TextTrimming);
            Assert.Equal(ordinaryLayout, layout);

            ordinary.Close();
            window.Close();
        });

    private static RunPrompt Run(string client, string directory, string command) => new()
    {
        Client = client,
        Reason = "Deploy the service.",
        ReasonWasTruncated = false,
        ReasonWasAltered = false,
        Program = "/usr/local/bin/deploy",
        Command = command,
        Directory = directory,
        Project = "ci",
        Profile = "dev",
        Variables = [new("DEPLOY_KEY", EntryPath, "DEPLOY_KEY")],
    };

    private static (RunApprovalWindow Window, string Layout) Layout(RunPrompt prompt)
    {
        var window = new RunApprovalWindow(new RunApprovalViewModel(prompt));
        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        var layout = string.Join(
            "; ",
            new[] { $"window {window.Bounds}" }.Concat(
                Buttons(window).Select(button => $"{button.Content} {button.TranslatePoint(default, window)} {button.Bounds.Size}")));

        return (window, layout);
    }
}
