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
using Keypaste.Core.Audit;
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
    });

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

        DrawActivityStates(shell, window, demo.Home, output);

        shell.Current = Destinations.Of(DestinationKind.Entries);
        shell.ShowToast("Copied the username. The clipboard clears in 30s.");
        Save(window, output, "90-toast");

        window.Close();
    }

    /// <summary>The Activity screen filtered to refusals, with the chain's verdict open, and after a careless edit.</summary>
    /// <remarks>Last, because the edit leaves the demo log broken.</remarks>
    private static void DrawActivityStates(ShellViewModel shell, Window window, string home, string output)
    {
        shell.Current = Destinations.Of(DestinationKind.Log);
        var log = (LogViewModel)shell.Content!;

        log.Filter = log.Filters.Single(option => option.Filter == LogFilter.Denied);
        Save(window, output, "03-activity-denied");

        log.Filter = log.Filters[0];
        log.VerifyCommand.Execute(null);
        Save(window, output, "03-activity-verify");

        var path = KeypasteHome.AuditPath(home);
        var lines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[3] = lines[3].Replace("the person denied it", "the person okayed it", StringComparison.Ordinal);
        File.WriteAllText(path, string.Join('\n', lines) + '\n');
        log.Refresh();
        Save(window, output, "03-activity-broken");
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
            WriteActivity(_directory);
        }

        internal string Path { get; }

        internal string Home => _directory;

        /// <summary>A morning of audit records, written through the real writer so the chain verifies.</summary>
        private static void WriteActivity(string home)
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 7, 28, 7, 40, 12, TimeSpan.Zero));
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(home), clock, out var log, out var error), error);

            using (log)
            {
                void Write(TimeSpan after, AuditRecord record)
                {
                    clock.Advance(after);
                    Assert.True(log.TryAppend(record, out var failure), failure);
                }

                var claude = new AuditClient("claude-code", "1.0.43", null);
                var cursor = new AuditClient("cursor", "1.2.4", null);

                static AuditRecord Credential(AuditClient client, string entry, string field, AuditDecision decision, AuditMethod method, string reason, int? seconds = null) => new()
                {
                    Tool = "request_credential",
                    Client = client,
                    Args = AuditArgs.ForCredentialRequest(entry, field, 3600, "Run the database migration for the api service."),
                    Decision = decision,
                    Method = method,
                    Reason = reason,
                    GrantedSeconds = seconds,
                };

                Write(TimeSpan.Zero, new AuditRecord
                {
                    Tool = "list_entry_names",
                    Client = claude,
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Exposure,
                    Reason = "every name listed lies inside the exposure",
                });
                Write(TimeSpan.FromSeconds(41), Credential(claude, "env/acme-api/DATABASE_URL", "password", AuditDecision.Granted, AuditMethod.Prompt, "approved for 1 hour", 3600));
                Write(TimeSpan.FromMinutes(17), new AuditRecord
                {
                    Tool = "run",
                    Client = claude,
                    Args = AuditArgs.ForRun(null, "Run the migration."),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.GrantCache,
                    Reason = "served from a grant given 17 minutes ago",
                    Entries = ["env/acme-api/DATABASE_URL", "env/acme-api/STRIPE_SECRET_KEY"],
                    Command = "npm run migrate",
                });
                Write(TimeSpan.FromMinutes(4), Credential(cursor, "Work/aws-console", "password", AuditDecision.Denied, AuditMethod.Prompt, "the person denied it"));
                Write(TimeSpan.FromMinutes(18), new AuditRecord
                {
                    Tool = "run",
                    Client = new AuditClient("keypaste run --token", "1.0.0", null),
                    Args = new AuditArgs { Entry = "env/acme-api" },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Token,
                    Reason = Core.Tokens.TokenAuditReason.Format("t7d2e", "ci-github-actions", "6 variable(s)"),
                    Entries = ["env/acme-api/DATABASE_URL", "env/acme-api/STRIPE_SECRET_KEY", "env/acme-api/OPENAI_API_KEY", "env/acme-api/REDIS_URL", "env/acme-api/JWT_SIGNING_KEY", "env/acme-api/SENTRY_DSN"],
                });
                Write(TimeSpan.FromMinutes(11), new AuditRecord
                {
                    Tool = "share",
                    Client = new AuditClient("keypaste share", "1.0.0", null),
                    Args = new AuditArgs { Entry = "env/acme-api/STRIPE_SECRET_KEY", Field = "password" },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.ShareCreated,
                    Reason = "share 3a11: 1 view, expires 2026-07-29T08:31:00Z, to maya@acme.dev, passphrase",
                });
                Write(TimeSpan.FromMinutes(14), Credential(cursor, "env/acme-web/VERCEL_TOKEN", "password", AuditDecision.Denied, AuditMethod.TimedOut, "nobody answered within 60 seconds"));
                Write(TimeSpan.FromMinutes(20), Credential(claude, "Work/github", "username", AuditDecision.Granted, AuditMethod.Policy, "rule 'github-username' in policy.toml"));
            }
        }

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
