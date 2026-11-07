using Avalonia.Controls;
using Keypaste.App.Notifications;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;

namespace Keypaste.App.Session;

/// <summary>
/// Asks the person at this desktop, in a prompt window of its own, whether an agent may have one
/// field or a <c>keypaste run --session</c> may have a project's variables, and
/// whether a change that gives an agent more may take effect; and tells them, in a window of its own,
/// when the change Always allow asked for did not.
/// </summary>
/// <remarks>
/// <para>
/// The gate owns the deadline, the cooldown and one prompt at a time; this only puts the prompt on
/// screen, counts down the gate's window on it and reports the answer. Anything but a press of an
/// allow, or of Always allow where the prompt offers it, is a denial: Deny, Escape, closing the window,
/// and every withdrawal, whether the gate's window ran out, the bridge hung up or the vault locked.
/// </para>
/// <para>
/// A withdrawal decides the answer where it happens, on whatever thread that is, and takes the
/// window down on the UI thread: at once when it is already there, as a lock and quitting are, and
/// posted otherwise. A click after that decides nothing. Nothing here waits for the UI thread, so a
/// dispatcher that has stopped cannot hold a request open past its denial.
/// </para>
/// </remarks>
/// <param name="clock">The clock the arming delay and the countdown run on.</param>
/// <param name="answerWindow">The gate's window, which the countdown shows.</param>
/// <param name="openSettings">What the Always allow notice's Open settings does with the agent's name, or null to offer Close alone.</param>
/// <param name="unexpected">Keeps a report of a window that could not be shown, at the site it names, or null.</param>
/// <param name="openBrowserFill">What the not-paired notice's Open Settings does, or null to offer Close alone.</param>
/// <param name="notices">What may also post a notification as a prompt appears, or null to post none.</param>
internal sealed class WindowApprovalChannel(
    TimeProvider clock,
    TimeSpan answerWindow,
    Action<string>? openSettings = null,
    Action<Exception, string>? unexpected = null,
    Action? openBrowserFill = null,
    RequestNotices? notices = null) : IApprovalChannel
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TimeSpan _answerWindow = answerWindow;

    // Read and written on the UI thread only.
    private Showing? _showing;
    private PairingNoticeWindow? _pairingNotice;

    // Requests asked on any thread whose prompts the UI thread has not yet taken up.
    private int _queued;

    /// <summary>Raised on the UI thread once a prompt window, or a notice, is on screen.</summary>
    internal event EventHandler<Window>? Shown;

    public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new ApprovalViewModel(prompt);
        return ShowAsync(request, () => new ApprovalWindow(request), cancellationToken);
    }

    /// <summary>Asks the person to pick which of the items a request names the agent gets, in a prompt window of its own.</summary>
    public async ValueTask<ItemPick> AskToPickAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new ItemPickViewModel(prompt);
        var answer = await ShowAsync(request, () => new ItemPickWindow(request), cancellationToken).ConfigureAwait(false);

        return new ItemPick(answer, answer is ApprovalAnswer.ApprovedOnce or ApprovalAnswer.Always ? request.PickedIndex : null);
    }

    public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new EnvApprovalViewModel(prompt);
        return ShowAsync(request, () => new EnvApprovalWindow(request), cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(RunPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new RunApprovalViewModel(prompt);
        return ShowAsync(request, () => new RunApprovalWindow(request), cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new AccessChangeViewModel(prompt);
        return ShowAsync(request, () => new AccessChangeWindow(request), cancellationToken);
    }

    public ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new TerminalEditViewModel(prompt);
        return ShowAsync(request, () => new TerminalEditWindow(request), cancellationToken);
    }

    /// <summary>Asks the person who armed pairing to pair a browser, under a name they may change, in a prompt window of its own.</summary>
    public async ValueTask<PairingAnswer> AskAsync(PairingPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new PairingViewModel(prompt);
        var answer = await ShowAsync(request, () => new PairingWindow(request), cancellationToken).ConfigureAwait(false);

        return answer == ApprovalAnswer.ApprovedOnce ? new PairingAnswer(answer, request.Name) : new PairingAnswer(answer, null);
    }

    /// <summary>Tells the person a browser asked to pair while pairing was not on, in one notice at a time, which takes nothing down.</summary>
    public void Tell(PairingNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        UiThread.Post(() =>
        {
            // One is enough to say it; a browser asking again while it is up adds nothing to read.
            if (_pairingNotice is not null)
            {
                return;
            }

            try
            {
                var window = new PairingNoticeWindow(notice, openBrowserFill);
                window.Closed += (_, _) => _pairingNotice = null;
                _pairingNotice = window;
                window.Show();
                Shown?.Invoke(this, window);
            }
            catch (Exception ex)
            {
                // A notice that cannot be shown changes nothing: the request stays refused, and its line says why.
                _pairingNotice = null;
                unexpected?.Invoke(ex, CrashReporting.AlwaysNoticeSite);
            }
        });
    }

    /// <summary>Whether a prompt waits for the person's answer or is on its way to the screen, which no file dropped on the main window or system dialog goes ahead of.</summary>
    internal bool IsAsking => Volatile.Read(ref _queued) > 0 || _showing is { Request.IsAnswered: false };

    /// <summary>
    /// Brings the prompt on screen forward with focus on Deny and its allows disarmed, arming them again
    /// <see cref="PromptViewModel.ArmingDelay"/> later. Called on the UI thread, for the person's
    /// press of Show the prompt on Agents or in the notification, and nothing else.
    /// </summary>
    /// <returns>False when no prompt is on screen or it is already answered.</returns>
    internal bool BringForward()
    {
        UiThread.VerifyAccess();

        if (_showing is not { } showing || showing.Request.IsAnswered)
        {
            return false;
        }

        showing.Request.Disarm();
        showing.Window.BringForward();
        _ = ArmAsync(showing.Request, showing.Window, showing.Withdrawn);
        return true;
    }

    public void Tell(AlwaysNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        UiThread.Post(() =>
        {
            try
            {
                var window = new NoticeWindow(notice, openSettings);
                window.Show();
                Shown?.Invoke(this, window);
            }
            catch (Exception ex)
            {
                // A notice that cannot be shown changes nothing: the release stands, and the change's line says why it did not.
                unexpected?.Invoke(ex, CrashReporting.AlwaysNoticeSite);
            }
        });
    }

    private async ValueTask<ApprovalAnswer> ShowAsync(
        PromptViewModel request,
        Func<PromptWindow> create,
        CancellationToken cancellationToken)
    {
        PromptWindow? window = null;
        RaisedNotice? raised = null;
        var started = _clock.GetTimestamp();
        var withdrawals = Withdrawals.Current;

        TimeSpan Remaining() => _answerWindow - _clock.GetElapsedTime(started);

        void TakeDown()
        {
            raised?.Remove();
            raised = null;

            var shown = window;
            window = null;

            if (ReferenceEquals(_showing?.Request, request))
            {
                _showing = null;
            }

            shown?.Close();
        }

        using var withdrawal = cancellationToken.Register(() =>
        {
            request.Withdraw();
            OnUiThread(TakeDown);
        });

        using var countdown = _clock.CreateTimer(
            _ => UiThread.Post(() => request.Tick(Remaining())), null, _tick, _tick);

        Interlocked.Increment(ref _queued);

        UiThread.Post(() =>
        {
            // From here _showing says so; nothing before it on this thread reads the count.
            Interlocked.Decrement(ref _queued);

            // Withdrawn once requested: a token is marked before its callbacks run, so before the tokens linked below it are.
            if (request.IsAnswered || cancellationToken.IsCancellationRequested || withdrawals.Any(token => token.IsCancellationRequested))
            {
                return;
            }

            try
            {
                request.Tick(Remaining());
                window = create();

                // Before the prompt shows, because showing it can give keypaste the focus that decides whether this is in the background.
                var earned = notices?.Earned() == true;
                var shown = window;
                shown.Show();
                shown.Activated += (_, _) =>
                {
                    request.Disarm();
                    _ = ArmAsync(request, shown, cancellationToken);
                };
                shown.Deactivated += (_, _) => request.Disarm();
                _showing = new Showing(request, shown, cancellationToken);
                Shown?.Invoke(this, shown);
                _ = ArmAsync(request, shown, cancellationToken);

                if (earned)
                {
                    raised = notices!.Raise(() => _ = BringForward());
                }
            }
            catch (Exception ex)
            {
                // A prompt that cannot be shown is a failure to ask, and a failure to ask is a no.
                request.Fail();
                TakeDown();
                unexpected?.Invoke(ex, CrashReporting.PromptSite);
            }
        });

        try
        {
            return await request.Answer.ConfigureAwait(false);
        }
        finally
        {
            OnUiThread(TakeDown);
        }
    }

    /// <summary>Arms the prompt a second from now, unless it has been disarmed since or has not got focus, when its activation arms it a second after that.</summary>
    private async Task ArmAsync(PromptViewModel request, PromptWindow window, CancellationToken cancellationToken)
    {
        var arming = request.Arming;

        try
        {
            await Task.Delay(PromptViewModel.ArmingDelay, _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        UiThread.Post(() =>
        {
            if (window.IsActive)
            {
                request.Arm(arming);
            }
        });
    }

    private static void OnUiThread(Action action)
    {
        if (UiThread.IsCurrent)
        {
            action();
        }
        else
        {
            UiThread.Post(action);
        }
    }

    /// <summary>The prompt on screen, its window, and the token that withdraws it.</summary>
    private sealed record Showing(PromptViewModel Request, PromptWindow Window, CancellationToken Withdrawn);
}
