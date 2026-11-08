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
/// that brings it back from another window. Denying needs no delay.
/// </para>
/// <para>
/// Where a prompt offers them, Always allow and Always block answer nothing by themselves: each turns the prompt to a
/// question confirming what keypaste will remember, whose confirm button arms a further <see cref="ArmingDelay"/> later,
/// and Back returns to the choices. Deny answers at every step.
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
        AllowOnceCommand = new RelayCommand(() => Decide(ApprovalAnswer.ApprovedOnce), () => IsArmed && CanAllow && !IsAnswered && Confirming is null);
        DenyCommand = new RelayCommand(() => Decide(ApprovalAnswer.Denied), () => !IsAnswered);
        AlwaysAllowCommand = new RelayCommand(
            () => Confirm(ApprovalAnswer.AlwaysAllowed),
            () => IsArmed && CanAllow && OffersAlways && !IsAnswered && Confirming is null);
        AlwaysBlockCommand = new RelayCommand(() => Confirm(ApprovalAnswer.AlwaysBlocked), () => OffersAlwaysBlock && !IsAnswered && Confirming is null);
        ConfirmAlwaysCommand = new RelayCommand(() => Decide(Confirming!.Value), () => IsArmed && Confirming is not null && !IsAnswered);
        BackCommand = new RelayCommand(() => Confirming = null, () => Confirming is not null && !IsAnswered);
    }

    /// <summary>Raised when a step of the prompt disarmed its allows, so whatever shows the prompt arms them again <see cref="ArmingDelay"/> later.</summary>
    internal event EventHandler? Disarmed;

    /// <summary>Allows this request and keeps nothing.</summary>
    internal RelayCommand AllowOnceCommand { get; }

    internal RelayCommand DenyCommand { get; }

    /// <summary>Asks the person to confirm that this agent always gets what this request asks for.</summary>
    internal RelayCommand AlwaysAllowCommand { get; }

    /// <summary>Asks the person to confirm that this agent is always refused what this request asks for.</summary>
    internal RelayCommand AlwaysBlockCommand { get; }

    /// <summary>Answers with the always the person is confirming.</summary>
    internal RelayCommand ConfirmAlwaysCommand { get; }

    /// <summary>Returns from the confirmation to the choices.</summary>
    internal RelayCommand BackCommand { get; }

    /// <summary>Whether the prompt offers Always allow.</summary>
    internal virtual bool OffersAlways => false;

    /// <summary>Whether the prompt offers Always block.</summary>
    internal virtual bool OffersAlwaysBlock => false;

    /// <summary>The always the person is confirming, or null while the prompt shows its choices.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming), nameof(IsChoosing), nameof(ShowsAlwaysAllow), nameof(ShowsAlwaysBlock), nameof(AlwaysCaption), nameof(ConfirmLabel))]
    internal partial ApprovalAnswer? Confirming { get; private set; }

    partial void OnConfirmingChanged(ApprovalAnswer? value) => Refresh();

    /// <summary>Whether the prompt asks the person to confirm an always.</summary>
    internal bool IsConfirming => Confirming is not null;

    /// <summary>Whether the prompt shows its choices.</summary>
    internal bool IsChoosing => Confirming is null;

    /// <summary>Whether Always allow is on the prompt now.</summary>
    internal bool ShowsAlwaysAllow => IsChoosing && OffersAlways;

    /// <summary>Whether Always block is on the prompt now.</summary>
    internal bool ShowsAlwaysBlock => IsChoosing && OffersAlwaysBlock;

    /// <summary>What is said above the buttons: what an always keeps, or the question confirming one.</summary>
    internal string AlwaysCaption => Confirming is { } always ? Confirmation(always) : Choices;

    /// <summary>What the confirm button says.</summary>
    internal string ConfirmLabel => Confirming == ApprovalAnswer.AlwaysBlocked ? "Always block" : "Always allow";

    /// <summary>The question confirming an always.</summary>
    /// <param name="always">Always allow or Always block.</param>
    /// <returns>The question.</returns>
    protected virtual string Confirmation(ApprovalAnswer always) => string.Empty;

    /// <summary>What is said above the choices, or nothing.</summary>
    protected virtual string Choices => string.Empty;

    /// <summary>How long is left to answer, as <c>m:ss</c>.</summary>
    [ObservableProperty]
    internal partial string Countdown { get; private set; } = string.Empty;

    /// <summary>The person's answer, or a denial for every way the prompt went away without one.</summary>
    internal Task<ApprovalAnswer> Answer => _answer.Task;

    internal bool IsAnswered => _answer.Task.IsCompleted;

    /// <summary>Whether the prompt has been up long enough for an allow to be pressed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowOnceCommand))]
    protected partial bool IsArmed { get; private set; }

    partial void OnIsArmedChanged(bool value) => Refresh();

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

    /// <summary>Whether what an allow would release is decided yet, as a picker's is only once an item is picked.</summary>
    protected virtual bool CanAllow => true;

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
            Refresh();
        }
    }

    /// <summary>Turns the prompt to the question confirming an always, its confirm button disarmed.</summary>
    private void Confirm(ApprovalAnswer always)
    {
        Disarm();
        Confirming = always;
        Disarmed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks every command again whether it can be pressed.</summary>
    protected void Refresh()
    {
        AllowOnceCommand.NotifyCanExecuteChanged();
        DenyCommand.NotifyCanExecuteChanged();
        AlwaysAllowCommand.NotifyCanExecuteChanged();
        AlwaysBlockCommand.NotifyCanExecuteChanged();
        ConfirmAlwaysCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        OnAllowsChanged();
    }

    /// <summary>Asks a prompt's own allow commands again, once arming or an answer changes what can be pressed.</summary>
    protected virtual void OnAllowsChanged()
    {
    }
}
