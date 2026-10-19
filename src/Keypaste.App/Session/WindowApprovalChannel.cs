using Avalonia.Controls;
using Avalonia.Threading;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;

namespace Keypaste.App.Session;

/// <summary>
/// Asks the person at this desktop, in a prompt window of its own, whether an agent may have one
/// field (D-0326) or a <c>keypaste run --session</c> may have a project's variables (D-0341), and
/// whether a change that gives an agent more may take effect; and tells them, in a window of its own,
/// when the change Always for asked for did not.
/// </summary>
/// <remarks>
/// <para>
/// The gate owns the deadline, the cooldown and one prompt at a time; this only puts the prompt on
/// screen, counts down the gate's window on it and reports the answer. Anything but a press of an
/// allow, or of Always for where the prompt offers it, is a denial: Deny, Escape, closing the window,
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
/// <param name="openSettings">What the Always for notice's Open settings does with the agent's name, or null to offer Close alone.</param>
internal sealed class WindowApprovalChannel(TimeProvider clock, TimeSpan answerWindow, Action<string>? openSettings = null) : IApprovalChannel
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TimeSpan _answerWindow = answerWindow;

    // Read and written on the UI thread only.
    private Showing? _showing;

    /// <summary>Raised on the UI thread once a prompt window, or a notice, is on screen.</summary>
    internal event EventHandler<Window>? Shown;

    public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new ApprovalViewModel(prompt);
        return ShowAsync(request, () => new ApprovalWindow(request), cancellationToken);
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

    /// <summary>
    /// Brings the prompt on screen forward with focus on Deny and its allows disarmed, arming them again
    /// <see cref="PromptViewModel.ArmingDelay"/> later (D-0456). Called on the UI thread, for the person's
    /// press of Show the prompt on Agents and nothing else.
    /// </summary>
    /// <returns>False when no prompt is on screen or it is already answered.</returns>
    internal bool BringForward()
    {
        Dispatcher.UIThread.VerifyAccess();

        if (_showing is not { } showing || showing.Request.IsAnswered)
        {
            return false;
        }

        showing.Request.Disarm();
        showing.Window.BringForward();
        _ = ArmAsync(showing.Request, showing.Withdrawn);
        return true;
    }

    public void Tell(AlwaysForNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var window = new NoticeWindow(notice, openSettings);
                window.Show();
                Shown?.Invoke(this, window);
            }
            catch (Exception)
            {
                // A notice that cannot be shown changes nothing: the release stands, and the change's line says why it did not.
            }
        });
    }

    private async ValueTask<ApprovalAnswer> ShowAsync(
        PromptViewModel request,
        Func<PromptWindow> create,
        CancellationToken cancellationToken)
    {
        PromptWindow? window = null;
        var started = _clock.GetTimestamp();
        var withdrawals = Withdrawals.Current;

        TimeSpan Remaining() => _answerWindow - _clock.GetElapsedTime(started);

        void TakeDown()
        {
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
            _ => Dispatcher.UIThread.Post(() => request.Tick(Remaining())), null, _tick, _tick);

        Dispatcher.UIThread.Post(() =>
        {
            // Withdrawn once requested: a token is marked before its callbacks run, so before the tokens linked below it are.
            if (request.IsAnswered || cancellationToken.IsCancellationRequested || withdrawals.Any(token => token.IsCancellationRequested))
            {
                return;
            }

            try
            {
                request.Tick(Remaining());
                window = create();
                window.Show();
                _showing = new Showing(request, window, cancellationToken);
                Shown?.Invoke(this, window);
                _ = ArmAsync(request, cancellationToken);
            }
            catch (Exception)
            {
                // A prompt that cannot be shown is a failure to ask, and a failure to ask is a no.
                request.Fail();
                TakeDown();
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

    private async Task ArmAsync(PromptViewModel request, CancellationToken cancellationToken)
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

        Dispatcher.UIThread.Post(() => request.Arm(arming));
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>The prompt on screen, its window, and the token that withdraws it.</summary>
    private sealed record Showing(PromptViewModel Request, PromptWindow Window, CancellationToken Withdrawn);
}
