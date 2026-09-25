using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Draws every screen of the app to a PNG with a demo vault, for comparing against the design in
/// docs/design. It runs only when KEYPASTE_SCREENS_OUT names a folder, and is skipped otherwise:
/// <code>
/// KEYPASTE_SCREENS_OUT=/tmp/screens dotnet test tests/Keypaste.App.Tests -- --filter-class Keypaste.App.Tests.Rendering.ScreenRenderer
/// </code>
/// (PowerShell: <c>$env:KEYPASTE_SCREENS_OUT="$env:TEMP\screens"</c> first.) It writes one file per
/// sidebar destination, the unlock screen, the approval window and the shell with a toast. Add a
/// screen by adding a <see cref="Save"/> call.
/// </summary>
public sealed class ScreenRenderer
{
    private const string _variable = "KEYPASTE_SCREENS_OUT";
    private const string _master = "correct horse battery staple";
    private const int _width = 1280;
    private const int _height = 800;

    [Fact]
    public Task Every_screen_is_drawn_to_a_file() => HeadlessSession.On(() =>
    {
        var output = Environment.GetEnvironmentVariable(_variable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"{_variable} is not set");
        Directory.CreateDirectory(output!);

        using var demo = new DemoVault();
        DrawUnlock(demo, output!);
        DrawShell(demo, output!);
        DrawApproval(output!);
        DrawComponents(output!);
        DrawLockAndImport(demo, output!);
    });

    /// <summary>The lock screen's states and the KDBX import dialog over the shell.</summary>
    private static void DrawLockAndImport(DemoVault demo, string output)
    {
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        var picker = new FakeVaultFilePicker();

        using (var session = new AppVaultSession(new ManualClock()))
        using (var unlock = new UnlockViewModel(session, demo.Home, picker, () => { }, lockedBy: VaultLockReason.Idle))
        {
            var window = Show(new UnlockView { DataContext = unlock });

            foreach (var c in "hunter22")
            {
                unlock.Type(c);
            }

            Save(window, output, "80-lock-idle");

            Wait(unlock.UnlockAsync());
            Assert.True(unlock.IsError);
            Save(window, output, "85-lock-wrong-password");

            picker.NewPath = Path.Combine(demo.Home, "work.kdbx");
            Assert.True(unlock.StartCreateCommand.CanExecute(null));
            Wait(unlock.StartCreateAsync());
            Save(window, output, "82-lock-create");

            foreach (var c in "one")
            {
                unlock.TypeNew(c);
            }

            foreach (var c in "two")
            {
                unlock.TypeConfirm(c);
            }

            Wait(unlock.CreateAsync());
            Assert.True(unlock.IsError);
            Save(window, output, "86-lock-create-mismatch");
            window.Close();
        }

        var empty = Directory.CreateTempSubdirectory("keypaste-screens-empty-").FullName;

        try
        {
            using var session = new AppVaultSession(new ManualClock());
            using var welcome = new UnlockViewModel(session, empty, picker, () => { });
            var window = Show(new UnlockView { DataContext = welcome });
            Save(window, output, "81-lock-welcome");
            window.Close();
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }

        var source = Path.Combine(demo.Home, "personal.kdbx");

        using (var foreign = Vault.Create(source, "keepassxc-demo"))
        {
            foreign.AddEntry(new VaultEntry { Title = "Checking", Username = "maya.ortiz", Password = "demo-bank-1", GroupPath = "Banking" });
            foreign.AddEntry(new VaultEntry { Title = "Savings", Username = "maya.ortiz", Password = "demo-bank-2", GroupPath = "Banking" });
            foreign.AddEntry(new VaultEntry { Title = "fastmail", Username = "maya@fastmail.com", Password = "demo-mail", GroupPath = "Email" });
            foreign.AddEntry(new VaultEntry { Title = "homelab", Username = "maya", Password = "demo-ssh-1", GroupPath = "SSH" });
            foreign.AddEntry(new VaultEntry { Title = "nas", Username = "admin", Password = "demo-ssh-2", GroupPath = "SSH" });
            foreign.AddEntry(new VaultEntry { Title = "POSTGRES_URL", Password = "demo-pg", GroupPath = "env/acme-jobs" });
            foreign.AddEntry(new VaultEntry { Title = "QUEUE_TOKEN", Password = "demo-queue", GroupPath = "env/acme-jobs" });
            foreign.AddEntry(new VaultEntry { Title = "old router", Password = "demo-old", GroupPath = "Email" });
            foreign.RemoveEntry(new EntryName("Email", "old router"));
            foreign.Save();
        }

        using (var session = new AppVaultSession(new ManualClock()))
        {
            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
            }

            picker.ExistingPath = source;
            using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(), picker: picker);
            var window = Show(new ShellView { DataContext = shell });

            Wait(shell.ImportCommand.ExecuteAsync());
            var import = shell.Import!;

            foreach (var c in "keepass")
            {
                import.TypePassword(c);
            }

            Save(window, output, "93-import-locked");

            Wait(import.UnlockAsync());
            Assert.True(import.HasMessage);
            Save(window, output, "98-import-wrong-password");

            foreach (var c in "keepassxc-demo")
            {
                import.TypePassword(c);
            }

            Wait(import.UnlockAsync());
            Assert.True(import.IsDecrypted, import.Message);
            Save(window, output, "94-import-mapped");

            import.Rows.First(row => row.SourceGroup == "SSH").Destination = ".keypaste/ssh";
            Save(window, output, "95-import-blocked");

            import.Rows.First(row => row.SourceGroup == "SSH").Destination = "personal/SSH";
            import.KeepEditingInPlace = true;
            Save(window, output, "96-import-in-place");

            import.KeepEditingInPlace = false;
            import.ConfirmCommand.Execute(null);
            Assert.Null(shell.Import);
            Save(window, output, "97-import-done");

            var damaged = Path.Combine(demo.Home, "notes.kdbx");
            File.WriteAllText(damaged, "not a vault");
            shell.OpenImport(damaged);
            Assert.True(shell.Import!.IsUnreadable);
            Save(window, output, "99-import-unreadable");
            shell.Import!.CancelCommand.Execute(null);
            window.Close();
        }

        using (var session = new AppVaultSession(new ManualClock()))
        using (var handOff = new UnlockViewModel(session, demo.Home, picker, () => { }))
        {
            Assert.True(handOff.Offer(source, null));
            var window = Show(new UnlockView { DataContext = handOff });
            Save(window, output, "87-lock-hand-off");
            window.Close();
        }

        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [
                new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow),
                new Core.Recent.RecentVault(source, DateTimeOffset.UtcNow.AddMinutes(-5)),
                new Core.Recent.RecentVault(Path.Combine(demo.Home, "moved.kdbx"), DateTimeOffset.UtcNow.AddDays(-2)),
            ]);

        using (var session = new AppVaultSession(new ManualClock()))
        using (var unlock = new UnlockViewModel(session, demo.Home, picker, () => { }, lockedBy: VaultLockReason.Requested))
        {
            var window = Show(new UnlockView { DataContext = unlock });
            Save(window, output, "83-lock-recent");

            Assert.True(unlock.OffersRestore);
            unlock.StartRestoreCommand.Execute(null);
            Save(window, output, "84-lock-restore");

            var restore = unlock.Restore!;

            foreach (var c in "wrong")
            {
                restore.Type(c);
            }

            Wait(restore.CheckAsync());
            Assert.True(restore.IsError);
            Save(window, output, "88-lock-restore-refused");

            foreach (var c in _master)
            {
                restore.Type(c);
            }

            Wait(restore.CheckAsync());
            Assert.True(restore.IsConfirming, restore.Message);
            Save(window, output, "89-lock-restore-confirm");
            window.Close();
        }
    }

    private static MainWindow Show(Control content)
    {
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = content;
        window.Show();
        WindowInput.Drain();
        return window;
    }

    /// <summary>Runs the dispatcher until <paramref name="task"/> is done, for work that hops off the UI thread.</summary>
    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
    }

    private static void DrawUnlock(DemoVault demo, string output)
    {
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        using var session = new AppVaultSession(new ManualClock());
        using var unlock = new UnlockViewModel(session, demo.Home, new FakeVaultFilePicker(), () => { });
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = unlock };
        window.Show();
        Save(window, output, "00-unlock");
        window.Close();
    }

    private static void DrawShell(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock());
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        foreach (var destination in Destinations.All)
        {
            shell.Current = destination;

            if (shell.Content is EntriesViewModel entries)
            {
                entries.Selected = entries.Rows.FirstOrDefault(row => row.Title == "github");
            }

            if (shell.Content is EnvSetsViewModel env)
            {
                env.OpenCommand.Execute("acme-api");
            }

            Save(window, output, $"{destination.Shortcut:00}-{Slug(destination.Title)}");
        }

        shell.Current = Destinations.Of(DestinationKind.Entries);
        shell.ShowToast("Copied the username. The clipboard clears in 30s.");
        Save(window, output, "90-toast");

        window.Close();
    }

    private static void DrawApproval(string output)
    {
        var prompt = ApprovalPrompt.For(
            "claude-code",
            new EntryName("env/acme-api", "DATABASE_URL"),
            "password",
            "Run the database migration for the api service.",
            3600,
            "work laptop");
        var window = new ApprovalWindow(new ApprovalViewModel(prompt));
        window.Show();
        Save(window, output, "91-approval");
        window.Close();
    }

    /// <summary>The shared classes from Theme/*.axaml side by side, for checking them against the design.</summary>
    private static void DrawComponents(string output)
    {
        static Button Button(string text, string classes, bool enabled = true)
        {
            var button = new Button { Content = text, IsEnabled = enabled };
            button.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return button;
        }

        static TextBlock Text(string text, string classes)
        {
            var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            block.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return block;
        }

        static Border Box(string classes, Control? child = null)
        {
            var border = new Border { Child = child };
            border.Classes.AddRange(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return border;
        }

        static StackPanel Row(params Control[] children)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.AddRange(children);
            return row;
        }

        var segments = Box("segmented", Row(
            new RadioButton { Content = "All", IsChecked = true, Classes = { "segment" } },
            new RadioButton { Content = "Agents", Classes = { "segment" } },
            new RadioButton { Content = "You", Classes = { "segment" } },
            new RadioButton { Content = "Denied", Classes = { "segment" } }));
        ((StackPanel)segments.Child!).Spacing = 0;

        var focused = new TextBox { Text = "DATABASE_URL", Width = 260, Classes = { "mono" } };

        var icons = new WrapPanel { MaxWidth = 1180 };
        foreach (var key in Application.Current!.Resources.MergedDictionaries
            .Select(provider => provider is ResourceInclude include ? include.Loaded : provider)
            .OfType<IResourceDictionary>()
            .SelectMany(dictionary => dictionary.Keys.OfType<string>())
            .Where(key => key.StartsWith("Icon.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            icons.Children.Add(new Border
            {
                Width = 36,
                Height = 32,
                Child = new KpIcon { Icon = key["Icon.".Length..], Size = 16 },
                [ToolTip.TipProperty] = key,
            });
        }

        var page = new StackPanel { Margin = new Thickness(32, 28), Spacing = 18 };
        page.Children.AddRange(
        [
            Row(new BrandLockup { MarkSize = 22, FontSize = 19 }, new BrandMark { Width = 56, Height = 56 }, new BrandMark { Width = 22, Height = 22, Classes = { "mono" } }),
            Row(Text("Agents", "h1"), Text("New share link", "dialog-title"), Text("Active grants", "section")),
            Row(Text("MCP clients and tokens that can ask for secrets.", "subtitle"), Text("Value", "label"), Text("TIME   ACTOR   RESULT", "th"), Text("kp://acme/api/dev/DATABASE_URL", "mono")),
            Row(Button("Connect client", ""), Button("Allow for 1 hour", "primary"), Button("Deny", "ghost"), Button("Revoke", "danger"), Button("New token", "link"), Button("Disabled", "primary", enabled: false), Button("Small", "sm"), Button("Large", "lg primary"), new Button { Classes = { "icon" }, Content = new KpIcon { Icon = "ellipsis", Size = 14 } }),
            Row(new TextBox { PlaceholderText = "Filter 7 secrets", Width = 260 }, focused, new TextBox { PlaceholderText = "Search secrets, agents, projects", Width = 300, Classes = { "search" } }),
            Row(new ComboBox { ItemsSource = new[] { "Ask every time", "Session grants up to 1h", "Inject only" }, SelectedIndex = 1, Width = 240 }, new CheckBox { Content = "Require a passphrase", IsChecked = true }, new CheckBox { Content = "Unchecked" }, new ToggleSwitch { IsChecked = true, Content = "Keep editing in place" }, new ToggleSwitch { IsChecked = false }),
            Row(segments, Box("chip", Text("dev", "")), Box("chip outline", Text("inject-only", "")), Box("chip amber", Text("approval required", "")), Box("keycap", Text("Ctrl K", ""))),
            Row(Box("dot ok"), Text("Granted", "ok medium"), Box("dot danger"), Text("Denied", "danger medium"), Box("dot info"), Text("Token", "info medium"), Box("dot idle"), Text("Idle", "muted"), Box("dot waiting"), Text("0:28", "mono sm amber"), new ProgressBar { Classes = { "thin" }, Value = 70, Width = 150 }),
            Row(
                new Border { Classes = { "card" }, Width = 260, Child = Text("card: hairline, no fill", "") },
                new Border { Classes = { "card", "filled" }, Width = 260, Child = Text("card filled", "") },
                new Border { Classes = { "terminal" }, Width = 300, Padding = new Thickness(12, 10), Child = Text("› keypaste run -p dev -- npm start", "") },
                new Border { Classes = { "field" }, Width = 300, Child = Text("kp://acme/api/dev/STRIPE_SECRET_KEY", "mono secondary") }),
            Row(
                new Border { Classes = { "dialog" }, Width = 360, Child = new StackPanel { Spacing = 8, Children = { Text("claude-code wants 2 secrets", "dialog-title"), Text("via MCP · ~/acme/api · profile dev", "subtitle") } } },
                new Border { Classes = { "toast" }, Child = Row(new KpIcon { Icon = "circle-check", Size = 15, Foreground = (IBrush)Application.Current.FindResource("KpOk")! }, Text("Granted claude-code for 1 hour", "")) }),
            icons,
        ]);

        var window = new Window { Width = _width, Height = 900, Content = new ScrollViewer { Content = page } };
        window.Show();
        focused.Focus();
        Save(window, output, "92-components");
        window.Close();
    }

    private static void Save(TopLevel window, string output, string name)
    {
        WindowInput.Drain();

        // Enough render ticks for every 120-200ms transition to finish, so no frame is mid-fade.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        WindowInput.Drain();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static string Slug(string title) => title.ToLowerInvariant().Replace(' ', '-');

    /// <summary>A vault that looks lived in: logins in groups, and env projects with keys.</summary>
    private sealed class DemoVault : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-screens-").FullName;

        internal DemoVault()
        {
            Path = System.IO.Path.Combine(_directory, "acme.kdbx");

            using var vault = Vault.Create(Path, _master);
            vault.AddEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "demo-gh-7Hq2x", Url = "https://github.com", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "aws-console", Username = "maya.ortiz", Password = "demo-aws-51Nf", Url = "https://console.aws.amazon.com", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "linear", Username = "maya@acme.dev", Password = "demo-lin-a91K", Url = "https://linear.app", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "gmail", Username = "maya.ortiz@gmail.com", Password = "demo-gm-3a11", Url = "https://mail.google.com", GroupPath = "Personal" });
            vault.AddEntry(new VaultEntry { Title = "home wifi", Password = "demo-wifi-f3a9", Notes = "Router in the hall cupboard.", GroupPath = "Personal" });

            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "OPENAI_API_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                vault.AddEntry(new VaultEntry { Title = key, Password = "demo-" + key.ToLowerInvariant(), GroupPath = "env/acme-api" });
            }

            foreach (var key in new[] { "NEXT_PUBLIC_API", "VERCEL_TOKEN" })
            {
                vault.AddEntry(new VaultEntry { Title = key, Password = "demo-" + key.ToLowerInvariant(), GroupPath = "env/acme-web" });
            }

            vault.Save();
        }

        internal string Path { get; }

        internal string Home => _directory;

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
