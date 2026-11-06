using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One question in a prompt window, answered once.</summary>
/// <remarks>
/// <para>
/// The answer is a latch. The first of Deny, an allow, closing the window and a withdrawal decides
/// it and nothing afterwards changes it, so a click that lands after a lock, a timeout or a hang-up
/// approves nothing.
/// </para>
/// <para>
/// Every allow is armed only after the prompt has been up for <see cref="ArmingDelay"/>: the window
/// can appear under a pointer that was about to click something else, and that click is not an
/// approval (D-0326). Bringing the prompt forward from Agents disarms it for another
/// <see cref="ArmingDelay"/>, and a wait begun before that arms nothing (D-0456), and so does a click
/// that brings it back from another window. Denying needs no delay. The timed allow exists only when the prompt offers a timed grant.
/// </para>
/// </remarks>
internal abstract partial class PromptViewModel : ObservableObject
{
    /// <summary>How long the prompt is shown before either allow button can be pressed.</summary>
    internal static readonly TimeSpan ArmingDelay = TimeSpan.FromSeconds(1);

    private readonly TaskCompletionSource<ApprovalAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arming;

    protected PromptViewModel()
    {
        ApproveCommand = new RelayCommand(() => Decide(ApprovalAnswer.Approved), () => IsArmed && OffersTimed && !IsAnswered);
        AllowOnceCommand = new RelayCommand(() => Decide(ApprovalAnswer.ApprovedOnce), () => IsArmed && !IsAnswered);
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
    [ObservableProperty]
    internal partial string Countdown { get; private set; } = string.Empty;

    /// <summary>The person's answer, or a denial for every way the prompt went away without one.</summary>
    internal Task<ApprovalAnswer> Answer => _answer.Task;

    internal bool IsAnswered => _answer.Task.IsCompleted;

    /// <summary>How long the timed grant lasts, or zero when the prompt offers only "allow once".</summary>
    protected abstract int TimedSeconds { get; }

    /// <summary>Whether the prompt has been up long enough for an allow to be pressed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(AllowOnceCommand))]
    protected partial bool IsArmed { get; private set; }

    partial void OnIsArmedChanged(bool value) => OnAllowsChanged();

    /// <summary>Which wait to arm the prompt is current; <see cref="Disarm"/> begins another.</summary>
    internal int Arming => _arming;

    /// <summary>Lets the allow buttons be pressed now.</summary>
    internal void Arm() => Arm(_arming);

    /// <summary>Lets the allow buttons be pressed, unless the prompt was disarmed after the wait for <paramref name="arming"/> began. Called on the UI thread.</summary>
    /// <param name="arming">The <see cref="Arming"/> read when the wait began.</param>
    internal void Arm(int arming)
    {
        if (arming == _arming)
        {
            IsArmed = true;
        }
    }

    /// <summary>What the prompt asks, in the words of its heading, for a screen reader.</summary>
    protected abstract string Asks { get; }

    /// <summary>What a screen reader is told once as the prompt opens: what it asks and how long is left, which never ticks.</summary>
    [ObservableProperty]
    internal partial string Opening { get; private set; } = string.Empty;

    private static string TimeLimit(int seconds) => seconds switch
    {
        >= 120 when seconds % 60 == 0 => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} minutes"),
        60 => "1 minute",
        1 => "1 second",
        _ => string.Create(CultureInfo.InvariantCulture, $"{seconds} seconds"),
    };

    /// <summary>Lets no allow be pressed until a wait begun now arms it. Called on the UI thread.</summary>
    internal void Disarm()
    {
        _arming++;
        IsArmed = false;
    }

    /// <summary>Shows how long is left to answer. Called on the UI thread.</summary>
    internal void Tick(TimeSpan remaining)
    {
        var seconds = remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalSeconds);

        if (Opening.Length == 0)
        {
            Opening = $"{Asks}. {TimeLimit(seconds)} to answer.";
        }

        Countdown = string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>The window closed with no answer, which is a no. Called on the UI thread.</summary>
    internal void Closed() => Decide(ApprovalAnswer.Denied);

    /// <summary>Nobody is waiting for the answer any more. Safe from any thread.</summary>
    internal void Withdraw() => _answer.TrySetResult(ApprovalAnswer.Denied);

    /// <summary>The prompt could not be put in front of anybody. Safe from any thread.</summary>
    internal void Fail() => _answer.TrySetResult(ApprovalAnswer.Failed);

    /// <summary>Answers the prompt, unless something already has.</summary>
    /// <param name="answer">The answer.</param>
    protected void Decide(ApprovalAnswer answer)
    {
        if (_answer.TrySetResult(answer))
        {
            OnPropertyChanged(nameof(IsAnswered));
            ApproveCommand.NotifyCanExecuteChanged();
            AllowOnceCommand.NotifyCanExecuteChanged();
            DenyCommand.NotifyCanExecuteChanged();
            OnAllowsChanged();
        }
    }

    /// <summary>Asks a prompt's own allow commands again, once arming or an answer changes what can be pressed.</summary>
    protected virtual void OnAllowsChanged()
    {
    }
}
