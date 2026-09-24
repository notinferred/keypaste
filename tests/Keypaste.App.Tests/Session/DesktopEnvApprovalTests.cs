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
/// the command and the directory; only a press of Approve releases the set (V-E.1c).
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
            Assert.Equal("DEPLOY_KEY", Text(window, "KeysText"));
            Assert.Equal("deploy --to \"staging area\"", Text(window, "CommandText"));
            Assert.EndsWith("work", Text(window, "DirectoryText"), StringComparison.Ordinal);

            app.Arm();
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
    public Task Every_way_but_Approve_releases_nothing_and_takes_the_prompt_down(string how, EnvOutcome outcome) =>
        HeadlessSession.On(async () =>
        {
            await using var app = await PromptedApp.StartAsync();
            var reply = app.AskEnv();
            var window = await app.PromptAsync();
            app.Arm();

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

            app.Arm();
            Click(window, "Approve");
            Assert.Equal(EnvOutcome.Resolved, (await reply.WaitAsync(_wait, Token))?.Set.Outcome);
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

    private static (EnvApprovalWindow Window, string Layout) Layout(EnvPreview preview, IReadOnlyList<string> command, string directory)
    {
        var window = new EnvApprovalWindow(new EnvApprovalViewModel(EnvReleasePrompt.For(preview, command, directory)));
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
        [.. window.GetVisualDescendants().OfType<Button>().Where(button => button.Name is "Deny" or "Approve")];
}
