using System.Text.RegularExpressions;
using Keypaste.App.Navigation;
using Keypaste.App.Terms;
using Keypaste.Core.Audit;
using Keypaste.Core.CrashReports;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The app's crash reports for a run (D-0594 to D-0598): kept only once the person chose Send crash reports under the terms
/// this build carries, deleted when they choose not to, the run marker turned into a report at the next start, and the
/// sender started only while on and only where an endpoint is named.
/// </summary>
/// <remarks>None of these sets the process's hooks, which <c>scripts/verify-crash-report.sh</c> drives in a process of its own.</remarks>
public sealed partial class CrashReportingTests : IDisposable
{
    private readonly TempHome _home = new();
    private readonly ManualClock _clock = new(AppClock.Start);

    private string Folder => KeypasteHome.CrashReportsPath(_home.Path);

    private string Marker => Path.Combine(Folder, CrashOutbox.RunMarkerName);

    private string SettingsFile => KeypasteHome.SettingsPath(_home.Path);

    public void Dispose() => _home.Dispose();

    [Fact]
    public void Before_the_person_answers_the_first_run_waits_and_nothing_is_kept()
    {
        using var crashes = Reporting();

        crashes.Start();

        Assert.True(crashes.AwaitsAnswer);
        Assert.False(crashes.IsOn);
        Assert.False(crashes.Crash(Thrown("SENTINEL-UNANSWERED")));
        Assert.False(Directory.Exists(Folder));
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void Send_crash_reports_is_saved_under_the_carried_terms_and_a_crash_is_kept_with_its_screen_and_uptime()
    {
        const string message = "SENTINEL-KEPT-MESSAGE-41ac";
        using var crashes = Reporting();

        Assert.True(crashes.Choose(send: true));

        var saved = AppSettings.Load(SettingsFile);
        Assert.Equal((true, TermsOfUse.Carried.Version), (saved.SendCrashReports, saved.TermsShown));
        Assert.False(crashes.AwaitsAnswer);
        Assert.True(File.Exists(Marker));

        crashes.Screen = CrashReporting.ScreenOf(DestinationKind.AgentActivity);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(crashes.Crash(Thrown(message)));

        var report = Assert.Single(crashes.Waiting()).Text;
        Assert.Contains("\"kind\":\"crash\"", report, StringComparison.Ordinal);
        Assert.Contains("\"screen\":\"agents\"", report, StringComparison.Ordinal);
        Assert.Contains("\"uptime\":\"<10m\"", report, StringComparison.Ordinal);
        Assert.Contains("\"occurred_on\":\"2026-07-28\"", report, StringComparison.Ordinal);
        Assert.DoesNotContain(message, report, StringComparison.Ordinal);
        Assert.DoesNotContain(_home.Path, report, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catch_everything_block_is_reported_with_its_site()
    {
        using var crashes = Reporting();
        crashes.Choose(send: true);

        crashes.Unexpected(Thrown("SENTINEL-UNEXPECTED"), CrashReporting.LockedNoticeSite);

        var report = Assert.Single(crashes.Waiting()).Text;
        Assert.Contains("\"kind\":\"unexpected\",\"site\":\"LockedNotice.Show\"", report, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_off_the_ui_thread_keeps_a_crash_report_that_stands_for_the_run_and_takes_its_marker()
    {
        using var crashes = Reporting();
        crashes.Choose(send: true);

        crashes.OnUnhandled(null, new UnhandledExceptionEventArgs(Thrown("SENTINEL-THREAD"), isTerminating: true));

        Assert.Contains("\"kind\":\"crash\"", Assert.Single(crashes.Waiting()).Text, StringComparison.Ordinal);
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void An_exception_off_the_ui_thread_while_switched_off_keeps_nothing_and_a_thrown_non_exception_is_no_report()
    {
        using var crashes = Reporting();
        crashes.Choose(send: false);

        crashes.OnUnhandled(null, new UnhandledExceptionEventArgs(Thrown("SENTINEL-OFF-THREAD"), isTerminating: true));
        Assert.False(Directory.Exists(Folder));

        crashes.Choose(send: true);
        crashes.OnUnhandled(null, new UnhandledExceptionEventArgs("not an exception", isTerminating: true));
        Assert.Empty(crashes.Waiting());
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public void A_failed_task_nobody_awaited_keeps_an_unobserved_task_report_and_the_marker()
    {
        using var crashes = Reporting();
        crashes.Choose(send: true);

        crashes.OnUnobserved(null, new UnobservedTaskExceptionEventArgs(new AggregateException(Thrown("SENTINEL-UNOBSERVED"))));

        var report = Assert.Single(crashes.Waiting()).Text;
        Assert.Contains("\"kind\":\"unobserved-task\"", report, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-UNOBSERVED", report, StringComparison.Ordinal);
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public void Dont_send_is_saved_and_deletes_the_reports_waiting_and_the_marker()
    {
        using var crashes = Reporting();
        crashes.Choose(send: true);
        crashes.Crash(Thrown("SENTINEL-BEFORE-OFF"));
        Assert.Single(crashes.Waiting());

        Assert.True(crashes.Choose(send: false));

        Assert.False(AppSettings.Load(SettingsFile).SendCrashReports);
        Assert.False(Directory.Exists(Folder));
        Assert.False(crashes.Crash(Thrown("SENTINEL-AFTER-OFF")));
        crashes.Unexpected(Thrown("SENTINEL-AFTER-OFF"), CrashReporting.PromptSite);
        Assert.False(Directory.Exists(Folder));
    }

    [Theory]
    [InlineData("crash_reports = 1\nterms = \"2026-01-01\"\n", false, true)]
    [InlineData("crash_reports = 2\nterms = \"{terms}\"\n", false, false)]
    [InlineData("crash_reports = 0\nterms = \"{terms}\"\n", false, false)]
    [InlineData("crash_reports = 1\n", false, true)]
    [InlineData("crash_reports = 1\nterms = \"{terms}\"\n", true, false)]
    public void Only_a_one_under_the_terms_this_build_carries_sends_and_other_terms_ask_again(string keys, bool on, bool asks)
    {
        File.WriteAllText(SettingsFile, "[[settings]]\n" + keys.Replace("{terms}", TermsOfUse.Carried.Version, StringComparison.Ordinal));

        using var crashes = Reporting();

        Assert.Equal((on, asks), (crashes.IsOn, crashes.AwaitsAnswer));
    }

    [Fact]
    public void An_app_toml_that_does_not_read_whole_sends_nothing_asks_nothing_and_is_left_as_it_is()
    {
        var written = "[[settings]]\ncrash_reports = true\nterms = \"" + TermsOfUse.Carried.Version + "\"\n";
        File.WriteAllText(SettingsFile, written);

        using var crashes = Reporting();
        crashes.Start();

        Assert.False(crashes.IsOn);
        Assert.False(crashes.AwaitsAnswer);
        Assert.False(crashes.Crash(Thrown("SENTINEL-UNREADABLE")));
        Assert.Equal(written, File.ReadAllText(SettingsFile));
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void A_start_reports_the_run_its_marker_says_ended_with_nothing_able_to_report_it_and_marks_its_own()
    {
        // What a killed run leaves: its choice and its marker, and nothing that ran at its end.
        File.WriteAllText(SettingsFile, "[[settings]]\ncrash_reports = 1\nterms = \"" + TermsOfUse.Carried.Version + "\"\n");
        Assert.True(new CrashOutbox(Folder, _clock).MarkRunning());

        _clock.Advance(TimeSpan.FromDays(1));
        using var next = Reporting();
        next.Start();

        var report = Assert.Single(next.Waiting()).Text;
        Assert.Contains("\"kind\":\"ended-unexpectedly\"", report, StringComparison.Ordinal);
        Assert.Contains("\"uptime\":null,\"screen\":null,\"occurred_on\":\"2026-07-28\"", report, StringComparison.Ordinal);
        Assert.Contains("\"exception\":null", report, StringComparison.Ordinal);
        Assert.True(File.Exists(Marker));

        next.Dispose();
        Assert.False(File.Exists(Marker));
        Assert.Single(next.Waiting());
    }

    [Fact]
    public void A_marker_another_version_left_is_taken_and_reports_nothing()
    {
        File.WriteAllText(SettingsFile, "[[settings]]\ncrash_reports = 1\nterms = \"" + TermsOfUse.Carried.Version + "\"\n");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Marker, "0.0.1 2026-07-27T10:00:00.0000000+00:00");

        using var next = Reporting();
        next.Start();

        Assert.Empty(next.Waiting());
        Assert.True(File.Exists(Marker));
        Assert.StartsWith(Core.CoreInfo.Version + " ", File.ReadAllText(Marker), StringComparison.Ordinal);
    }

    [Fact]
    public void A_start_after_dont_send_deletes_a_report_put_in_the_outbox()
    {
        using (var on = Reporting())
        {
            on.Choose(send: true);
            on.Crash(Thrown("SENTINEL-PLANTED"));
        }

        File.WriteAllText(SettingsFile, "[[settings]]\ncrash_reports = 0\nterms = \"" + TermsOfUse.Carried.Version + "\"\n");
        Assert.NotEmpty(Directory.GetFiles(Folder, "*.json"));

        using var off = Reporting();
        off.Start();

        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public async Task While_on_the_sender_posts_the_waiting_report_after_thirty_seconds_and_stops_when_switched_off()
    {
        using var server = new FakeCrashServer();
        using var crashes = new CrashReporting(_home.Path, new DesktopPreferences(_home.Path), CrashSender.KeypasteCom, _clock, () => new Forwarding(server));

        crashes.Choose(send: true);
        crashes.Crash(Thrown("SENTINEL-SENT"));
        var waiting = Assert.Single(crashes.Waiting());

        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(server.Requests);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await server.NextRequestAsync();

        var sent = Assert.Single(server.Requests);
        Assert.Equal(new Uri("https://keypaste.com/api/crash"), sent.Uri);
        Assert.Equal(waiting.Json.ToArray(), sent.Body);

        for (var i = 0; i < 100 && crashes.Waiting().Count > 0; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Empty(crashes.Waiting());

        crashes.Choose(send: false);
        crashes.Choose(send: true);
        crashes.Crash(Thrown("SENTINEL-AGAIN"));
        crashes.Choose(send: false);
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Single(server.Requests);
    }

    [Fact]
    public void Without_an_endpoint_reports_are_kept_and_never_sent()
    {
        using var server = new FakeCrashServer();
        using var crashes = new CrashReporting(_home.Path, new DesktopPreferences(_home.Path), endpoint: null, _clock, () => new Forwarding(server));

        crashes.Choose(send: true);
        crashes.Crash(Thrown("SENTINEL-UNSENT"));
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Empty(server.Requests);
        Assert.Single(crashes.Waiting());
    }

    [Fact]
    public void Reports_go_to_a_loopback_server_the_variable_names_else_where_the_package_says_and_a_source_build_says_nowhere()
    {
        Assert.Equal(new Uri("http://127.0.0.1:5123"), CrashReporting.EndpointFor("http://127.0.0.1:5123", packaged: null));
        Assert.Equal(new Uri("http://127.0.0.1:5123"), CrashReporting.EndpointFor("http://127.0.0.1:5123", CrashSender.KeypasteCom));
        Assert.Equal(CrashSender.KeypasteCom, CrashReporting.EndpointFor(null, CrashSender.KeypasteCom));
        Assert.Equal(CrashSender.KeypasteCom, CrashReporting.EndpointFor(string.Empty, CrashSender.KeypasteCom));
        Assert.Null(CrashReporting.EndpointFor("https://example.com", CrashSender.KeypasteCom));
        Assert.Null(CrashReporting.EndpointFor(null, packaged: null));
        Assert.Null(CrashReporting.PackagedEndpoint(typeof(CrashReporting).Assembly));
    }

    [Fact]
    public void Every_place_has_a_screen_name_a_report_keeps()
    {
        foreach (var kind in Enum.GetValues<DestinationKind>())
        {
            Assert.Matches(ScreenName(), CrashReporting.ScreenOf(kind));
        }

        Assert.Equal(Enum.GetValues<DestinationKind>().Length, Enum.GetValues<DestinationKind>().Select(CrashReporting.ScreenOf).Distinct().Count());
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$")]
    private static partial Regex ScreenName();

    private CrashReporting Reporting() => new(_home.Path, new DesktopPreferences(_home.Path), endpoint: null, _clock);

    private static InvalidOperationException Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message, new IOException(message));
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    /// <summary>The fake server behind a handler of its own, which the reporting disposes when sending stops and the server outlives.</summary>
    private sealed class Forwarding(HttpMessageHandler inner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var invoker = new HttpMessageInvoker(inner, disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
