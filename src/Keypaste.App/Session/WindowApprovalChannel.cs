using Avalonia.Controls;
using Avalonia.Threading;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;

namespace Keypaste.App.Session;

/// <summary>
/// Asks the person at this desktop, in a prompt window of its own, whether an agent may have one
/// field (D-0326) or a <c>keypaste run --session</c> may have a project's variables (D-0341).
/// </summary>
/// <remarks>
/// <para>
/// The gate owns the deadline, the cooldown and one prompt at a time; this only puts the prompt on
/// screen, counts down the gate's window on it and reports the answer. Anything but a press of Allow
/// once or the timed allow is a denial: Deny, Escape, closing the window, and every withdrawal,
/// whether the gate's window ran out, the bridge hung up or the vault locked.
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
internal sealed class WindowApprovalChannel(TimeProvider clock, TimeSpan answerWindow) : IApprovalChannel
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly TimeSpan _answerWindow = answerWindow;

    /// <summary>Raised on the UI thread once a prompt window is on screen.</summary>
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

    private async ValueTask<ApprovalAnswer> ShowAsync(
        PromptViewModel request,
        Func<Window> create,
        CancellationToken cancellationToken)
    {
        Window? window = null;
        var started = _clock.GetTimestamp();

        TimeSpan Remaining() => _answerWindow - _clock.GetElapsedTime(started);

        void TakeDown()
        {
            var shown = window;
            window = null;
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
            if (request.IsAnswered)
            {
                return;
            }

            try
            {
                request.Tick(Remaining());
                window = create();
                window.Show();
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
        try
        {
            await Task.Delay(PromptViewModel.ArmingDelay, _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatcher.UIThread.Post(request.Arm);
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
}
