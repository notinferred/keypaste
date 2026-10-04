using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Launch;
using Keypaste.Core.Projects;
using Xunit;
using static Keypaste.App.Tests.Session.JourneyDriver;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// V-C.4: through the app as launch composes it (D-0342), the Projects screen shows both projects,
/// where each value lives, the entry two environments share and the tag that puts its entry in no
/// project; it adds an entry to an environment, takes another out, replaces the shared value, adds a
/// key with no entry chosen and imports a <c>.env</c>, and a declined or refused act writes nothing;
/// Run gives a child exactly the environment's fields, and the project mapped in <c>projects.json</c>
/// runs too.
/// </summary>
/// <remarks>
/// Driven as <see cref="OneHomeJourneyTests"/> is, pressing controls where the window draws them,
/// with every frame held to N.1a2's amber rule. The vault is written through Core with the layout
/// <c>scripts/verify-keepassxc-projects.sh</c> has KeePassXC make, where KeePassXC then reads every
/// write. Run uses the machine's terminal as the app finds it: <c>cmd.exe</c> on Windows, and on Linux
/// a stand-in <c>x-terminal-emulator</c> put first on <c>PATH</c> that runs its <c>-e</c> command, since a
/// runner has no display; where the app opens no terminal, macOS until E.1d, the journey holds the
/// refusal instead.
/// </remarks>
public sealed class ProjectsJourneyTests
{
    private const string _webKey = "web-journey-c4";
    private const string _stripeKey = "stripe-journey-c4";
    private const string _replaced = "stripe-replaced-c4";
    private const string _imported = "imported-journey-c4";

    private static readonly string[] _reported = ["STRIPE_KEY", "MAIL_KEY", "QUEUE_KEY", "NEW_KEY", "IMPORTED_KEY", "WEB_KEY"];

    [Fact]
    public Task The_projects_screen_shows_where_values_live_and_changes_entries_only_when_asked() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var vault = Path.Combine(fixture.Home, "projects.kdbx");
            var stripe = new EntryName("services", "Stripe");
            var mail = new EntryName("services", "Mail");
            var queue = new EntryName("services", "Queue");
            var home = EnvStore.HomeEntry("billing", EnvProfileNames.Default);

            using (var created = Vault.Create(vault, TempVault.Password))
            {
                Seed(created, "apps", "Web", "WEB_KEY", _webKey, "env:web");
                Seed(created, "services", "Stripe", "STRIPE_KEY", _stripeKey, "env:billing", "env:billing:staging");
                Seed(created, "services", "Mail", "MAIL_KEY", "mail-journey-c4", "env:billing");
                Seed(created, "services", "Odd", "ODD_KEY", "odd-journey-c4", "env:billing:Prod");
                Seed(created, "services", "Queue", "QUEUE_KEY", "queue-journey-c4");
                created.Save();
            }

            var webDirectory = Directory.CreateDirectory(Path.Combine(fixture.Home, "web")).FullName;
            var billingDirectory = Directory.CreateDirectory(Path.Combine(fixture.Home, "billing")).FullName;
            var webPipe = "kp-c4-web-" + Guid.NewGuid().ToString("N");
            var billingPipe = "kp-c4-billing-" + Guid.NewGuid().ToString("N");
            Map(vault, "web", webDirectory, Reporter(webPipe));

            var dotEnv = Path.Combine(fixture.Home, "billing.env");
            File.WriteAllText(dotEnv, $"IMPORTED_KEY={_imported}\n");
            var references = Path.Combine(billingDirectory, EnvReferenceFile.FileName);
            var picker = new FakeVaultFilePicker { DotEnvPath = dotEnv, ReferencePath = references };

            var path = Environment.GetEnvironmentVariable("PATH");
            var stubs = Directory.CreateDirectory(Path.Combine(fixture.Home, "terminal")).FullName;
            List<int> started = [];

            try
            {
                if (OperatingSystem.IsLinux())
                {
                    StandInTerminal(stubs);
                    Environment.SetEnvironmentVariable("PATH", stubs + Path.PathSeparator + path);
                }

                using var lifetime = new ClassicDesktopStyleApplicationLifetime();
                Attach(lifetime);
                using var app = new App { Pickers = _ => picker };
                app.Launch(lifetime);
                lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

                var main = lifetime.MainWindow!;
                main.Width = 1280;
                main.Height = 800;
                main.Show();
                var root = main.FindControl<ContentControl>("Root")!;

                var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
                Assert.True(unlock.Offer(vault, null));
                WindowInput.Drain();
                WindowInput.Click(main, Named<MaskedInput>(main, "Password"));
                WindowInput.Type(main, TempVault.Password);
                Key(main, PhysicalKey.Enter, RawInputModifiers.None);
                await Until(() => root.Content is ShellView);
                var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);
                var unlocked = app.Authority!.Session.Unlocked!;
                DrawnFrame.Capture(main);

                // Both projects, each value's entry, the entry two environments share and the ignored tag.
                Press(main, Row(main, "Nav", row => row is ProjectRow { Name: "billing" }));
                var screen = Assert.IsType<EnvSetsViewModel>(shell.Content);
                var names = AutomationNames(main);
                Assert.Contains("web", names);
                Assert.Contains("billing", names);
                Assert.Contains("services/Stripe · also staging", names);
                Assert.Contains("services/Mail", names);
                Assert.Contains("also dev", names);
                Assert.Contains("Add environment", names);
                Assert.Contains("Run with this environment", names);
                Assert.Contains(names, name => name.StartsWith("services/Odd has the tag env:billing:Prod, which puts it in no project", StringComparison.Ordinal));
                AmberElements.AssertOne(main, null, "a project's keys and entries");

                // An entry added to staging: asked first, nothing written when declined.
                Press(main, ByName<Button>(main, "Add an entry to staging"));
                AmberElements.AssertOne(main, null, "the add-entry form");
                Fill(main, "Find an entry", "Queue");
                Press(main, Row(main, "EntryCandidates", candidate => candidate is EnvEntryCandidate { Display: "services/Queue" }));
                Assert.Contains("env:billing:staging puts services/Queue in billing/staging.", AutomationNames(main));
                Assert.Contains("Joining billing/staging: QUEUE_KEY.", AutomationNames(main));
                AssertOneRevealed(main, Named<Button>(main, "ConfirmTagChange"), "adding an entry, asked");
                Unchanged(vault, () => Press(main, Named<Button>(main, "CancelTagChange")));
                Press(main, Row(main, "EntryCandidates", candidate => candidate is EnvEntryCandidate { Display: "services/Queue" }));
                Press(main, Named<Button>(main, "ConfirmTagChange"));
                Assert.Null(screen.Error);
                Assert.Contains("env:billing:staging", Saved(vault, queue));

                // Mail taken out of dev, kept with its field; declined first.
                Press(main, ByName<Button>(main, "Remove services/Mail from dev"));
                Assert.Contains("Leaving billing/dev: MAIL_KEY.", AutomationNames(main));
                AmberElements.AssertOne(main, null, "taking an entry out, asked");
                Unchanged(vault, () => Press(main, Named<Button>(main, "CancelTagChange")));
                Press(main, ByName<Button>(main, "Remove services/Mail from dev"));
                Press(main, Named<Button>(main, "ConfirmTagChange"));
                Assert.Null(screen.Error);
                Assert.Empty(Saved(vault, mail));
                Assert.NotNull(unlocked.ReadField(mail, "MAIL_KEY"));

                // The shared value replaced, its form naming both environments; cancelled first.
                var project = screen.OpenProject!;
                Press(main, RowAction(main, "STRIPE_KEY", "Replace"));
                Assert.Contains(
                    "New value for STRIPE_KEY on services/Stripe, which billing/dev and billing/staging read. The old one stays in the entry's history.",
                    AutomationNames(main));
                AssertOneRevealed(main, ByCommand(main, project.ConfirmReplaceCommand), "replacing the shared value");
                Unchanged(vault, () => Press(main, ByCommand(main, project.CancelReplaceCommand)));
                Press(main, RowAction(main, "STRIPE_KEY", "Replace"));
                WindowInput.Click(main, Named<MaskedInput>(main, "ReplacementEnvValue"));
                WindowInput.Type(main, _replaced);
                Press(main, ByCommand(main, project.ConfirmReplaceCommand));
                Assert.Null(screen.Error);
                Assert.Equal(_replaced, unlocked.ReadField(stripe, "STRIPE_KEY"));

                // A key with no entry chosen lands on the environment's home entry; a name no field can have is refused.
                Press(main, ByName<Button>(main, "Add a key to dev"));
                Fill(main, "New key", "api_key");
                AssertOneRevealed(main, ByCommand(main, project.ConfirmAddCommand), "adding a key");
                Unchanged(vault, () => Press(main, ByCommand(main, project.ConfirmAddCommand)));
                Assert.NotNull(screen.Error);
                Press(main, ByCommand(main, project.CancelAddCommand));
                Press(main, ByName<Button>(main, "Add a key to dev"));
                Fill(main, "New key", "NEW_KEY");
                Press(main, ByCommand(main, project.ConfirmAddCommand));
                Assert.Null(screen.Error);
                Assert.Equal(["env:billing"], Saved(vault, home));
                Assert.True(unlocked.Fields(home)!.Single(field => field.Name == "NEW_KEY").IsProtected);

                // A .env imported, previewed first.
                Press(main, ByName<Button>(main, "Import .env"));
                await Until(() => project.Import.IsPreviewing, () => screen.Error ?? "no preview");
                AssertOneRevealed(main, ByCommand(main, project.Import.ConfirmCommand), "the import preview");
                Press(main, ByCommand(main, project.Import.ConfirmCommand));
                Assert.Null(screen.Error);
                Assert.Equal(_imported, unlocked.ReadField(home, "IMPORTED_KEY"));

                // References only, for keypaste run to resolve.
                Press(main, ByName<Button>(main, "Export .env.keypaste"));
                await Until(() => File.Exists(references));
                Assert.EndsWith(
                    "IMPORTED_KEY=kp://billing/dev/IMPORTED_KEY\nNEW_KEY=kp://billing/dev/NEW_KEY\nSTRIPE_KEY=kp://billing/dev/STRIPE_KEY\n",
                    File.ReadAllText(references),
                    StringComparison.Ordinal);

                // Run gives a child exactly dev's fields, the confirmation naming where they come from.
                Fill(main, "Project directory", billingDirectory);
                Fill(main, "Command", Reporter(billingPipe));
                Press(main, ByCommand(main, project.Launch.SaveCommand));
                Assert.True(project.Launch.IsMapped, screen.Error);

                if (!project.Launch.IsSupported)
                {
                    Assert.False(ByName<Button>(main, "Run in a terminal").IsEffectivelyEnabled);
                    Assert.Contains("This app opens terminals on Windows and Linux only. Copy the run command instead.", AutomationNames(main));
                }
                else
                {
                    var billing = await RunAsync(main, shell, billingPipe, "From env/billing/.env, services/Stripe", started);
                    Assert.Equal(
                        [$"STRIPE_KEY={_replaced}", "MAIL_KEY unset", "QUEUE_KEY unset", "NEW_KEY=" + unlocked.ReadField(home, "NEW_KEY"), $"IMPORTED_KEY={_imported}", "WEB_KEY unset"],
                        billing);

                    // The project projects.json maps runs where it is mapped.
                    Press(main, Row(main, "Nav", row => row is ProjectRow { Name: "web" }));
                    Assert.Equal("web", Assert.IsType<EnvSetsViewModel>(shell.Content).OpenProject?.Name);
                    Assert.Equal(
                        ["STRIPE_KEY unset", "MAIL_KEY unset", "QUEUE_KEY unset", "NEW_KEY unset", "IMPORTED_KEY unset", $"WEB_KEY={_webKey}"],
                        await RunAsync(main, shell, webPipe, "From apps/Web", started));
                }

                Chord(main, PhysicalKey.L);
                await Until(() => root.Content is UnlockView);
                main.Close();
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", path);
                started.ForEach(End);
            }
        });

    /// <summary>Presses Run in a terminal and Start, reads what the child reports, and hangs up so it exits.</summary>
    private static async Task<List<string>> RunAsync(Window main, ShellViewModel shell, string pipeName, string sources, List<int> started)
    {
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = pipe.WaitForConnectionAsync(Token);
        var screen = Assert.IsType<EnvSetsViewModel>(shell.Content);
        var project = screen.OpenProject!;

        // A toast, such as Save's, draws over the foot of the window for its few seconds.
        await Until(() => !shell.HasToast);
        var run = ByName<Button>(main, "Run in a terminal");
        Press(main, run);
        await Until(() => project.Launch.IsConfirming, () => screen.Error ?? $"no confirmation and no error; the press reached {Hit(main, run)}");
        Assert.Contains(sources, AutomationNames(main));
        AssertOneRevealed(main, ByCommand(main, project.Launch.ConfirmLaunchCommand), "the run confirmation");
        Press(main, ByCommand(main, project.Launch.ConfirmLaunchCommand));
        await Until(() => project.Launch.LastStart is not null);
        var start = Assert.IsType<ChildResult>(project.Launch.LastStart);
        Assert.Equal(ChildOutcome.Started, start.Outcome);
        started.Add(start.ProcessId);

        await connected.WaitAsync(TimeSpan.FromSeconds(60), Token);
        List<string> report = [];
        using var reader = new StreamReader(pipe, leaveOpen: true);

        while (await reader.ReadLineAsync(Token) is { } line && line != "end")
        {
            report.Add(line);
        }

        return [.. report.Where(line => _reported.Any(name => line.StartsWith(name + "=", StringComparison.Ordinal) || line == name + " unset"))];
    }

    /// <summary>What the window finds at the centre of <paramref name="control"/>, for a press that did nothing.</summary>
    private static string Hit(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) is { } centre
            && window.InputHitTest(centre) is Control hit
            ? $"{hit.GetType().Name} in {(hit.FindAncestorOfType<Button>(includeSelf: true) is { } button ? $"the button '{AutomationProperties.GetName(button)}'" : "no button")}"
            : "nothing";

    private static void Seed(Vault vault, string group, string title, string key, string value, params string[] tags)
    {
        var entry = new EntryName(group, title);
        vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = title + "-login" });
        Assert.True(vault.SetFields(entry, [new FieldWrite(key, value)]));

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(entry, tag));
        }
    }

    /// <summary>Maps a project in the home's <c>projects.json</c>, beside whatever other tests put there.</summary>
    private static void Map(string vault, string project, string directory, string command)
    {
        var mappings = KeypasteHome.ProjectsPath(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
        Assert.True(ProjectMappings.TryLoad(mappings, out var held));
        Assert.True(ProjectMappings.Save(mappings, ProjectMappings.Put(held, new ProjectMapping(Path.GetFullPath(vault), project, directory, command))));
    }

    /// <summary>The command that starts <c>Keypaste.EnvReporter</c>, reporting the journey's keys over the pipe.</summary>
    private static string Reporter(string pipeName)
    {
        var reporter = Helper("Keypaste.EnvReporter");
        Assert.True(File.Exists(reporter), $"build Keypaste.EnvReporter first: {reporter}");
        var names = string.Join(' ', _reported);

        return OperatingSystem.IsWindows() ? $"\"{reporter}\" {pipeName} {names}" : $"'{reporter}' {pipeName} {names}";
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

    /// <summary>Runs what follows <c>-e</c> as an emulator does, with nothing on its input.</summary>
    [SupportedOSPlatform("linux")]
    private static void StandInTerminal(string directory)
    {
        var stub = Path.Combine(directory, "x-terminal-emulator");
        File.WriteAllText(stub, "#!/bin/sh\n[ \"$1\" = -e ] && shift\nexec \"$@\" </dev/null\n");
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Ends the terminal a Run started, and what it started, if it is still there.</summary>
    private static void End(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            // It had already exited.
        }
    }

    /// <summary>The tags the saved file holds for an entry, read by a second open as another program would.</summary>
    private static IReadOnlyList<string> Saved(string vault, EntryName entry)
    {
        using var reopened = Vault.Open(vault, TempVault.Password);
        return reopened.Tags(entry) ?? [];
    }

    private static void Unchanged(string vault, Action act)
    {
        var before = File.ReadAllBytes(vault);
        act();
        Assert.Equal(before, File.ReadAllBytes(vault));
    }

    private static void AssertOneRevealed(Window window, Control primary, string at)
    {
        WindowInput.Reveal(window, primary);
        AmberElements.AssertOne(window, primary, at);
    }

    private static void Fill(Window window, string field, string text)
    {
        var box = ByName<TextBox>(window, field);
        WindowInput.Click(window, box);
        box.Clear();
        WindowInput.Type(window, text);
    }

    private static T ByName<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);

    private static Button ByCommand(Window window, System.Windows.Input.ICommand command) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && ReferenceEquals(button.Command, command));

    /// <summary>A matrix row's own action, which shows while the row is hovered.</summary>
    private static Button RowAction(Window window, string key, string action) =>
        window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.DataContext is EnvVariableRow { Key: var held } && held == key && button.Content as string == action);
}
