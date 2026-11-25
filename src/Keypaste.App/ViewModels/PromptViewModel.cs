using System.Globalization;
using System.Windows.Input;
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
/// An allow answers only while the prompt's window has focus, and the window takes a click on one only once it has had
/// focus a moment, so the click that brings it forward, or one on a window still appearing, only brings it forward. A
/// click on an allow in front answers at once. A key reaches an allow only once the prompt is armed as well, which is
/// <see cref="ArmingDelay"/> after it is shown, brought forward from Agents or given focus back: a keystroke typed for
/// another window lands on the prompt as it appears, and that keystroke is not an approval. A wait begun before the
/// latest of those arms nothing. Denying needs no focus and no delay.
/// </para>
/// <para>
/// Where a prompt offers it, Always allow answers at once, as Allow once does, and keypaste remembers it.
/// </para>
/// <para>
/// A claim card answers Return with its primary, Allow once, only once the prompt is armed, and Escape with Deny. A
/// destructive primary takes no Return.
/// </para>
/// </remarks>
internal abstract partial class PromptViewModel : ObservableObject
{
    /// <summary>How long the prompt is shown, and in focus, before a key can press an allow.</summary>
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
        AllowOnceCommand = new RelayCommand(() => Decide(ApprovalAnswer.ApprovedOnce), () => IsInFront && CanAllow && !IsAnswered);
        DenyCommand = new RelayCommand(() => Decide(ApprovalAnswer.Denied), () => !IsAnswered);
        AlwaysAllowCommand = new RelayCommand(() => Decide(ApprovalAnswer.AlwaysAllowed), () => IsInFront && CanAllow && OffersAlways && !IsAnswered);
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

    /// <summary>Whether Return answers with the primary: a claim card's allow, never a destructive one.</summary>
    internal virtual bool AnswersReturn => Requester is not null;

    /// <summary>Return: presses Allow once when the prompt is armed and it can be pressed; anything else does nothing. Called on the UI thread.</summary>
    internal void Return()
    {
        if (AnswersReturn && IsArmed && AllowOnceCommand.CanExecute(null))
        {
            AllowOnceCommand.Execute(null);
        }
    }

    /// <summary>Escape: Deny. Called on the UI thread.</summary>
    internal void Escape() => DenyCommand.Execute(null);

    /// <summary>Whether <paramref name="command"/> allows, so that a key reaches it only once the prompt is armed.</summary>
    internal bool IsAllow(ICommand? command) => command is not null && (command == AllowOnceCommand || command == AlwaysAllowCommand);

    /// <summary>An item as a card names it: its title, and the vault it is in when one is named.</summary>
    protected string InVault(string title) => Vault is { } vault ? $"{title} \u00b7 {vault}" : title;

    /// <summary>Allows this request and keeps nothing.</summary>
    internal RelayCommand AllowOnceCommand { get; }

    internal RelayCommand DenyCommand { get; }

    /// <summary>Allows this request, and this agent always gets what it asks for from now on.</summary>
    internal RelayCommand AlwaysAllowCommand { get; }

    /// <summary>Whether the prompt offers Always allow.</summary>
    internal virtual bool OffersAlways => false;

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

    /// <summary>Whether a key on an allow waits only for the prompt to arm, which the time bar shows.</summary>
    internal bool IsArming => !IsArmed && CanAllow && !IsAnswered;

    /// <summary>The person's answer, or a denial for every way the prompt went away without one.</summary>
    internal Task<ApprovalAnswer> Answer => _answer.Task;

    internal bool IsAnswered => _answer.Task.IsCompleted;

    /// <summary>Whether the prompt's window has focus, without which no allow can be pressed or invoked; set by the window.</summary>
    [ObservableProperty]
    internal partial bool IsInFront { get; set; }

    partial void OnIsInFrontChanged(bool value) => Refresh();

    /// <summary>Whether the prompt has been up and in focus long enough for a key to press an allow.</summary>
    [ObservableProperty]
    internal partial bool IsArmed { get; private set; }

    partial void OnIsArmedChanged(bool value) => OnPropertyChanged(nameof(IsArming));

    /// <summary>Which wait to arm the prompt is current; <see cref="Disarm"/> begins another.</summary>
    internal int Arming => _arming;

    /// <summary>Lets a key press an allow now.</summary>
    internal void Arm() => Arm(_arming);

    /// <summary>Lets a key press an allow, unless the prompt was disarmed after the wait for <paramref name="arming"/> began. Called on the UI thread.</summary>
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

    /// <summary>Lets no key press an allow until a wait begun now arms it. Called on the UI thread.</summary>
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

    /// <summary>Asks every command again whether it can be pressed.</summary>
    protected void Refresh()
    {
        AllowOnceCommand.NotifyCanExecuteChanged();
        DenyCommand.NotifyCanExecuteChanged();
        AlwaysAllowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsArming));
        OnAllowsChanged();
    }

    /// <summary>Asks a prompt's own allow commands again, once arming or an answer changes what can be pressed.</summary>
    protected virtual void OnAllowsChanged()
    {
    }
}
