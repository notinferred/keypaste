using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Launch;
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
/// The launch goes through <see cref="AppVaultSession.Environments"/> and the platform's terminal:
/// <c>cmd.exe</c> on Windows, and on Linux a stand-in emulator script that runs its <c>-e</c> command
/// as a real emulator would, since a runner has no display. The child is a real process that reports
/// what its own environment holds over a pipe, so nothing is taken from the view model's word. Where
/// the app opens no terminal, macOS until E.1d, each test holds the refusal the app gives instead.
/// </remarks>
public sealed class EnvLaunchThroughAppTests : IDisposable
{
    private const string _apiKey = "sk_live_e1b_api_4d9a";
    private const string _dbUrl = "postgres://e1b:db_71c2@localhost/app";
    private const string _imported = "imported_e1b_value_b83f";
    private const int _releaseSeconds = 3;

    private readonly DateTime _startedUtc = DateTime.UtcNow.AddSeconds(-1);
    private readonly TempVault _fixture = new();
    private readonly string _project = Directory.CreateTempSubdirectory("keypaste-e1b-project-").FullName;
    private readonly string _stubs = Directory.CreateTempSubdirectory("keypaste-e1b-terminal-").FullName;
    private readonly FakeVaultFilePicker _picker = new();
    private readonly CountingLauncher _launcher = new();
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));

    public EnvLaunchThroughAppTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            ProjectVariables.Set(vault, "dev", "API_KEY", _apiKey);
            ProjectVariables.Set(vault, "dev", "DB_URL", _dbUrl);
            vault.Save();
        }

        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home);
        Unlock();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        // On Windows a process that has not finished exiting still holds the project directory it runs in.
        foreach (var terminal in _launcher.Terminals)
        {
            End(terminal, grace: TimeSpan.Zero);
        }

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
        if (RefusedWithoutTerminal(screen, launch, run: true))
        {
            return;
        }

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);

        Assert.True(launch.IsConfirming);
        Assert.Contains(pipeName, launch.ConfirmStarts, StringComparison.Ordinal);
        Assert.Equal(_project, launch.ConfirmDirectory);
        Assert.Equal("API_KEY, DB_URL", launch.ConfirmKeys);
        Assert.Equal($"From {ApprovalPrompt.Shown(ProjectVariables.Home("dev"))}", launch.ConfirmSources);
        Assert.Equal(0, _launcher.Calls);

        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        var started = Assert.IsType<ChildResult>(launch.LastStart);
        Assert.Equal(ChildOutcome.Started, started.Outcome);

        var report = await ReportAsync(pipe, connected);

        Assert.Equal("end", report[^1]);
        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        AssertStartedIn(report);

        var commandLine = report.Single(line => line.StartsWith("cmdline=", StringComparison.Ordinal));
        AssertNoValue(commandLine, "the child's command line");

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
        if (RefusedWithoutTerminal(screen, launch, run: true))
        {
            return;
        }

        using var pipe = Listen(pipeName);
        var connected = pipe.WaitForConnectionAsync(Cancel);

        var running = launch.LaunchAsync(run: true);
        launch.ConfirmLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        var report = await ReportAsync(pipe, connected);

        Assert.Contains($"API_KEY={_apiKey}", report);
        Assert.Contains($"DB_URL={_dbUrl}", report);
        AssertStartedIn(report);
    }

    [Fact]
    public async Task Open_terminal_starts_the_terminal_in_the_directory_after_confirming()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out _);
        if (RefusedWithoutTerminal(screen, launch, run: false))
        {
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
        if (RefusedWithoutTerminal(screen, launch, run: true))
        {
            return;
        }

        var running = launch.LaunchAsync(run: true);
        launch.CancelLaunchCommand.Execute(null);
        await running.WaitAsync(Cancel);

        Assert.Equal(0, _launcher.Calls);
        Assert.Null(launch.LastStart);
        Assert.False(launch.IsConfirming);
        Assert.Equal("Nothing was started.", screen.Error);
    }

    [Fact]
    public async Task A_lock_while_the_confirmation_waits_withdraws_it_and_starts_nothing()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out _);
        if (RefusedWithoutTerminal(screen, launch, run: true))
        {
            return;
        }

        var running = launch.LaunchAsync(run: true);
        Assert.True(launch.IsConfirming);

        _session.Lock(VaultLockReason.Manual);
        await running.WaitAsync(TimeSpan.FromSeconds(30), Cancel);

        Assert.Equal(0, _launcher.Calls);
        Assert.False(launch.IsConfirming);
        Assert.Equal("The vault was locked, so nothing was started.", screen.Error);
    }

    [Fact]
    public void A_lock_between_confirming_and_starting_starts_nothing()
    {
        using var screen = Screen();
        var launch = Mapped(screen, out _);
        if (RefusedWithoutTerminal(screen, launch, run: true))
        {
            return;
        }

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

        Assert.Equal(0, _launcher.Calls);
        Assert.Null(launch.LastStart);
        Assert.Equal("The vault was locked, so nothing was started.", screen.Error);
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
        if (RefusedWithoutTerminal(screen, launch, run: true))
        {
            return;
        }

        await launch.LaunchAsync(run: true).WaitAsync(Cancel);

        Assert.Equal(0, _launcher.Calls);
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

    /// <summary>
    /// Where the app opens no terminal, a launch is refused before anyone is asked and starts nothing (D-0340).
    /// </summary>
    /// <returns>Whether the platform refused, which is then all the test can hold.</returns>
    private bool RefusedWithoutTerminal(EnvSetsViewModel screen, ProjectLaunchViewModel launch, bool run)
    {
        if (launch.IsSupported)
        {
            return false;
        }

        Assert.True(launch.LaunchAsync(run).IsCompletedSuccessfully, "the refusal waited on something");
        Assert.Equal(0, _launcher.Calls);
        Assert.False(launch.IsConfirming);
        Assert.Null(launch.LastStart);
        Assert.Equal("Nothing was started: the app opens terminals on Windows and Linux only; copy the run command instead.", screen.Error);
        return true;
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

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    private EnvSetsViewModel Screen() => new(_session, _countdown, _picker, new ProjectLaunching(Terminal(), _launcher, EnvironmentMerge.Inherited));

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

    private ProjectLaunchViewModel Mapped(EnvSetsViewModel screen, out string pipeName, bool background = false)
    {
        pipeName = "kp-e1b-" + Guid.NewGuid().ToString("N");
        var reporter = Helper("Keypaste.EnvReporter");
        Assert.True(File.Exists(reporter), $"build Keypaste.EnvReporter first: {reporter}");

        screen.OpenCommand.Execute("dev");
        var launch = screen.OpenProject!.Launch;
        launch.Directory = _project;
        launch.Command = (OperatingSystem.IsWindows(), background) switch
        {
            (true, false) => $"\"{reporter}\" {pipeName} API_KEY DB_URL",
            (true, true) => $"start \"\" /b \"{reporter}\" {pipeName} --linger {_releaseSeconds * 2} API_KEY DB_URL",
            (false, false) => $"'{reporter}' {pipeName} API_KEY DB_URL",
            (false, true) => $"'{reporter}' {pipeName} --linger {_releaseSeconds * 2} API_KEY DB_URL &",
        };
        launch.SaveCommand.Execute(null);

        Assert.True(launch.IsMapped, screen.Error);
        return launch;
    }

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
