using System.Collections.Concurrent;

namespace Keypaste.Core.Approval;

/// <summary>The request in front of a person, as a list shows it.</summary>
/// <param name="Prompt">What the person is being shown.</param>
/// <param name="Remaining">How long is left before the gate answers for them.</param>
public sealed record WaitingRequest(ApprovalPrompt Prompt, TimeSpan Remaining);

/// <summary>An agent's run in front of a person, as a list shows it.</summary>
/// <param name="Prompt">What the person is being shown.</param>
/// <param name="Remaining">How long is left before the gate answers for them.</param>
public sealed record WaitingRun(RunPrompt Prompt, TimeSpan Remaining);

/// <summary>
/// The gate's one slot, kept from a person's Always for answer for the confirmation that follows it, so
/// no other request can come in front of them between the two.
/// </summary>
/// <remarks>Disposing it gives the slot back, once; the confirmation asked in it leaves it kept.</remarks>
public sealed class KeptSlot : IDisposable
{
    private ApprovalGate? _gate;

    internal KeptSlot(ApprovalGate gate)
    {
        _gate = gate;
    }

    /// <summary>Whether this is the slot of that gate, still kept.</summary>
    internal bool IsOf(ApprovalGate gate) => ReferenceEquals(Volatile.Read(ref _gate), gate);

    /// <inheritdoc/>
    public void Dispose() => Interlocked.Exchange(ref _gate, null)?.GiveBack();
}

/// <summary>A <c>keypaste run --session</c> request in front of a person, as a list shows it.</summary>
/// <param name="Prompt">What the person is being shown.</param>
/// <param name="Remaining">How long is left before the gate answers for them.</param>
public sealed record WaitingEnv(EnvReleasePrompt Prompt, TimeSpan Remaining);

/// <summary>
/// Puts one request in front of a human, enforces the deadline, and denies everything else.
/// </summary>
/// <remarks>
/// <para>
/// The gate exists so that no channel has to be trusted with the rules. A channel that forgets its
/// own timeout, throws, answers twice, or answers late cannot turn any of those into a release: the
/// deadline is measured here, and anything that is not a clean <see cref="ApprovalAnswer.Approved"/>
/// or <see cref="ApprovalAnswer.ApprovedOnce"/> arriving inside the window is a denial
/// (docs/PRODUCT.md laws 3.2 and 3.7).
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
    private readonly ConcurrentDictionary<string, Deadline> _cooldowns = new(StringComparer.Ordinal);
    private volatile Asking? _asking;
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

    /// <summary>The cooldown key for one field of one entry, the same whether a credential request or a run asked for it.</summary>
    /// <param name="handle">The entry's <see cref="EntryHandle"/>.</param>
    /// <param name="field">The field.</param>
    /// <returns>The key.</returns>
    internal static string FieldKey(string handle, string field) => $"{handle}|{field}";

    /// <summary>The request a person is being asked about now, or null when nobody is being asked.</summary>
    public WaitingRequest? Waiting =>
        _asking is { Prompt: { } prompt } asking ? new WaitingRequest(prompt, asking.Window.Remaining(_clock)) : null;

    /// <summary>The agent's run a person is being asked about now, or null.</summary>
    public WaitingRun? WaitingRun =>
        _asking is { Run: { } run } asking ? new WaitingRun(run, asking.Window.Remaining(_clock)) : null;

    /// <summary>The <c>keypaste run --session</c> request a person is being asked about now, or null.</summary>
    public WaitingEnv? WaitingEnv =>
        _asking is { Env: { } env } asking ? new WaitingEnv(env, asking.Window.Remaining(_clock)) : null;

    /// <summary>Asks a human, and answers for them when they cannot be asked.</summary>
    /// <param name="cooldownKey">
    /// What counts as "the same request" for the purposes of the post-denial cooldown. The caller
    /// chooses it, because only the caller knows which parts of a request are its identity.
    /// </param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The answer, which is a denial unless <see cref="ApprovalAnswers.Releases"/> says otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKey"/> or <paramref name="prompt"/> is null.</exception>
    public ValueTask<ApprovalAnswer> AskAsync(
        string cooldownKey,
        ApprovalPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);
        ArgumentNullException.ThrowIfNull(prompt);

        return AskThroughAsync([cooldownKey], new Asked(prompt, null, null), token => _channel.AskAsync(prompt, token), cancellationToken);
    }

    /// <summary>
    /// Asks a human about a credential request, as <see cref="AskAsync(string, ApprovalPrompt, CancellationToken)"/>
    /// does, and keeps the one slot when they answer Always for on a prompt that offered it.
    /// </summary>
    /// <param name="cooldownKey">What counts as "the same request" after a refusal.</param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>
    /// The answer, and for Always for the slot, which the caller asks the confirmation that follows in
    /// and then disposes, whatever became of the request.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKey"/> or <paramref name="prompt"/> is null.</exception>
    public async ValueTask<(ApprovalAnswer Answer, KeptSlot? Kept)> AskKeepingAsync(
        string cooldownKey,
        ApprovalPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);
        ArgumentNullException.ThrowIfNull(prompt);

        KeptSlot? kept = null;
        var answer = await AskThroughAsync(
            [cooldownKey],
            new Asked(prompt, null, null),
            token => _channel.AskAsync(prompt, token),
            cancellationToken,
            keep: answered => prompt.AlwaysFor is not null && answered == ApprovalAnswer.AlwaysForProject ? kept = new KeptSlot(this) : null).ConfigureAwait(false);

        return (answer, kept);
    }

    /// <summary>Asks a human about a project's env set, under the same window, slot and cooldown.</summary>
    /// <param name="cooldownKey">What counts as "the same request" after a refusal.</param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The answer, which is a denial unless <see cref="ApprovalAnswers.Releases"/> says otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKey"/> or <paramref name="prompt"/> is null.</exception>
    /// <remarks>
    /// One slot for every kind, so an env prompt and an agent's prompt are never on screen together
    /// (THREATS.md T-11). <see cref="Waiting"/> lists only an agent's credential request.
    /// </remarks>
    public ValueTask<ApprovalAnswer> AskAsync(
        string cooldownKey,
        EnvReleasePrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);
        ArgumentNullException.ThrowIfNull(prompt);

        return AskThroughAsync([cooldownKey], new Asked(null, null, prompt), token => _channel.AskAsync(prompt, token), cancellationToken);
    }

    /// <summary>Asks a human about an agent's run, under the same window, slot and cooldown.</summary>
    /// <param name="cooldownKeys">
    /// Each identity under which a refusal holds the next run back: a run cooling under any of them is
    /// refused unasked, and a refusal cools all of them.
    /// </param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The answer, which is a denial unless <see cref="ApprovalAnswers.Releases"/> says otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKeys"/> or <paramref name="prompt"/> is null.</exception>
    public ValueTask<ApprovalAnswer> AskAsync(
        IReadOnlyList<string> cooldownKeys,
        RunPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cooldownKeys);
        ArgumentNullException.ThrowIfNull(prompt);

        return AskThroughAsync(cooldownKeys, new Asked(null, prompt, null), token => _channel.AskAsync(prompt, token), cancellationToken);
    }

    /// <summary>Asks a human to confirm a change that gives an agent more, under the same window, slot and cooldown.</summary>
    /// <param name="cooldownKeys">Each identity under which a refusal holds the next change back: one cooling under any of them is refused unasked, and a refusal cools all of them.</param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="coolUnanswered">Whether a confirmation nobody answered, because it expired or was withdrawn, cools down as a refusal does.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The answer, which confirms nothing unless <see cref="ApprovalAnswers.Releases"/> says otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKeys"/> or <paramref name="prompt"/> is null.</exception>
    /// <remarks>
    /// A request for more that reaches the owner from an agent or a terminal may come from a program that
    /// gains by keeping a confirmation up until somebody presses yes, so its unanswered requests hold the
    /// next one back too (THREATS.md T-11).
    /// </remarks>
    public ValueTask<ApprovalAnswer> AskAsync(
        IReadOnlyList<string> cooldownKeys,
        AccessChangePrompt prompt,
        bool coolUnanswered,
        CancellationToken cancellationToken) =>
        AskAsync(cooldownKeys, prompt, coolUnanswered, null, cancellationToken);

    /// <summary>Asks a human to confirm a change that gives an agent more, in the slot Always for kept when there is one.</summary>
    /// <param name="cooldownKeys">Each identity under which a refusal holds the next change back.</param>
    /// <param name="prompt">What the human is shown.</param>
    /// <param name="coolUnanswered">Whether a confirmation nobody answered cools down as a refusal does.</param>
    /// <param name="kept">The slot <see cref="AskKeepingAsync"/> kept, which stays kept, or null to take the slot as any request does.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>The answer, which confirms nothing unless <see cref="ApprovalAnswers.Releases"/> says otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKeys"/> or <paramref name="prompt"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="kept"/> is not this gate's slot, or was given back.</exception>
    public ValueTask<ApprovalAnswer> AskAsync(
        IReadOnlyList<string> cooldownKeys,
        AccessChangePrompt prompt,
        bool coolUnanswered,
        KeptSlot? kept,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cooldownKeys);
        ArgumentNullException.ThrowIfNull(prompt);

        if (kept is not null && !kept.IsOf(this))
        {
            throw new ArgumentException("the slot is not this gate's, or was given back", nameof(kept));
        }

        return AskThroughAsync(cooldownKeys, new Asked(null, null, null), token => _channel.AskAsync(prompt, token), cancellationToken, coolUnanswered, inKept: kept is not null);
    }

    private async ValueTask<ApprovalAnswer> AskThroughAsync(
        IReadOnlyList<string> cooldownKeys,
        Asked listed,
        Func<CancellationToken, ValueTask<ApprovalAnswer>> ask,
        CancellationToken cancellationToken,
        bool coolUnanswered = false,
        Func<ApprovalAnswer, KeptSlot?>? keep = null,
        bool inKept = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return ApprovalAnswer.Cancelled;
        }

        if (cooldownKeys.Any(InCooldown))
        {
            return ApprovalAnswer.Cooldown;
        }

        // Wait(0), not WaitAsync: taking the slot must either succeed now or refuse now. Waiting
        // for it would queue the request behind a prompt a human has not answered yet.
        if (!inKept && !_oneAtATime.Wait(0, CancellationToken.None))
        {
            return ApprovalAnswer.Busy;
        }

        var kept = false;

        try
        {
            _asking = new Asking(listed.Prompt, listed.Run, listed.Env, Deadline.Starting(_clock, Limits.Window));

            var answer = await AskOnceAsync(ask, cancellationToken).ConfigureAwait(false);

            if (answer == ApprovalAnswer.Denied
                || (coolUnanswered && answer is ApprovalAnswer.TimedOut or ApprovalAnswer.Cancelled))
            {
                foreach (var cooldownKey in cooldownKeys)
                {
                    _cooldowns[cooldownKey] = Deadline.Starting(_clock, Limits.DenialCooldown);
                }
            }

            kept = !inKept && keep?.Invoke(answer) is not null;
            return answer;
        }
        finally
        {
            _asking = null;

            if (!inKept && !kept)
            {
                _oneAtATime.Release();
            }
        }
    }

    /// <summary>Gives back the slot a <see cref="KeptSlot"/> kept.</summary>
    internal void GiveBack()
    {
        try
        {
            _oneAtATime.Release();
        }
        catch (ObjectDisposedException)
        {
            // A gate closed while the slot was kept has nothing left to give it back to.
        }
    }

    private async ValueTask<ApprovalAnswer> AskOnceAsync(
        Func<CancellationToken, ValueTask<ApprovalAnswer>> ask,
        CancellationToken cancellationToken)
    {
        using var withdraw = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The window opens before the person is asked, so time spent putting the prompt up counts
        // against it rather than starting a fresh window once the prompt is showing.
        var window = Task.Delay(Limits.Window, _clock, withdraw.Token);

        Task<ApprovalAnswer> asking;

        try
        {
            // Separately from SettleAsync, because a channel can throw before it ever returns a
            // task — a missing dialog binary is exactly that shape — and an exception thrown
            // synchronously here would leave the gate entirely rather than becoming a denial.
            asking = ask(withdraw.Token).AsTask();
        }
        catch (OperationCanceledException)
        {
            await withdraw.CancelAsync().ConfigureAwait(false);
            return ApprovalAnswer.Cancelled;
        }
        catch (Exception)
        {
            await withdraw.CancelAsync().ConfigureAwait(false);
            return ApprovalAnswer.Failed;
        }

        var first = await Task.WhenAny(asking, window).ConfigureAwait(false);

        // Withdraw either way. When the channel answered first this only stops the window's timer;
        // when the window closed first it takes the prompt off the screen, and the gate then waits
        // for the channel to acknowledge — returning while a dialog is still up is how a human
        // approves a request nobody is waiting for any more.
        await withdraw.CancelAsync().ConfigureAwait(false);

        var settled = await SettleAsync(asking, cancellationToken).ConfigureAwait(false);

        // Both rules here rather than per branch, so no future branch can miss one. A yes after the
        // window closed is a no; a yes for an abandoned request is a no, because it would seed a
        // grant the agent's own retry then spends without a human seeing the second request.
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
    /// Public so the approver can consult it <em>before</em> an Automatic release, which is what
    /// makes "a person's explicit no outranks a mode; a mode never resurrects it" a property of the
    /// ordering rather than an accident of how the branches happen to fall today. <see cref="AskAsync(string, ApprovalPrompt, CancellationToken)"/> keeps its own check, so removing this one narrows nothing.
    /// </remarks>
    public bool IsInCooldown(string cooldownKey)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);

        return InCooldown(cooldownKey);
    }

    /// <summary>How long a refusal under this key still holds the same request back, or nothing when none does.</summary>
    /// <param name="cooldownKey">What counts as "the same request".</param>
    /// <returns>The time left, measured as <see cref="IsInCooldown"/> measures it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cooldownKey"/> is null.</exception>
    public TimeSpan CooldownLeft(string cooldownKey)
    {
        ArgumentNullException.ThrowIfNull(cooldownKey);

        return _cooldowns.TryGetValue(cooldownKey, out var refusal) ? refusal.Standing(_clock) : TimeSpan.Zero;
    }

    /// <summary>Tells the person, through the channel, that the change Always for asked for did not take effect.</summary>
    /// <param name="notice">What to tell them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="notice"/> is null.</exception>
    /// <remarks>
    /// It asks nothing, so it takes no slot and waits for nothing, and a channel that fails to show it,
    /// or a gate already closed, cannot undo the release the person already allowed.
    /// </remarks>
    public void Tell(AlwaysForNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        if (_disposed)
        {
            return;
        }

        try
        {
            _channel.Tell(notice);
        }
        catch (Exception)
        {
            // Saying it failed; the release and its line stand as they are.
        }
    }

    /// <summary>
    /// Whether a refusal still stands, on whichever clock has run <em>least</em>.
    /// </summary>
    /// <remarks>
    /// The opposite of the rule a grant uses, and deliberately. A grant expires on whichever clock
    /// ran furthest, because both kinds of clock error should cost at most another prompt. A
    /// cooldown is a person's "no", so the failure to avoid is ending it early: a wall clock nudged
    /// forward, or a machine that suspended, must not become a way to ask the same question again.
    /// <see cref="Deadline"/> holds both readings so each side can pick its own direction.
    /// </remarks>
    private bool InCooldown(string cooldownKey)
    {
        if (!_cooldowns.TryGetValue(cooldownKey, out var refusal))
        {
            return false;
        }

        if (refusal.StillInForce(_clock))
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

    private sealed record Asking(ApprovalPrompt? Prompt, RunPrompt? Run, EnvReleasePrompt? Env, Deadline Window);

    private readonly record struct Asked(ApprovalPrompt? Prompt, RunPrompt? Run, EnvReleasePrompt? Env);
}
