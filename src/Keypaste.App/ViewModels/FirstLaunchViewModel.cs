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
    /// <param name="agree">Takes Continue with the person's answer on crash reports.</param>
    internal FirstLaunchViewModel(TermsOfUse terms, Action<bool> agree)
        : this(terms, agree, Strings.ContinuingAgreesToTheTermsOfUse)
    {
    }

    private FirstLaunchViewModel(TermsOfUse terms, Action<bool> agree, string agreement)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(agree);

        Terms = terms;
        _agree = agree;
        Agreement = agreement;
        Opening = $"{agreement} {Strings.AReportHoldsTheVersionSystemScreenX3de709}";
        ContinueCommand = new RelayCommand(() => _agree(SendCrashReports));
    }

    /// <summary>The terms Settings' switch asks for before it turns crash reports on.</summary>
    /// <param name="terms">The terms the link opens.</param>
    /// <param name="agree">Takes Continue.</param>
    internal static FirstLaunchViewModel ForSwitch(TermsOfUse terms, Action agree)
    {
        ArgumentNullException.ThrowIfNull(agree);

        return new(terms, _ => agree(), Strings.ContinuingAgreesToTheTermsAndTurnsCrashReportsOn) { SendCrashReports = true };
    }

    /// <summary>The terms of use.</summary>
    internal TermsOfUse Terms { get; }

    /// <summary>What continuing agrees to.</summary>
    internal string Agreement { get; }

    /// <summary>What a screen reader hears as the screen opens, since Continue takes the keyboard and Return agrees.</summary>
    internal string Opening { get; }

    /// <summary>Whether the person said yes to sending crash reports.</summary>
    internal bool SendCrashReports { get; set; }

    /// <summary>Agrees and goes on.</summary>
    internal RelayCommand ContinueCommand { get; }
}
