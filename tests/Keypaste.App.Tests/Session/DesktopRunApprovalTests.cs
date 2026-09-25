using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Views;
using Keypaste.Core;
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

            Assert.Equal($"{DesktopApprovalTests.Label} wants to run a command with 1 secret", Text(window, "TitleText"));
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
