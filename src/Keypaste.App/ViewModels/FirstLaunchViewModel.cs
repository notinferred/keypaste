using Keypaste.App.Resources;
using Keypaste.App.Terms;

namespace Keypaste.App.ViewModels;

/// <summary>The one first-launch screen: continuing agrees to the terms of use, which include sending crash reports.</summary>
internal sealed class FirstLaunchViewModel
{
    /// <param name="terms">The terms the link opens.</param>
    /// <param name="agree">Takes Continue.</param>
    internal FirstLaunchViewModel(TermsOfUse terms, Action agree)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(agree);

        Terms = terms;
        ContinueCommand = new RelayCommand(agree);
    }

    /// <summary>The terms of use.</summary>
    internal TermsOfUse Terms { get; }

    /// <summary>What a screen reader hears as the screen opens, since Continue takes the keyboard and Return agrees.</summary>
    internal string Opening { get; } = $"{Strings.ContinuingAgreesToTheTermsOfUse} {Strings.AReportHoldsTheVersionSystemScreenX3de709}";

    /// <summary>Agrees and goes on.</summary>
    internal RelayCommand ContinueCommand { get; }
}
