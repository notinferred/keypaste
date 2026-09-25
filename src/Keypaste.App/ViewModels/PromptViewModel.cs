using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One question in a prompt window, answered once.</summary>
/// <remarks>
/// <para>
/// The answer is a latch. The first of Deny, Allow once, the timed allow, closing the window and a
/// withdrawal decides it and nothing afterwards changes it, so a click that lands after a lock, a
/// timeout or a hang-up approves nothing.
/// </para>
/// <para>
/// Both allow buttons are armed only after the prompt has been up for <see cref="ArmingDelay"/>:
/// the window can appear under a pointer that was about to click something else, and that click is
/// not an approval (D-0326). Denying needs no delay. The timed allow exists only when the prompt
/// offers a timed grant.
/// </para>
/// </remarks>
internal abstract class PromptViewModel : ObservableObject
{
    /// <summary>How long the prompt is shown before either allow button can be pressed.</summary>
    internal static readonly TimeSpan ArmingDelay = TimeSpan.FromSeconds(1);

    private readonly TaskCompletionSource<ApprovalAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _armed;
    private string _countdown = string.Empty;

    protected PromptViewModel()
    {
        ApproveCommand = new RelayCommand(() => Decide(ApprovalAnswer.Approved), () => _armed && OffersTimed && !IsAnswered);
        AllowOnceCommand = new RelayCommand(() => Decide(ApprovalAnswer.ApprovedOnce), () => _armed && !IsAnswered);
        DenyCommand = new RelayCommand(() => Decide(ApprovalAnswer.Denied), () => !IsAnswered);
    }

    /// <summary>Allows this request and keeps the timed grant the prompt offers.</summary>
    internal RelayCommand ApproveCommand { get; }

    /// <summary>Allows this request and keeps nothing.</summary>
    internal RelayCommand AllowOnceCommand { get; }

    internal RelayCommand DenyCommand { get; }

    /// <summary>Whether the prompt offers a timed grant beside "allow once".</summary>
    internal bool OffersTimed => TimedSeconds > 0;

    /// <summary>What the timed allow button says.</summary>
    internal virtual string TimedLabel => $"Allow for {ApprovalLimits.Describe(TimedSeconds)}";

    /// <summary>How long is left to answer, as <c>m:ss</c>.</summary>
    internal string Countdown
    {
        get => _countdown;
        private set => Set(ref _countdown, value);
    }

    /// <summary>The person's answer, or a denial for every way the prompt went away without one.</summary>
    internal Task<ApprovalAnswer> Answer => _answer.Task;

    internal bool IsAnswered => _answer.Task.IsCompleted;

    /// <summary>How long the timed grant lasts, or zero when the prompt offers only "allow once".</summary>
    protected abstract int TimedSeconds { get; }

    /// <summary>Lets the allow buttons be pressed. Called on the UI thread once the prompt has been up long enough.</summary>
    internal void Arm()
    {
        _armed = true;
        ApproveCommand.RaiseCanExecuteChanged();
        AllowOnceCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Shows how long is left to answer. Called on the UI thread.</summary>
    internal void Tick(TimeSpan remaining)
    {
        var seconds = remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);
        Countdown = string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
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
            AllowOnceCommand.RaiseCanExecuteChanged();
            DenyCommand.RaiseCanExecuteChanged();
        }
    }
}
