using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An agent's run over the app's real endpoint raises the design's approval window: who asks, the
/// exact program and command line, where, each variable and its entry, and the agent's claim. Only a
/// press of Allow once or the timed allow releases anything (D-0358).
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

            Assert.Equal($"{DesktopApprovalTests.Label} wants 1 secret", Text(window, "TitleText"));
            Assert.StartsWith("via MCP · ", Text(window, "SubtitleText"), StringComparison.Ordinal);
            Assert.EndsWith(" · profile dev", Text(window, "SubtitleText"), StringComparison.Ordinal);
            Assert.Equal("tool: keypaste.run", Text(window, "ToolText"));
            Assert.Equal("deploy --to staging", Text(window, "CommandText"));
            Assert.StartsWith("runs ", Text(window, "ProgramText"), StringComparison.Ordinal);
            Assert.Contains("the command itself can read the values", Text(window, "ExplainerText"), StringComparison.Ordinal);
            Assert.Equal(TextWrapping.Wrap, window.FindControl<TextBlock>("CommandText")!.TextWrapping);
            Assert.Equal(TextTrimming.None, window.FindControl<TextBlock>("CommandText")!.TextTrimming);
            Assert.Equal("Allow for 15 minutes", window.FindControl<Button>("Approve")!.Content);

            Click(window, "Deny");
            Assert.Equal(EnvOutcome.Declined, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
            await PromptedApp.WithdrawnAsync(window);
            AutomationSurface.AssertNothingExposes(window, Sentinel);
        });

    [Fact]
    public Task AllowOnce_ReleasesTheRun_AndKeepsNothing() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskRun();
            var window = await app.PromptAsync();

            app.Arm();
            Click(window, "AllowOnce");
            var answered = await reply.WaitAsync(_wait, Token);

            Assert.Equal(EnvOutcome.Resolved, answered?.Set.Outcome);
            Assert.Equal(Sentinel, Assert.Single(answered!.Set.Variables).Value);
            Assert.Equal(AuditMethod.Prompt, answered.Method);
            Assert.Empty(app.Authority.Activity.EnvGrants);
        });

    [Fact]
    public Task AllowForTheWindow_ServesTheNextSameRun_AndAnEditEndsIt() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskRun();
            var window = await app.PromptAsync();

            app.Arm();
            Click(window, "Approve");
            Assert.Equal(EnvOutcome.Resolved, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
            await PromptedApp.WithdrawnAsync(window);

            var again = await app.AskRun().WaitAsync(_wait, Token);

            Assert.Equal(AuditMethod.GrantCache, again?.Method);
            Assert.Single(app.Windows);
            Assert.Equal("run", Assert.Single(app.Authority.Grants()).Kind);

            var vault = app.Authority.Session.Unlocked!;
            vault.UpdateEntry(vault.Find(new EntryName("env/ci", "DEPLOY_KEY"))! with { Password = "rotated" });

            Assert.Empty(app.Authority.Activity.EnvGrants);
        });

    [Fact]
    public Task BothAllowButtons_ArmAfterTheDelay() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskRun();
            var window = await app.PromptAsync();

            Click(window, "AllowOnce");
            Click(window, "Approve");
            await Task.Delay(200, Token);
            Assert.False(reply.IsCompleted, "a click before the prompt was armed answered it");

            app.Arm();
            Click(window, "AllowOnce");
            Assert.Equal(EnvOutcome.Resolved, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
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
            Assert.DoesNotContain(Buttons(hostile), button => button.IsDefault);

            ordinary.Close();
            hostile.Close();
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
        Variables = [new("DEPLOY_KEY", EntryPath, "password")],
        GrantSeconds = 900,
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

    private static List<Button> Buttons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "Deny" or "AllowOnce" or "Approve")];

    [Fact]
    public Task EnterOnOpen_Denies() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskRun();
            var window = await app.PromptAsync();
            app.Arm();

            Key(window, PhysicalKey.Enter);

            var answered = await reply.WaitAsync(_wait, Token);
            Assert.Equal(EnvOutcome.Declined, answered?.Set.Outcome);
            Assert.Empty(answered!.Set.Variables);
        });
}
