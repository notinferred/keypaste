using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One question in a prompt window, answered once.</summary>
/// <remarks>
/// <para>
/// The answer is a latch. The first of Approve, Deny, closing the window and a withdrawal decides
/// it and nothing afterwards changes it, so a click that lands after a lock, a timeout or a hang-up
/// approves nothing.
/// </para>
/// <para>
/// Approve is armed only after the prompt has been up for <see cref="ArmingDelay"/>: the window can
/// appear under a pointer that was about to click something else, and that click is not an
/// approval (D-0326). Denying needs no delay.
/// </para>
/// </remarks>
internal abstract class PromptViewModel : ObservableObject
{
    /// <summary>How long the prompt is shown before Approve can be pressed.</summary>
    internal static readonly TimeSpan ArmingDelay = TimeSpan.FromSeconds(1);

    private readonly TaskCompletionSource<ApprovalAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _armed;

    protected PromptViewModel()
    {
        ApproveCommand = new RelayCommand(() => Decide(ApprovalAnswer.Approved), () => _armed && !IsAnswered);
        DenyCommand = new RelayCommand(() => Decide(ApprovalAnswer.Denied), () => !IsAnswered);
    }

    internal RelayCommand ApproveCommand { get; }

    internal RelayCommand DenyCommand { get; }

    /// <summary>The person's answer, or a denial for every way the prompt went away without one.</summary>
    internal Task<ApprovalAnswer> Answer => _answer.Task;

    internal bool IsAnswered => _answer.Task.IsCompleted;

    /// <summary>Lets Approve be pressed. Called on the UI thread once the prompt has been up long enough.</summary>
    internal void Arm()
    {
        _armed = true;
        ApproveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>The window closed with no answer, which is a no. Called on the UI thread.</summary>
    internal void Closed() => Decide(ApprovalAnswer.Denied);

    /// <summary>Nobody is waiting for the answer any more. Safe from any thread.</summary>
    internal void Withdraw() => _answer.TrySetResult(ApprovalAnswer.Denied);

    /// <summary>The prompt could not be put in front of anybody. Safe from any thread.</summary>
    internal void Fail() => _answer.TrySetResult(ApprovalAnswer.Failed);

    private void Decide(ApprovalAnswer answer)
    {
        if (_answer.TrySetResult(answer))
        {
            ApproveCommand.RaiseCanExecuteChanged();
            DenyCommand.RaiseCanExecuteChanged();
        }
    }
}
