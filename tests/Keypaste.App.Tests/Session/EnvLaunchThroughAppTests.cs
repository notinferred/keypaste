using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Keypaste.Core.Projects;
using Keypaste.Core.Tests;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The Env Sets screen imports a <c>.env</c> and starts a project's command in a real terminal with
/// its set in the child's environment, only after the person confirms it and only while the unlock
/// that asked is live (E.1b, V-E.1b).
/// </summary>
/// <remarks>
/// <para>
/// The launch goes through <see cref="AppVaultSession.Environments"/> and the platform's terminal:
/// <c>cmd.exe</c> on Windows, and on Linux a stand-in emulator script that runs its <c>-e</c> command
/// as a real emulator would, since a runner has no display. The child is a real process that reports
/// what its own environment holds over a pipe, so nothing is taken from the view model's word.
/// </para>
/// <para>
/// On macOS the set reaches the child through Terminal (E.1d): the app's real authority on its own
/// endpoint, an assembled <c>keypaste.app</c> whose launcher is the real one and whose <c>keypaste</c>
/// is the built CLI, and <see cref="TerminalStandIn"/> for <c>open</c>. The launcher reads the
/// person's own request folder, so these tests run alone. On every platform the app's real authority
/// writes each Run's History line, on Windows and Linux before the terminal starts (D-NEW-C2).
/// </para>
/// </remarks>
[Collection(nameof(TerminalRequestFolder))]
public sealed class EnvLaunchThroughAppTests : IDisposable
{
    private const string _macOSOnly = "Terminal is opened through keypaste run on macOS only.";
    private const string _passSpent = "keypaste run: the run you confirmed in the app expired or was already used, so nothing was started";
    private const string _apiKey = "sk_live_e1b_api_4d9a";
    private const string _dbUrl = "postgres://e1b:db_71c2@localhost/app";
    private const string _imported = "imported_e1b_value_b83f";
    private const string _stagingKey = "sk_live_e1d_staging_5e0b";
    private const int _releaseSeconds = 3;

    private readonly DateTime _startedUtc = DateTime.UtcNow.AddSeconds(-1);
    private readonly TempVault _fixture = new();
    private readonly string _project = Directory.CreateTempSubdirectory("keypaste-e1b-project-").FullName;
    private readonly string _stubs = Directory.CreateTempSubdirectory("keypaste-e1b-terminal-").FullName;
    private readonly FakeVaultFilePicker _picker = new();
    private readonly CountingLauncher _launcher = new();
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));
    private readonly CountingChannel _asked = new();
    private readonly AppAuthority? _authority;
    private readonly TerminalStandIn? _terminal;

    public EnvLaunchThroughAppTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            ProjectVariables.Set(vault, "dev", "API_KEY", _apiKey);
            ProjectVariables.Set(vault, "dev", "DB_URL", _dbUrl);
            vault.Save();
        }

        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);

        if (OperatingSystem.IsMacOS())
        {
            TerminalStandIn.EmptyFolder();
            _terminal = new TerminalStandIn(_stubs);
        }

        _authority = new AppAuthority(_session, "kp-e1d-" + Guid.NewGuid().ToString("N")[..12], () => _asked);
        Unlock();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS())
        {
            _terminal?.Dispose();
            TerminalStandIn.EmptyFolder();
        }

        // On Windows a process that has not finished exiting still holds the project directory it runs in.
        foreach (var terminal in _launcher.Terminals)
        {
            End(terminal, grace: TimeSpan.Zero);
        }

        _authority?.Dispose();
        _session.Dispose();
        _countdown.Dispose();
        _fixture.Dispose();
        DeleteOnceReleased(_project);
        Directory.Delete(_stubs, recursive: true);
    }

    [Fact]
    public async Task Run_starts_a_real_child_in_the_directory_with_the_set_in_its_environment_and_nowhere_else()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);

        Assert.True(launch.IsConfirming);
        Assert.Contains(pipeName, launch.ConfirmStarts, StringComparison.Ordinal);
        Assert.Equal(_project, launch.ConfirmDirectory);
        Assert.Equal("API_KEY, DB_URL", launch.ConfirmKeys);
        Assert.Equal($"From {ApprovalPrompt.Shown(ProjectVariables.Home("dev"))}", launch.ConfirmSources);
        AssertNothingStarted();

        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Equal("end", report[^1]);
        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        AssertStartedIn(report);

        var commandLine = report.Single(line => line.StartsWith("cmdline=", StringComparison.Ordinal));
        AssertNoValue(commandLine, "the child's command line");
        AssertRecorded(2, ApprovalPrompt.Shown(ProjectVariables.Home("dev")));

        AssertNoValue(File.ReadAllText(KeypasteHome.ProjectsPath(_fixture.Home)), "projects.json");
        AssertNoValue(string.Join("\n", screen.Notice, screen.Error, launch.ConfirmTitle, launch.ConfirmStarts, launch.ConfirmKeys, launch.ConfirmSources), "the screen");
        AssertNoFileHoldsAValue();
    }

    /// <summary>
    /// A command sent to the background still runs in the directory with the set, though the
    /// terminal's own command has returned (PRODUCT §2).
    /// </summary>
    /// <remarks>
    /// The child outlives its terminal and, after the test hangs up, lingers longer than disposal
    /// retries the delete, so the project directory can be removed only once the test has waited for
    /// the child itself (F.42).
    /// </remarks>
    [Fact]
    public async Task A_command_run_in_the_background_has_the_set_in_the_directory_after_its_terminal_returns()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName, background: true);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);
        var report = await ReportAsync(pipe, connected);

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        AssertStartedIn(report);
    }

    [Fact]
    public async Task Open_terminal_starts_the_terminal_in_the_directory_after_confirming()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);

        if (OperatingSystem.IsMacOS())
        {
            await OpenTerminalThroughTerminalAsync(screen, launch, pipeName);
            return;
        }

        var running = launch.LaunchAsync(run: false);
        Assert.True(launch.IsConfirming);
        Assert.Equal(_project, launch.ConfirmDirectory);

        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        var started = Assert.IsType<ChildResult>(launch.LastStart);
        Assert.Equal(ChildOutcome.Started, started.Outcome);
        var start = Assert.Single(_launcher.Started);
        Assert.Equal(_project, start.WorkingDirectory);
        Assert.Equal(_apiKey, start.Environment["API_KEY"]);
        AssertNoValue(string.Join(" ", start.Arguments) + start.CommandLine, "the terminal's arguments");
    }

    [Fact]
    public async Task Cancelling_the_confirmation_starts_nothing()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out _);

        var running = launch.LaunchAsync(run: true);
        launch.CancelLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertNothingStarted();
        Assert.Null(launch.LastStart);
        Assert.False(launch.IsConfirming);
        Assert.Equal("Nothing was started.", screen.Error);
    }

    [Fact]
    public async Task A_lock_while_the_confirmation_waits_withdraws_it_and_starts_nothing()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out _);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);

        _session.Lock(VaultLockReason.Manual);
        await running.WaitAsync(TimeSpan.FromSeconds(30), Cancel);

        AssertNothingStarted();
        Assert.False(launch.IsConfirming);
        Assert.Equal("The vault was locked, so nothing was started.", screen.Error);
    }

    [Fact]
    public void A_lock_between_confirming_and_starting_starts_nothing()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out _);

        var queue = new QueuedContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(queue);

        try
        {
            var running = launch.LaunchAsync(run: true);
            Assert.True(launch.IsConfirming);

            // Confirmed, and the answer queued behind the lock: the set is read again only once the
            // queue runs, by which time the unlock that asked has ended.
            launch.ConfirmLaunchCommand.Execute(null);
            _session.Lock(VaultLockReason.Manual);
            queue.RunUntil(running);

            Assert.True(running.IsCompletedSuccessfully);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        AssertNothingStarted();
        Assert.Null(launch.LastStart);
        Assert.Equal("The vault was locked, so nothing was started.", screen.Error);
    }

    /// <summary>Once Start has released the set the launch is the person's: closing the project, which disposes its card, does not take it back.</summary>
    [Fact]
    public async Task Closing_the_project_right_after_Start_does_not_take_the_launch_back()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        launch.ConfirmLaunchCommand.Execute(null);
        screen.CloseCommand.Execute(null);
        await running.WaitAsync(Cancel);

        Assert.Null(screen.OpenProject);
        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        AssertHandedOver();
    }

    /// <summary>The unlock that confirmed the run holds its pass, so a lock before Terminal's window asks leaves that window nothing to start, even once the vault is unlocked again.</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_a_lock_between_Start_and_the_windows_request_ends_its_pass_and_starts_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        using var screen = Screen();
        var marker = Path.Combine(_project, "ran");
        var launch = MappedTo(screen, $"touch '{marker}'");
        _terminal!.BeforeWindow = () =>
        {
            _session.Lock(VaultLockReason.Manual);
            Unlock();
        };

        var running = launch.LaunchAsync(run: true);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        var (exit, output) = Assert.Single(_terminal.Windows).Ended();

        Assert.NotEqual(0, exit);
        Assert.Contains(_passSpent, output, StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "the command ran");
        Assert.Empty(TerminalStandIn.Waiting());
        Assert.Equal(0, _asked.Count);
        AssertNoValue(output, "the window");
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_when_Terminal_does_not_open_the_request_and_its_pass_are_withdrawn()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        using var screen = Screen();
        var marker = Path.Combine(_project, "ran");
        var launch = MappedTo(screen, $"touch '{marker}'");
        _terminal!.Refusal = "LSOpenURLsWithRole() failed with error -10810";

        var running = launch.LaunchAsync(run: true);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        Assert.Equal("Nothing was started: Terminal did not open: LSOpenURLsWithRole() failed with error -10810.", screen.Error);
        Assert.Empty(TerminalStandIn.Waiting());

        // The request as a Terminal opening late would have found it: its pass went with it.
        TerminalStandIn.PutBack(Assert.Single(Assert.Single(_terminal.Openings).Waiting));
        var (exit, output) = _terminal.Open(_terminal.Launcher).Ended();

        Assert.NotEqual(0, exit);
        Assert.Contains(_passSpent, output, StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "the command ran");
        Assert.Equal(0, _asked.Count);
    }

    /// <summary>A Terminal the app starts keeps the environment <c>open</c> was given for every window, so it is the app's less every keypaste setting, and the window's run still gets the set (T-42).</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_Terminal_is_opened_with_the_apps_environment_less_every_KEYPASTE_variable()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        var parent = new Dictionary<string, string>(EnvironmentMerge.Inherited())
        {
            ["KEYPASTE_HOME"] = _fixture.Home,
            ["KEYPASTE_TOKEN"] = "kpt_e1d_never_in_terminal",
            ["E1D_MARKER"] = "kept",
        };
        using var screen = new EnvSetsViewModel(_session, _countdown, _picker, Launching() with { Parent = () => parent });
        var launch = Mapped(screen, out var pipeName);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);
        var opened = Assert.Single(_terminal!.Openings).Environment;
        Assert.DoesNotContain(opened.Keys, name => name.StartsWith("KEYPASTE_", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("kept", opened["E1D_MARKER"]);
        Assert.Equal(parent["PATH"], opened["PATH"]);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
    }

    /// <summary>
    /// A Run asks the app's own launch endpoint, so it starts and is recorded while agents cannot reach
    /// the vault: a <c>KEYPASTE_APPROVER</c> naming no pipe, or a vault's endpoint whose socket path
    /// something else holds (D-0451).
    /// </summary>
    [Theory]
    [InlineData("names no pipe")]
    [InlineData("held")]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_Run_starts_and_is_recorded_while_the_app_cannot_serve_agents(string why)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        var approver = why == "held" ? "kp-e1d-" + Guid.NewGuid().ToString("N")[..12] : "kp/e1d";
        var held = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + approver);

        if (why == "held")
        {
            Directory.CreateDirectory(held);
        }

        try
        {
            _session.Lock(VaultLockReason.Manual);

            // The authority owns its session, as it does at launch.
#pragma warning disable CA2000
            using var authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home), approver, () => _asked);
#pragma warning restore CA2000
            Unlock(authority.Session);
            Assert.IsType<AuthorityStatus.NotServing>(authority.Status);

            using var screen = new EnvSetsViewModel(authority.Session, _countdown, _picker, ProjectLaunching.ForMacBundle(authority, _terminal!.Bundle, _terminal.Open));
            var launch = Mapped(screen, out var pipeName);

            using var pipe = Listen(pipeName);
            var connected = pipe.WaitForConnectionAsync(Cancel);

            var running = launch.LaunchAsync(run: true);
            Assert.True(launch.IsConfirming, screen.Error);
            launch.ConfirmLaunchCommand.Execute(null);
            await running.WaitAsync(Cancel);

            AssertStarted(screen, launch);

            var report = await ReportAsync(pipe, connected);
            AssertHandedOver();

            Assert.Contains($"API_KEY={_apiKey}", report);
            Assert.Contains($"DB_URL={_dbUrl}", report);

            using var line = LastAuditLine();
            Assert.Equal("granted", line.RootElement.GetProperty("decision").GetString());
            Assert.Equal("2 variable(s), confirmed in the app", line.RootElement.GetProperty("reason").GetString());
        }
        finally
        {
            if (why == "held")
            {
                Directory.Delete(held);
            }
        }
    }

    /// <summary>The card is the one question a Run asks, so it shows the longest command a mapping holds whole, on every platform.</summary>
    [Fact]
    public async Task The_card_shows_the_longest_command_a_mapping_holds_whole()
    {
        using var screen = Screen();
        var command = Longest("make");
        var launch = MappedTo(screen, command);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        Assert.Equal(command, launch.ConfirmStarts);

        launch.CancelLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertNothingStarted();
    }

    /// <summary>A Run of the longest command a mapping holds starts through Terminal, though its request's command line is longer than the owner's prompt shows: its pass holds it to the card (D-0452).</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_the_longest_command_a_mapping_holds_runs_through_Terminal()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);
        launch.Command = Longest(launch.Command);
        launch.SaveCommand.Execute(null);
        Assert.Equal(ProjectMappings.MaximumCommandLength, launch.Mapping?.Command.Length);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
    }

    /// <summary>The card asked about one environment, so choosing another on the project while it is up does not change what Start starts.</summary>
    [Fact]
    public async Task Choosing_another_environment_while_the_card_is_up_starts_the_one_the_card_named()
    {
        Reopened(vault =>
        {
            ProjectVariables.Set(vault, "dev", "staging", "API_KEY", _stagingKey);
            ProjectVariables.Set(vault, "dev", "staging", "DB_URL", _stagingKey);
        });

        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        Assert.Equal("Run this in a terminal with 2 variables from dev?", launch.ConfirmTitle);

        screen.OpenProject!.SelectedProfile = "staging";
        Assert.Equal("staging", launch.Profile);

        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        Assert.DoesNotContain(report, line => line.Contains(_stagingKey, StringComparison.Ordinal));
    }

    /// <summary>The window's <c>keypaste run</c> names the project by <c>--project=</c>, so a name starting with <c>-</c> runs through Terminal like any other and its History line names its items (D-0453).</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_a_project_whose_name_starts_with_a_dash_runs_through_Terminal_like_any_other()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        Reopened(vault =>
        {
            ProjectVariables.Set(vault, "--help", "API_KEY", _stagingKey);
            ProjectVariables.Set(vault, "--help", "DB_URL", _dbUrl);
        });

        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName, project: "--help");

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver(project: "--help");

        Assert.Contains($"API_KEY={_stagingKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);

        using var line = LastAuditLine();
        Assert.Equal("granted", line.RootElement.GetProperty("decision").GetString());
        Assert.Equal(["env/--help/.env"], line.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()).Distinct());
    }

    /// <summary>The request carries any path, so a project directory holding a line break and other control characters runs through Terminal as a launch on Linux does, the window's run starting in it with the set (D-0451).</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_a_directory_holding_a_line_break_runs_through_Terminal_like_any_other()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        var directory = Directory.CreateDirectory(Path.Combine(_project, "web\napp\t\u001b[2J\\")).FullName;

        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);
        launch.Directory = directory;
        launch.Command = "touch ran && " + launch.Command;
        launch.SaveCommand.Execute(null);
        Assert.Equal(directory, launch.Mapping?.Directory);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        Assert.True(File.Exists(Path.Combine(directory, "ran")), "the command did not run in the directory");
    }

    /// <summary>A Run is recorded in one History line, which names each of thirty wide items, more than 2 KiB of names, on every platform (D-0454, D-NEW-C2).</summary>
    [Fact]
    public async Task A_set_from_thirty_wide_items_runs_and_its_History_line_names_every_one()
    {
        var items = Wide(30, 76);

        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);
        launch.Command += " KEY_29_" + new string('W', 69);
        launch.SaveCommand.Execute(null);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"KEY_29_{new string('W', 69)}=w29", report);

        using var line = LastAuditLine();
        Assert.Equal("granted", line.RootElement.GetProperty("decision").GetString());
        Assert.Equal(
            ["env/dev/.env", .. items.Order(StringComparer.Ordinal)],
            line.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()!).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>A set whose items one History line could not name is refused unasked, before the card, on every platform, since every Run writes a line (D-NEW-C2).</summary>
    [Fact]
    public async Task A_set_whose_items_one_History_line_could_not_name_is_refused_before_anyone_is_asked()
    {
        // Each item's name takes more bytes in the line than its title has characters.
        Wide((SessionAuthority.MaximumAuditedEntriesBytes / EntryNameSanitizer.MaximumLength) + 1, EntryNameSanitizer.MaximumLength);

        using var screen = Screen();
        var launch = MappedTo(screen, "make");

        var running = launch.LaunchAsync(run: true);
        Assert.False(launch.IsConfirming);
        await running.WaitAsync(Cancel);

        Assert.Equal(
            "Nothing was started: dev's variables come from more items than one History line can name, and a Run from the app writes one. Copy the run command to start it in a terminal instead.",
            screen.Error);
        AssertNothingStarted();
        Assert.Equal(0, _asked.Count);
    }

    /// <summary>Without the private temporary folder a request goes through, a Run is refused before the card, saying what to do instead.</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_a_Run_with_no_private_temporary_folder_is_refused_before_anyone_is_asked()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        using var screen = new EnvSetsViewModel(_session, _countdown, _picker, Launching() with { Requests = null });
        var launch = MappedTo(screen, "make");

        var running = launch.LaunchAsync(run: true);
        Assert.False(launch.IsConfirming);
        await running.WaitAsync(Cancel);

        Assert.Equal("Nothing was started: macOS named no private temporary folder to hand the run to Terminal through. Copy the run command instead.", screen.Error);
        AssertNothingStarted();
        Assert.Equal(0, _asked.Count);
    }

    /// <summary>
    /// Every Run writes a History line, so while the app's audit log will not open a Run is refused before the card on
    /// every platform, naming the log's error and what to do instead; mending the log needs no new unlock (T-6, D-NEW-C2).
    /// </summary>
    [Fact]
    public async Task A_Run_while_the_audit_log_will_not_open_is_refused_before_anyone_is_asked()
    {
        var log = KeypasteHome.AuditPath(_fixture.Home);
        Directory.CreateDirectory(log);

        using var screen = Screen();
        var launch = MappedTo(screen, "make");

        var running = launch.LaunchAsync(run: true);
        Assert.False(launch.IsConfirming);
        await running.WaitAsync(Cancel);

        Assert.StartsWith(
            $"Nothing was started: a Run from the app writes a History line, and the audit log at '{log}' could not be opened: ",
            screen.Error,
            StringComparison.Ordinal);
        Assert.EndsWith(". Copy the run command to start it in a terminal instead.", screen.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("..", screen.Error, StringComparison.Ordinal);
        AssertNothingStarted();
        Assert.Equal(0, _asked.Count);

        Directory.Delete(log);
        Assert.Null(_authority!.AuditFailure);
    }

    /// <summary>The owner hands a set to Terminal's run in the one reply bounded past an ordinary frame, so a set of 200 KiB, a certificate bundle, runs through Terminal (D-0454).</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_a_set_of_200_KiB_runs_through_Terminal()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        var certificate = new string('c', 200 * 1024);
        Reopened(vault => ProjectVariables.Set(vault, "dev", "CERTIFICATE", certificate));

        using var screen = Screen();
        var launch = Mapped(screen, out var pipeName);
        launch.Command += " CERTIFICATE";
        launch.SaveCommand.Execute(null);

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);
        AssertHandedOver();

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"CERTIFICATE={certificate}", report);
    }

    /// <summary>A set too large even for that reply, at the encoder's costliest escaping, is still refused after Start, before a pass is issued or Terminal opens.</summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task On_macOS_a_set_too_large_for_one_reply_is_refused_before_Terminal_opens()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOSOnly);

        Reopened(vault => ProjectVariables.Set(vault, "dev", "CERTIFICATE", new string('+', (ApproverProtocol.MaximumEnvReplyFrameBytes / 6) + 1)));

        using var screen = Screen();
        var launch = MappedTo(screen, "make");

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        Assert.Equal("Nothing was started: dev's variables are too large for keypaste to hand to Terminal.", screen.Error);
        AssertNothingStarted();
        Assert.Equal(0, _asked.Count);
    }

    [Fact]
    public async Task A_set_E1a_refuses_is_named_before_anyone_is_asked_and_starts_nothing()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.SetExpiryUnchecked(ProjectVariables.Home("dev"), _session.Clock.GetUtcNow().AddDays(-1));
            vault.Save();
        }

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        using var screen = Screen();
        var launch = Mapped(screen, out _);

        await launch.LaunchAsync(run: true).WaitAsync(Cancel);

        AssertNothingStarted();
        Assert.False(launch.IsConfirming);
        Assert.NotNull(screen.Error);
        Assert.Contains("dev/dev cannot be used, so nothing was started", screen.Error, StringComparison.Ordinal);
        Assert.Contains("API_KEY expired", screen.Error, StringComparison.Ordinal);
        Assert.Contains("DB_URL expired", screen.Error, StringComparison.Ordinal);
        AssertNoValue(screen.Error, "the refusal");
    }

    [Fact]
    public async Task Importing_a_dotenv_stores_exactly_its_keys_and_the_preview_never_shows_a_value()
    {
        var file = Path.Combine(_project, ".env");
        File.WriteAllText(file, $"API_KEY={_apiKey}\nFRESH={_imported}\nDB_URL=replaced_{_dbUrl}\n");
        _picker.DotEnvPath = file;

        using var screen = Screen();
        screen.OpenCommand.Execute("dev");
        var import = screen.OpenProject!.Import;

        await import.ChooseCommand.ExecuteAsync();

        Assert.True(import.IsPreviewing);
        Assert.Equal(
            ["API_KEY  unchanged", "DB_URL  replaces the value on env/dev/.env, which keeps it in history", "FRESH  new on env/dev/.env"],
            import.Rows);
        Assert.DoesNotContain(import.Rows, row => row.Contains(_imported, StringComparison.Ordinal) || row.Contains(_apiKey, StringComparison.Ordinal));

        import.ConfirmCommand.Execute(null);

        Assert.False(import.IsPreviewing);
        Assert.Null(screen.Error);
        Assert.Contains("Imported 2 variables into dev/dev.", screen.Notice, StringComparison.Ordinal);
        Assert.DoesNotContain(_imported, screen.Notice, StringComparison.Ordinal);
        Assert.Equal(3, screen.OpenProject.Count);
        Assert.True(File.Exists(file));

        using var reread = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.Equal(
            [new EnvVariable("API_KEY", _apiKey), new EnvVariable("DB_URL", "replaced_" + _dbUrl), new EnvVariable("FRESH", _imported)],
            EnvResolution.List(reread, "dev", "dev").Variables);
    }

    [Fact]
    public async Task A_previewed_import_writes_nothing_until_it_is_confirmed()
    {
        var file = Path.Combine(_project, ".env");
        File.WriteAllText(file, $"FRESH={_imported}\n");
        _picker.DotEnvPath = file;

        using var screen = Screen();
        screen.OpenCommand.Execute("dev");
        var import = screen.OpenProject!.Import;

        await import.ChooseCommand.ExecuteAsync();
        Assert.True(import.IsPreviewing);
        import.CancelCommand.Execute(null);

        Assert.False(import.IsPreviewing);
        using var reread = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.Equal(["API_KEY", "DB_URL"], EnvResolution.List(reread, "dev", "dev").Variables.Select(variable => variable.Key));
    }

    [Fact]
    public async Task A_dotenv_with_a_problem_imports_nothing_and_names_the_line_not_the_text()
    {
        var file = Path.Combine(_project, ".env");
        File.WriteAllText(file, $"FRESH={_imported}\nBROKEN=\"{_imported}\n");
        _picker.DotEnvPath = file;

        using var screen = Screen();
        screen.OpenCommand.Execute("dev");
        await screen.OpenProject!.Import.ChooseCommand.ExecuteAsync();

        Assert.False(screen.OpenProject.Import.IsPreviewing);
        Assert.Contains("nothing was imported", screen.Error, StringComparison.Ordinal);
        Assert.Contains("line 2", screen.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(_imported, screen.Error, StringComparison.Ordinal);

        using var reread = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.DoesNotContain(EnvResolution.List(reread, "dev", "dev").Variables, variable => variable.Key == "FRESH");
    }

    [Fact]
    public async Task A_reference_file_is_refused_before_the_preview_and_the_vault_keeps_its_values()
    {
        var file = Path.Combine(_project, EnvReferenceFile.FileName);
        File.WriteAllText(file, EnvReferenceFile.Format("dev", "dev", ["API_KEY", "DB_URL"]));
        _picker.DotEnvPath = file;

        using var screen = Screen();
        screen.OpenCommand.Execute("dev");
        await screen.OpenProject!.Import.ChooseCommand.ExecuteAsync();

        Assert.False(screen.OpenProject.Import.IsPreviewing);
        Assert.Contains("Nothing was imported", screen.Error, StringComparison.Ordinal);
        Assert.Contains(EnvReferenceFile.FileName, screen.Error, StringComparison.Ordinal);

        using var reread = Vault.Open(_fixture.Path_, TempVault.Password);
        Assert.Equal(
            [new EnvVariable("API_KEY", _apiKey), new EnvVariable("DB_URL", _dbUrl)],
            EnvResolution.List(reread, "dev", "dev").Variables);
    }

    /// <summary>Open terminal on macOS: the window's run ends in the person's shell, here one that reports what it was given.</summary>
    [SupportedOSPlatform("macos")]
    private async Task OpenTerminalThroughTerminalAsync(EnvSetsViewModel screen, ProjectLaunchViewModel launch, string pipeName)
    {
        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);
        var shell = Path.Combine(_stubs, "reporting-shell");
        File.WriteAllText(shell, $"#!/bin/sh\nexec '{Helper("Keypaste.EnvReporter")}' {pipeName} API_KEY DB_URL\n");
        File.SetUnixFileMode(shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _terminal!.Shell = shell;

        var running = launch.LaunchAsync(run: false);
        Assert.True(launch.IsConfirming);
        Assert.Equal(TerminalOpen.ShownName, launch.ConfirmStarts);
        Assert.Equal(_project, launch.ConfirmDirectory);

        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        AssertStarted(screen, launch);

        var report = await ReportAsync(pipe, connected);

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        AssertStartedIn(report);
        AssertHandedOver();
    }

    /// <summary>Requires that the terminal started: on macOS, that Terminal was asked to open the bundle's launcher with one private request waiting for it.</summary>
    private void AssertStarted(EnvSetsViewModel screen, ProjectLaunchViewModel launch)
    {
        Assert.Null(screen.Error);

        if (OperatingSystem.IsMacOS())
        {
            var opening = Assert.Single(_terminal!.Openings);
            Assert.Equal("/usr/bin/open", opening.FileName);
            Assert.Equal(["-b", "com.apple.Terminal", _terminal.Launcher], opening.Arguments);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, Assert.Single(opening.Waiting).Mode);
            Assert.Null(launch.LastStart);
            return;
        }

        var started = Assert.IsType<ChildResult>(launch.LastStart);
        Assert.Equal(ChildOutcome.Started, started.Outcome);
    }

    /// <summary>Requires that nothing was started, and on macOS that Terminal was not asked and no request was left for it.</summary>
    private void AssertNothingStarted()
    {
        Assert.Equal(0, _launcher.Calls);

        if (OperatingSystem.IsMacOS())
        {
            Assert.Empty(_terminal!.Openings);
            Assert.Empty(TerminalStandIn.Waiting());
        }
    }

    /// <summary>
    /// On macOS, requires that the window's launcher took its request, that the window said whose
    /// confirmation it ran on and showed no value, and that the app asked nobody, the card having been
    /// the only question.
    /// </summary>
    private void AssertHandedOver(string project = "dev")
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Empty(TerminalStandIn.Waiting());
            Assert.Equal(0, _asked.Count);

            var (exit, output) = Assert.Single(_terminal!.Windows).Ended();
            Assert.True(exit == 0, output);
            Assert.Contains($"keypaste run: starting with {project} (dev) as you confirmed in the keypaste app", output, StringComparison.Ordinal);
            AssertNoValue(output, "the window");
        }
    }

    /// <summary>
    /// The process, held by a handle so that its id cannot pass to another while the test waits on
    /// it; null if it has gone.
    /// </summary>
    private static Process? Held(int processId)
    {
        Process? process = null;

        try
        {
            process = Process.GetProcessById(processId);
            _ = process.SafeHandle;
            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            process?.Dispose();
            return null;
        }
    }

    /// <summary>Gives the process <paramref name="grace"/> to exit, ends its tree if it has not, and waits until it has.</summary>
    private static void End(Process process, TimeSpan grace)
    {
        using (process)
        {
            if (!process.WaitForExit(grace))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
                {
                    // It, or something it started, was already exiting.
                }
            }

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), $"process {process.Id} did not exit");
        }
    }

    /// <summary>
    /// Deletes the directory, giving a process the test could not hold, such as a terminal that had
    /// finished before the launcher returned, a few seconds to finish exiting from it.
    /// </summary>
    private static void DeleteOnceReleased(string directory)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (waited.Elapsed > TimeSpan.FromSeconds(_releaseSeconds))
                {
                    throw new IOException($"{directory} is still held by: {Holders(directory)}", ex);
                }

                Thread.Sleep(50);
            }
        }
    }

    /// <summary>The processes Windows says have the directory open, by name and id.</summary>
    private static string Holders(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "unknown on this platform";
        }

        using var handle = Native.CreateFileW(directory, Native.ReadAttributes, Native.ShareAll, IntPtr.Zero, Native.OpenExisting, Native.BackupSemantics, IntPtr.Zero);
        var ids = new byte[8 + (8 * 64)];
        if (handle.IsInvalid || Native.NtQueryInformationFile(handle, new byte[16], ids, ids.Length, Native.ProcessIdsUsingFile) != 0)
        {
            return "unknown";
        }

        return string.Join(", ", Enumerable.Range(0, BitConverter.ToInt32(ids, 0))
            .Select(i => (int)BitConverter.ToInt64(ids, 8 + (8 * i)))
            .Where(id => id != Environment.ProcessId)
            .Select(Named));
    }

    private static string Named(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return $"{process.ProcessName} {processId}";
        }
        catch (ArgumentException)
        {
            return $"exiting process {processId}";
        }
    }

    private static NamedPipeServerStream Listen(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    /// <summary>Reads the child's report, then hangs up and waits for the child to exit.</summary>
    /// <remarks>
    /// On Windows the child holds the project directory until it has exited; disposal's retry covers
    /// only a process the test could not hold.
    /// </remarks>
    private static async Task<List<string>> ReportAsync(NamedPipeServerStream pipe, Task connected)
    {
        await connected.WaitAsync(TimeSpan.FromSeconds(60), Cancel);

        List<string> report = [];
        using (var reader = new StreamReader(pipe, leaveOpen: true))
        {
            while (await reader.ReadLineAsync(Cancel) is { } line)
            {
                report.Add(line);
                if (line == "end")
                {
                    break;
                }
            }
        }

        var id = int.Parse(report.Single(line => line.StartsWith("pid=", StringComparison.Ordinal))[4..], CultureInfo.InvariantCulture);
        var child = Held(id) ?? throw new InvalidOperationException($"the child {id} exited before the test hung up");
        pipe.Dispose();
        End(child, grace: TimeSpan.FromSeconds(30));
        return report;
    }

    private void AssertStartedIn(List<string> report) =>
        Assert.True(
            PathIdentity.SameFile(_project, report.Single(line => line.StartsWith("cwd=", StringComparison.Ordinal))[4..]),
            "the child did not start in the chosen directory");

    private static void AssertNoValue(string? text, string where)
    {
        foreach (var value in new[] { _apiKey, _dbUrl, _imported })
        {
            Assert.False(text?.Contains(value, StringComparison.Ordinal) == true, $"{where} holds a value");
        }
    }

    private static string Helper(string name)
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.app.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("Could not locate keypaste.app.slnx above " + AppContext.BaseDirectory);
        }

        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase) ? "debug" : "release";
        return Path.Combine(directory, "artifacts", "bin", name, configuration, OperatingSystem.IsWindows() ? name + ".exe" : name);
    }

    /// <summary>Checks the project directory, keypaste's home and every file the run touched under the temp path.</summary>
    private void AssertNoFileHoldsAValue()
    {
        var roots = new[] { _project, _fixture.Home, Path.GetTempPath() };

        foreach (var root in roots)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try
                {
                    // An empty file holds no value, and a FIFO, such as a running .NET process's debugger
                    // pipe on Linux, reports no length and would block the read.
                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc < _startedUtc || info.Length is 0 or > 16 * 1024 * 1024 || path.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AssertNoValue(File.ReadAllText(path), path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held open by another process, which is not this run's.
                }
            }
        }
    }

    private void Unlock(AppVaultSession? session = null)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, (session ?? _session).TryUnlock(_fixture.Path_, master.Value));
    }

    /// <summary>Changes the vault's file as another program would, then unlocks it again so the session reads the change.</summary>
    private void Reopened(Action<Vault> change)
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            change(vault);
            vault.Save();
        }

        _session.Lock(VaultLockReason.Manual);
        Unlock();
    }

    /// <summary>Gives dev one variable from each of <paramref name="count"/> items, each titled in <paramref name="length"/> characters, and returns the items as a History line names them.</summary>
    private List<string> Wide(int count, int length)
    {
        List<string> items = [];

        Reopened(vault =>
        {
            foreach (var i in Enumerable.Range(0, count))
            {
                var key = $"KEY_{i}_";
                var wide = new EntryName("services", key + new string('W', length - key.Length));
                vault.AddEntry(new VaultEntry { GroupPath = wide.GroupPath, Title = wide.Title });
                Assert.True(vault.SetFields(wide, [new FieldWrite(wide.Title, $"w{i}")]));
                Assert.True(vault.AddTag(wide, "env:dev"));
                items.Add(ApprovalPrompt.Shown(wide));
            }
        });

        return items;
    }

    private EnvSetsViewModel Screen() => new(_session, _countdown, _picker, Launching());

    private ProjectLaunching Launching() =>
        OperatingSystem.IsMacOS()
            ? ProjectLaunching.ForMacBundle(_authority, _terminal!.Bundle, _terminal.Open)
            : new ProjectLaunching(Terminal(), _launcher, EnvironmentMerge.Inherited) { Authority = _authority };

    /// <summary>The newest History line, read beside the app holding the log open for writing, which a read asking for <see cref="FileShare.Read"/> cannot share on Windows.</summary>
    private JsonDocument LastAuditLine()
    {
        using var stream = new FileStream(KeypasteHome.AuditPath(_fixture.Home), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return JsonDocument.Parse(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    /// <summary>Requires that the newest History line is the Run's release, as a macOS Run's window and a Windows or Linux Run each write it (D-0452, D-NEW-C2).</summary>
    private void AssertRecorded(int variables, params string[] entries)
    {
        using var line = LastAuditLine();
        var root = line.RootElement;

        Assert.Equal("run", root.GetProperty("tool").GetString());
        Assert.Equal("granted", root.GetProperty("decision").GetString());
        Assert.Equal("prompt", root.GetProperty("method").GetString());
        Assert.Equal($"{variables} variable(s), confirmed in the app", root.GetProperty("reason").GetString());
        Assert.Equal(entries, root.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()));
    }

    private TerminalLaunch Terminal()
    {
        if (!OperatingSystem.IsLinux())
        {
            return TerminalLaunch.ForThisMachine();
        }

        // Runs what follows -e as an emulator does, with nothing on its input so the shell it ends in exits.
        var stub = Path.Combine(_stubs, "x-terminal-emulator");
        File.WriteAllText(stub, "#!/bin/sh\n[ \"$1\" = -e ] && shift\nexec \"$@\" </dev/null\n");
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new TerminalLaunch(TerminalPlatform.Linux, "unused", name => name == "x-terminal-emulator" ? stub : null);
    }

    private ProjectLaunchViewModel Mapped(EnvSetsViewModel screen, out string pipeName, bool background = false, string project = "dev")
    {
        // Short, since a pipe's socket path on macOS is its temporary folder's and at most 103 bytes.
        pipeName = "kp-e1b-" + Guid.NewGuid().ToString("N")[..16];
        var reporter = Helper("Keypaste.EnvReporter");
        Assert.True(File.Exists(reporter), $"build Keypaste.EnvReporter first: {reporter}");

        return MappedTo(screen, (OperatingSystem.IsWindows(), background) switch
        {
            (true, false) => $"\"{reporter}\" {pipeName} API_KEY DB_URL",
            (true, true) => $"start \"\" /b \"{reporter}\" {pipeName} --linger {_releaseSeconds * 2} API_KEY DB_URL",
            (false, false) => $"'{reporter}' {pipeName} API_KEY DB_URL",
            (false, true) => $"'{reporter}' {pipeName} --linger {_releaseSeconds * 2} API_KEY DB_URL &",
        }, project);
    }

    private ProjectLaunchViewModel MappedTo(EnvSetsViewModel screen, string command, string project = "dev")
    {
        screen.OpenCommand.Execute(project);
        var launch = screen.OpenProject!.Launch;
        launch.Directory = _project;
        launch.Command = command;
        launch.SaveCommand.Execute(null);

        Assert.True(launch.IsMapped, screen.Error);
        return launch;
    }

    /// <summary>The command, then a shell comment of quotes and spaces filling it to the longest a mapping holds.</summary>
    private static string Longest(string command) =>
        (command + " #" + string.Concat(Enumerable.Repeat(" \"q\" 'q'", ProjectMappings.MaximumCommandLength)))[..(ProjectMappings.MaximumCommandLength - 1)] + "q";

    /// <summary>
    /// The real launcher, counted, so a refusal can be shown to have started nothing, and holding
    /// each terminal it starts so the test can wait for it.
    /// </summary>
    private sealed class CountingLauncher : IProcessLauncher
    {
        private readonly DetachedProcessLauncher _real = new();

        internal int Calls => Started.Count;

        internal List<ChildStart> Started { get; } = [];

        internal List<Process> Terminals { get; } = [];

        public ChildResult Run(ChildStart start)
        {
            Started.Add(start);
            var result = _real.Run(start);

            if (result.Outcome == ChildOutcome.Started && Held(result.ProcessId) is { } terminal)
            {
                Terminals.Add(terminal);
            }

            return result;
        }
    }

    /// <summary>The app's own prompt, counting every question put to it.</summary>
    private sealed class CountingChannel : IApprovalChannel
    {
        private int _count;

        internal int Count => Volatile.Read(ref _count);

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) => Asked();

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken) => Asked();

        public ValueTask<ApprovalAnswer> AskAsync(RunPrompt prompt, CancellationToken cancellationToken) => Asked();

        private ValueTask<ApprovalAnswer> Asked()
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(ApprovalAnswer.NoChannel);
        }
    }

    /// <summary>Asks Windows which processes have a file open.</summary>
    private static class Native
    {
        internal const uint ReadAttributes = 0x80;
        internal const uint ShareAll = 0x7;
        internal const uint OpenExisting = 3;
        internal const uint BackupSemantics = 0x02000000;
        internal const int ProcessIdsUsingFile = 47;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("ntdll.dll")]
        internal static extern int NtQueryInformationFile(SafeFileHandle file, byte[] status, byte[] information, int length, int informationClass);
    }

    /// <summary>Holds every continuation until the test runs the queue, so a lock can be placed between two of them.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                _queue.Enqueue((d, state));
            }
        }

        internal void RunUntil(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);

            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                (SendOrPostCallback Callback, object? State) next;

                lock (_queue)
                {
                    if (!_queue.TryDequeue(out next))
                    {
                        next = default;
                    }
                }

                if (next.Callback is null)
                {
                    Thread.Sleep(10);
                    continue;
                }

                next.Callback(next.State);
            }
        }
    }
}
