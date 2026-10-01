using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Keypaste.App.Tests.Rendering;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// With the tray on and the app running in a home, a second start under the same user and home shows the first one's
/// window and exits, and a second start at login exits showing nothing (V-F.38).
/// </summary>
/// <remarks>
/// The first app is composed by <see cref="App.Launch"/> into Avalonia's own lifetime holding the claim
/// <c>Program.Run</c> takes, and the second start is <c>Program.Run</c> itself, given a start that fails the test if
/// it is ever asked to run a second app.
/// </remarks>
public sealed class SecondStartTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(500);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task A_second_start_shows_the_running_apps_window_and_exits() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
            Assert.False(app.Main!.IsVisible);

            using var stderr = new StringWriter();
            Assert.Equal(0, await SecondStartAsync([], home, stderr, _wait));

            Assert.True(app.Main.IsVisible, "the start exited before the running app showed its window");
            Assert.Same(app.Main, lifetime.MainWindow);
            Assert.Equal(1, claim.Requests);
            Assert.Empty(stderr.ToString());
            app.Main.Close();
        });

    [Fact]
    public Task A_second_start_at_login_exits_showing_nothing() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            using var stderr = new StringWriter();
            Assert.Equal(0, Program.Run(["--background"], home.Path, NoSecondApp, stderr, _wait));

            await Task.Delay(_settle, Token);
            WindowInput.Drain();
            Assert.False(app.Main!.IsVisible, "a start at login showed the running app's window");
            Assert.Null(lifetime.MainWindow);
            Assert.Equal(0, claim.Requests);

            // The running app was listening all along: a person's start still reaches it.
            Assert.Equal(0, await SecondStartAsync([], home, stderr, _wait));
            Assert.True(app.Main.IsVisible);
            Assert.Equal(1, claim.Requests);
            app.Main.Close();
        });

    [Fact]
    public Task A_start_while_the_running_app_quits_is_not_told_it_was_shown() =>
        HeadlessSession.On(async () =>
        {
            using var home = new TempHome();
            using var claim = Claim(home);
            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { Preferences = Tray(home), Claim = claim };
            app.Launch(lifetime, background: true);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            app.Tray!.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == AppTray.QuitHeader).Command!.Execute(null);

            using var stderr = new StringWriter();
            Assert.Equal(1, await SecondStartAsync([], home, stderr, _settle));
            Assert.False(app.Main!.IsVisible);
            Assert.Equal(0, claim.Requests);
        });

    [Fact]
    public void A_start_the_holder_never_answers_says_so_and_runs_nothing()
    {
        using var home = new TempHome();
        using var claim = Claim(home);
        using var stderr = new StringWriter();

        Assert.Equal(1, Program.Run([], home.Path, NoSecondApp, stderr, TimeSpan.FromMilliseconds(300)));
        Assert.Contains("already running", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_runs_the_app_as_soon_as_the_holder_that_never_answered_quits()
    {
        using var home = new TempHome();
        using var claim = Claim(home);
        AppClaim? taken = null;
        var started = Stopwatch.GetTimestamp();

        var starting = Task.Run(
            () => Program.Run([], home.Path, given => { taken = given; return 7; }, TextWriter.Null, _wait),
            Token);
        await Task.Delay(_settle, Token);
        claim.Dispose();

        Assert.Equal(7, await starting.WaitAsync(_wait, Token));
        Assert.True(Stopwatch.GetElapsedTime(started) < _wait / 2, "the start waited out its time instead of taking the freed home");
        Assert.NotNull(taken);
    }

    [Fact]
    public void A_start_the_runtime_refuses_at_a_squatted_socket_says_so_at_once()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Assert.Skip("Windows has no socket file, and root may connect to any");
            return;
        }

        using var home = new TempHome();
        using var claim = Claim(home);
        var path = Path.Combine(Path.GetTempPath(), "CoreFxPipe_") + claim.Endpoint;
        using var squatter = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            squatter.Bind(new UnixDomainSocketEndPoint(path));
            squatter.Listen(1);
            File.SetUnixFileMode(path, UnixFileMode.None);

            using var stderr = new StringWriter();
            var started = Stopwatch.GetTimestamp();
            Assert.Equal(1, Program.Run([], home.Path, NoSecondApp, stderr, _wait));
            Assert.True(Stopwatch.GetElapsedTime(started) < _wait / 2, "a refused start waited out its time");
            Assert.Contains("already running", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Off the UI thread, which the running app needs to show its window before it answers.
    private static async Task<int> SecondStartAsync(string[] args, TempHome home, TextWriter stderr, TimeSpan wait)
    {
        var starting = Task.Run(() => Program.Run(args, home.Path, NoSecondApp, stderr, wait), Token);
        await Until(() => starting.IsCompleted);
        return await starting;
    }

    private static int NoSecondApp(AppClaim _) =>
        throw new InvalidOperationException("a second start ran a second app beside the first");

    private static AppClaim Claim(TempHome home)
    {
        Assert.True(AppClaim.TryAcquire(home.Path, out var claim));
        return claim;
    }

    private static DesktopPreferences Tray(TempHome home)
    {
        var preferences = new DesktopPreferences(home.Path);
        preferences.Update(AppSettings.Default with { StayInTray = true });
        return preferences;
    }

    private static void Attach(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod(
            "SubscribeGlobalEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(subscribe is not null, "Avalonia no longer has SubscribeGlobalEvents; attach the lifetime another way");
        subscribe.Invoke(lifetime, null);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the app never reached that state");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }
}
