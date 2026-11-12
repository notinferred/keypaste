using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// The desktop app's crash reports across real processes (4.11c): tests/Keypaste.AppDriver runs keypaste-app's own start on a headless
/// display, crash reports composed as that start composes them, and stands in on loopback for keypaste.com's /api/crash.
/// </summary>
/// <remarks>The first-launch screen and what a UI-thread exception does inside the app are App.Tests' <c>CrashJourneyTests</c>.</remarks>
public sealed class CrashReportTests
{
    private const string _entry = "SENTINEL-ENTRY-2b7d90";
    private const string _secret = "SENTINEL-PASSWORD-8e4a13";
    private const string _message = "SENTINEL-MESSAGE-3c9f57";
    private const string _vaultName = "SENTINEL-VAULT-6f1c2a";

    [Fact]
    public void A_UI_thread_crash_with_a_copy_ends_with_code_70_keeps_one_report_holding_no_sentinel_and_the_next_start_sends_it_byte_for_byte()
    {
        using var setup = new Setup();
        using var endpoint = Endpoint.Start(setup, silent: false);
        var home = setup.Home("on");

        CrashWithCopy(setup, home, endpoint.Url, switchedOn: true);
        AssertChose(home, 1);
        var report = Assert.Single(Waiting(home));
        var sent = File.ReadAllBytes(report);
        setup.AssertHoldsNothing(File.ReadAllText(report));
        var json = JsonDocument.Parse(sent).RootElement;
        Assert.Equal(("crash", JsonValueKind.Null, "items", "<1m", 1), (json.GetProperty("kind").GetString(), json.GetProperty("site").ValueKind, json.GetProperty("screen").GetString(), json.GetProperty("uptime").GetString(), json.GetProperty("count").GetInt32()));
        var exception = json.GetProperty("exception");
        Assert.Equal("System.InvalidOperationException", exception.GetProperty("type").GetString());
        Assert.NotEqual(0, exception.GetProperty("frames").GetArrayLength());
        Assert.Equal("System.IO.IOException", Assert.Single(exception.GetProperty("inner").EnumerateArray()).GetProperty("type").GetString());
        Assert.False(File.Exists(Path.Combine(home, "crash-reports", "running")), "the run a crash ended left its run marker");

        using var again = Launch(setup, home, endpoint.Url);
        again.WaitForOutput("^screen unlock");
        Assert.DoesNotContain(again.Output, line => line == "screen first-launch");
        endpoint.Process.WaitForOutput("^received 1");
        Assert.Equal(sent, File.ReadAllBytes(Path.Combine(endpoint.Folder, "1.json")));
        var head = File.ReadAllLines(Path.Combine(endpoint.Folder, "1.head"));
        Assert.Equal("POST /api/crash HTTP/1.1", head[0].TrimEnd('\r'));
        Assert.Contains(head, line => line.StartsWith("content-type: application/json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(head, line => System.Text.RegularExpressions.Regex.IsMatch(line, "^(cookie|user-agent|traceparent|tracestate|request-id|authorization):", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Until(() => Waiting(home).Count == 0, "the sent report is still waiting");
        Quit(again);
    }

    [Fact]
    public void Switched_off_the_same_crash_keeps_nothing_and_a_report_put_in_its_outbox_is_deleted_unsent()
    {
        using var setup = new Setup();
        using var endpoint = Endpoint.Start(setup, silent: false);
        var home = setup.Home("off");

        CrashWithCopy(setup, home, endpoint.Url, switchedOn: false);
        AssertChose(home, 0);
        Assert.False(Directory.Exists(Path.Combine(home, "crash-reports")), "switched off, the crash kept an outbox");

        Directory.CreateDirectory(Path.Combine(home, "crash-reports"));
        File.WriteAllText(Path.Combine(home, "crash-reports", "planted.json"), """{"kind":"crash"}""");
        using var app = Launch(setup, home, endpoint.Url);
        app.WaitForOutput("^screen unlock");
        Until(() => !Directory.Exists(Path.Combine(home, "crash-reports")), "switched off, the next start kept the outbox");
        Quit(app);
        Assert.False(File.Exists(Path.Combine(endpoint.Folder, "1.json")), "a report reached the endpoint from a home that switched reports off");
    }

    [Fact]
    public void An_endpoint_holding_the_request_delays_neither_an_unlock_nor_quitting_and_the_report_stays_waiting()
    {
        using var setup = new Setup();
        using var silent = Endpoint.Start(setup, silent: true);
        var home = setup.Home("held");
        using (var first = Launch(setup, home, silent.Url))
        {
            Agree(first);
            first.WaitForOutput("^screen unlock");
            first.Send($"unlock {setup.Vault}");
            first.WaitForOutput("^screen shell");
            first.Send($"crash {_message}");
            first.WaitForOutput("^shut down with exit code 70");
        }

        Assert.Single(Waiting(home));
        using var app = Launch(setup, home, silent.Url);
        app.WaitForOutput("^screen unlock");
        app.Send($"unlock {setup.Vault}");
        app.WaitForOutput("^screen shell");
        silent.Process.WaitForOutput("^connected");

        app.Send("lock");
        app.WaitForOutput("^screen unlock", 2);
        var unlock = Stopwatch.StartNew();
        app.Send($"unlock {setup.Vault}");
        app.WaitForOutput("^screen shell", 2);
        Assert.True(unlock.Elapsed < TimeSpan.FromSeconds(8), $"an unlock while the endpoint held the request took {unlock.Elapsed}");

        var quit = Stopwatch.StartNew();
        Quit(app);
        Assert.True(quit.Elapsed < TimeSpan.FromSeconds(5), $"quitting while the endpoint held the request took {quit.Elapsed}");
        Assert.Single(Waiting(home));
    }

    [Fact]
    public void Nothing_is_kept_before_Continue_an_older_date_asks_again_and_an_app_toml_that_does_not_read_is_left_as_it_is()
    {
        using var setup = new Setup();
        using var endpoint = Endpoint.Start(setup, silent: false);

        var passed = setup.Home("passed");
        using (var app = Launch(setup, passed, endpoint.Url))
        {
            app.WaitForOutput("^screen first-launch");
            Assert.False(File.Exists(Path.Combine(passed, "app.toml")), "the screen wrote app.toml before Continue");
            Assert.False(Directory.Exists(Path.Combine(passed, "crash-reports")), "the app wrote an outbox before Continue");
            Quit(app);
        }

        using (var app = Launch(setup, passed, endpoint.Url))
        {
            Agree(app);
            app.WaitForOutput("^screen unlock");
            AssertChose(passed, 1);
            Assert.True(File.Exists(Path.Combine(passed, "crash-reports", "running")), "agreeing set no run marker");
            Quit(app);
        }

        var older = setup.Home("older");
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(older, "app.toml"), "[[settings]]\ncrash_reports = 1\nterms = \"2020-01-01\"\n");
        using (var app = Launch(setup, older, endpoint.Url))
        {
            app.WaitForOutput("^screen first-launch");
            Assert.False(File.Exists(Path.Combine(older, "crash-reports", "running")), "the run marker was set before the new terms were agreed to");
            app.Send("continue");
            app.WaitForOutput("^screen unlock");
            AssertChose(older, 1);
            Assert.DoesNotContain("terms = \"2020-01-01\"", File.ReadAllText(Path.Combine(older, "app.toml")), StringComparison.Ordinal);
            Quit(app);
        }

        var broken = setup.Home("broken");
        Directory.CreateDirectory(broken);
        const string unreadable = "[[settings]\nhalf an edit\n";
        File.WriteAllText(Path.Combine(broken, "app.toml"), unreadable);
        using (var app = Launch(setup, broken, endpoint.Url))
        {
            app.WaitForOutput("^screen unlock");
            Assert.DoesNotContain(app.Output, line => line == "screen first-launch");
            Quit(app);
        }

        Assert.Equal(unreadable, File.ReadAllText(Path.Combine(broken, "app.toml")));
        Assert.False(Directory.Exists(Path.Combine(broken, "crash-reports")), "an app.toml that does not read kept an outbox");
    }

    [Fact]
    public void A_killed_run_is_reported_as_ended_unexpectedly_at_the_next_start_and_a_failed_task_nobody_awaited_as_unobserved()
    {
        using var setup = new Setup();
        var home = setup.Home("killed");
        using (var app = Launch(setup, home, url: ""))
        {
            Agree(app);
            app.WaitForOutput("^screen unlock");
            Assert.True(File.Exists(Path.Combine(home, "crash-reports", "running")), "a run sending reports set no run marker");
            app.Kill();
        }

        Assert.True(File.Exists(Path.Combine(home, "crash-reports", "running")), "the killed run's marker is gone");
        Assert.Empty(Waiting(home));

        using (var app = Launch(setup, home, url: ""))
        {
            app.WaitForOutput("^screen unlock");
            var ended = JsonDocument.Parse(File.ReadAllText(Assert.Single(Waiting(home)))).RootElement;
            Assert.Equal("ended-unexpectedly", ended.GetProperty("kind").GetString());
            foreach (var absent in new[] { "exception", "site", "screen", "uptime" })
            {
                Assert.Equal(JsonValueKind.Null, ended.GetProperty(absent).ValueKind);
            }

            setup.AssertHoldsNothing(ended.ToString());
            Assert.True(File.Exists(Path.Combine(home, "crash-reports", "running")), "the start after a killed run set no marker of its own");
            Quit(app);
        }

        Assert.False(File.Exists(Path.Combine(home, "crash-reports", "running")), "a run that quit cleanly left its marker");

        using (var app = Launch(setup, home, url: ""))
        {
            app.WaitForOutput("^screen unlock");
            app.Send($"unobserved {_message}");
            app.WaitForOutput("^collected");
            string? unobserved = null;
            Until(() => (unobserved = Waiting(home).FirstOrDefault(file => JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("kind").GetString() == "unobserved-task")) is not null, "a failed task nobody awaited left no report");
            setup.AssertHoldsNothing(File.ReadAllText(unobserved!));
            Assert.DoesNotContain(app.Output, line => line.StartsWith("shut down", StringComparison.Ordinal));
            Quit(app);
        }
    }

    private static void CrashWithCopy(Setup setup, string home, string url, bool switchedOn)
    {
        if (!switchedOn)
        {
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "app.toml"), "[[settings]]\ncrash_reports = 0\nterms = \"2020-01-01\"\n");
        }

        using var app = Launch(setup, home, url);
        if (switchedOn)
        {
            Agree(app);
        }

        app.WaitForOutput("^screen unlock");
        Assert.False(!switchedOn && app.Output.Contains("screen first-launch"), "the first-launch screen showed over a kept crash_reports = 0");
        app.Send($"unlock {setup.Vault}");
        app.WaitForOutput("^screen shell");
        app.Send($"copy {_entry}");
        app.WaitForOutput("^clipboard set");
        app.Send($"crash {_message} {setup.Vault} {_entry} {_secret}");
        app.WaitForOutput("^shut down with exit code 70");
        Assert.Contains(app.Output, line => line == "clipboard cleared");
        Assert.DoesNotContain(app.Error, line => line.Contains(_message, StringComparison.Ordinal));
        setup.AssertHoldsNothing(string.Join('\n', app.Output));
    }

    private static RunningProcess Launch(Setup setup, string home, string url)
    {
        Directory.CreateDirectory(home);
        var app = RunningProcess.Start(
            Built.AppDriver,
            new Dictionary<string, string> { ["KEYPASTE_HOME"] = home, ["KEYPASTE_CRASH_URL"] = url, ["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master },
            "launch");
        app.WaitForOutput("^running as process");
        return app;
    }

    private static void Agree(RunningProcess app)
    {
        app.WaitForOutput("^screen first-launch");
        app.Send("continue");
    }

    private static void Quit(RunningProcess app)
    {
        app.CloseInput();
        app.WaitForOutput("^shut down with exit code 0");
    }

    private static void AssertChose(string home, int reports)
    {
        var settings = File.ReadAllText(Path.Combine(home, "app.toml"));
        Assert.Contains($"crash_reports = {reports}", settings, StringComparison.Ordinal);
        Assert.Matches("(?m)^terms = \"[0-9]{4}-[0-9]{2}-[0-9]{2}\"", settings);
    }

    private static List<string> Waiting(string home) =>
        Directory.Exists(Path.Combine(home, "crash-reports")) ? [.. Directory.GetFiles(Path.Combine(home, "crash-reports"), "*.json").Order(StringComparer.Ordinal)] : [];

    private static void Until(Func<bool> condition, string failure)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(deadline.Elapsed < RunningProcess.Wait, failure);
            Thread.Sleep(200);
        }
    }

    /// <summary>A vault named for a sentinel, with an item named for one and holding another, and the homes each start uses.</summary>
    private sealed class Setup : IDisposable
    {
        private readonly Workspace _workspace = new();

        internal Setup()
        {
            Vault = Path.Combine(Root, $"{_vaultName}.kdbx");
            _workspace.Keypaste("init", Workspace.Master, Workspace.Master, Vault);
            _workspace.Keypaste("add", Workspace.Master, _secret, _entry, "--vault", Vault);
        }

        internal string Vault { get; }

        internal string Root => Path.GetDirectoryName(_workspace.Vault)!;

        internal string Home(string name) => Path.Combine(Root, name);

        internal void AssertHoldsNothing(string text)
        {
            foreach (var needle in new[] { _vaultName, _entry, _secret, _message, Path.GetFileName(Root) })
            {
                Assert.DoesNotContain(needle, text, StringComparison.Ordinal);
            }
        }

        public void Dispose() => _workspace.Dispose();
    }

    /// <summary>tests/Keypaste.AppDriver standing in for /api/crash on loopback, answering 204 or, silent, never answering.</summary>
    private sealed class Endpoint : IDisposable
    {
        private Endpoint(string folder, bool silent)
        {
            Folder = folder;
            var home = $"{folder}-home";
            Directory.CreateDirectory(home);
            Process = RunningProcess.Start(Built.AppDriver, new Dictionary<string, string> { ["KEYPASTE_HOME"] = home }, silent ? ["crash-endpoint", folder, "--silent"] : ["crash-endpoint", folder]);
            Url = Process.WaitForOutput("^listening on http://127.0.0.1:")["listening on ".Length..];
        }

        internal RunningProcess Process { get; }

        internal string Folder { get; }

        internal string Url { get; }

        internal static Endpoint Start(Setup setup, bool silent) => new(setup.Home(silent ? "held-received" : "received"), silent);

        public void Dispose()
        {
            Process.CloseInput();
            Process.Dispose();
        }
    }
}
