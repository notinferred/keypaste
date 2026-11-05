using Avalonia.Controls;
using Keypaste.App.Notifications;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;
using static Keypaste.App.Tests.Session.DesktopApprovalTests;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// A request arriving while keypaste is in the background also posts a notification that says nothing of the request,
/// whose only action brings the prompt forward disarmed, and which is gone, press included, once the request is (Q.14).
/// </summary>
public sealed class SystemNotificationTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task A_background_request_posts_only_fixed_words_and_its_prompt_still_appears() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications();
            await using var app = await PromptedApp.StartAsync(notifications: system);
            var request = app.Credential();
            var reply = app.Ask();
            var window = await app.PromptAsync();
            await PromptedApp.UntilAsync(() => system.Posts.Count > 0);

            var post = Assert.Single(system.Posts);
            Assert.Same(SystemNotice.RequestWaiting, post.Notice);
            Assert.True(post.Notice.TimeSensitive);
            Assert.Equal("A request is waiting for your answer", post.Notice.Title);
            Assert.Equal("Show the prompt to see who is asking and for what.", post.Notice.Body);
            Assert.Equal("Show the prompt", post.Notice.Action);

            var said = string.Join('\n', post.Notice.Kind, post.Notice.Title, post.Notice.Body, post.Notice.Action);

            foreach (var held in new[] { Sentinel, DesktopApprovalTests.Label, request.ClientName, request.Reason, "DEPLOY_KEY", "env/ci", "agents.kdbx", request.Field })
            {
                Assert.DoesNotContain(held!, said, StringComparison.OrdinalIgnoreCase);
            }

            Assert.True(window.IsVisible);
            Assert.False(reply.IsCompleted, "posting a notification answered the request");
            Click(window, "Deny");
            Assert.Equal(AuditDecision.Denied, (await reply.WaitAsync(_wait, Token))!.Decision);
        });

    [Fact]
    public Task A_request_while_keypaste_is_in_front_posts_nothing_and_shows_its_prompt() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications();
            await using var app = await PromptedApp.StartAsync(notifications: system, inFront: () => true);
            var reply = app.Ask();
            var window = await app.PromptAsync();

            Assert.Empty(system.Posts);
            Assert.True(window.IsVisible);
            Click(window, "Deny");
            await reply.WaitAsync(_wait, Token);
        });

    [Fact]
    public Task Its_action_brings_the_prompt_forward_disarmed_on_Deny_and_answers_nothing() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications();
            await using var app = await PromptedApp.StartAsync(notifications: system);
            var reply = app.Ask();
            var window = await app.PromptAsync();
            await app.ArmAsync();
            window.FindControl<Button>("AllowOnce")!.Focus();
            await PromptedApp.UntilAsync(() => system.Posts.Count > 0);

            system.Posts[0].Pressed();
            WindowInput.Drain();

            Assert.False(window.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled, "the notification left the prompt armed");
            Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
            Assert.True(window.IsVisible);
            Click(window, "AllowOnce");
            await Task.Delay(200, Token);
            Assert.False(reply.IsCompleted, "a press of the notification or within its second answered the request");

            app.Clock.Advance(PromptViewModel.ArmingDelay);
            await PromptedApp.UntilAsync(() => window.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled);
            Click(window, "AllowOnce");
            Assert.Equal(AuditDecision.Granted, (await reply.WaitAsync(_wait, Token))!.Decision);
        });

    [Fact]
    public Task An_answer_removes_the_notification_and_its_late_press_reaches_nothing_even_the_next_prompt() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications();
            await using var app = await PromptedApp.StartAsync(notifications: system);
            var first = app.Ask();
            var window = await app.PromptAsync();
            await app.ArmAsync();
            Click(window, "AllowOnce");
            await first.WaitAsync(_wait, Token);
            await PromptedApp.UntilAsync(() => system.Posts is [{ Removed: true }]);

            system.Posts[0].Pressed();
            WindowInput.Drain();
            Assert.False(window.IsVisible);

            var next = app.AskEnv();
            var prompt = await app.PromptAsync(count: 2);
            await app.ArmAsync();
            Assert.Single(system.Posts);

            system.Posts[0].Pressed();
            WindowInput.Drain();
            Assert.True(prompt.FindControl<Button>("AllowOnce")!.IsEffectivelyEnabled, "a press left over from an ended request disarmed the next prompt");

            Click(prompt, "Deny");
            await next.WaitAsync(_wait, Token);
        });

    [Fact]
    public Task A_lock_removes_the_notification_and_its_late_press_opens_nothing() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications();
            await using var app = await PromptedApp.StartAsync(notifications: system);
            var reply = app.Ask();
            var window = await app.PromptAsync();

            app.Authority.Session.Lock(VaultLockReason.Manual);
            await PromptedApp.WithdrawnAsync(window);
            await reply.WaitAsync(_wait, Token);
            await PromptedApp.UntilAsync(() => system.Posts is [{ Removed: true }]);

            system.Posts[0].Pressed();
            WindowInput.Drain();

            Assert.False(window.IsVisible);
            Assert.Single(app.Windows);
        });

    [Fact]
    public Task A_press_from_before_a_lock_changes_nothing_on_the_next_sessions_prompt_while_that_prompts_own_press_works() =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications();
            await using var app = await PromptedApp.StartAsync(notifications: system);
            var reply = app.Ask();
            await app.PromptAsync();

            app.Authority.Session.Lock(VaultLockReason.Manual);
            await reply.WaitAsync(_wait, Token);
            await PromptedApp.UntilAsync(() => system.Posts is [{ Removed: true }]);

            await app.UnlockAgainAsync();
            app.Clock.Advance(RequestNotices.Spacing);
            var next = app.Ask();
            var prompt = await app.PromptAsync(count: 2);
            await PromptedApp.UntilAsync(() => system.Posts.Count == 2);
            await app.ArmAsync();
            var allow = prompt.FindControl<Button>("AllowOnce")!;

            system.Posts[0].Pressed();
            WindowInput.Drain();
            Assert.True(allow.IsEffectivelyEnabled, "a press left over from the last session disarmed this session's prompt");

            system.Posts[1].Pressed();
            await PromptedApp.UntilAsync(() => !allow.IsEffectivelyEnabled);

            Click(prompt, "Deny");
            Assert.Equal(AuditDecision.Denied, (await next.WaitAsync(_wait, Token))!.Decision);
        });

    [Theory]
    [InlineData(SystemAnswer.Refuses)]
    [InlineData(SystemAnswer.RefusesAfterAMoment)]
    [InlineData(SystemAnswer.Throws)]
    [InlineData(SystemAnswer.NeverAnswers)]
    [InlineData(SystemAnswer.Stalls)]
    public Task A_system_that_cannot_post_leaves_the_prompt_as_it_is(SystemAnswer mode) =>
        HeadlessSession.On(async () =>
        {
            using var system = new FakeNotifications { Answers = mode };
            await using var app = await PromptedApp.StartAsync(notifications: system);
            var reply = app.Ask();
            var window = await app.PromptAsync();
            await app.ArmAsync();

            Click(window, "AllowOnce");

            Assert.Equal(AuditDecision.Granted, (await reply.WaitAsync(_wait, Token))!.Decision);
        });

    [Fact]
    public void A_burst_of_requests_posts_one_until_the_spacing_has_passed()
    {
        using var system = new FakeNotifications();
        var clock = new ManualClock();
        var notices = new RequestNotices(system, clock, () => false);

        Assert.True(notices.Earned());

        for (var request = 0; request < 20; request++)
        {
            Assert.False(notices.Earned());
        }

        clock.Advance(RequestNotices.Spacing - TimeSpan.FromMilliseconds(1));
        Assert.False(notices.Earned());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(notices.Earned());
    }

    [Fact]
    public async Task A_notice_removed_before_the_system_answers_is_taken_down_when_it_does()
    {
        using var system = new FakeNotifications { Answers = SystemAnswer.Held };
        var raised = RaisedNotice.Raise(system, SystemNotice.VaultLocked, () => { });
        await PromptedApp.UntilAsync(() => system.Posts.Count > 0);

        raised.Remove();
        Assert.False(system.Posts[0].Removed);

        system.Release();
        Assert.True(await raised.AcceptedWithinAsync(_wait, TimeProvider.System));
        Assert.True(system.Posts[0].Removed);
    }

    [Fact]
    public async Task Removing_a_notice_twice_takes_it_down_once_and_a_failing_removal_stays_quiet()
    {
        using var system = new FakeNotifications { RemovalThrows = true };
        var raised = RaisedNotice.Raise(system, SystemNotice.VaultLocked, () => { });
        Assert.True(await raised.AcceptedWithinAsync(_wait, TimeProvider.System));

        raised.Remove();
        raised.Remove();

        Assert.Equal(1, system.Posts[0].Removals);
    }

    /// <summary>The ordinary macOS process a development build or this test runs in must never ask for the center, which would end it.</summary>
    [Fact]
    public async Task A_process_outside_an_app_bundle_posts_nothing_and_keeps_running()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Only macOS has the bundle's rule.");
            return;
        }

        Assert.False(MacNotifications.InBundle());
        using var notifications = SystemNotifications.ForThisProcess();
        Assert.Null(await notifications.PostAsync(SystemNotice.VaultLocked, () => { }));
    }

    /// <summary>A message the system does not answer ends the process, and posting needs a bundle and a person, so everything short of the post is run here.</summary>
    [Fact]
    public void What_macOS_is_given_reads_back_as_the_fixed_words_and_the_center_answers_every_message()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Only macOS has the center.");
            return;
        }

        var request = MacNotifications.Composed(SystemNotice.RequestWaiting, "an-id");
        Assert.Equal("an-id", request.Identifier);
        Assert.Equal(SystemNotice.RequestWaiting.Title, request.Title);
        Assert.Equal(SystemNotice.RequestWaiting.Body, request.Body);
        Assert.Equal(SystemNotice.RequestWaiting.Kind, request.Kind);
        Assert.Equal((nuint)2, request.Level);
        Assert.Empty(request.Unanswered);

        Assert.Equal((nuint)1, MacNotifications.Composed(SystemNotice.VaultLocked, "an-id").Level);
    }
}
