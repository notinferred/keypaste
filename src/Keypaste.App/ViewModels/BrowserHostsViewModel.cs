using Keypaste.App.Resources;
using Keypaste.Core.Browser;

namespace Keypaste.App.ViewModels;

/// <summary>One browser as Settings › Browser fill lists it: who it starts for keypaste's extension, and whether the extension is in it.</summary>
/// <param name="status">What the browser's manifest and folders show.</param>
internal sealed partial class BrowserHostRow(HostStatus status) : ObservableObject
{
    internal HostBrowser Browser => status.Browser;

    internal string Name => status.Browser.ToString();

    internal string Answer => status.State switch
    {
        HostState.Keypaste => $"{Name} starts keypaste at {status.Program}.",
        HostState.KeypasteMissing => $"{Name} looks for keypaste at {status.Program}, which no longer exists. Turn on to point it at this keypaste.",
        HostState.Other => $"{Name} starts another program for keypaste's extension: {status.Program}.",
        _ => $"Not set up. Turn on so {Name} can start keypaste for its extension.",
    };

    internal string Extension => status.Extension switch
    {
        ExtensionState.Installed => $"keypaste's extension is installed in {Name}.",
        ExtensionState.NotFound => $"keypaste's extension isn't installed in {Name}. Get it from {Store}.",
        _ => $"{Name} isn't installed.",
    };

    internal bool IsOn => status.State == HostState.Keypaste;

    /// <summary>The other browsers that read this one's manifest: Brave and Chrome on Windows.</summary>
    internal IReadOnlyList<HostBrowser> SharesWith => status.SharesWith;

    /// <summary>Whether turning on would overwrite another program's manifest, which keypaste cannot put back.</summary>
    internal bool ReplacesAnother => status.State == HostState.Other;

    internal string ActionLabel => IsOn ? Strings.TurnOff : Strings.TurnOn;

    internal string ActionName => IsOn ? $"Turn off for {Name}" : $"Turn on for {Name}";

    internal string ReplaceName => $"Replace the other program for {Name}";

    internal string ReplacePrompt => $"Turning on replaces {status.Program} for {Name}, and keypaste can't put it back.";

    [ObservableProperty]
    internal partial bool IsConfirmingReplace { get; set; }

    private string Store => status.Browser switch
    {
        HostBrowser.Edge => BrowserIdentity.EdgeAddOns.Store,
        HostBrowser.Firefox => BrowserIdentity.FirefoxAddOns.Store,
        _ => BrowserIdentity.ChromeWebStore.Store,
    };
}

/// <summary>
/// Settings › Browser fill's browsers: Turn on writes the manifest each browser reads to start keypaste for keypaste's
/// extension, and Turn off removes keypaste's own; every change reads all five again.
/// </summary>
/// <remarks>Turning on over another program's manifest asks first, since that program's cannot be put back.</remarks>
internal sealed partial class BrowserHostsViewModel : ObservableObject
{
    private readonly BrowserHosts? _hosts;

    /// <param name="hosts">The hosts, naming the keypaste a terminal finds; null when there is none to name.</param>
    /// <param name="unavailable">Why there is none, shown in place of the browsers.</param>
    internal BrowserHostsViewModel(BrowserHosts? hosts, string unavailable)
    {
        _hosts = hosts;
        UnavailableText = hosts is null ? $"keypaste can't be set up in browsers: {unavailable.TrimEnd('.')}." : string.Empty;

        ChangeCommand = new RelayCommand<BrowserHostRow>(Change, row => row is not null && _hosts is not null);
        CancelReplaceCommand = new RelayCommand<BrowserHostRow>(row => row!.IsConfirmingReplace = false, row => row is not null);
        ReplaceCommand = new RelayCommand<BrowserHostRow>(row => TurnOn(row!), row => row is { ReplacesAnother: true } && _hosts is not null);

        Rows = Read();
    }

    /// <summary>Turns a browser off when keypaste answers it, else on, asking first over another program.</summary>
    internal RelayCommand<BrowserHostRow> ChangeCommand { get; }

    internal RelayCommand<BrowserHostRow> CancelReplaceCommand { get; }

    /// <summary>Turns a browser on over another program's manifest, having been confirmed.</summary>
    internal RelayCommand<BrowserHostRow> ReplaceCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharedText), nameof(HasSharedText))]
    internal partial IReadOnlyList<BrowserHostRow> Rows { get; private set; } = [];

    internal bool IsAvailable => _hosts is not null;

    internal string UnavailableText { get; }

    /// <summary>On Windows, that Brave reads Chrome's manifest, said once.</summary>
    internal string SharedText => Rows.FirstOrDefault(row => row.SharesWith.Count > 0) is { } shared
        ? $"{Names(shared.SharesWith.Prepend(shared.Browser).Order())} read one setting, so they turn on and off together."
        : string.Empty;

    internal bool HasSharedText => SharedText.Length > 0;

    /// <summary>What the last change did, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    private void Change(BrowserHostRow? row)
    {
        if (row is null || _hosts is null)
        {
            return;
        }

        if (row.IsOn)
        {
            Said(_hosts.TurnOff(row.Browser), "off");
        }
        else if (row.ReplacesAnother)
        {
            row.IsConfirmingReplace = true;
        }
        else
        {
            TurnOn(row);
        }
    }

    private void TurnOn(BrowserHostRow row) => Said(_hosts!.TurnOn(row.Browser), "on");

    private void Said(HostChange change, string word)
    {
        var browser = change.Browser;
        var together = Rows.Single(row => row.Browser == browser).SharesWith;
        var with = together.Count switch
        {
            0 => string.Empty,
            1 => $" {together[0]} reads the same setting, so it is {word} too.",
            _ => $" {Names(together)} read the same setting, so they are {word} too.",
        };

        Message = change switch
        {
            { Outcome: HostOutcome.On, Replaced: { } replaced } => $"{browser} now starts keypaste at {_hosts!.Program}, in place of {replaced}.{with}",
            { Outcome: HostOutcome.On } => $"{browser} now starts keypaste at {_hosts!.Program}.{with}",
            { Outcome: HostOutcome.Off } => $"{browser} no longer starts keypaste.{with}",
            { Outcome: HostOutcome.LeftOther } => $"{browser} starts another program, {change.Replaced}, which keypaste leaves as it is.",
            { Outcome: HostOutcome.Failed } => $"{browser} was not turned {word}: {change.Reason?.TrimEnd('.')}. Try again.",
            _ => $"{browser} is already {word}.",
        };

        Rows = Read();
    }

    private static string Names(IEnumerable<HostBrowser> browsers) => string.Join(" and ", browsers);

    private IReadOnlyList<BrowserHostRow> Read() => _hosts is null ? [] : [.. _hosts.Read().Select(status => new BrowserHostRow(status))];
}
