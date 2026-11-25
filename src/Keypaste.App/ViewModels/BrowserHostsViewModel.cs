using Keypaste.App.Resources;
using Keypaste.Core.Browser;

namespace Keypaste.App.ViewModels;

/// <summary>One browser as Settings › Browser fill lists it: who it starts for keypaste's extension, and whether the extension is in it.</summary>
/// <param name="status">What the browser's manifest and folders show.</param>
internal sealed partial class BrowserHostRow(HostStatus status) : ObservableObject
{
    internal HostBrowser Browser => status.Browser;

    internal string Name => status.Browser.ToString();

    /// <summary>Whether the browser is on this computer; a browser that isn't shows no switch.</summary>
    internal bool IsInstalled => status.Extension != ExtensionState.BrowserNotFound;

    /// <summary>The row's one line: whether keypaste answers the browser's extension, or what is in the way.</summary>
    internal string Status => status switch
    {
        { Extension: ExtensionState.BrowserNotFound } => Strings.NotInstalled,
        { State: HostState.KeypasteMissing } => Strings.KeypasteMovedTurnOnAgain,
        { State: HostState.Other } => Strings.AnotherProgramAnswersItsExtension,
        { Extension: ExtensionState.Installed } => Strings.ExtensionInstalled,
        _ => Strings.ExtensionNotInstalled,
    };

    /// <summary>Where to get the extension, while the browser lacks it.</summary>
    internal string Hint => IsInstalled && status.Extension == ExtensionState.NotFound
        ? $"Get it from {Store}."
        : string.Empty;

    internal bool HasHint => Hint.Length > 0;

    /// <summary>The program the browser starts for keypaste's extension, for the row's tooltip.</summary>
    internal string? Program => status.Program;

    internal bool OffersSwitch => IsInstalled && !IsConfirmingReplace;

    /// <summary>The switch's position: on while keypaste answers, and while turning on over another program waits on its confirmation.</summary>
    internal bool SwitchedOn => IsOn || IsConfirmingReplace;

    /// <summary>Whether the browser has the extension and keypaste doesn't answer it, so the Browsers disclosure starts open.</summary>
    internal bool NeedsAttention => IsInstalled && (status.State is HostState.KeypasteMissing or HostState.Other || (status.Extension == ExtensionState.Installed && !IsOn));

    internal bool IsOn => status.State == HostState.Keypaste;

    /// <summary>The other browsers that read this one's manifest: Brave and Chrome on Windows.</summary>
    internal IReadOnlyList<HostBrowser> SharesWith => status.SharesWith;

    /// <summary>Whether turning on would overwrite another program's manifest, which keypaste cannot put back.</summary>
    internal bool ReplacesAnother => status.State == HostState.Other;

    internal string ActionName => IsOn ? $"Turn off for {Name}" : $"Turn on for {Name}";

    internal string ReplaceName => $"Replace the other program for {Name}";

    internal string ReplacePrompt => $"Turning on replaces {status.Program} for {Name}, and keypaste can't put it back.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersSwitch), nameof(SwitchedOn))]
    internal partial bool IsConfirmingReplace { get; set; }

    private string Store => status.Browser switch
    {
        HostBrowser.Edge => BrowserIdentity.EdgeAddOns.Store,
        HostBrowser.Firefox or HostBrowser.LibreWolf => BrowserIdentity.FirefoxAddOns.Store,
        _ => BrowserIdentity.ChromeWebStore.Store,
    };
}

/// <summary>
/// Settings › Browser fill's browsers: Turn on writes the manifest each browser reads to start keypaste for keypaste's
/// extension, and Turn off removes keypaste's own; every change reads all six again.
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
        IsOpen = Rows.Any(row => row.NeedsAttention);
    }

    /// <summary>Whether the Browsers disclosure is open; it starts open while any browser needs attention.</summary>
    [ObservableProperty]
    internal partial bool IsOpen { get; set; }

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

    /// <summary>Each group of browsers that read one manifest, said once: LibreWolf with Firefox, and Brave with Chrome on Windows and macOS.</summary>
    internal string SharedText => string.Join(
        " ",
        Rows.Where(row => row.SharesWith.Count > 0)
            .Select(row => Names(row.SharesWith.Append(row.Browser).Order()))
            .Distinct(StringComparer.Ordinal)
            .Select(names => $"{names} read one setting, so they turn on and off together."));

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
