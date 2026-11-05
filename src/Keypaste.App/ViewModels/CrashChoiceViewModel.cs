using Keypaste.App.Terms;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The crash-report choice with a link to the terms of use that cover them: two equal buttons, neither the default, so either answer
/// is something the person did, and leaving it unanswered sends nothing.
/// </summary>
internal sealed partial class CrashChoiceViewModel : ObservableObject
{
    private const string _changeLater = "You can change this in Settings.";
    private const string _notSaved = "This couldn't be saved, so it lasts until keypaste quits.";

    private readonly Func<bool, bool> _choose;

    /// <param name="terms">The terms the link opens.</param>
    /// <param name="choose">Records the choice, true to send reports, and says whether <c>app.toml</c> took it.</param>
    internal CrashChoiceViewModel(TermsOfUse terms, Func<bool, bool> choose)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(choose);

        Terms = terms;
        _choose = choose;
        SendCommand = new RelayCommand(() => Answer(send: true), () => IsAsking);
        DontSendCommand = new RelayCommand(() => Answer(send: false), () => IsAsking);
    }

    /// <summary>The terms of use.</summary>
    internal TermsOfUse Terms { get; }

    /// <summary>Sends crash reports from now on.</summary>
    internal RelayCommand SendCommand { get; }

    /// <summary>Sends none.</summary>
    internal RelayCommand DontSendCommand { get; }

    /// <summary>What was answered, once it has been.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking), nameof(HasOutcome))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(DontSendCommand))]
    internal partial string? Outcome { get; private set; }

    /// <summary>Whether the choice is still open.</summary>
    internal bool IsAsking => Outcome is null;

    internal bool HasOutcome => Outcome is not null;

    private void Answer(bool send)
    {
        var saved = _choose(send);
        var said = send ? "Crash reports will be sent." : "No crash reports will be sent.";
        Outcome = $"{said} {(saved ? _changeLater : _notSaved)}";
    }
}
