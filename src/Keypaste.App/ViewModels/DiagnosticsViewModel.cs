using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Keypaste.App.Clipboard;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.App.Terms;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.CrashReports;

namespace Keypaste.App.ViewModels;

/// <summary>A crash report waiting to be sent: a row naming when and what failed, whose View shows the exact bytes that would be sent.</summary>
internal sealed partial class CrashReportRow : ObservableObject
{
    internal CrashReportRow(CrashReport report, Action<CrashReportRow> delete)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(delete);

        Report = report;
        Title = TitleOf(report);
        AskDeleteCommand = new RelayCommand(() => IsConfirming = true);
        CancelDeleteCommand = new RelayCommand(() => IsConfirming = false);
        DeleteCommand = new RelayCommand(() => delete(this));
        ViewCommand = new RelayCommand(() => IsViewing = true);
        CloseViewCommand = new RelayCommand(() => IsViewing = false);
    }

    internal CrashReport Report { get; }

    /// <summary>The report's JSON, byte for byte what the request carries.</summary>
    internal string Json => Report.Text;

    /// <summary>When and what failed: Crash on 2026-11-19 · InvalidOperationException.</summary>
    internal string Title { get; }

    /// <summary>Whether the sheet showing the JSON is open.</summary>
    [ObservableProperty]
    internal partial bool IsViewing { get; private set; }

    internal RelayCommand ViewCommand { get; }

    internal RelayCommand CloseViewCommand { get; }

    /// <summary>Whether the row is asking to confirm its delete, which cannot be undone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    internal partial bool IsConfirming { get; private set; }

    /// <summary>The row's words: what failed, or the question its delete asks.</summary>
    internal string Shown => IsConfirming ? Strings.DeleteThisReportItWontBeSent : Title;

    /// <summary>Asks first.</summary>
    internal RelayCommand AskDeleteCommand { get; }

    /// <summary>Takes the question back.</summary>
    internal RelayCommand CancelDeleteCommand { get; }

    /// <summary>Deletes it unsent.</summary>
    internal RelayCommand DeleteCommand { get; }

    private static string TitleOf(CrashReport report)
    {
        using var json = JsonDocument.Parse(report.Json);
        var root = json.RootElement;
        var day = root.TryGetProperty("occurred_on", out var on) ? on.GetString() : null;
        var type = root.TryGetProperty("exception", out var exception) && exception.TryGetProperty("type", out var named) ? named.GetString() : null;

        return $"Crash on {day} · {type?[(type.LastIndexOf('.') + 1)..]}";
    }
}

/// <summary>
/// Settings › Diagnostics: the paths and version this app is using, and crash reports: whether they are sent, and those waiting.
/// </summary>
/// <remarks>
/// The cheapest support tool this project will ever build. Nearly every "it can't find my vault" is
/// answered by showing which paths are actually in use and whether an environment variable is
/// overriding them.
/// </remarks>
internal sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly CrashReporting? _crashes;
    private readonly AppAuthority? _authority;
    private readonly ClipboardCountdown? _clipboard;

    internal DiagnosticsViewModel(AppVaultSession session, string? home, CrashReporting? crashes = null, AppAuthority? authority = null, ClipboardCountdown? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _authority = authority;
        _clipboard = clipboard;

        _session = session;
        _home = home;
        _crashes = crashes;
        CopyCommand = new AsyncRelayCommand(CopyAsync, () => _clipboard is not null);
        ReadReports();
    }

    /// <summary>The vault that is open.</summary>
    internal string VaultPath => _session.VaultPath ?? "none";

    /// <summary>Where the machine-local files live.</summary>
    internal string HomePath => KeypasteHome.Resolve(_home);

    /// <summary>Whether <c>KEYPASTE_HOME</c> chose that folder, said under it only when it did.</summary>
    internal bool HomeIsOverridden => !string.IsNullOrEmpty(_home);

    /// <summary>Whether an unlock of this app answers agents, in words; which process and unlock is the tooltip, for a support request.</summary>
    internal string AnsweringAgents => _authority?.Status is AuthorityStatus.Serving ? Strings.Yes : Strings.No;

    internal string AnsweringAgentsDetail => _authority?.Status is AuthorityStatus.Serving serving
        ? string.Create(CultureInfo.InvariantCulture, $"process {serving.Owner.ProcessId} · unlock {serving.Session}")
        : string.Empty;

    /// <summary>The app's version as a release names it, for a bug report.</summary>
#pragma warning disable CA1822
    internal string Version => CoreInfo.Version;
#pragma warning restore CA1822

    /// <summary>Copies every row above as text, for a support request.</summary>
    internal AsyncRelayCommand CopyCommand { get; }

    /// <summary>Whether the app composed crash reports, so their group is shown.</summary>
    internal bool HasCrashReports => _crashes is not null;

    /// <summary>Shows the terms screen when the switch is turned on under terms not agreed to; the view sets it.</summary>
    internal Action<FirstLaunchViewModel>? AskTerms { get; set; }

    /// <summary>Whether crash reports are sent; turning it off deletes the reports waiting, and turning it on under terms not agreed to asks first.</summary>
    internal bool SendCrashReports
    {
        get => _crashes?.IsOn == true;
        set
        {
            if (_crashes is not { } crashes || value == crashes.IsOn)
            {
                return;
            }

            if (value && !crashes.HasAgreed)
            {
                AskTerms?.Invoke(FirstLaunchViewModel.ForSwitch(TermsOfUse.Carried, () => TookAgreement(crashes)));
                return;
            }

            var saved = crashes.SetSending(value);
            CrashSwitchMoved();
            Message = saved ? string.Empty : "That preference could not be saved, so it will not survive a restart. Check that keypaste's folder can be written, then change it again.";
        }
    }

    /// <summary>What the last change or copy did, or nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>The crash reports waiting to be sent, oldest first.</summary>
    internal ObservableCollection<CrashReportRow> Reports { get; } = [];

    internal bool HasReports => Reports.Count > 0;

    /// <summary>How many reports wait, and what becomes of them.</summary>
    internal string ReportsSummary => _crashes switch
    {
        null => string.Empty,
        { IsOn: false } when Reports.Count == 0 => "Send crash reports is off, so none are kept.",
        _ when Reports.Count == 0 => "No crash reports are waiting to be sent.",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"{Reports.Count} crash {(Reports.Count == 1 ? "report is" : "reports are")} waiting to be sent.") + Where(),
    };

    private string Where() => _crashes switch
    {
        { IsOn: false } => " Send crash reports is off, so none of them is sent.",
        { Endpoint: null } => " This copy of keypaste was not built as one of its packages, so it sends none of them.",
        _ => string.Empty,
    };

    private void TookAgreement(CrashReporting crashes)
    {
        var saved = crashes.Agree(send: true);
        CrashSwitchMoved();
        Message = saved ? string.Empty : "Crash reports are on until you quit, because keypaste could not keep this in its settings file, app.toml.";
    }

    private void CrashSwitchMoved()
    {
        OnPropertyChanged(nameof(SendCrashReports));
        ReadReports();
    }

    private async Task CopyAsync()
    {
        if (_clipboard is null)
        {
            return;
        }

        var text = string.Join(
            Environment.NewLine,
            $"{Strings.Version}: {Version}",
            $"{Strings.Vault}: {VaultPath}",
            $"{Strings.KeypasteHome}: {HomePath}{(HomeIsOverridden ? $" ({Strings.SetByKeypasteHome})" : string.Empty)}",
            $"{Strings.AnsweringAgents}: {AnsweringAgents}{(AnsweringAgentsDetail.Length > 0 ? $" ({AnsweringAgentsDetail})" : string.Empty)}");

        await _clipboard.CopyPlainAsync(text, Strings.Diagnostics).ConfigureAwait(true);
    }

    private void ReadReports()
    {
        Reports.Clear();

        foreach (var report in _crashes?.Waiting() ?? [])
        {
            Reports.Add(new CrashReportRow(report, Delete));
        }

        OnPropertyChanged(nameof(ReportsSummary));
        OnPropertyChanged(nameof(HasReports));
    }

    private void Delete(CrashReportRow row)
    {
        _crashes?.Delete(row.Report);
        ReadReports();
    }
}
