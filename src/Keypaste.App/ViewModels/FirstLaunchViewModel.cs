using Keypaste.App.Resources;
using Keypaste.App.Terms;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The terms of use, asked on the one first-launch screen beside a yes of its own for crash reports, or by Settings' switch,
/// whose turning on is that yes.
/// </summary>
internal sealed class FirstLaunchViewModel
{
    private readonly Action<bool> _agree;

    /// <summary>The first-launch screen, whose crash-report choice is off until the person turns it on.</summary>
    /// <param name="terms">The terms the link opens.</param>
    /// <param name="agree">Takes Agree with the person's answer on crash reports.</param>
    /// <param name="quit">Takes Quit, which agrees to nothing.</param>
    internal FirstLaunchViewModel(TermsOfUse terms, Action<bool> agree, Action? quit = null)
        : this(terms, agree, Strings.OnlineServicesHaveTerms, quit)
    {
    }

    private FirstLaunchViewModel(TermsOfUse terms, Action<bool> agree, string agreement, Action? quit = null)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(agree);

        Terms = terms;
        _agree = agree;
        Agreement = agreement;
        Opening = $"{Strings.TermsOfUse}. {agreement}";
        AgreeCommand = new RelayCommand(() => _agree(SendCrashReports));
        QuitCommand = new RelayCommand(() => quit?.Invoke());
    }

    /// <summary>The terms Settings' switch asks for before it turns crash reports on.</summary>
    /// <param name="terms">The terms the link opens.</param>
    /// <param name="agree">Takes Agree.</param>
    internal static FirstLaunchViewModel ForSwitch(TermsOfUse terms, Action agree)
    {
        ArgumentNullException.ThrowIfNull(agree);

        return new(terms, _ => agree(), Strings.AgreeingTurnsCrashReportsOn) { SendCrashReports = true };
    }

    /// <summary>The terms of use.</summary>
    internal TermsOfUse Terms { get; }

    /// <summary>The one line under the heading: what agreeing is to.</summary>
    internal string Agreement { get; }

    /// <summary>What a screen reader hears as the screen opens, since Agree takes the keyboard and Return agrees.</summary>
    internal string Opening { get; }

    /// <summary>Whether the person said yes to sending crash reports.</summary>
    internal bool SendCrashReports { get; set; }

    /// <summary>Agrees and goes on.</summary>
    internal RelayCommand AgreeCommand { get; }

    /// <summary>Leaves the app without agreeing; the first-launch screen's alone.</summary>
    internal RelayCommand QuitCommand { get; }
}
