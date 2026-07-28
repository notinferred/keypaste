using System.Collections.Concurrent;

namespace Keypaste.Core.Approval;

/// <summary>
/// Puts one request in front of a human, enforces the deadline, and denies everything else.
/// </summary>
/// <remarks>
/// <para>
/// The gate exists so that no channel has to be trusted with the rules. A channel that forgets its
/// own timeout, throws, answers twice, or answers late cannot turn any of those into a release: the
/// deadline is measured here, and anything that is not a clean
/// <see cref="ApprovalAnswer.Approved"/> arriving inside the window is a denial (docs/PRODUCT.md laws 3.2
/// and 3.7).
/// </para>
/// <para>
/// <b>One request in front of a human at a time.</b> The MCP SDK dispatches tool calls
/// concurrently — measured, and pinned by <c>ServerToolsTests.TwoToolCalls_RunAtTheSameTime</c> —
/// so without this two agents, or one agent twice, really can race two prompts onto one screen. A
/// second request is refused immediately rather than queued: a queue is a pipeline that eventually
/// shows every prompt, which is the storm it was supposed to prevent (THREATS.md T-11).
/// </para>
/// <para>
/// <b>Nothing here throws for an answer it does not like.</b> Cancellation comes back as
/// <see cref="ApprovalAnswer.Cancelled"/> and a channel's exception as
/// <see cref="ApprovalAnswer.Failed"/>, because the caller has to write an audit line for every one
/// of them before it answers the agent, and an exception is the shape most likely to skip that.
/// </para>
/// </remarks>
public sealed class ApprovalGate : IDisposable
{
    private readonly IApprovalChannel _channel;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Builds a gate over one channel.</summary>
    /// <param name="channel">Where a human is asked.</param>
    /// <param name="clock">The clock the window and the cooldown are measured on.</param>
    /// <param name="limits">The window, the TTL ceiling and the cooldown.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ApprovalGate(IApprovalChannel channel, TimeProvider clock, ApprovalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(limits);

        _channel = channel;
        _clock = clock;
        Limits = limits;
    }

    /// <summary>The window, TTL ceiling and cooldown in force.</summary>
    public ApprovalLimits Limits { get; }

    /// <summary>Asks a human, and answers for them when they cannot be asked.</summary>
    /// <param name="cooldownKey">
    /// What counts as "the same request" for the purposes of the post-denial cooldown. The caller
    /// chooses it, because only the caller knows which parts of a request are its identity.
    /// </param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The answer, which is a denial unless it is <see cref="ApprovalAnswer.Approved"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKey"/> or <paramref name="prompt"/> is null.</exception>
    public async ValueTask<ApprovalAnswer> AskAsync(
        string cooldownKey,
        ApprovalPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);
        ArgumentNullException.ThrowIfNull(prompt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return ApprovalAnswer.Cancelled;
        }

        if (InCooldown(cooldownKey))
        {
            return ApprovalAnswer.Cooldown;
        }

        // Wait(0), not WaitAsync: taking the slot must either succeed now or refuse now. Waiting
        // for it would queue the request behind a prompt a human has not answered yet.
        if (!_oneAtATime.Wait(0, CancellationToken.None))
        {
            return ApprovalAnswer.Busy;
        }

        try
        {
            var answer = await AskOnceAsync(prompt, cancellationToken).ConfigureAwait(false);

            if (answer == ApprovalAnswer.Denied)
            {
                _cooldowns[cooldownKey] = _clock.GetUtcNow() + Limits.DenialCooldown;
            }

            return answer;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async ValueTask<ApprovalAnswer> AskOnceAsync(
        ApprovalPrompt prompt,
        CancellationToken cancellationToken)
    {
        using var withdraw = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<ApprovalAnswer> asking;

        try
        {
            // Separately from SettleAsync, because a channel can throw before it ever returns a
            // task — a missing dialog binary is exactly that shape — and an exception thrown
            // synchronously here would leave the gate entirely rather than becoming a denial.
            asking = _channel.AskAsync(prompt, withdraw.Token).AsTask();
        }
        catch (OperationCanceledException)
        {
            return ApprovalAnswer.Cancelled;
        }
        catch (Exception)
        {
            return ApprovalAnswer.Failed;
        }

        var window = Task.Delay(Limits.Window, _clock, withdraw.Token);

        var first = await Task.WhenAny(asking, window).ConfigureAwait(false);

        // Withdraw either way. When the channel answered first this only stops the window's timer;
        // when the window closed first it takes the prompt off the screen, and the gate then waits
        // for the channel to acknowledge — returning while a dialog is still up is how a human
        // approves a request nobody is waiting for any more.
        await withdraw.CancelAsync().ConfigureAwait(false);

        var settled = await SettleAsync(asking, cancellationToken).ConfigureAwait(false);

        // The two rules that make a yes worthless, applied here rather than per branch so no future
        // branch can miss one. A yes that arrives after the window closed is a no, because the
        // human's window is the human's window whatever the channel did with the token it was
        // handed. And a yes for a request the caller has abandoned is a no, because nobody is
        // waiting for it: releasing would put a secret on a wire no one reads and seed a grant that
        // the agent's own retry then spends without a human ever seeing the second request.
        if (cancellationToken.IsCancellationRequested)
        {
            return ApprovalAnswer.Cancelled;
        }

        return first == asking ? settled : ApprovalAnswer.TimedOut;
    }

    private static async ValueTask<ApprovalAnswer> SettleAsync(
        Task<ApprovalAnswer> asking,
        CancellationToken cancellationToken)
    {
        try
        {
            return await asking.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? ApprovalAnswer.Cancelled
                : ApprovalAnswer.TimedOut;
        }
        catch (Exception)
        {
            // A channel spawns processes and reads terminals. Anything it can throw has to become
            // a denial here rather than an exception out of a tool call (docs/PRODUCT.md law 3.7).
            return ApprovalAnswer.Failed;
        }
    }

    /// <summary>Whether the same request was refused recently enough that asking again is refused.</summary>
    /// <param name="cooldownKey">What counts as "the same request".</param>
    /// <returns><see langword="true"/> if a refusal is still in force.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKey"/> is null.</exception>
    /// <remarks>
    /// Public so the approver can consult it <em>before</em> evaluating the policy, which is what
    /// makes "a person's explicit no outranks a rule; a rule never resurrects it" a property of the
    /// ordering rather than an accident of how the branches happen to fall today (DECISIONS.md
    /// D-0029). <see cref="AskAsync"/> keeps its own check, so removing this one narrows nothing.
    /// </remarks>
    public bool IsInCooldown(string cooldownKey)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);

        return InCooldown(cooldownKey);
    }

    private bool InCooldown(string cooldownKey)
    {
        if (!_cooldowns.TryGetValue(cooldownKey, out var until))
        {
            return false;
        }

        if (_clock.GetUtcNow() < until)
        {
            return true;
        }

        _cooldowns.TryRemove(cooldownKey, out _);
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _oneAtATime.Dispose();
    }
}
