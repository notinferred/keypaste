using System.Collections.ObjectModel;
using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core.Audit;
using Keypaste.Core.CrashReports;

namespace Keypaste.App.ViewModels;

/// <summary>A crash report waiting to be sent, shown as the exact bytes that would be sent.</summary>
internal sealed class CrashReportRow
{
    internal CrashReportRow(CrashReport report, Action<CrashReportRow> delete)
    {
        ArgumentNullException.ThrowIfNull(delete);

        Report = report;
        DeleteCommand = new RelayCommand(() => delete(this));
    }

    internal CrashReport Report { get; }

    /// <summary>The report's JSON, byte for byte what the request carries.</summary>
    internal string Json => Report.Text;

    /// <summary>Deletes it unsent.</summary>
    internal RelayCommand DeleteCommand { get; }
}

/// <summary>
/// Settings › Advanced › Diagnostics: the paths and version this app is using, and the crash reports waiting to be sent.
/// </summary>
/// <remarks>
/// The cheapest support tool this project will ever build. Nearly every "it can't find my vault" is
/// answered by showing which paths are actually in use and whether an environment variable is
/// overriding them.
/// </remarks>
internal sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly CrashReporting? _crashes;

    internal DiagnosticsViewModel(AppVaultSession session, string? home, CrashReporting? crashes = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _home = home;
        _crashes = crashes;
        ReadReports();
    }

    /// <summary>The vault that is open.</summary>
    internal string VaultPath => _session.VaultPath ?? "none";

    /// <summary>Where the machine-local files live.</summary>
    internal string HomePath => KeypasteHome.Resolve(_home);

    /// <summary>Whether <c>KEYPASTE_HOME</c> is overriding that.</summary>
    internal string HomeOverride => string.IsNullOrEmpty(_home) ? "not set" : _home;

    /// <summary>The app's version, for a bug report.</summary>
    internal static string Version =>
        typeof(DiagnosticsViewModel).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Whether the app composed crash reports, so their list is shown.</summary>
    internal bool HasCrashReports => _crashes is not null;

    /// <summary>The crash reports waiting to be sent, oldest first.</summary>
    internal ObservableCollection<CrashReportRow> Reports { get; } = [];

    /// <summary>How many reports wait, and what becomes of them.</summary>
    internal string ReportsSummary => _crashes switch
    {
        null => string.Empty,
        { IsOn: false } when Reports.Count == 0 => "Send crash reports is off in Settings, so none are kept.",
        _ when Reports.Count == 0 => "No crash reports are waiting to be sent.",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"{Reports.Count} crash {(Reports.Count == 1 ? "report is" : "reports are")} waiting to be sent. Each is shown as it would be sent.") + Where(),
    };

    private string Where() => _crashes switch
    {
        { IsOn: false } => " Send crash reports is off in Settings, so none of them is sent.",
        { Endpoint: null } => " This copy of keypaste was not built as one of its packages, so it sends none of them.",
        _ => string.Empty,
    };

    private void ReadReports()
    {
        Reports.Clear();

        foreach (var report in _crashes?.Waiting() ?? [])
        {
            Reports.Add(new CrashReportRow(report, Delete));
        }

        Raise(nameof(ReportsSummary));
    }

    private void Delete(CrashReportRow row)
    {
        _crashes?.Delete(row.Report);
        ReadReports();
    }
}
