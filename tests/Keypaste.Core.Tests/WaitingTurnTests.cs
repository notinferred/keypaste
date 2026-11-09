using System.Collections.Concurrent;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// F.80's acceptance at the gate: an agent's request that arrives while a prompt is open waits its turn and is prompted in
/// order, leaves the queue once withdrawn, a lock emptying it, and no agent has more than its bound waiting.
/// </summary>
public sealed class WaitingTurnTests : IDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly ManualClock _clock = new();
    private readonly TurnChannel _channel = new();
    private readonly ApprovalGate _gate;

    public WaitingTurnTests() => _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _gate.Dispose();

    private static ApprovalPrompt Prompt(string entry) => ApprovalPrompt.For("Claude Code", new EntryName("env/dev", entry), "password", "deploy");

    private Task<ApprovalAnswer> Ask(string entry, string? asker = "agent\0claude-code", CancellationToken? token = null) =>
        _gate.AskAsync($"{entry}|password", Prompt(entry), token ?? Token, asker).AsTask();

    [Fact]
    public async Task Two_requests_of_one_agent_are_each_prompted_in_the_order_they_came()
    {
        var first = Ask("FIRST");
        await _channel.ShownAsync(1);
        var second = Ask("SECOND");
        await Task.Delay(100, Token);

        Assert.Equal(["env/dev/FIRST"], _channel.Shown);

        _channel.Answer(0, ApprovalAnswer.ApprovedOnce);
        await _channel.ShownAsync(2);
        _channel.Answer(1, ApprovalAnswer.ApprovedOnce);

        Assert.Equal((ApprovalAnswer.ApprovedOnce, ApprovalAnswer.ApprovedOnce), (await first.WaitAsync(_wait, Token), await second.WaitAsync(_wait, Token)));
        Assert.Equal(["env/dev/FIRST", "env/dev/SECOND"], _channel.Shown);
    }

    [Fact]
    public async Task A_withdrawn_request_leaves_the_queue_and_is_never_shown()
    {
        var first = Ask("FIRST");
        await _channel.ShownAsync(1);
        using var withdraw = new CancellationTokenSource();
        var second = Ask("SECOND", token: withdraw.Token);
        await Task.Delay(100, Token);

        await withdraw.CancelAsync();

        Assert.Equal(ApprovalAnswer.Cancelled, await second.WaitAsync(_wait, Token));
        _channel.Answer(0, ApprovalAnswer.Denied);
        await first.WaitAsync(_wait, Token);

        var third = Ask("THIRD");
        await _channel.ShownAsync(2);

        Assert.Equal(["env/dev/FIRST", "env/dev/THIRD"], _channel.Shown);
        _channel.Answer(1, ApprovalAnswer.Denied);
        await third.WaitAsync(_wait, Token);
    }

    /// <summary>A prompt nobody answers times out in its own window, which starts when it shows, and the next takes the turn.</summary>
    [Fact]
    public async Task A_prompt_that_times_out_hands_the_turn_to_the_next()
    {
        var first = Ask("FIRST");
        await _channel.ShownAsync(1);
        var second = Ask("SECOND");
        await Task.Delay(100, Token);

        _clock.Advance(ApprovalLimits.Default.Window);

        Assert.Equal(ApprovalAnswer.TimedOut, await first.WaitAsync(_wait, Token));
        await _channel.ShownAsync(2);
        _channel.Answer(1, ApprovalAnswer.ApprovedOnce);
        Assert.Equal(ApprovalAnswer.ApprovedOnce, await second.WaitAsync(_wait, Token));
    }

    /// <summary>A lock withdraws every request through the token each was asked under, which empties the queue.</summary>
    [Fact]
    public async Task A_lock_empties_the_queue()
    {
        using var lockNow = new CancellationTokenSource();
        var first = Ask("FIRST", token: lockNow.Token);
        await _channel.ShownAsync(1);
        var waiting = new[] { Ask("SECOND", token: lockNow.Token), Ask("THIRD", "agent\0cursor", lockNow.Token) };
        await Task.Delay(100, Token);

        await lockNow.CancelAsync();

        Assert.All(await Task.WhenAll(waiting).WaitAsync(_wait, Token), answer => Assert.Equal(ApprovalAnswer.Cancelled, answer));
        Assert.Equal(ApprovalAnswer.Cancelled, await first.WaitAsync(_wait, Token));
        Assert.Single(_channel.Shown);
    }

    [Fact]
    public async Task No_agent_has_more_than_its_bound_waiting_and_a_request_naming_none_never_waits()
    {
        var first = Ask("FIRST");
        await _channel.ShownAsync(1);
        using var end = new CancellationTokenSource();
        List<Task<ApprovalAnswer>> waiting = [.. Enumerable.Range(0, ApprovalLimits.MaximumWaiting).Select(index => Ask($"WAITING{index}", token: end.Token))];
        var other = Ask("OTHER", "agent\0cursor", end.Token);
        await Task.Delay(100, Token);

        Assert.Equal(ApprovalAnswer.Busy, await Ask("ONE-TOO-MANY").WaitAsync(_wait, Token));
        Assert.Equal(ApprovalAnswer.Busy, await Ask("NAMELESS", asker: null).WaitAsync(_wait, Token));
        Assert.False(other.IsCompleted);

        await end.CancelAsync();
        await Task.WhenAll([.. waiting, other]).WaitAsync(_wait, Token);
        _channel.Answer(0, ApprovalAnswer.Denied);
        await first.WaitAsync(_wait, Token);
    }

    /// <summary>A request waiting behind the same one, which the person refuses, is held back by that refusal and not shown.</summary>
    [Fact]
    public async Task A_refusal_holds_back_the_same_request_waiting_behind_it()
    {
        var first = Ask("SAME");
        await _channel.ShownAsync(1);
        var again = Ask("SAME");
        await Task.Delay(100, Token);

        _channel.Answer(0, ApprovalAnswer.Denied);

        Assert.Equal(ApprovalAnswer.Denied, await first.WaitAsync(_wait, Token));
        Assert.Equal(ApprovalAnswer.Cooldown, await again.WaitAsync(_wait, Token));
        Assert.Single(_channel.Shown);
    }

    /// <summary>A channel whose prompts the test answers one by one, in the order they were shown.</summary>
    private sealed class TurnChannel : IApprovalChannel
    {
        private readonly ConcurrentQueue<string> _shown = [];
        private readonly ConcurrentDictionary<int, TaskCompletionSource<ApprovalAnswer>> _answers = [];

        internal IReadOnlyList<string> Shown => [.. _shown];

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            var answer = _answers.GetOrAdd(_shown.Count, _ => new TaskCompletionSource<ApprovalAnswer>(TaskCreationOptions.RunContinuationsAsynchronously));
            _shown.Enqueue(prompt.Entry);
            return await answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        internal void Answer(int index, ApprovalAnswer answer) =>
            _answers.GetOrAdd(index, _ => new TaskCompletionSource<ApprovalAnswer>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(answer);

        internal async Task ShownAsync(int count)
        {
            var deadline = DateTime.UtcNow + _wait;

            while (_shown.Count < count)
            {
                Assert.True(DateTime.UtcNow < deadline, $"only {_shown.Count} of {count} prompts were shown");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }
}
