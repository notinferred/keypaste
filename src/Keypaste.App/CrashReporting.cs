using System.Reflection;
using Keypaste.App.Navigation;
using Keypaste.App.Terms;
using Keypaste.Core.Audit;
using Keypaste.Core.CrashReports;
using Keypaste.Core.Settings;

namespace Keypaste.App;

/// <summary>
/// The desktop app's crash reports for one run: the hooks that catch what nothing else did, the reports kept while the
/// person has chosen to send them, the run marker and the sender (D-0556, D-0594 to D-0598).
/// </summary>
/// <remarks>
/// Nothing is kept or sent unless <c>app.toml</c>, read whole, says 1 under the terms this build shows. Every report is
/// <see cref="CrashScrubber"/>'s; this adds only when it failed, how long the app had run and the screen's name. Nothing
/// here throws: a report that cannot be made or kept is lost, and the app carries on or ends as it would have.
/// </remarks>
internal sealed class CrashReporting : IDisposable
{
    /// <summary>The assembly metadata only app.yml's packages carry, naming keypaste.com (D-0598).</summary>
    internal const string EndpointMetadata = "KeypasteCrashEndpoint";

    /// <summary>The locked notice an agent's request raises could not be shown (D-0596).</summary>
    internal const string LockedNoticeSite = "LockedNotice.Show";

    /// <summary>The notice after an Always for could not be shown.</summary>
    internal const string AlwaysForNoticeSite = "WindowApprovalChannel.Tell";

    /// <summary>A prompt window could not be shown, so the request was refused.</summary>
    internal const string PromptSite = "WindowApprovalChannel.Show";

    /// <summary>Windows Hello failed in a way it does not report as an outcome.</summary>
    internal const string WindowsHelloSite = "WindowsHello.Sign";

    /// <summary>The owner refused a request as one that could not be completed.</summary>
    internal const string OwnerSite = "SessionAuthority.Decided";

    private readonly DesktopPreferences _preferences;
    private readonly CrashOutbox _outbox;
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly Func<HttpMessageHandler> _transport;
    private readonly string _terms;
    private readonly Lock _gate = new();
    private HttpMessageHandler? _handler;
    private CancellationTokenSource? _sending;
    private string? _screen;
    private bool _hooked;
    private bool _disposed;

    /// <summary>Reports for one home.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null for the default home.</param>
    /// <param name="preferences">The <c>app.toml</c> the app runs from, which holds the person's choice.</param>
    /// <param name="endpoint">Where reports go, from <see cref="EndpointFor"/>; null sends none.</param>
    /// <param name="clock">The clock reports are dated and the sender's passes timed on.</param>
    /// <param name="transport">Makes the sender's transport; <see cref="CrashSender.CreateTransport"/> unless a test gives another.</param>
    /// <param name="terms">The terms' version a choice is made under; the carried terms' unless a test gives another.</param>
    internal CrashReporting(
        string? home,
        DesktopPreferences preferences,
        Uri? endpoint,
        TimeProvider? clock = null,
        Func<HttpMessageHandler>? transport = null,
        string? terms = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        _preferences = preferences;
        _clock = clock ?? TimeProvider.System;
        _outbox = new CrashOutbox(KeypasteHome.CrashReportsPath(home), _clock);
        _started = _clock.GetTimestamp();
        _transport = transport ?? CrashSender.CreateTransport;
        _terms = terms ?? TermsOfUse.Carried.Version;
        Endpoint = endpoint;
    }

    /// <summary>The reports of the process the app's start runs, sent where this build and <c>KEYPASTE_CRASH_URL</c> say.</summary>
    internal static CrashReporting ForThisProcess(string? home, DesktopPreferences preferences) =>
        new(
            home,
            preferences,
            EndpointFor(Environment.GetEnvironmentVariable(CrashSender.EnvironmentVariable), PackagedEndpoint(typeof(CrashReporting).Assembly)));

    /// <summary>Where reports go, or null where nothing is sent.</summary>
    internal Uri? Endpoint { get; }

    /// <summary>The outbox's folder.</summary>
    internal string Folder => _outbox.Folder;

    /// <summary>Whether reports are kept and sent: the person chose to under the terms this build shows.</summary>
    internal bool IsOn => IsChosen(_preferences.Current, _terms);

    /// <summary>Whether the first run's notice is to be shown: nobody chose under these terms, and <c>app.toml</c> can take the answer.</summary>
    internal bool AwaitsAnswer => _preferences.Readable && _preferences.Current.TermsShown != _terms;

    /// <summary>The app's name for the screen open, which a report carries.</summary>
    internal string? Screen
    {
        get => Volatile.Read(ref _screen);
        set => Volatile.Write(ref _screen, value);
    }

    /// <summary>Where reports go: the loopback server <c>KEYPASTE_CRASH_URL</c> names in any build, else what the package names, else nowhere.</summary>
    /// <param name="fromEnvironment">The variable's value.</param>
    /// <param name="packaged">What <see cref="PackagedEndpoint"/> found.</param>
    internal static Uri? EndpointFor(string? fromEnvironment, Uri? packaged) =>
        string.IsNullOrEmpty(fromEnvironment) ? packaged
        : CrashSender.TryResolveEndpoint(fromEnvironment, out var loopback) ? loopback
        : null;

    /// <summary>keypaste.com when the assembly is one app.yml packaged, which names it in <see cref="EndpointMetadata"/>; otherwise null.</summary>
    internal static Uri? PackagedEndpoint(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var named = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(metadata => metadata.Key == EndpointMetadata)?.Value;

        return Uri.TryCreate(named, UriKind.Absolute, out var uri) && uri.AbsoluteUri == CrashSender.KeypasteCom.AbsoluteUri
            ? CrashSender.KeypasteCom
            : null;
    }

    /// <summary>Whether <paramref name="settings"/> sends reports under the terms whose version is <paramref name="terms"/>.</summary>
    internal static bool IsChosen(AppSettings settings, string terms)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.SendCrashReports == true && settings.TermsShown == terms;
    }

    /// <summary>A screen's name as a report carries it, the place the person sees.</summary>
    internal static string ScreenOf(DestinationKind kind) => kind switch
    {
        DestinationKind.Entries => "items",
        DestinationKind.EnvSets => "projects",
        DestinationKind.AgentActivity => "agents",
        DestinationKind.AgentHistory => "agent-history",
        DestinationKind.Log => "activity-log",
        DestinationKind.Settings => "settings",
        DestinationKind.Advanced => "advanced",
        DestinationKind.Trash => "trash",
        DestinationKind.Sharing => "share-links",
        DestinationKind.Tokens => "scoped-tokens",
        DestinationKind.Diagnostics => "diagnostics",
        _ => "other",
    };

    /// <summary>Takes the person's choice, at first run or in Settings, under the terms this build shows.</summary>
    /// <param name="send">Whether to send reports.</param>
    /// <returns>Whether <c>app.toml</c> took it; the choice holds for this run either way.</returns>
    internal bool Choose(bool send)
    {
        var saved = _preferences.Update(_preferences.Current with { SendCrashReports = send, TermsShown = _terms });
        Apply(takeEnded: false);
        return saved;
    }

    /// <summary>
    /// Starts the run: while on, an earlier run that ended with nothing able to report it is reported, the marker is set and
    /// the sender started; once the person chose not to send, whatever waits is deleted.
    /// </summary>
    internal void Start() => Apply(takeEnded: true);

    /// <summary>Catches what nothing else does in this process: an exception on a thread but the UI's, whose own handler the app sets, and a failed task nobody awaited.</summary>
    /// <remarks>Only the app's start calls this, since the hooks are the process's and a test's process is shared.</remarks>
    internal void Hook()
    {
        lock (_gate)
        {
            if (_hooked || _disposed)
            {
                return;
            }

            _hooked = true;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobserved;
        }
    }

    /// <summary>Keeps a report of an exception nothing caught.</summary>
    /// <returns>Whether a report was kept.</returns>
    internal bool Crash(Exception exception) => Keep(() => CrashScrubber.Crash(exception, Context()));

    /// <summary>Keeps a report of an exception a catch-everything block caught at <paramref name="site"/>, one of this class's constants.</summary>
    internal void Unexpected(Exception exception, string site) => Keep(() => CrashScrubber.Unexpected(exception, site, Context()));

    /// <summary>The reports waiting, oldest first.</summary>
    internal IReadOnlyList<CrashReport> Waiting() => _outbox.Waiting();

    /// <summary>Deletes a waiting report, as Diagnostics' Delete does.</summary>
    /// <returns>Whether it no longer waits as it was.</returns>
    internal bool Delete(CrashReport report) => _outbox.Remove(report);

    /// <summary>Ends the run cleanly: the hooks come off, sending stops without waiting, and the marker goes.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_hooked)
            {
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
                TaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            StopSending();
            _outbox.ClearRunning();
        }
    }

    private void Apply(bool takeEnded)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (IsOn)
            {
                if (takeEnded && _outbox.TakeEndedRun() is { } ended)
                {
                    _outbox.Add(ended);
                }

                _outbox.MarkRunning();
                StartSending();
                return;
            }

            StopSending();

            // An answer still to be given under new terms leaves what waits for it.
            if (_preferences.Current.SendCrashReports == false)
            {
                _outbox.Clear();
            }
        }
    }

    private void StartSending()
    {
        if (_sending is not null || Endpoint is not { } endpoint)
        {
            return;
        }

        _handler = _transport();
        _sending = new CancellationTokenSource();

        // Its first await is the 30-second wait, and every await after it leaves the calling thread, the UI's included.
        _ = new CrashSender(_handler, endpoint, _outbox, _clock).RunAsync(_sending.Token);
    }

    private void StopSending()
    {
        _sending?.Cancel();
        _sending?.Dispose();
        _sending = null;
        _handler?.Dispose();
        _handler = null;
    }

    private bool Keep(Func<CrashReport> report)
    {
        if (!IsOn)
        {
            return false;
        }

        try
        {
            return _outbox.Add(report());
        }
        catch (Exception)
        {
            // A report that cannot be made is lost; the failure it would have described goes on as it would have.
            return false;
        }
    }

    private CrashContext Context() => new(_clock.GetUtcNow(), _clock.GetElapsedTime(_started), Screen);

    /// <summary>What <see cref="Hook"/> has an exception off the UI thread do: keep its report, which then stands for the run's end.</summary>
    /// <remarks><c>internal</c> so a test runs it as the runtime does, which a test process cannot survive.</remarks>
    internal void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // The next start makes no second report from the marker.
        if (e.ExceptionObject is Exception exception && Crash(exception) && e.IsTerminating)
        {
            _outbox.ClearRunning();
        }
    }

    /// <summary>What <see cref="Hook"/> has a failed task nobody awaited do: keep its report, and the app runs on.</summary>
    internal void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        Keep(() => CrashScrubber.UnobservedTask(e.Exception, Context()));
    }
}
