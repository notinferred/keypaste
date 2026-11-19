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
/// approval. Bringing the prompt forward from Agents disarms it for another
/// <see cref="ArmingDelay"/>, and a wait begun before that arms nothing, and so does a click
/// that brings it back from another window. Denying needs no delay.
/// </para>
/// <para>
/// Where a prompt offers it, Always allow answers nothing by itself: it turns the prompt to a question confirming what
/// keypaste will remember, whose confirm button arms a further <see cref="ArmingDelay"/> later, and Cancel returns to the
/// choices. Deny answers at every step but that one, where Cancel is the way back.
/// </para>
/// <para>
/// A claim card answers Return with its primary, Allow once or the confirmation, only once that is armed, and Escape with
/// Deny or, while confirming, Cancel. A destructive primary takes no Return.
/// </para>
/// </remarks>
internal abstract partial class PromptViewModel : ObservableObject
{
    /// <summary>How long the prompt is shown before either allow button can be pressed.</summary>
    internal static readonly TimeSpan ArmingDelay = TimeSpan.FromSeconds(1);

    private readonly TaskCompletionSource<ApprovalAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arming;

    protected PromptViewModel()
        : this(null)
    {
    }

    /// <summary>A prompt whose card names <paramref name="requester"/> on its requester line.</summary>
    /// <param name="requester">Who is asking, as Core checked it, or null for a prompt with no requester line.</param>
    protected PromptViewModel(RequesterIdentity? requester)
    {
        Requester = requester;
        AllowOnceCommand = new RelayCommand(() => Decide(ApprovalAnswer.ApprovedOnce), () => IsArmed && CanAllow && !IsAnswered && Confirming is null);
        DenyCommand = new RelayCommand(() => Decide(ApprovalAnswer.Denied), () => !IsAnswered);
        AlwaysAllowCommand = new RelayCommand(
            () => Confirm(ApprovalAnswer.AlwaysAllowed),
            () => IsArmed && CanAllow && OffersAlways && !IsAnswered && Confirming is null);
        ConfirmAlwaysCommand = new RelayCommand(() => Decide(Confirming!.Value), () => IsArmed && Confirming is not null && !IsAnswered);
        BackCommand = new RelayCommand(() => Confirming = null, () => Confirming is not null && !IsAnswered);
    }

    /// <summary>Who is asking, as Core checked it with the system, or null for a prompt with no requester line.</summary>
    internal RequesterIdentity? Requester { get; }

    /// <summary>The agent the requester line names.</summary>
    internal string RequesterAgent => Requester?.Agent ?? string.Empty;

    /// <summary>
    /// The agent as the question and the card's sentences name it, cut to <see cref="AgentLength"/> characters: an agent names
    /// itself, so its name may not wrap a line and move the buttons (THREATS.md T-2). The requester line trims the whole name.
    /// </summary>
    internal string Agent => RequesterAgent.Length > AgentLength ? string.Concat(RequesterAgent.AsSpan(0, AgentLength - 1), "…") : RequesterAgent;

    /// <summary>The longest agent name a question or sentence on the card holds.</summary>
    internal const int AgentLength = 16;

    /// <summary>The app the agent runs in, with its separator, when keypaste verified it; otherwise empty.</summary>
    internal string RequesterApp => Requester?.App is { } app ? $" \u00b7 {app}" : string.Empty;

    /// <summary>Whether keypaste checked who is asking.</summary>
    internal bool IsVerified => Requester?.Verified == true;

    /// <summary>Whether the requester line says it could not check who is asking.</summary>
    internal bool IsNotVerified => Requester is { Verified: false };

    /// <summary>What pointing at Verified or Not verified says.</summary>
    internal virtual string VerificationExplained => Requester?.Explanation ?? string.Empty;

    /// <summary>The program on the connection and its parent, for Details, or that it was not read.</summary>
    internal string Process => Requester?.Process ?? "not read";

    /// <summary>Whether Return answers with the primary: a claim card's allow or confirmation, never a destructive one.</summary>
    internal virtual bool AnswersReturn => Requester is not null;

    /// <summary>Return: presses the primary, Allow once or the confirmation, when it is armed; anything else does nothing. Called on the UI thread.</summary>
    internal void Return()
    {
        var primary = Confirming is null ? AllowOnceCommand : ConfirmAlwaysCommand;

        if (AnswersReturn && primary.CanExecute(null))
        {
            primary.Execute(null);
        }
    }

    /// <summary>Escape: Cancel while confirming an always, otherwise Deny. Called on the UI thread.</summary>
    internal void Escape()
    {
        if (Confirming is null)
        {
            DenyCommand.Execute(null);
        }
        else
        {
            BackCommand.Execute(null);
        }
    }

    /// <summary>An item as a card names it: its title, and the vault it is in when one is named.</summary>
    protected string InVault(string title) => Vault is { } vault ? $"{title} \u00b7 {vault}" : title;

    /// <summary>Raised when a step of the prompt disarmed its allows, so whatever shows the prompt arms them again <see cref="ArmingDelay"/> later.</summary>
    internal event EventHandler? Disarmed;

    /// <summary>Allows this request and keeps nothing.</summary>
    internal RelayCommand AllowOnceCommand { get; }

    internal RelayCommand DenyCommand { get; }

    /// <summary>Asks the person to confirm that this agent always gets what this request asks for.</summary>
    internal RelayCommand AlwaysAllowCommand { get; }

    /// <summary>Answers with the always the person is confirming.</summary>
    internal RelayCommand ConfirmAlwaysCommand { get; }

    /// <summary>Returns from the confirmation to the choices.</summary>
    internal RelayCommand BackCommand { get; }

    /// <summary>Whether the prompt offers Always allow.</summary>
    internal virtual bool OffersAlways => false;

    /// <summary>The always the person is confirming, or null while the prompt shows its choices.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming), nameof(IsChoosing), nameof(ShowsAlwaysAllow), nameof(ConfirmText))]
    internal partial ApprovalAnswer? Confirming { get; private set; }

    partial void OnConfirmingChanged(ApprovalAnswer? value) => Refresh();

    /// <summary>Whether the prompt asks the person to confirm an always.</summary>
    internal bool IsConfirming => Confirming is not null;

    /// <summary>Whether the prompt shows its choices.</summary>
    internal bool IsChoosing => Confirming is null;

    /// <summary>Whether Always allow is on the prompt now.</summary>
    internal bool ShowsAlwaysAllow => IsChoosing && OffersAlways;

    /// <summary>What the card says under the question confirming Always allow, or nothing while it shows its choices.</summary>
    internal string ConfirmText => Confirming is null ? string.Empty : AlwaysKept;

    /// <summary>What Always allow keeps, said under its confirmation.</summary>
    internal string AlwaysKept => $"keypaste won't ask again for this key. Change it in Agents \u203a {Agent}.";

    /// <summary>What pointing at Always allow says.</summary>
    internal virtual string AlwaysTip => string.Empty;

    /// <summary>Why the card offers no Always allow, for Details, or nothing when it offers one.</summary>
    internal virtual string NoAlways => string.Empty;

    /// <summary>How long until the prompt denies by itself, in words, which the time bar is named by.</summary>
    [ObservableProperty]
    internal partial string DeniesIn { get; private set; } = string.Empty;

    /// <summary>The share of the time to answer still left, from 1 as the prompt opens to 0 as it denies, which the time bar draws.</summary>
    [ObservableProperty]
    internal partial double TimeLeft { get; private set; } = 1;

    private double _answerSeconds;

    /// <summary>Whether an allow waits only for the prompt to arm, which the time bar shows rather than the buttons.</summary>
    internal bool IsArming => !IsArmed && CanAllow && !IsAnswered;

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

    /// <summary>The vault the request is for, which the card names beside its item, or null; set before the prompt shows.</summary>
    internal string? Vault { get; set; }

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
            var limit = TimeLimit(seconds);
            Opening = (Asks.EndsWith('?'), Vault) switch
            {
                (true, { } vault) => $"{Asks} In {vault}. {limit} to answer.",
                (true, null) => $"{Asks} {limit} to answer.",
                (false, { } vault) => $"{Asks}, in {vault}. {limit} to answer.",
                _ => $"{Asks}. {limit} to answer.",
            };
        }

        if (_answerSeconds == 0)
        {
            _answerSeconds = Math.Max(remaining.TotalSeconds, 1);
        }

        TimeLeft = Math.Clamp(remaining.TotalSeconds / _answerSeconds, 0, 1);
        DeniesIn = string.Create(CultureInfo.InvariantCulture, $"Denies in {seconds / 60}:{seconds % 60:00}");
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
        ConfirmAlwaysCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsArming));
        OnAllowsChanged();
    }

    /// <summary>Asks a prompt's own allow commands again, once arming or an answer changes what can be pressed.</summary>
    protected virtual void OnAllowsChanged()
    {
    }
}
