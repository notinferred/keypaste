using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The gate is where docs/PRODUCT.md law 3.2's "default is deny" stops being a sentence and becomes code,
/// so these tests are mostly about what it refuses rather than what it allows.
/// </summary>
/// <remarks>
/// No processes, no terminals, no vault: a fake channel and a clock the test moves by hand. That is
/// the whole reason the rules live in the core rather than inside a dialog implementation — a
/// forty-five second window is tested in microseconds, and every hostile ordering is reachable.
/// </remarks>
public sealed class ApprovalGateTests
{
    private static ApprovalPrompt Prompt() =>
        ApprovalPrompt.For("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password", "deploy billing", 300);

    private static (ApprovalGate Gate, ManualClock Clock) Build(IApprovalChannel channel, ApprovalLimits? limits = null)
    {
        var clock = new ManualClock();
        return (new ApprovalGate(channel, clock, limits ?? ApprovalLimits.Default), clock);
    }

    [Fact]
    public async Task AYesInsideTheWindow_IsTheOnlyThingThatApproves()
    {
        var (gate, _) = Build(new ScriptedChannel(ApprovalAnswer.Approved));
        using var owned = gate;

        Assert.Equal(ApprovalAnswer.Approved, await owned.AskAsync("k", Prompt(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANo_IsADenial()
    {
        var (gate, _) = Build(new ScriptedChannel(ApprovalAnswer.Denied));
        using var owned = gate;

        Assert.Equal(ApprovalAnswer.Denied, await owned.AskAsync("k", Prompt(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Silence is a denial. The channel here never answers at all, which is exactly what a human
    /// who has walked away from the keyboard looks like.
    /// </summary>
    [Fact]
    public async Task NoAnswerBeforeTheWindowCloses_IsADenial()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved) { Park = true };
        var (gate, clock) = Build(channel);
        using var owned = gate;

        var asking = owned.AskAsync("k", Prompt(), TestContext.Current.CancellationToken).AsTask();

        await channel.Entered.WaitAsync(TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds));

        Assert.Equal(ApprovalAnswer.TimedOut, await asking);

        // The prompt has to have been withdrawn, not merely abandoned. A dialog left on screen for
        // a request nobody is waiting for is how somebody approves something into the void.
        Assert.True(channel.WasWithdrawn);
    }

    /// <summary>
    /// The single most important test in this file, and the one a "simplification" would break: a
    /// channel that answers yes after the deadline must not release anything. The human's window is
    /// the human's window, whatever the channel decided to do about the token it was handed.
    /// </summary>
    [Fact]
    public async Task ADeadlinePassed_IsADenialEvenIfTheChannelSaysYes()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved) { Park = true, IgnoreWithdrawal = true };
        var (gate, clock) = Build(channel);
        using var owned = gate;

        var asking = owned.AskAsync("k", Prompt(), TestContext.Current.CancellationToken).AsTask();

        await channel.Entered.WaitAsync(TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds));

        // Only now does the channel get around to saying yes.
        channel.Release();

        var answer = await asking;

        Assert.Equal(ApprovalAnswer.TimedOut, answer);
        Assert.NotEqual(ApprovalAnswer.Approved, answer);
    }

    /// <summary>
    /// A channel spawns processes and reads terminals, so it can throw for reasons that have
    /// nothing to do with what the human wanted. docs/PRODUCT.md law 3.7 makes every one of them a denial,
    /// and none of them an exception out of a tool call.
    /// </summary>
    [Fact]
    public async Task AChannelThatThrows_IsADenialRatherThanAnException()
    {
        var (gate, _) = Build(new ThrowingChannel());
        using var owned = gate;

        Assert.Equal(ApprovalAnswer.Failed, await owned.AskAsync("k", Prompt(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACallerThatGivesUp_IsCancelledRatherThanDenied()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved) { Park = true };
        var (gate, _) = Build(channel);
        using var owned = gate;

        using var caller = new CancellationTokenSource();

        var asking = owned.AskAsync("k", Prompt(), caller.Token).AsTask();

        await channel.Entered.WaitAsync(TestContext.Current.CancellationToken);

        await caller.CancelAsync();

        Assert.Equal(ApprovalAnswer.Cancelled, await asking);
        Assert.True(channel.WasWithdrawn);
    }

    [Fact]
    public async Task ARequestArrivingCancelled_IsNeverShownToAnybody()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved);
        var (gate, _) = Build(channel);
        using var owned = gate;

        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        Assert.Equal(ApprovalAnswer.Cancelled, await owned.AskAsync("k", Prompt(), caller.Token));
        Assert.Equal(0, channel.Asked);
    }

    /// <summary>
    /// The SDK dispatches tool calls concurrently, so two requests really can arrive while a human
    /// is looking at a third. Refused rather than queued: a queue is a pipeline that eventually
    /// shows every prompt, which is the storm it was meant to prevent (THREATS.md T-11).
    /// </summary>
    [Fact]
    public async Task ASecondRequestWhileSomebodyIsDeciding_IsRefusedNotQueued()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved) { Park = true };
        var (gate, _) = Build(channel);
        using var owned = gate;

        var first = owned.AskAsync("a", Prompt(), TestContext.Current.CancellationToken).AsTask();

        await channel.Entered.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalAnswer.Busy, await owned.AskAsync("b", Prompt(), TestContext.Current.CancellationToken));

        // Exactly one prompt reached a human, which is the claim. Asserting the second answer alone
        // would pass for a gate that showed both and discarded one.
        Assert.Equal(1, channel.Asked);

        channel.Release();
        await first;
    }

    [Fact]
    public async Task OnceTheFirstIsAnswered_TheNextRequestIsAskedNormally()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved);
        var (gate, _) = Build(channel);
        using var owned = gate;

        Assert.Equal(ApprovalAnswer.Approved, await owned.AskAsync("a", Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(ApprovalAnswer.Approved, await owned.AskAsync("b", Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(2, channel.Asked);
    }

    /// <summary>
    /// "The human said no, ask again immediately" is the other half of the storm, and the half a
    /// busy check does not catch because the first prompt is long gone by then.
    /// </summary>
    [Fact]
    public async Task TheSameRequestRightAfterARefusal_IsDeniedWithoutAskingAgain()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Denied);
        var (gate, _) = Build(channel);
        using var owned = gate;

        Assert.Equal(ApprovalAnswer.Denied, await owned.AskAsync("same", Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(ApprovalAnswer.Cooldown, await owned.AskAsync("same", Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(1, channel.Asked);
    }

    [Fact]
    public async Task ADifferentRequest_IsNotHeldBackBySomebodyElsesCooldown()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Denied);
        var (gate, _) = Build(channel);
        using var owned = gate;

        await owned.AskAsync("one", Prompt(), TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalAnswer.Denied, await owned.AskAsync("two", Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(2, channel.Asked);
    }

    [Fact]
    public async Task ACooldownExpires()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Denied);
        var (gate, clock) = Build(channel);
        using var owned = gate;

        await owned.AskAsync("same", Prompt(), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultCooldownSeconds + 1));

        Assert.Equal(ApprovalAnswer.Denied, await owned.AskAsync("same", Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(2, channel.Asked);
    }

    /// <summary>
    /// An approval does not start a cooldown: a human who says yes should be able to say yes again
    /// to the next request rather than having their own answer held against them.
    /// </summary>
    [Fact]
    public async Task AnApprovalStartsNoCooldown()
    {
        var channel = new ScriptedChannel(ApprovalAnswer.Approved);
        var (gate, _) = Build(channel);
        using var owned = gate;

        await owned.AskAsync("same", Prompt(), TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalAnswer.Approved, await owned.AskAsync("same", Prompt(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AChannelWithNowhereToAsk_Denies()
    {
        var (gate, _) = Build(new ScriptedChannel(ApprovalAnswer.NoChannel));
        using var owned = gate;

        Assert.Equal(ApprovalAnswer.NoChannel, await owned.AskAsync("k", Prompt(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheGateRejectsNulls()
    {
        var clock = new ManualClock();

        Assert.Throws<ArgumentNullException>(() => new ApprovalGate(null!, clock, ApprovalLimits.Default));
        Assert.Throws<ArgumentNullException>(() => new ApprovalGate(new ScriptedChannel(ApprovalAnswer.Denied), null!, ApprovalLimits.Default));
        Assert.Throws<ArgumentNullException>(() => new ApprovalGate(new ScriptedChannel(ApprovalAnswer.Denied), clock, null!));
    }

    /// <summary>A channel that answers what it was told to, when it is told to.</summary>
    /// <remarks>
    /// Both completion sources are built with
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>, and that is not decoration:
    /// <see cref="ManualClock.Advance"/> runs timer callbacks on the calling thread, so an inline
    /// continuation would drag the rest of the gate onto the test thread mid-assertion.
    /// </remarks>
    private sealed class ScriptedChannel(ApprovalAnswer answer) : IApprovalChannel
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Asked { get; private set; }

        internal bool WasWithdrawn { get; private set; }

        /// <summary>Whether to wait for <see cref="Release"/> before answering.</summary>
        internal bool Park { get; init; }

        /// <summary>Whether to keep the human waiting even after being told to withdraw.</summary>
        internal bool IgnoreWithdrawal { get; init; }

        /// <summary>Completes once a request has genuinely reached the channel.</summary>
        internal Task Entered => _entered.Task;

        internal void Release() => _released.TrySetResult();

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;
            _entered.TrySetResult();

            if (!Park)
            {
                return answer;
            }

            using var registration = cancellationToken.Register(() =>
            {
                WasWithdrawn = true;

                if (!IgnoreWithdrawal)
                {
                    _released.TrySetResult();
                }
            });

            await _released.Task.ConfigureAwait(false);

            return answer;
        }
    }

    private sealed class ThrowingChannel : IApprovalChannel
    {
        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the dialog tool is not installed");
    }
}
