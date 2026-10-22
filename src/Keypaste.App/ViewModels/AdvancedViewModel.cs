using System.Globalization;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.Core.Sharing;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Settings › Advanced: the way to the screens the everyday ones leave out, each a screen under this one.
/// </summary>
internal sealed class AdvancedViewModel
{
    internal AdvancedViewModel(AppVaultSession session, Action<DestinationKind>? open = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        OpenActivityLogCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Log), () => open is not null);
        OpenShareLinksCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Sharing), () => open is not null);
        OpenScopedTokensCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Tokens), () => open is not null);
        OpenDiagnosticsCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Diagnostics), () => open is not null);
        ShareLinksNote = ShareLinksNoteOf(session);
    }

    /// <summary>The whole activity log, with its hash check.</summary>
    internal RelayCommand OpenActivityLogCommand { get; }

    /// <summary>The share links made from this vault.</summary>
    internal RelayCommand OpenShareLinksCommand { get; }

    /// <summary>The vault's scoped tokens, minting and revoking one.</summary>
    internal RelayCommand OpenScopedTokensCommand { get; }

    /// <summary>The paths and version this app is using.</summary>
    internal RelayCommand OpenDiagnosticsCommand { get; }

    /// <summary>How many of this vault's links may still open, from their recorded expiry only.</summary>
    internal string ShareLinksNote { get; }

    private static string ShareLinksNoteOf(AppVaultSession session)
    {
        if (session.Unlocked is not { } vault)
        {
            return string.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        var open = new ShareStore(vault).List().Count(share => share.Expires > now);

        return open switch
        {
            0 => "Links you make from an item's Share… are listed here. None may still open.",
            1 => "Links you make from an item's Share… are listed here. 1 may still open.",
            _ => string.Create(CultureInfo.InvariantCulture, $"Links you make from an item's Share… are listed here. {open} may still open."),
        };
    }
}
