using Avalonia.Threading;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;

namespace Keypaste.App.Session;

/// <summary>
/// Asks the person at this desktop, in a prompt window of its own, whether an agent may have one
/// field (D-0326).
/// </summary>
/// <remarks>
/// <para>
/// The gate owns the deadline, the cooldown and one prompt at a time; this only puts the prompt on
/// screen and reports the answer. Anything but a press of Approve is a denial: Deny, Escape, closing
/// the window, and every withdrawal, whether the gate's window ran out, the bridge hung up or the
/// vault locked.
/// </para>
/// <para>
/// A withdrawal decides the answer where it happens, on whatever thread that is, and takes the
/// window down on the UI thread: at once when it is already there, as a lock and quitting are, and
/// posted otherwise. A click after that decides nothing. Nothing here waits for the UI thread, so a
/// dispatcher that has stopped cannot hold a request open past its denial.
/// </para>
/// </remarks>
internal sealed class WindowApprovalChannel(TimeProvider clock) : IApprovalChannel
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Raised on the UI thread once a prompt window is on screen.</summary>
    internal event EventHandler<ApprovalWindow>? Shown;

    public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var request = new ApprovalViewModel(prompt);
        ApprovalWindow? window = null;

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

        Dispatcher.UIThread.Post(() =>
        {
            if (request.IsAnswered)
            {
                return;
            }

            try
            {
                window = new ApprovalWindow(request);
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

    private async Task ArmAsync(ApprovalViewModel request, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ApprovalViewModel.ArmingDelay, _clock, cancellationToken).ConfigureAwait(false);
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
