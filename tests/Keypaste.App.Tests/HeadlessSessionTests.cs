using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The headless session runs an asynchronous body to completion before it says it is done.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a test about a test harness, which is worth the words.</b>
/// <see cref="HeadlessSession.On(Action)"/> was the only overload, and an <c>async</c> lambda binds
/// to it as <c>async void</c>: the returned task completes at the body's first <c>await</c>, so
/// every assertion after that point runs outside the lifetime xunit observes. Four tests in
/// <c>MaskedInputAutomationTests</c> were written that way, two of them D-0099 differentials, and
/// they had been reported green without their assertions ever being watched (STEPS F.16).
/// </para>
/// <para>
/// <b>Two shapes were tried and rejected before this one, because a regression that does not
/// discriminate is worse than none.</b> Making one of those bodies throw after its <c>await</c>
/// does not turn the run red — it hangs it, because the exception reaches the headless dispatcher
/// with no test still listening. Setting a flag after <c>await Task.Yield()</c> passes either way:
/// the continuation is posted to the dispatcher, and the session pumps it before
/// <c>Dispatch(Action)</c> returns, so the flag is set regardless of which overload bound.
/// </para>
/// <para>
/// <b>What discriminates is a suspension the session cannot pump.</b> The body waits on a gate this
/// test owns and nothing else can complete. With only the <see cref="Action"/> overload the
/// returned task completes while the body is still parked on that gate, which is exactly the defect
/// and exactly what the first assertion refuses.
/// </para>
/// </remarks>
public sealed class HeadlessSessionTests
{
    [Fact]
    public async Task The_returned_task_does_not_complete_while_the_body_is_still_running()
    {
        // RunContinuationsAsynchronously so completing the gate below cannot run the rest of the
        // body inline on this thread and make the timing look better than it is.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;

        var run = HeadlessSession.On(async () =>
        {
            await gate.Task.ConfigureAwait(true);
            finished = true;
        });

        // Nothing but this test can complete the gate, so a body that is still inside it cannot
        // finish. If On says it is done anyway, it never looked at the body's task.
        var saidDone = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).ConfigureAwait(true) == run;

        Assert.False(
            saidDone,
            "On reported the body finished while it was parked on a gate nothing has completed: "
                + "an async lambda bound to the Action overload and ran as async void");

        gate.SetResult();
        await run;

        Assert.True(finished, "On returned before the asynchronous body had finished");
    }

    [Fact]
    public async Task A_synchronous_body_still_runs()
    {
        var ran = false;

        await HeadlessSession.On(() => ran = true);

        Assert.True(ran);
    }
}
