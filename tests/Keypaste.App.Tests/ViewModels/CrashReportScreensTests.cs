using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Terms;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.CrashReports;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Settings' Send crash reports switch shows whether reports are sent and changes it, and Diagnostics lists each report
/// waiting as the exact JSON a request would carry, with Delete (D-0594).
/// </summary>
public sealed class CrashReportScreensTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;
    private readonly DesktopPreferences _preferences;
    private readonly CrashReporting _crashes;

    public CrashReportScreensTests()
    {
        _session = new AppVaultSession(_clock, home: _fixture.Home);
        _preferences = new DesktopPreferences(_fixture.Home);
        _crashes = new CrashReporting(_fixture.Home, _preferences, endpoint: null, _clock);
    }

    private string SettingsFile => KeypasteHome.SettingsPath(_fixture.Home);

    public void Dispose()
    {
        _crashes.Dispose();
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void The_switch_shows_the_state_and_turning_it_off_deletes_what_waits_and_saves_zero()
    {
        _crashes.Agree();
        _crashes.Crash(Thrown());
        using var settings = Settings();

        Assert.True(settings.HasCrashReports);
        Assert.True(settings.SendCrashReports);

        settings.SendCrashReports = false;

        Assert.False(settings.SendCrashReports);
        Assert.False(_crashes.IsOn);
        Assert.Empty(_crashes.Waiting());
        Assert.False(Directory.Exists(_crashes.Folder));
        Assert.False(AppSettings.Load(SettingsFile).SendCrashReports);
        Assert.False(settings.HasMessage);

        settings.SendCrashReports = true;

        Assert.True(_crashes.IsOn);
        Assert.Equal((true, TermsOfUse.Carried.Version), (AppSettings.Load(SettingsFile).SendCrashReports, AppSettings.Load(SettingsFile).TermsAgreed));
    }

    [Fact]
    public void A_preference_changed_after_the_switch_keeps_it()
    {
        _crashes.Agree();
        _crashes.SetSending(false);
        using var settings = Settings();

        settings.SendCrashReports = true;
        settings.Theme = AppTheme.Dark;
        settings.LockWhenMinimized = true;

        var saved = AppSettings.Load(SettingsFile);
        Assert.Equal((true, AppTheme.Dark), (saved.SendCrashReports, saved.Theme));
        Assert.True(_crashes.IsOn);
    }

    [Fact]
    public void Turned_on_under_terms_not_agreed_to_the_switch_asks_and_only_Continue_starts_reports()
    {
        File.WriteAllText(SettingsFile, "[[settings]]\ncrash_reports = 0\nterms = \"2020-01-01\"\n");
        var preferences = new DesktopPreferences(_fixture.Home);
        using var crashes = new CrashReporting(_fixture.Home, preferences, endpoint: null, _clock);
        using var settings = new SettingsViewModel(_session, _fixture.Home, preferences, _ => { }) { Crashes = crashes };
        FirstLaunchViewModel? asked = null;
        settings.AskTerms = terms => asked = terms;

        settings.SendCrashReports = true;

        Assert.NotNull(asked);
        Assert.False(settings.SendCrashReports);
        Assert.Equal((false, "2020-01-01"), (AppSettings.Load(SettingsFile).SendCrashReports, AppSettings.Load(SettingsFile).TermsAgreed));

        asked.ContinueCommand.Press();

        Assert.True(settings.SendCrashReports);
        Assert.Equal((true, TermsOfUse.Carried.Version), (AppSettings.Load(SettingsFile).SendCrashReports, AppSettings.Load(SettingsFile).TermsAgreed));
        Assert.False(settings.HasMessage);
    }

    [Fact]
    public void Agreeing_from_the_switch_over_a_file_that_does_not_read_says_it_lasts_the_run_and_leaves_the_file()
    {
        const string handEdit = "[[settings]\nhalf an edit";
        File.WriteAllText(SettingsFile, handEdit);
        var preferences = new DesktopPreferences(_fixture.Home);
        using var crashes = new CrashReporting(_fixture.Home, preferences, endpoint: null, _clock);
        using var settings = new SettingsViewModel(_session, _fixture.Home, preferences, _ => { }) { Crashes = crashes };
        settings.AskTerms = terms => terms.ContinueCommand.Press();

        settings.SendCrashReports = true;

        Assert.True(crashes.IsOn);
        Assert.Equal(handEdit, File.ReadAllText(SettingsFile));
        Assert.Contains("until you quit", settings.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_crash_reports_composed_the_switch_is_not_offered()
    {
        using var settings = new SettingsViewModel(_session, _fixture.Home, _preferences, _ => { });

        Assert.False(settings.HasCrashReports);
        Assert.False(settings.SendCrashReports);

        settings.SendCrashReports = true;

        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Diagnostics_lists_each_waiting_report_as_its_exact_json_and_delete_removes_it()
    {
        _crashes.Agree();
        _crashes.Crash(Thrown());
        _crashes.Unexpected(Thrown(), CrashReporting.PromptSite);
        var waiting = _crashes.Waiting();

        var diagnostics = new DiagnosticsViewModel(_session, _fixture.Home, _crashes);

        Assert.True(diagnostics.HasCrashReports);
        Assert.Equal(waiting.Select(report => report.Text), diagnostics.Reports.Select(row => row.Json));
        Assert.Equal(
            "2 crash reports are waiting to be sent. Each is shown as it would be sent. This copy of keypaste was not built as one of its packages, so it sends none of them.",
            diagnostics.ReportsSummary);

        diagnostics.Reports[0].AskDeleteCommand.Press();
        Assert.True(diagnostics.Reports[0].IsConfirming);
        Assert.Equal(2, _crashes.Waiting().Count);
        diagnostics.Reports[0].CancelDeleteCommand.Press();
        Assert.False(diagnostics.Reports[0].IsConfirming);
        Assert.Equal(2, _crashes.Waiting().Count);

        diagnostics.Reports[0].DeleteCommand.Press();

        Assert.Equal([waiting[1].Text], diagnostics.Reports.Select(row => row.Json));
        Assert.Equal([waiting[1].Text], _crashes.Waiting().Select(report => report.Text));
        Assert.StartsWith("1 crash report is waiting to be sent.", diagnostics.ReportsSummary, StringComparison.Ordinal);

        diagnostics.Reports[0].DeleteCommand.Press();

        Assert.Empty(diagnostics.Reports);
        Assert.Equal("No crash reports are waiting to be sent.", diagnostics.ReportsSummary);
    }

    [Fact]
    public void Diagnostics_says_when_the_switch_is_off_and_where_nothing_is_composed_shows_no_list()
    {
        _crashes.Agree();
        _crashes.SetSending(false);

        Assert.Equal("Send crash reports is off in Settings, so none are kept.", new DiagnosticsViewModel(_session, _fixture.Home, _crashes).ReportsSummary);

        var none = new DiagnosticsViewModel(_session, _fixture.Home);
        Assert.False(none.HasCrashReports);
        Assert.Empty(none.Reports);
    }

    [Fact]
    public void Diagnostics_names_reports_a_sending_build_has_waiting_with_nothing_more()
    {
        using var sending = new CrashReporting(_fixture.Home, _preferences, CrashSender.KeypasteCom, _clock, () => new HttpClientHandler());
        sending.Agree();
        sending.Crash(Thrown());

        Assert.Equal(
            "1 crash report is waiting to be sent. Each is shown as it would be sent.",
            new DiagnosticsViewModel(_session, _fixture.Home, sending).ReportsSummary);
    }

    [Fact]
    public void The_shell_tells_crash_reports_each_screen_it_shows()
    {
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(_session, _fixture.Home, null, clock: _clock, preferences: _preferences) { Crashes = _crashes };

        Assert.Equal("items", _crashes.Screen);

        shell.Current = Destinations.Of(DestinationKind.Settings);
        Assert.Equal("settings", _crashes.Screen);
        Assert.True(Assert.IsType<SettingsViewModel>(shell.Content).HasCrashReports);

        shell.Current = Destinations.Of(DestinationKind.Diagnostics);
        Assert.Equal("diagnostics", _crashes.Screen);
        Assert.True(Assert.IsType<DiagnosticsViewModel>(shell.Content).HasCrashReports);
    }

    private SettingsViewModel Settings() =>
        new(_session, _fixture.Home, _preferences, _ => { }) { Crashes = _crashes };

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("SENTINEL-SCREENS-MESSAGE");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }
}
