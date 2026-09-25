using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Session;
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
/// A <c>keypaste run --session</c> request over the app's real endpoint raises the app's own prompt
/// window, drawn by Skia and clicked through hit-testing, naming the project, its variable names,
/// the command and the directory; only a press of Allow once or the timed allow releases the set (V-E.1c).
/// </summary>
public sealed class DesktopEnvApprovalTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task Approve_releases_the_set_and_the_prompt_shows_names_never_values() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskEnv();
            var window = Assert.IsType<EnvApprovalWindow>(await app.PromptAsync());

            Assert.Equal("ci", Text(window, "ProjectText"));
            Assert.Equal("dev", Text(window, "ProfileText"));
            Assert.Equal("DEPLOY_KEY", Text(window, "KeysText"));
            Assert.Equal("deploy --to \"staging area\"", Text(window, "CommandText"));
            Assert.EndsWith("work", Text(window, "DirectoryText"), StringComparison.Ordinal);

            await app.ArmAsync();
            Click(window, "Approve");
            var answered = await reply.WaitAsync(_wait, Token);

            Assert.NotNull(answered);
            Assert.Equal(EnvOutcome.Resolved, answered.Set.Outcome);
            Assert.Equal(Sentinel, Assert.Single(answered.Set.Variables).Value);
            await PromptedApp.WithdrawnAsync(window);
            AutomationSurface.AssertNothingExposes(window, Sentinel);
        });

    public static TheoryData<string, EnvOutcome> Refusals => new()
    {
        { "deny", EnvOutcome.Declined },
        { "escape", EnvOutcome.Declined },
        { "close", EnvOutcome.Declined },
        { "enter", EnvOutcome.Declined },
        { "timeout", EnvOutcome.Declined },
        { "lock", EnvOutcome.Locked },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public Task Every_way_but_an_allow_releases_nothing_and_takes_the_prompt_down(string how, EnvOutcome outcome) =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskEnv();
            var window = await app.PromptAsync();
            await app.ArmAsync();

            switch (how)
            {
                case "deny":
                    Click(window, "Deny");
                    break;
                case "escape":
                    Key(window, PhysicalKey.Escape);
                    break;
                case "close":
                    window.Close();
                    break;
                case "enter":
                    // Focus starts on Deny, so Enter refuses.
                    Key(window, PhysicalKey.Enter);
                    break;
                case "timeout":
                    app.Clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds));
                    break;
                case "lock":
                    app.Authority.Session.Lock(VaultLockReason.Manual);
                    break;
            }

            var answered = await reply.WaitAsync(_wait, Token);

            Assert.NotNull(answered);
            Assert.Equal(outcome, answered.Set.Outcome);
            Assert.Empty(answered.Set.Variables);
            await PromptedApp.WithdrawnAsync(window);
        });

    [Fact]
    public Task Approve_is_not_armed_until_the_prompt_has_been_up_for_a_second() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskEnv();
            var window = await app.PromptAsync();

            Click(window, "Approve");
            await Task.Delay(200, Token);
            Assert.False(reply.IsCompleted, "a click before the prompt was armed answered it");

            await app.ArmAsync();
            Click(window, "Approve");
            Assert.Equal(EnvOutcome.Resolved, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
        });

    [Fact]
    public Task EnvAllowForTheHour_TheSameRunIsNotAskedAgain() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskEnv();
            var window = await app.PromptAsync();

            Assert.Equal("Allow this command for 15 minutes", window.FindControl<Button>("Approve")!.Content);
            Assert.Contains("exactly this command", Text(window, "TimedCaptionText"), StringComparison.Ordinal);

            await app.ArmAsync();
            Click(window, "Approve");
            Assert.Equal(EnvOutcome.Resolved, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
            await PromptedApp.WithdrawnAsync(window);

            var again = await app.AskEnv().WaitAsync(_wait, Token);

            Assert.Equal(EnvOutcome.Resolved, again?.Set.Outcome);
            Assert.Equal(Sentinel, Assert.Single(again!.Set.Variables).Value);
            Assert.Single(app.Windows);
            Assert.Single(app.Authority.Activity.EnvGrants);
        });

    [Fact]
    public Task EnvAllowOnce_AsksAgain() =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskEnv();
            var window = await app.PromptAsync();

            await app.ArmAsync();
            Click(window, "AllowOnce");
            Assert.Equal(EnvOutcome.Resolved, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
            await PromptedApp.WithdrawnAsync(window);

            var again = app.AskEnv();
            var second = await app.PromptAsync(count: 2);
            Click(second, "Deny");

            Assert.Equal(EnvOutcome.Declined, (await again.WaitAsync(_wait, Token))?.Set.Outcome);
            Assert.Empty(app.Authority.Activity.EnvGrants);
        });

    /// <summary>A command, directory and names the runner sent cannot move the window or its buttons, however long they are.</summary>
    [Fact]
    public Task A_long_command_leaves_the_window_and_buttons_where_they_were() =>
        HeadlessSession.On(() =>
        {
            var (ordinary, ordinaryLayout) = Layout(new EnvPreview("ci", ["DEPLOY_KEY"]), ["deploy"], "/work");
            var (hostile, hostileLayout) = Layout(
                new EnvPreview("ci", [.. Enumerable.Range(0, 200).Select(i => $"KEY_{i}")]),
                ["deploy", .. Enumerable.Repeat("--flag=" + new string('x', 40), 90)],
                "/" + string.Join('/', Enumerable.Repeat("deep", 200)));

            Assert.Equal(ordinaryLayout, hostileLayout);
            Assert.DoesNotContain(Buttons(hostile), button => button.IsDefault);

            ordinary.Close();
            hostile.Close();
        });

    /// <summary>A requester the runner names is one trimmed line, however long it says it is.</summary>
    [Fact]
    public Task A_long_requester_leaves_the_window_and_buttons_where_they_were() =>
        HeadlessSession.On(() =>
        {
            var preview = new EnvPreview("ci", ["DEPLOY_KEY"]);
            var (ordinary, ordinaryLayout) = Layout(preview, ["deploy"], "/work", "claude-code");
            var (hostile, hostileLayout) = Layout(preview, ["deploy"], "/work", string.Join(' ', Enumerable.Repeat("claude-code", 60)));

            Assert.Equal(ordinaryLayout, hostileLayout);

            ordinary.Close();
            hostile.Close();
        });

    private static (EnvApprovalWindow Window, string Layout) Layout(
        EnvPreview preview, IReadOnlyList<string> command, string directory, string? requester = null)
    {
        var prompt = EnvReleasePrompt.For(preview, command, directory) with { Requester = requester };
        var window = new EnvApprovalWindow(new EnvApprovalViewModel(prompt));
        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        var layout = string.Join(
            "; ",
            new[] { $"window {window.Bounds}" }.Concat(
                Buttons(window).Select(button => $"{button.Content} {button.TranslatePoint(default, window)} {button.Bounds.Size}")));

        return (window, layout);
    }

    /// <summary>The window's own buttons; a scrolling box adds its scroll bar's, which move nothing that matters.</summary>
    private static List<Button> Buttons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "Deny" or "AllowOnce" or "Approve")];
}
