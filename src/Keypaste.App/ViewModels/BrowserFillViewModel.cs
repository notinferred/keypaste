using Keypaste.App.Globalization;
using Keypaste.App.Session;
using Keypaste.Core.Ownership;

namespace Keypaste.App.ViewModels;

/// <summary>A browser's pairing as Settings lists it.</summary>
/// <param name="name">The pairing's name, as the extension lists it.</param>
/// <param name="browser">The browser the paired build runs in.</param>
/// <param name="identity">The build that paired, with its extension ID.</param>
/// <param name="used">When it was last proven here.</param>
internal sealed partial class PairingItem(string name, string browser, string identity, string used) : ObservableObject
{
    internal string Name { get; } = name;

    internal string Identity { get; } = identity;

    /// <summary>The browser and its last use: Chrome · last used 2026-11-17 14:02.</summary>
    internal string Summary { get; } = $"{browser} · {used}";

    [ObservableProperty]
    internal partial bool IsConfirmingRemove { get; set; }
}

/// <summary>
/// Settings › Browser fill: how a browser pairs, from keypaste's extension, and the pairings this computer holds for the vaults
/// open here, each with its last use and Remove.
/// </summary>
/// <remarks>
/// Nothing here starts a pairing: pressing Connect in the extension brings keypaste's confirmation. A pairing made or removed
/// anywhere reads the list again.
/// </remarks>
internal sealed partial class BrowserFillViewModel : ObservableObject, IDisposable
{
    private readonly AppAuthority _authority;
    private readonly Action<Action> _post;

    /// <param name="authority">The app's authority, which pairs browsers for each unlock.</param>
    /// <param name="post">Runs an action on the UI thread, or null to run it where a pairing lands.</param>
    internal BrowserFillViewModel(AppAuthority authority, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(authority);

        _authority = authority;
        _post = post ?? (run => run());

        AskRemoveCommand = new RelayCommand<PairingItem>(row => SetConfirming(row, true), row => row is not null);
        CancelRemoveCommand = new RelayCommand<PairingItem>(row => SetConfirming(row, false), row => row is not null);
        RemoveCommand = new RelayCommand<PairingItem>(Remove, row => row is not null);

        Rows = Read();
        _authority.PairingsChanged += OnPairingsChanged;
    }

    /// <summary>Asks in the row before removing its pairing.</summary>
    internal RelayCommand<PairingItem> AskRemoveCommand { get; }

    internal RelayCommand<PairingItem> CancelRemoveCommand { get; }

    /// <summary>Removes one pairing, having been confirmed, so the browser holding it is refused from its next check.</summary>
    internal RelayCommand<PairingItem> RemoveCommand { get; }

    /// <summary>The pairings this computer holds for the vaults open here, by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(HasNoRows))]
    internal partial IReadOnlyList<PairingItem> Rows { get; private set; } = [];

    internal bool HasRows => Rows.Count > 0;

    internal bool HasNoRows => Rows.Count == 0;

    /// <summary>What the last act here did, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    public void Dispose() => _authority.PairingsChanged -= OnPairingsChanged;

    private void OnPairingsChanged(object? sender, EventArgs e) => _post(() => Rows = Read());

    private static void SetConfirming(PairingItem? row, bool confirming)
    {
        if (row is not null)
        {
            row.IsConfirmingRemove = confirming;
        }
    }

    private void Remove(PairingItem? row)
    {
        if (row is null)
        {
            return;
        }

        var removed = _authority.RemovePairing(row.Name);
        Message = removed.Removed
            ? $"Removed the pairing {row.Name}. That browser has to pair again before it can ask for anything."
            : $"The pairing {row.Name} was not removed: {removed.Reason?.TrimEnd('.')}. Try again.";
        Rows = Read();
    }

    private IReadOnlyList<PairingItem> Read() =>
        [.. _authority.Pairings().Select(row => new PairingItem(row.Pairing.Id, row.Pairing.Identity.Browser, row.Pairing.Identity.Described, Used(row)))];

    private static string Used(PairingRow row) => row.LastUsed is { } at
        ? "last used " + PersonTime.Moment(at.ToLocalTime())
        : "paired " + PersonTime.Date(row.Pairing.Created.ToLocalTime());
}
