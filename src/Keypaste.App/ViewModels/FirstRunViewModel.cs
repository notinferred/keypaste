using Keypaste.App.Terms;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The first run, before the welcome: the terms of use shown as a notice with Continue, then the crash-report choice as two
/// equal buttons, neither the default, so either answer is something the person did (D-0556, D-0594).
/// </summary>
/// <remarks>
/// Continue is not "I agree": the AGPL lets anyone run keypaste without accepting anything, so the terms are a notice and the
/// one thing asked is whether to send reports. The same terms are shown again when a build carries terms with a new date.
/// </remarks>
internal sealed class FirstRunViewModel : ObservableObject
{
    private readonly Func<bool, bool> _choose;
    private readonly Action _done;
    private bool _choosing;

    /// <summary>The first run's two steps.</summary>
    /// <param name="terms">The terms shown.</param>
    /// <param name="choose">Records the choice, true to send reports, and says whether <c>app.toml</c> took it.</param>
    /// <param name="done">Shows what comes next, the welcome or the unlock screen.</param>
    internal FirstRunViewModel(TermsOfUse terms, Func<bool, bool> choose, Action done)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(choose);
        ArgumentNullException.ThrowIfNull(done);

        Terms = terms;
        _choose = choose;
        _done = done;

        ContinueCommand = new RelayCommand(() => IsChoosing = true, () => !IsChoosing);
        SendCommand = new RelayCommand(() => Answer(send: true), () => IsChoosing);
        DontSendCommand = new RelayCommand(() => Answer(send: false), () => IsChoosing);

        DependsOn(nameof(IsReading), nameof(IsChoosing));
        DependsOn(ContinueCommand, nameof(IsChoosing));
        DependsOn(SendCommand, nameof(IsChoosing));
        DependsOn(DontSendCommand, nameof(IsChoosing));
    }

    /// <summary>The terms of use.</summary>
    internal TermsOfUse Terms { get; }

    /// <summary>Whether the terms are showing.</summary>
    internal bool IsReading => !_choosing;

    /// <summary>Whether the crash-report choice is showing.</summary>
    internal bool IsChoosing
    {
        get => _choosing;
        private set => Set(ref _choosing, value);
    }

    /// <summary>Goes on from the terms to the choice.</summary>
    internal RelayCommand ContinueCommand { get; }

    /// <summary>Sends crash reports from now on.</summary>
    internal RelayCommand SendCommand { get; }

    /// <summary>Sends none.</summary>
    internal RelayCommand DontSendCommand { get; }

    /// <summary>Whether the last answer reached <c>app.toml</c>; false means it holds for this run and the next start asks again.</summary>
    internal bool Saved { get; private set; } = true;

    private void Answer(bool send)
    {
        Saved = _choose(send);
        _done();
    }
}
