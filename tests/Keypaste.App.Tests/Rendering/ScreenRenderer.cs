using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Session;
using Keypaste.App.Tests.ViewModels;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Activity;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Draws every screen of the app to a PNG with a demo vault, for comparing against
/// docs/DESIGN.md. It runs only when KEYPASTE_SCREENS_OUT names a folder, and is skipped otherwise:
/// <code>
/// KEYPASTE_SCREENS_OUT=/tmp/screens bash scripts/verify.sh test Keypaste.App.Tests.Rendering.ScreenRenderer
/// </code>
/// (PowerShell: <c>$env:KEYPASTE_SCREENS_OUT="$env:TEMP\screens"</c> first.) It writes every screen
/// four times, into <c>dark</c>, <c>light</c>, <c>high-contrast-dark</c> and <c>high-contrast-light</c>
/// folders, with the system set to each and no theme chosen, as the app starts (N.7), and fails a frame holding more than one amber element. Dark and light
/// are also drawn at 150% and 200%, into <c>zoom-150</c> and <c>zoom-200</c>, where the 200% folders add <c>s-</c> frames on small screens. Add a
/// screen by adding a <see cref="Save"/> call: <see cref="Every_screen_keeps_every_control_within_reach"/>, which needs no folder, walks each frame at each zoom.
/// </summary>
public sealed class ScreenRenderer
{
    private const string _variable = "KEYPASTE_SCREENS_OUT";
    private const string _master = "correct horse battery staple";
    private const int _width = 1280;
    private const int _height = 800;

    private static readonly EntryName _apiHome = new("env/acme-api", ".env");
    private static readonly EntryName _openAi = new("Services", "OpenAI");

    // Set while a test walks every frame, for Save to add what each cuts off.
    private static List<string>? _cutOff;

    /// <summary>Makes the palette's folder and sets the system to it; skips the test unless a folder is named.</summary>
    private static string Prepare(string palette, int percent = 100)
    {
        var root = Environment.GetEnvironmentVariable(_variable);
        Assert.SkipWhen(string.IsNullOrEmpty(root), $"{_variable} is not set");
        InterfaceZoom.Set(percent);
        var output = percent == 100 ? Path.Combine(root!, palette) : Path.Combine(root!, $"zoom-{percent}", palette);
        Directory.CreateDirectory(output);
        PlatformTheme.Set(
            palette.EndsWith("light", StringComparison.Ordinal) ? Avalonia.Platform.PlatformThemeVariant.Light : Avalonia.Platform.PlatformThemeVariant.Dark,
            palette.StartsWith("high-contrast", StringComparison.Ordinal) ? Avalonia.Platform.ColorContrastPreference.High : Avalonia.Platform.ColorContrastPreference.NoPreference);
        return output;
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("high-contrast-dark")]
    [InlineData("high-contrast-light")]
    public Task Every_screen_is_drawn_to_a_file(string palette) => HeadlessSession.On(() => DrawEveryScreen(Prepare(palette)));

    [Theory]
    [InlineData(150, "dark")]
    [InlineData(150, "light")]
    [InlineData(200, "dark")]
    [InlineData(200, "light")]
    public Task Every_screen_is_drawn_to_a_file_at_each_zoom(int percent, string palette) => HeadlessSession.On(async () =>
    {
        var output = Prepare(palette, percent);
        DrawEveryScreen(output);
        await DrawAgents(output);
    });

    /// <summary>The whole app in a language that does not exist: right to left, and every word half as long again.</summary>
    [Theory]
    [InlineData("rtl", 100, "dark")]
    [InlineData("rtl", 100, "light")]
    [InlineData("rtl", 200, "dark")]
    [InlineData("rtl", 200, "light")]
    [InlineData("long", 100, "dark")]
    [InlineData("long", 100, "light")]
    [InlineData("long", 200, "dark")]
    [InlineData("long", 200, "light")]
    [InlineData("en-US", 100, "dark")]
    [InlineData("en-US", 100, "light")]
    [InlineData("de-DE", 100, "dark")]
    public Task Every_screen_is_drawn_in_a_pseudo_locale(string locale, int percent, string palette) => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.Named(locale);
        DrawEveryScreen(Directory.CreateDirectory(Path.Combine(Prepare(palette, percent), locale)).FullName);
    });

    /// <summary>A long language must not push a control out of reach or a right-to-left one out of the window, at the sizes people use.</summary>
    [Theory]
    [InlineData("long", 100)]
    [InlineData("long", 200)]
    [InlineData("rtl", 100)]
    [InlineData("rtl", 200)]
    [InlineData("en-US", 100)]
    [InlineData("de-DE", 100)]
    public Task Every_screen_keeps_every_control_within_reach_in_a_pseudo_locale(string locale, int percent) => HeadlessSession.On(() =>
    {
        using var pseudo = PseudoLocale.Named(locale);
        var output = Directory.CreateTempSubdirectory("keypaste-reach-").FullName;
        _cutOff = [];

        try
        {
            InterfaceZoom.Set(percent);
            DrawEveryScreen(output);
            Assert.True(_cutOff.Count == 0, string.Join('\n', _cutOff.Distinct()));
        }
        finally
        {
            _cutOff = null;
            Directory.Delete(output, recursive: true);
        }
    });

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task Every_security_prompt_draws_the_same_under_right_to_left(string palette) => HeadlessSession.On(() =>
    {
        var english = Directory.CreateTempSubdirectory("keypaste-prompts-").FullName;
        var rightToLeft = Directory.CreateTempSubdirectory("keypaste-prompts-rtl-").FullName;

        try
        {
            PlatformTheme.Set(
                palette == "light" ? Avalonia.Platform.PlatformThemeVariant.Light : Avalonia.Platform.PlatformThemeVariant.Dark,
                Avalonia.Platform.ColorContrastPreference.NoPreference);
            DrawEveryPrompt(english);

            using (PseudoLocale.RightToLeft())
            {
                DrawEveryPrompt(rightToLeft);
            }

            var names = Directory.GetFiles(english, "*.png").Select(Path.GetFileName).ToList();
            Assert.Contains("91-approval.png", names);
            Assert.Contains("00e-terms-window.png", names);
            Assert.All(names, name => Assert.True(
                File.ReadAllBytes(Path.Combine(english, name!)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(rightToLeft, name!))),
                name + " changed under right to left"));
        }
        finally
        {
            Directory.Delete(english, recursive: true);
            Directory.Delete(rightToLeft, recursive: true);
        }
    });

    private static void DrawEveryPrompt(string output)
    {
        DrawApproval(output);
        DrawPrompts(output);
        DrawTerms(output);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public Task Every_screen_keeps_every_control_within_reach(int percent) => HeadlessSession.On(async () =>
    {
        var output = Directory.CreateTempSubdirectory("keypaste-reach-").FullName;
        _cutOff = [];

        try
        {
            InterfaceZoom.Set(percent);
            DrawEveryScreen(output);
            await DrawAgents(output);
            Assert.True(_cutOff.Count == 0, string.Join('\n', _cutOff.Distinct()));
        }
        finally
        {
            _cutOff = null;
            Directory.Delete(output, recursive: true);
        }
    });

    private static void DrawEveryScreen(string output)
    {
        using var demo = new DemoVault();
        DrawUnlock(demo, output);
        DrawShell(demo, output);
        DrawEnv(demo, output);
        DrawApproval(output);
        DrawComponents(output);
        DrawSecrets(demo, output);
        DrawLockAndImport(demo, output);
        DrawPrompts(output);
        DrawGroupNoTrashCannotDelete(output);
        DrawSameNameItems(output);
        DrawTrashWithRows(demo, output);
        DrawHardwareKey(output);
        DrawSmallScreens(demo, output);
        DrawDrops(demo, output);
        DrawPolish(demo, output);
        DrawEmptyVault(demo, output);
    }

    /// <summary>A vault with nothing in it, as a new person's first look at each place finds it.</summary>
    private static void DrawEmptyVault(DemoVault demo, string output)
    {
        var path = Path.Combine(demo.Home, "first.kdbx");
        File.Delete(path);

        using (var made = Vault.Create(path, _master))
        {
            made.Save();
        }

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value));
        }

        var emptyHome = Directory.CreateTempSubdirectory("keypaste-empty-home-").FullName;
        using var shell = new ShellViewModel(session, emptyHome, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.Entries);
        Save(window, output, "60-empty-items");
        shell.Current = Destinations.Of(DestinationKind.EnvSets);
        Save(window, output, "60-empty-projects");
        shell.Current = Destinations.Of(DestinationKind.AgentActivity);
        Save(window, output, "60-agents-unavailable");
        shell.Current = Destinations.Of(DestinationKind.Log);
        Save(window, output, "60-empty-log");
        shell.Current = Destinations.Of(DestinationKind.Settings);
        Save(window, output, "60-empty-settings");

        using var authority = new AppAuthority(session, null, () => new Allowing());
        var noTools = new FakeTools(Path.Combine(emptyHome, "computer"));
        using (var connected = new ShellViewModel(authority.Session, emptyHome, authority, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker())
        {
            Tools = () => noTools.Connector(),
        })
        {
            window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = connected };
            connected.Current = Destinations.Of(DestinationKind.AgentActivity);
            Wait(Assert.IsType<AgentActivityViewModel>(connected.Content).Looking);
            WindowInput.Drain();
            Save(window, output, "60-empty-agents");
        }

        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };

        window.Width = 720;
        window.Height = 520;
        shell.Current = Destinations.Of(DestinationKind.Entries);
        Save(window, output, "60-empty-items-720");

        window.Width = _width;
        window.Height = _height;
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.BeginCreateGroupCommand.Press();
        entries.DraftGroupName = "Work";
        entries.ConfirmCreateGroupCommand.Press();
        entries.SelectedGroup = entries.Groups.First(group => group.Path == "Work");
        Save(window, output, "60-empty-group");

        session.Write(
            vault =>
            {
                vault.AddEntry(new VaultEntry { Title = "github", Password = "demo-gh-7Hq2x", GroupPath = "Work" });
                return true;
            },
            _ => true);
        entries.Reload();
        entries.Selected = entries.Rows[0];
        entries.DeleteCommand.Press();

        if (entries.IsConfirmingDelete)
        {
            entries.ConfirmDeleteCommand.Press();
        }

        Save(window, output, "60-first-delete-format");

        window.Close();
        Directory.Delete(emptyHome, recursive: true);
    }

    /// <summary>What the title bar says of the vault, the banners under it and the two countdowns.</summary>
    private static void DrawVaultStates(Window window, ShellViewModel shell, string output)
    {
        shell.Current = Destinations.Of(DestinationKind.Entries);
        var entries = (EntriesViewModel)shell.Content!;
        entries.Selected = entries.Rows.First(row => row.Title == "github");

        void Words(string words, StatusTone tone)
        {
            SetDrawnState(shell, nameof(shell.VaultSaveWords), words);
            SetDrawnState(shell, nameof(shell.VaultStatusTone), tone);
        }

        Words("merging", StatusTone.Pending);
        Save(window, output, "61-title-merging");

        Words("updated 14:02", StatusTone.Ok);
        Save(window, output, "61-title-updated");

        Words("file unreadable", StatusTone.Danger);
        Save(window, output, "61-title-file-unreadable");

        Words("changed on disk", StatusTone.Danger);
        SetDrawnState(shell, nameof(shell.VaultChanged), "Another program saved acme.kdbx. Agents are refused until you reload it.");
        Save(window, output, "61-title-changed-on-disk");

        var width = window.Width;
        window.Width = 720;
        Save(window, output, "61-title-changed-on-disk-720");
        window.Width = width;

        SetDrawnState(shell, nameof(shell.VaultChanged), "Another program saved acme.kdbx, and keypaste could not merge it: the file uses an older KeePass format, which keypaste does not merge; choose the newer format in KeePassXC's database settings. Agents are refused until you reload it.");
        Save(window, output, "61-reload-banner-merge-refused");
        SetDrawnState(shell, nameof(shell.VaultChanged), null);

        Words("saved", StatusTone.Ok);
        SetDrawnState(shell, nameof(shell.Notice), "Another program saved acme.kdbx, and keypaste merged it with your changes and saved. github changed in both, and the newer one is kept; the other is in its history.");
        Save(window, output, "61-merge-notice");
        SetDrawnState(shell, nameof(shell.Notice), null);

        SetDrawnState(shell, nameof(shell.Countdown), "Locking in 28 seconds.");
        Save(window, output, "61-title-idle-countdown");
        window.Width = 720;
        Save(window, output, "61-title-idle-countdown-720");
        window.Width = width;
        SetDrawnState(shell, nameof(shell.Countdown), string.Empty);

        Wait(entries.Detail!.CopyPasswordCommand.PressAsync());
        Save(window, output, "61-clipboard-countdown");
        shell.Clipboard.ClearNowCommand.Press();
        WindowInput.Drain();
    }

    /// <summary>What Q.17 drew: Settings' theme where it is offered, Learn more, toasts under the titlebar, the tight widths, the Items icon and the prompt without focus.</summary>
    private static void DrawPolish(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker()) { OffersTheme = true };
        var window = new MainWindow { Width = _width * InterfaceZoom.Factor, Height = _height * InterfaceZoom.Factor };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.FirstOrDefault(row => row.Title == "github");
        shell.ShowToast("Saved github.", ToastTone.Success);
        shell.ShowToast("claude-code can no longer see github.", ToastTone.Information);
        Save(window, output, "50-toasts-under-the-titlebar");

        window.Width = 720 * InterfaceZoom.Factor;
        window.Height = 520 * InterfaceZoom.Factor;
        Save(window, output, "50b-toasts-720");
        shell.Toasts.Shown.ToList().ForEach(toast => toast.CloseCommand.Press());
        Save(window, output, "50c-items-rail-720");

        var agentsPlace = shell.RailPlaces.Single(place => place.Destination.Kind == DestinationKind.AgentActivity);
        foreach (var (state, live, refusing) in new[] { ("running", true, false), ("refusing", false, true), ("stopped", false, false) })
        {
            agentsPlace.DotLive = live;
            agentsPlace.DotDanger = refusing;
            Save(window, output, $"50d-rail-agents-{state}");
        }

        shell.Current = Destinations.Of(DestinationKind.EnvSets);
        foreach (var width in new[] { 840, 900, 999 })
        {
            window.Width = width * InterfaceZoom.Factor;
            window.Height = 680 * InterfaceZoom.Factor;
            Save(window, output, $"51-env-{width}");
        }

        shell.Current = Destinations.Of(DestinationKind.Log);
        foreach (var width in new[] { 840, 900, 999 })
        {
            window.Width = width * InterfaceZoom.Factor;
            Save(window, output, $"52-log-{width}");
        }

        // What a person with macOS's or Windows' always-visible scroll bars gets, at the widths the tight treatment turns on.
        var app = Assert.IsType<App>(Application.Current);
        app.ScrollBarsAutoHide = false;
        try
        {
            window.Height = 480 * InterfaceZoom.Factor;
            foreach (var width in new[] { 840, 976, 1000 })
            {
                window.Width = width * InterfaceZoom.Factor;
                Save(window, output, $"52b-log-{width}-bars");
            }

            shell.Current = Destinations.Of(DestinationKind.EnvSets);
            foreach (var width in new[] { 840, 1000 })
            {
                window.Width = width * InterfaceZoom.Factor;
                Save(window, output, $"51b-env-{width}-bars");
            }
        }
        finally
        {
            app.ScrollBarsAutoHide = true;
        }

        window.Width = 1000 * InterfaceZoom.Factor;
        window.Height = 680 * InterfaceZoom.Factor;
        Save(window, output, "51-env-1000");
        shell.Current = Destinations.Of(DestinationKind.Log);
        Save(window, output, "52-log-1000");

        window.Width = _width * InterfaceZoom.Factor;
        window.Height = _height * InterfaceZoom.Factor;
        shell.Current = Destinations.Of(DestinationKind.Settings);
        Save(window, output, "53-settings-with-the-theme");

        shell.Current = Destinations.Of(DestinationKind.Tokens);
        var tokens = Assert.IsType<ScopedTokensViewModel>(shell.Content);
        tokens.OpenFormCommand.Execute(null);
        WindowInput.Drain();
        Save(window, output, "54-token-form");
        tokens.CreateCommand.Execute(null);
        WindowInput.Drain();
        Save(window, output, "54c-token-form-problems");
        var tokenFormWidth = window.Width;
        window.Width = 720 * InterfaceZoom.Factor;
        Save(window, output, "54d-token-form-720");
        window.Width = tokenFormWidth;
        tokens.CancelFormCommand.Execute(null);
        tokens.OpenFormCommand.Execute(null);
        WindowInput.Drain();
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "LearnMoreScopes").Flyout!.ShowAt(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "LearnMoreScopes"));
        WindowInput.Drain();
        Save(window, output, "54b-learn-more-scopes");

        foreach (var (make, name) in new (Func<PromptWindow>, string)[]
        {
            (() => new ApprovalWindow(new ApprovalViewModel(ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploying", "work laptop"))), "55-approval-inactive"),
        })
        {
            var prompt = make();
            ((PromptViewModel)prompt.DataContext!).Tick(TimeSpan.FromSeconds(28));
            prompt.ShowActivated = false;
            prompt.Show();
            Save(prompt, output, name);
            prompt.Close();
        }
    }

    /// <summary>A drag's highlight on each surface that takes one, the rail's included, and what a file the system names says.</summary>
    private static void DrawDrops(DemoVault demo, string output)
    {
        var export = Path.Combine(demo.Home, "bitwarden-export.json");
        File.WriteAllText(export, """{"encrypted":false,"folders":[],"items":[{"type":1,"name":"a","login":{"username":"u","password":"p"}}]}""");
        var photo = Path.Combine(demo.Home, "passport scan.png");
        File.WriteAllText(photo, "not much");
        var env = Path.Combine(demo.Home, ".env");
        File.WriteAllText(env, "ADDED=value\n");
        var other = Path.Combine(demo.Home, "other.kdbx");

        if (!File.Exists(other))
        {
            using var made = Vault.Create(other, _master);
            made.Save();
        }

        Core.Recent.RecentVaults.Save(Core.Audit.KeypasteHome.RecentPath(demo.Home), [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        using (var session = new AppVaultSession(new ManualClock(AppClock.Start)))
        using (var unlock = new UnlockViewModel(session, demo.Home, new FakeVaultFilePicker(), () => { }))
        {
            var view = new UnlockView { DataContext = unlock };
            var window = Show(view);
            using var vaultFile = WindowDrag.Files(other);
            WindowDrag.Over(window, WindowDrag.Centre(window, view), vaultFile);
            Save(window, output, "d0-unlock-drop");
            WindowDrag.Drop(window, WindowDrag.Centre(window, view), vaultFile);
            Save(window, output, "d1-unlock-dropped");
            unlock.Type('x');
            unlock.TakeHanded(demo.Path);
            Save(window, output, "d2-unlock-handed-left");
            unlock.ClearPassword();
            unlock.Forget(other);
            unlock.TakeHanded(other);
            Save(window, output, "d3-unlock-handed-new");
            window.Close();
        }

        using var opened = new AppVaultSession(new ManualClock(AppClock.Start), TimeSpan.FromHours(8), demo.Home);

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, opened.TryUnlock(demo.Path, master.Value));
        }

        using var shell = new ShellViewModel(opened, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start));
        var shellView = new ShellView { DataContext = shell };
        var main = Show(shellView);
        main.Width = _width * InterfaceZoom.Factor;
        WindowInput.Drain();
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        var content = shellView.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ContentArea");

        using (var exportFile = WindowDrag.Files(export))
        {
            WindowDrag.Over(main, WindowDrag.Centre(main, content), exportFile);
            Save(main, output, "d4-items-import-drop");
        }

        entries.Selected = entries.Rows.First(row => row.Title == "github");
        WindowInput.Drain();

        using (var photoFile = WindowDrag.Files(photo))
        {
            WindowDrag.Over(main, WindowDrag.Centre(main, content), photoFile);
            Save(main, output, "d5-items-attach-drop");
        }

        using (var envFile = WindowDrag.Files(env))
        {
            WindowDrag.Over(main, WindowDrag.Centre(main, content), envFile);
            Save(main, output, "d6-items-env-drop");
        }

        var nav = shellView.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "Nav");
        using var item = FileDragData.ItemPayload();
        shellView.HoldItem(entries.Selected!);
        var personal = (Control)nav.ContainerFromItem(nav.Items.OfType<GroupRow>().Single(row => row.Path == "Personal"))!;
        WindowDrag.Over(main, WindowDrag.Centre(main, personal), item);
        Save(main, output, "d7-items-move-to-group");
        shellView.ReleaseItem();

        shell.AskOpenHanded(other, () => { });
        Save(main, output, "d8-handed-ask");
        shell.KeepVaultCommand.Press();

        main.Width = 720 * InterfaceZoom.Factor;
        WindowInput.Drain();
        Save(main, output, "d9-narrow-before");
        shellView.HoldItem(entries.Selected!);
        var railGroups = shellView.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RailGroups");
        WindowDrag.Over(main, WindowDrag.Centre(main, railGroups), item);
        Save(main, output, "d10-narrow-rail-hold");
        shellView.OpenGroupsForHeldItem();
        WindowInput.Drain();
        Save(main, output, "d11-narrow-groups-open");
        personal = (Control)nav.ContainerFromItem(nav.Items.OfType<GroupRow>().Single(row => row.Path == "Personal"))!;
        WindowDrag.Over(main, WindowDrag.Centre(main, personal), item);
        Save(main, output, "d12-narrow-move-to-group");
        shellView.ReleaseItem();
        shell.CloseSidebarOverlay();
        main.Close();
    }

    /// <summary>Items and the tallest prompt at the largest zoom on a 1366×768 screen, at 100% and at Windows' 150%, whose working areas hold less than the zoomed minimum window.</summary>
    private static void DrawSmallScreens(DemoVault demo, string output)
    {
        if (InterfaceZoom.Percent < 200)
        {
            return;
        }

        foreach (var (name, area) in new[] { ("1366x768", new ScreenArea(new PixelRect(0, 0, 1366, 728), 1)), ("1366x768-at-150", new ScreenArea(new PixelRect(0, 0, 1366, 720), 1.5)) })
        {
            using var session = new AppVaultSession(new ManualClock(AppClock.Start), TimeSpan.FromHours(8), demo.Home);

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
            }

            using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start));
            var window = Show(new ShellView { DataContext = shell });
            window.MinWidth = 0;
            window.MinHeight = 0;
            window.Width = area.Size.Width;
            window.Height = area.Size.Height;
            Save(window, output, $"s-items-on-{name}");
            window.Close();

            var run = new RunApprovalWindow(new RunApprovalViewModel(LongRun())) { OpensOn = () => area };
            DrawWindow(run, output, $"s-run-approval-on-{name}");
        }
    }

    private static RunPrompt LongRun() => new()
    {
        Client = "claude-code",
        Label = "work laptop",
        Reason = string.Concat(Enumerable.Repeat("Run the pending database migration for the api service before the deploy. ", 20)),
        ReasonWasTruncated = false,
        ReasonWasAltered = false,
        Program = "/usr/local/bin/npm",
        Command = "npm run migrate",
        Directory = "/home/maya/acme/api",
        Project = "acme-api",
        Profile = "dev",
        Variables = [new("DATABASE_URL", "env/acme-api/.env", "DATABASE_URL"), new("STRIPE_SECRET_KEY", "env/acme-api/.env", "STRIPE_SECRET_KEY")],
    };

    /// <summary>A vault that also needs a YubiKey: the unlock screen offering it, waiting for a touch, a save waiting for one, and adding one in Settings.</summary>
    private static void DrawHardwareKey(string output)
    {
        var home = Directory.CreateTempSubdirectory("keypaste-screens-yubikey-").FullName;
        var path = Path.Combine(home, "acme.kdbx");
        var device = new Core.Tests.HardwareKeys.SoftwareYubiKey(new byte[20]);

        try
        {
            using (var key = new Core.HardwareKeys.HardwareKey(device, 2))
            using (var vault = Vault.CreateWith(path, _master, null, key))
            {
                vault.AddEntry(new VaultEntry { Title = "github", Password = "demo-gh-7Hq2x", GroupPath = "Work" });
                vault.Save();
            }

            Core.Recent.RecentVaults.Save(
                Core.Audit.KeypasteHome.RecentPath(home), [new Core.Recent.RecentVault(path, DateTimeOffset.UtcNow, null, 2)]);

            using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: home, hardwareKeys: device);
            using (var unlock = new UnlockViewModel(session, home, new FakeVaultFilePicker(), () => { }, action => Avalonia.Threading.Dispatcher.UIThread.Post(action)))
            {
                var window = Show(new UnlockView { DataContext = unlock });
                foreach (var c in "hunter22")
                {
                    unlock.Type(c);
                }

                Save(window, output, "70-yubikey-unlock");

                device.NeverTouched = true;
                var unlocking = unlock.UnlockAsync();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!unlock.IsWaitingForTouch && DateTime.UtcNow < deadline)
                {
                    WindowInput.Drain();
                    Thread.Sleep(1);
                }

                Save(window, output, "71-yubikey-touch");
                unlock.CancelTouchCommand.Press();
                Wait(unlocking);
                window.Close();
            }

            Core.Recent.RecentVaults.Save(
                Core.Audit.KeypasteHome.RecentPath(home), [new Core.Recent.RecentVault(path, DateTimeOffset.UtcNow)]);

            using (var unlock = new UnlockViewModel(session, home, new FakeVaultFilePicker(), () => { }))
            {
                var window = Show(new UnlockView { DataContext = unlock });
                Save(window, output, "70b-lock-more-options");
                window.Close();
            }

            device.NeverTouched = false;
            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value, null, 2));
            }

            using var shell = new ShellViewModel(session, home, null, clipboard: new FakeClipboard(), post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
            var shellWindow = Show(new ShellView { DataContext = shell });

            device.NeverTouched = true;
            var vaultOpen = session.Unlocked!;
            vaultOpen.AddEntry(new VaultEntry { Title = "stripe", Password = "demo-sk", GroupPath = "Work" });
            var saving = Task.Run(vaultOpen.Save);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!shell.IsWaitingForTouch && DateTime.UtcNow < until)
            {
                WindowInput.Drain();
                Thread.Sleep(1);
            }

            Save(shellWindow, output, "72-yubikey-save-touch");
            shell.CancelTouchCommand.Press();
            Assert.ThrowsAny<VaultException>(() => Wait(saving));
            shellWindow.Close();
            device.NeverTouched = false;

            using var settings = new SettingsViewModel(session, home, new DesktopPreferences(home), _ => { }, new FakeVaultFilePicker());
            foreach (var c in _master)
            {
                settings.Access.TypeCurrent(c);
            }

            settings.Access.AttachHardwareKey = true;
            settings.Access.NewSlotOne = true;
            settings.Access.ReviewCommand.Press();
            var settingsWindow = Show(new SettingsView { DataContext = settings });
            settingsWindow.Height = 1900;
            Save(settingsWindow, output, "73-yubikey-settings");
            SetDrawnState(settings.Access, nameof(settings.Access.Busy), true);
            Save(settingsWindow, output, "73b-vault-access-changing");
            SetDrawnState(settings.Access, nameof(settings.Access.Busy), false);
            settingsWindow.Close();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>
    /// Agents with what a working session holds: two agents attached over the app's own endpoint whose
    /// requests a person allowed for an hour, a request still waiting at the prompt, the AI tools found on a
    /// computer of the test's own, settings and two tokens; then the page in a wide and a narrow window, a row's
    /// Settings open, a tool's Connect pane while a request waits and while none does, the token form, a minted
    /// token and a token revoke being confirmed.
    /// </summary>
    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("high-contrast-dark")]
    [InlineData("high-contrast-light")]
    public Task The_agents_screen_is_drawn_with_grants_clients_and_tokens(string palette) => HeadlessSession.On(() => DrawAgents(Prepare(palette)));

    private static async Task DrawAgents(string output)
    {
        using var demo = new DemoVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, TimeSpan.FromHours(8), demo.Home);
        using var authority = new AppAuthority(session, null, () => new Allowing());

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(demo.Path, master.Value));
        }

        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        await using var claude = await AgentAsync(serving, demo.Path, new Core.Ipc.AttachClient("claude", "2.0", "claude-code"));
        // zed is a bridge set up by hand under a name no AI tool found here uses, so it is an agent row of its own.
        await using var zed = await AgentAsync(serving, demo.Path, new Core.Ipc.AttachClient("zed", "0.160", "zed"));
        await AskAsync(claude, serving, demo.Path, "claude-code", "env/acme-api/.env", "DATABASE_URL");
        await AskAsync(claude, serving, demo.Path, "claude-code", "env/acme-api/.env", "STRIPE_SECRET_KEY");
        clock.Advance(TimeSpan.FromMinutes(18));
        await AskAsync(zed, serving, demo.Path, "zed", "env/acme-web/.env", "NEXT_PUBLIC_API");
        var waiting = AskAsync(zed, serving, demo.Path, "zed", Allowing.HeldEntry, Allowing.HeldField);
        await WaitUntilAsync(() => authority.Activity.Waiting.Count == 1);

        var tools = new FakeTools(Path.Combine(demo.Home, "computer")).ClaudeCode(connected: true).Cursor(connected: false).GeminiUnreadable();
        using var shell = new ShellViewModel(authority.Session, demo.Home, authority, clipboard: new FakeClipboard(), clock: clock, post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action))
        {
            Tools = () => tools.Connector(),
        };
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.AgentActivity);
        var agents = Assert.IsType<AgentActivityViewModel>(shell.Content);
        await agents.Looking;
        WindowInput.Drain();
        Save(window, output, "20-agents");

        SetDrawnState(agents, nameof(agents.IsLooking), true);
        Save(window, output, "20a-agents-looking");
        SetDrawnState(agents, nameof(agents.IsLooking), false);

        SetDrawnState(agents, nameof(agents.ClientsProblem), $"{Core.Audit.KeypasteHome.AgentsPath(demo.Home)}: line 3: [[client]] is not a section keypaste reads here. Every agent request is refused until it is fixed; deleting it asks about every value again.");
        Save(window, output, "20b-agents-clients-unreadable");
        SetDrawnState(agents, nameof(agents.ClientsProblem), string.Empty);

        window.Height = 1500;
        Save(window, output, "21-agents-full");

        window.Width = 1920;
        window.Height = 1100;
        Save(window, output, "25-agents-wide");
        window.Width = 960;
        Save(window, output, "29-agents-narrow");
        window.Width = 720;
        window.Height = 520;
        Save(window, output, "29b-agents-720");
        window.Height = 1100;
        window.Width = _width;
        window.Height = 1500;

        var claudeRow = agents.Rows.Single(row => row.Key == "claude-code");
        claudeRow.IsOpen = true;
        Save(window, output, "27-agents-settings-open");
        window.Width = 960;
        Save(window, output, "27-agents-settings-narrow");
        window.Width = _width;

        claudeRow.IsOpen = false;
        agents.ConnectCommand.Press(agents.Rows.Single(row => row.Key == "cursor"));
        await agents.Opening;
        Save(window, output, "24-agents-connect");
        var connecting = agents.Connection!;
        DrawCheckWorking(window, output, connecting);

        // With nothing waiting, the open pane's Connect action is the frame's one amber.
        await zed.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => waiting);
        await WaitUntilAsync(() => authority.Activity.Waiting.Count == 0);
        agents.Refresh();
        Save(window, output, "28-agents-quiet");

        // Scoped tokens, under Settings › Advanced.
        window.Height = _height;
        shell.Current = Destinations.Of(DestinationKind.Tokens);
        var tokens = Assert.IsType<ScopedTokensViewModel>(shell.Content);
        Assert.True(tokens.Create("ci-github-actions", "read:acme-api/staging/*", TimeSpan.FromDays(30), false).Ok);
        Assert.True(tokens.Create("local-evals", "read:acme-api/dev/OPENAI_API_KEY", TimeSpan.FromDays(7), false).Ok);
        Save(window, output, "22-settings-tokens");

        tokens.OpenFormCommand.Press();
        tokens.Name = "deploy-preview";
        tokens.Scope = "read:acme-web/preview/*";
        tokens.Expiry = "7d";
        Save(window, output, "22b-settings-tokens-new");

        tokens.CreateCommand.Press();
        Save(window, output, "23-settings-tokens-minted");
        DrawRevealed(window, output, "MintedToken", "23b-settings-tokens-minted-revealed");

        tokens.DoneMintedCommand.Press();
        tokens.AskRevokeCommand.Press(tokens.Rows[0]);
        Save(window, output, "26-settings-tokens-revoke");

        shell.Current = Destinations.Of(DestinationKind.Settings);
        var settingsScreen = Assert.IsType<SettingsViewModel>(shell.Content);
        settingsScreen.Section = SettingsSectionChoice.Browser;
        var browserFill = settingsScreen.BrowserFill!;
        browserFill.PairCommand.Press();
        var pairing = new PairingItem("work laptop", "chrome-extension://oboonakemofpalcgghocfoadofidjkkk", "Last used 2 days ago");
        SetDrawnState(browserFill, nameof(browserFill.Rows), new[] { pairing });
        WindowInput.Drain();
        foreach (var toast in shell.Toasts.Shown.ToList())
        {
            toast.CloseCommand.Press();
        }

        var stopPairing = window.GetVisualDescendants().OfType<Button>().First(button => button.Name == "StopPairing");
        var scroller = stopPairing.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, scroller.Offset.Y + stopPairing.TranslatePoint(default, scroller)!.Value.Y - 160);
        WindowInput.Drain();
        Save(window, output, "04-settings-browser-fill-armed");
        pairing.IsConfirmingRemove = true;
        Save(window, output, "04-settings-browser-fill-remove");
        browserFill.StopCommand.Press();

        settingsScreen.Section = SettingsSectionChoice.General;
        WindowInput.Drain();
        Save(window, output, "04-settings-general");
        settingsScreen.Section = SettingsSectionChoice.ThisVault;
        WindowInput.Drain();
        Save(window, output, "04-settings-this-vault");
        settingsScreen.Access.OpenFormCommand.Execute(null);
        WindowInput.Drain();
        Save(window, output, "04-settings-this-vault-change");
        settingsScreen.Access.CloseFormCommand.Execute(null);

        // Agents › What agents get: the username switch and each category's rule, an Allow waiting on its confirmation, and the
        // notice that the rules on file are ignored.
        shell.Current = Destinations.Of(DestinationKind.AgentActivity);
        WindowInput.Drain();
        var agentsScreen = Assert.IsType<AgentActivityViewModel>(shell.Content);
        agentsScreen.AddMcpManuallyCommand.Execute(null);
        WindowInput.Drain();
        Save(window, output, "02-agents-connections-manual");
        agentsScreen.Choose(agentsScreen.Rows.Single(row => row.IsEveryOther));
        WindowInput.Drain();
        Save(window, output, "02-agents-tab");
        var rules = agentsScreen.RulesForEveryAgent!;
        var card = window.GetVisualDescendants().OfType<StackPanel>().First(panel => panel.Name == "AgentRulesCard");
        scroller = card.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, scroller.Offset.Y + card.TranslatePoint(default, scroller)!.Value.Y - 24);
        WindowInput.Drain();
        Save(window, output, "04-settings-agent-rules");
        rules.ChooseCommand.Press(rules.Rows.Single(row => row.Category == Core.Agents.FieldCategory.Password).Choices.Single(choice => choice.Rule == Core.Agents.CategoryRule.Allow));
        WindowInput.Drain();
        Save(window, output, "04-settings-agent-rules-allow");
        rules.CancelAllowCommand.Press();
        SetDrawnState(rules, nameof(rules.Ignored), "Agent rules were changed outside keypaste and are ignored until you set them again. Until then every value is asked about and no agent may run commands.");
        WindowInput.Drain();
        Save(window, output, "04-settings-agent-rules-ignored");

        window.Close();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(20, deadline.Token);
        }
    }

    private static async Task<Core.Ipc.ApproverClient> AgentAsync(AuthorityStatus.Serving serving, string vault, Core.Ipc.AttachClient identity)
    {
        var client = await Core.Ipc.ApproverClient.TryConnectAsync(serving.Endpoint, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new Core.Ipc.AttachRequest(vault) { Client = identity }, CancellationToken.None))!.Attached);
        return client;
    }

    private static async Task AskAsync(Core.Ipc.ApproverClient client, AuthorityStatus.Serving serving, string vault, string label, string entry, string field)
    {
        var reply = await client.RequestAsync(
            new Core.Ipc.CredentialRequest
            {
                Entry = entry,
                Field = field,
                Reason = "run the api tests",
                Exposure = ["env/**"],
                ClientName = label,
                ClientLabel = label,
                Vault = vault,
                Session = serving.Session,
            },
            CancellationToken.None);
        Assert.NotNull(reply?.Value);
    }

    /// <summary>Allows every request but one, which it leaves waiting until the session ends.</summary>
    private sealed class Allowing : IApprovalChannel
    {
        internal const string HeldEntry = "env/acme-web/.env";
        internal const string HeldField = "VERCEL_TOKEN";

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            if (prompt.Entry == HeldEntry && prompt.Field == HeldField)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return ApprovalAnswer.ApprovedOnce;
        }
    }

    /// <summary>The Secrets screen in its states: a login, a project's home entry, its menu, the new-entry form, history, and the default window.</summary>
    private static void DrawSecrets(DemoVault demo, string output)
    {
        using (var vault = Vault.Open(demo.Path, _master))
        {
            vault.UpdateEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "demo-gh-rotated-9Qk", Url = "https://github.com", Notes = "Recovery codes are in the safe.", GroupPath = "Work" });

            vault.Save();
        }

        // A live authority and this vault's audit lines, so the rows' dots and the Agent access card
        // are read the way the app reads them.
        var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000 // The authority owns and disposes its session.
        using var authority = new AppAuthority(new AppVaultSession(clock, home: demo.Home), null, () => new Session.NobodyToAsk());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(demo.Path, master.Value));
        }

        var vaultKey = authority.Session.Identity!.Key;
        Granted(demo.Home, clock, _apiHome, "claude-code", vaultKey, TimeSpan.FromMinutes(4));
        Granted(demo.Home, clock, _apiHome, "cursor", vaultKey, TimeSpan.FromMinutes(52));
        Granted(demo.Home, clock, _openAi, "cursor", vaultKey, TimeSpan.FromMinutes(70));
        Granted(demo.Home, clock, new EntryName("env/acme-api", ".env.staging"), "claude-code", vaultKey, TimeSpan.FromHours(3));
        Granted(demo.Home, clock, new EntryName("Work", "github"), "claude-code", vaultKey, TimeSpan.FromDays(2));

        using var shell = new ShellViewModel(authority.Session, demo.Home, authority, clipboard: new FakeClipboard(), clock: clock);
        var window = new MainWindow { Width = _width, Height = _height };
        var commands = new AppCommands(new AppCommandHost { Session = authority.Session, Shell = () => shell, Unlock = () => null });
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell, Commands = commands };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.Entries);
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);

        entries.Selected = entries.Rows.First(row => row.Title == "github");
        Save(window, output, "10-secrets-login");
        DrawRevealed(window, output, "CurrentPassword", "10b-secrets-login-revealed");

        entries.SelectedGroup = entries.Groups.First(group => group.Path == "env/acme-api");
        entries.Selected = entries.Rows.First(row => row.Name == _apiHome);
        Save(window, output, "11-secrets-home-entry");
        var fieldMenu = window.GetVisualDescendants().OfType<Button>().First(button => button.Name == "FieldMenu" && button.IsEffectivelyVisible);
        fieldMenu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Save(window, output, "11a-secrets-field-menu");
        var opened = (MenuFlyout)FlyoutBase.GetAttachedFlyout(fieldMenu)!;
        Save(TopLevel.GetTopLevel(opened.Items.OfType<MenuItem>().First())!, output, "11a-secrets-field-menu-popup");
        opened.Hide();
        WindowInput.Drain();

        WindowInput.Drain();
        window.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == "EntryMenu").IsChecked = true;
        Save(window, output, "12-secrets-menu");
        window.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == "EntryMenu").IsChecked = false;

        entries.SelectedGroup = entries.Groups[0];
        entries.BeginAddCommand.Press();
        Save(window, output, "13-secrets-new");
        var newItemWidth = window.Width;
        window.Width = 720 * InterfaceZoom.Factor;
        Save(window, output, "13d-secrets-new-720");
        window.Width = newItemWidth;
        entries.CancelAddCommand.Press();

        entries.Selected = entries.Rows.First(row => row.Title == "github");
        WindowInput.Drain();
        entries.Detail!.History.ToggleCommand.Press();
        WindowInput.Drain();
        entries.Detail.History.Selected = entries.Detail.History.Rows[0];
        Save(window, output, "14-secrets-history");
        entries.Detail.History.ToggleCommand.Press();

        window.Width = 1000;
        window.Height = 680;
        entries.Selected = entries.Rows.First(row => row.Name == _apiHome);
        Save(window, output, "15-secrets-1000");

        window.Width = 960;
        window.Height = 520;
        entries.Selected = entries.Rows.First(row => row.Title == "github");
        Save(window, output, "16-secrets-960");

        window.Width = 720;
        Save(window, output, "16b-secrets-720");

        shell.OpenSidebarOverlay();
        Save(window, output, "16c-secrets-720-sidebar-open");
        shell.CloseSidebarOverlay();

        shell.ToggleSidebar();
        Save(window, output, "16e-secrets-720-sidebar-hidden");
        shell.ToggleSidebar();

        window.Width = _width;
        window.Height = _height;
        DrawnFrame.Capture(window);
        shell.ToggleSidebar();
        Save(window, output, "16d-secrets-sidebar-hidden");
        shell.ToggleSidebar();

        entries.SelectedGroup = entries.Groups.First(group => group.Path == "Work");
        entries.Selected = entries.Rows.First(row => row.Title == "github");
        Save(window, output, "17-items-group");

        shell.Search = "git";
        Save(window, output, "17b-items-group-filter");

        shell.Search = "aws";
        Save(window, output, "17c-items-filter-hides-the-selected");

        shell.Search = "no-such-item";
        Save(window, output, "17d-items-group-no-match");
        shell.Search = string.Empty;

        entries.ShowEveryItem();
        shell.FocusSearch();
        WindowInput.Drain();
        DrawnFrame.Capture(window);
        AmberElements.AssertOne(window, null, "a focused search beside New");
        Save(window, output, "17e-items-search-focused");
        entries.Selected = entries.Rows.First(row => row.Title == "github");
        window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "Rows").ContainerFromIndex(0)!.Focus();
        WindowInput.Drain();

        entries.SelectedGroup = entries.Groups.First(group => group.Path == "Work");
        entries.Selected = entries.Rows.First(row => row.Title == "github");
        entries.Detail!.EditCommand.Press();
        entries.ShowEveryItem();
        Save(window, output, "17f-items-edit-names-its-group");
        entries.Detail.CancelCommand.Press();

        window.Width = _width;
        window.Height = _height;
        entries.Detail!.RotateCommand.Press();
        Save(window, output, "17-secrets-rotate");
        entries.Detail.CancelRotateCommand.Press();

        entries.DeleteCommand.Press();
        Save(window, output, "18-secrets-delete");
        entries.UndoCommand.Press();

        SetDrawnState(entries, nameof(EntriesViewModel.Error), "That item could not be saved. Unlock the vault, then try again.");
        Save(window, output, "18b-items-problem-note");
        SetDrawnState(entries, nameof(EntriesViewModel.Error), null);

        entries.OrganizeCommand.Press();
        Save(window, output, "19-secrets-organize");
        entries.CancelOrganizeCommand.Press();

        entries.Detail.EditCommand.Press();
        Save(window, output, "20-secrets-edit");
        entries.Detail.CancelCommand.Press();

        entries.Detail.Fields.Single(field => field.Name == "GITHUB_TOKEN").ReplaceCommand.Press();
        Save(window, output, "20c-secrets-replace-field");
        entries.Detail.CancelReplaceFieldCommand.Press();

        entries.Detail.Fields.Single(field => field.Name == "Region").RenameCommand.Press();
        Save(window, output, "20d-secrets-rename-field");
        entries.Detail.CancelRenameFieldCommand.Press();

        // V.12: Generate on the saved password opens Edit fields with one, and Generate password… stands on its own.
        entries.Detail.GeneratePasswordCommand.Press();
        Save(window, output, "20b-secrets-edit-generated");
        entries.Detail.CancelCommand.Press();

        shell.OpenGeneratorCommand.Press();
        Save(window, output, "13b-secrets-generate");
        DrawRevealed(window, output, "GeneratedValue", "13b-secrets-generate-revealed");
        shell.GeneratorDialog!.Generator.UseWords = true;
        Save(window, output, "13c-secrets-generate-words");
        shell.CloseGeneratorCommand.Press();

        // A filter that matches nothing.
        shell.Search = "no-such-secret";
        Save(window, output, "23-secrets-no-match");
        shell.Search = string.Empty;

        window.Close();
    }

    /// <summary>The lock screen's states and the KDBX import dialog over the shell.</summary>
    private static void DrawLockAndImport(DemoVault demo, string output)
    {
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        var picker = new FakeVaultFilePicker();

        using (var session = new AppVaultSession(new ManualClock(AppClock.Start)))
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
            SetDrawnState(unlock, nameof(unlock.Busy), true);
            Save(window, output, "82b-lock-creating");
            SetDrawnState(unlock, nameof(unlock.Busy), false);

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
            using var session = new AppVaultSession(new ManualClock(AppClock.Start));
            using var welcome = new UnlockViewModel(session, empty, picker, () => { });
            var window = Show(new UnlockView { DataContext = welcome });
            Save(window, output, "81-lock-welcome");
            window.Close();

            var personal = Path.Combine(empty, "personal.kdbx");
            File.WriteAllBytes(personal, [1]);
            var ini = Path.Combine(empty, "keepassxc.ini");
            File.WriteAllText(ini, $"[General]\nLastActiveDatabase={demo.Path.Replace('\\', '/')}\nLastDatabases={personal.Replace(@"\", @"\\", StringComparison.Ordinal)}\n");
            using var fromKeePassXc = new UnlockViewModel(session, empty, picker, () => { }, keePassXcConfigs: () => [ini]);
            window = Show(new UnlockView { DataContext = fromKeePassXc });
            Save(window, output, "81b-lock-welcome-keepassxc");
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

        using (var session = new AppVaultSession(new ManualClock(AppClock.Start)))
        {
            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
            }

            picker.ExistingPath = source;
            using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: picker);
            var window = Show(new ShellView { DataContext = shell });

            Wait(shell.ImportCommand.PressAsync());
            var import = shell.Import!;

            foreach (var c in "keepass")
            {
                import.TypePassword(c);
            }

            Save(window, output, "93-import-locked");
            SetDrawnState(import, nameof(import.Busy), true);
            Save(window, output, "93b-import-unlocking");
            SetDrawnState(import, nameof(import.Busy), false);

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
            import.ConfirmCommand.Press();
            Assert.Null(shell.Import);
            Save(window, output, "97-import-done");

            var damaged = Path.Combine(demo.Home, "notes.kdbx");
            File.WriteAllText(damaged, "not a vault");
            shell.OpenImport(damaged);
            Assert.True(shell.Import!.IsUnreadable);
            Save(window, output, "99-import-unreadable");
            shell.Import!.CancelCommand.Press();

            var export = Path.Combine(demo.Home, "bitwarden_export.json");
            File.WriteAllText(export, """
                {
                  "encrypted": false,
                  "folders": [ { "id": "folder-personal", "name": "Personal" } ],
                  "items": [
                    {
                      "id": "item-github", "type": 1, "name": "github",
                      "login": { "username": "maya@acme.dev", "password": "demo-bw-gh", "totp": "JBSWY3DPEHPK3PXP", "uris": [ { "match": null, "uri": "https://github.com" } ] }
                    },
                    {
                      "id": "item-fastmail", "folderId": "folder-personal", "type": 1, "name": "fastmail",
                      "fields": [ { "name": "Password", "value": "demo-bw-old", "type": 1, "linkedId": null } ],
                      "login": { "username": "maya@fastmail.com", "password": "demo-bw-mail" }
                    },
                    {
                      "id": "item-visa", "folderId": "folder-personal", "type": 3, "name": "Visa",
                      "card": { "cardholderName": "Maya Ortiz", "brand": "Visa", "number": "4111111111111111", "expMonth": "4", "expYear": "2030", "code": "123" }
                    },
                    { "id": "item-router", "type": 1, "name": "old router", "deletedDate": "2025-01-01T00:00:00.000Z", "login": { "password": "demo-bw-router" } }
                  ]
                }
                """);
            shell.OpenImport(export);
            var exported = shell.ExportImport!;
            Wait(exported.ReadAsync());
            exported.Into = "Work";
            Assert.True(exported.HasHeld);
            Save(window, output, "9a-export-preview");

            exported.ConfirmCommand.Press();
            Assert.True(exported.IsImported, exported.Message);
            Save(window, output, "9b-export-imported");
            exported.CancelCommand.Press();
            window.Close();
        }

        using (var session = new AppVaultSession(new ManualClock(AppClock.Start)))
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

        using (var session = new AppVaultSession(new ManualClock(AppClock.Start)))
        using (var unlock = new UnlockViewModel(session, demo.Home, picker, () => { }, lockedBy: VaultLockReason.Requested))
        {
            var window = Show(new UnlockView { DataContext = unlock });
            Save(window, output, "83-lock-recent");

            Assert.True(unlock.OffersRestore);
            unlock.StartRestoreCommand.Press();
            Save(window, output, "84-lock-restore");

            var restore = unlock.Restore!;
            SetDrawnState(restore, nameof(restore.Busy), true);
            Save(window, output, "84b-lock-restore-checking");
            SetDrawnState(restore, nameof(restore.Busy), false);

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
            WindowInput.Drain();
            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>The env release and run prompts beside the credential one, each at its own size.</summary>
    // A group that a vault with no Trash cannot delete, whose form offers only Cancel.
    private static void DrawGroupNoTrashCannotDelete(string output)
    {
        using var noTrash = new RenderedShell(seed: vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "box", GroupPath = "servers" });
            vault.SetRecyclesDeletedEntries(false);
        });
        var items = noTrash.Show<EntriesViewModel>(DestinationKind.Entries);
        items.SelectedGroup = items.Groups.Single(group => group.Path == "servers");
        items.BeginDeleteGroupCommand.Press();
        Save(noTrash.Window, output, "12-items-group-not-empty-no-trash");
    }

    private static void DrawSameNameItems(string output)
    {
        using var shell = new RenderedShell(seed: vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "Mail", Username = "alice@example.test", Password = "alice-password", Url = "https://mail.example.test", GroupPath = "services" });
            vault.AddEntry(new VaultEntry { Title = "Mail", Username = "bob@example.test", Password = "bob-password", Url = "https://mail.example.test", GroupPath = "services" });
            vault.AddEntry(new VaultEntry { Title = "Forum", Username = "carol@example.test", Password = "carol-password", Url = "https://forum.example.test", GroupPath = "services" });
            vault.SetRecyclesDeletedEntries(false);
        });
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        items.Selected = items.Rows.Single(row => row.Username.StartsWith("alice", StringComparison.Ordinal));
        Save(shell.Window, output, "12a-items-same-name");

        var (width, height) = (shell.Window.Width, shell.Window.Height);
        shell.Window.Width = 720 * InterfaceZoom.Factor;
        Save(shell.Window, output, "12b-items-same-name-720");
        shell.Window.Width = width;
        shell.Window.Height = height;

        items.DeleteCommand.Press();
        Save(shell.Window, output, "12c-items-same-name-delete");
        items.CancelDeleteCommand.Press();

        // The access card, with a release recorded under the name both items have.
        var released = EntryActivity.Build(
            [],
            string.Empty,
            ApproverActivity.None,
            [new ReleaseSeen("services/Mail", "claude-code", "always allowed", AppClock.Start.AddMinutes(-4))],
            AppClock.Start,
            [new AgentReach("claude-code", (_, _) => ExposureReach.Entry)]);
        items.Detail!.Apply(released);

        foreach (var row in items.Rows)
        {
            row.Apply(released, AppClock.Start);
        }

        shell.Window.Height = 1400;
        Save(shell.Window, output, "12d-items-same-name-agent-access");
        shell.Window.Height = height;

        DrawSameNameNothingTells(output);
        DrawSameNameElsewhere(output);
    }

    private static void DrawSameNameNothingTells(string output)
    {
        using var shell = new RenderedShell(seed: vault =>
        {
            vault.AddEntry(new VaultEntry { Title = "Mail", Password = "first-password", GroupPath = "services" });
            vault.AddEntry(new VaultEntry { Title = "Mail", Password = "second-password", GroupPath = "services" });
            vault.SetRecyclesDeletedEntries(false);
        });
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        items.Selected = items.Rows.First(row => row.Title == "Mail");
        Save(shell.Window, output, "12e-items-same-name-nothing-tells");

        var (width, height) = (shell.Window.Width, shell.Window.Height);
        shell.Window.Width = 720 * InterfaceZoom.Factor;
        Save(shell.Window, output, "12f-items-same-name-nothing-tells-720");
        shell.Window.Width = width;
        shell.Window.Height = height;

        items.DeleteCommand.Press();
        Save(shell.Window, output, "12g-items-same-name-nothing-tells-delete");
        items.CancelDeleteCommand.Press();
    }

    private static void DrawSameNameElsewhere(string output)
    {
        using var shell = new RenderedShell(seed: vault =>
        {
            const string Notes = "OPENAI_API_KEY=sk-abcdefghijklmnopqrstuvwxyz0123456789";
            vault.AddEntry(new VaultEntry { Title = "Mail", Username = "alice@example.test", Password = "alice-password", Notes = Notes, GroupPath = "services" });
            vault.AddEntry(new VaultEntry { Title = "Mail", Username = "bob@example.test", Password = "bob-password", Notes = Notes, GroupPath = "services" });
            vault.AddEntry(new VaultEntry { Title = "Forum", Username = "carol@example.test", Password = "carol-correct-horse-battery-staple", GroupPath = "services" });
            vault.AddEntry(new VaultEntry { Title = "Forum", Username = "dan@example.test", Password = "dan-correct-horse-battery-staple", GroupPath = "services" });
        });

        Wait(shell.Shell.Recommendations.Checking);
        shell.Show<RecommendationsViewModel>(DestinationKind.Recommendations);
        var settingsWidth = shell.Window.Width;
        shell.Window.Height = 1400;
        Save(shell.Window, output, "12h-recommendations-same-name");

        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        foreach (var row in items.Rows.Where(row => row.Title == "Forum").ToList())
        {
            items.Selected = row;
            items.DeleteCommand.Press();
        }

        var trash = shell.Show<TrashViewModel>(DestinationKind.Trash);
        Assert.Equal(2, trash.Rows.Count(row => row.Title == "Forum"));
        shell.Window.Width = settingsWidth;
        Save(shell.Window, output, "12i-trash-same-name");
    }

    private static void DrawTerms(string output)
    {
        var terms = new TermsWindow { DataContext = Terms.TermsOfUse.Carried };
        terms.Show();
        Save(terms, output, "00e-terms-window");
        terms.Close();

        var asked = new TermsAgreementWindow { DataContext = new FirstLaunchViewModel(Terms.TermsOfUse.Carried, () => { }) };
        asked.Show();
        Save(asked, output, "00h-settings-terms-asked");
        asked.Close();
    }

    private static void DrawPrompts(string output)
    {
        var directory = OperatingSystem.IsWindows() ? @"C:\Users\maya\acme\api" : "/home/maya/acme/api";
        var preview = new EnvPreview("acme-api", ["DATABASE_URL", "STRIPE_SECRET_KEY", "STRIPE_RESTRICTED_KEY_FOR_WEBHOOK_SIGNING_IN_PRODUCTION"]);
        var env = EnvReleasePrompt.For(preview, ["npm", "run", "migrate"], directory);
        DrawWindow(new EnvApprovalWindow(new EnvApprovalViewModel(env)), output, "91b-env-approval");

        var run = new RunPrompt
        {
            Client = "claude-code",
            Label = "work laptop",
            Reason = "Run the pending database migration for the api service before the deploy.",
            ReasonWasTruncated = false,
            ReasonWasAltered = false,
            Program = OperatingSystem.IsWindows() ? @"C:\Program Files\nodejs\npm.cmd" : "/usr/local/bin/npm",
            Command = "npm run migrate",
            Directory = directory,
            Project = "acme-api",
            Profile = "dev",
            Variables = [new("DATABASE_URL", "env/acme-api/.env", "DATABASE_URL"), new("STRIPE_SECRET_KEY", "env/acme-api/.env", "STRIPE_SECRET_KEY")],
        };
        DrawWindow(new RunApprovalWindow(new RunApprovalViewModel(run)), output, "91c-run-approval");
        var protectedPrompt = ApprovalPrompt.For(
            "claude-code",
            new EntryName("env/acme-api", ".env.prod"),
            "DATABASE_URL",
            "I need the \u202eproduction database URL to check the schema.",
            null);
        DrawWindow(new ApprovalWindow(new ApprovalViewModel(protectedPrompt)), output, "91d-approval-protected");
        var ordinary = ApprovalPrompt.For("claude-code", new EntryName("api", "Stripe"), "password", "Run the webhook tests against the dev Stripe account.", "work laptop");
        DrawWindow(new ApprovalWindow(new ApprovalViewModel(ordinary with { Label = null })), output, "91e3-approval-unnamed");

        var asked = ApprovalPrompt.For(
            "claude-code",
            new EntryName("env/acme-api", ".env"),
            "STRIPE_SECRET_KEY",
            "Run the webhook tests against the dev Stripe account.",
            "work laptop");
        DrawWindow(new ApprovalWindow(new ApprovalViewModel(asked)), output, "91e-approval");

        DrawWindow(
            new TerminalEditWindow(new TerminalEditViewModel(new TerminalEditPrompt
            {
                Command = "keypaste set",
                Item = "work/github",
                Changes = ["Replaces the password of work/github with the password the terminal sent; the old one stays in its history."],
            })),
            output,
            "91h-terminal-edit");

        DrawPickers(output);

        var arming = new ApprovalWindow(new ApprovalViewModel(asked));
        arming.Show();
        Save(arming, output, "91j-approval-arming");
        arming.Close();

        var locked = new LockedNoticeWindow("acme.kdbx", () => { }, new ManualClock(AppClock.Start));
        locked.Arm();
        locked.Show();
        Save(locked, output, "91i-locked-notice");
        locked.Close();
    }

    /// <summary>The picker armed with nothing picked, with an item picked, with a protected one picked, and with as many items as it offers.</summary>
    private static void DrawPickers(string output)
    {
        static ItemCandidate Candidate(int number, string username, string url, bool protectedItem) => new()
        {
            Name = new EntryName("work", "GitHub"),
            Item = new string((char)('a' + number), 32),
            Title = "GitHub",
            Group = "work",
            Username = username,
            Url = url,
        };

        var pair = ApprovalPrompt.For("claude-code", new EntryName("work", "GitHub"), "password", "Push the release branch to the company repository.", "work laptop") with
        {
            Candidates =
            [
                Candidate(0, "dana@acme.example", "https://github.com", protectedItem: false),
                Candidate(1, "dana-personal", "https://github.com/login", protectedItem: true),
            ],
        };
        var many = pair with
        {
            Candidates = [.. Enumerable.Range(0, ApprovalPrompt.MaximumCandidates).Select(number => Candidate(number, $"bot-{number + 1}", "https://github.com", protectedItem: false))],
        };

        foreach (var (prompt, pick, name) in new (ApprovalPrompt, int?, string)[]
        {
            (pair, null, "91k-picker-nothing-picked"),
            (pair, 0, "91k2-picker-picked"),
            (pair, 1, "91k3-picker-protected-picked"),
            (many, 3, "91k4-picker-most-items"),
        })
        {
            var picker = new ItemPickViewModel(prompt);
            var window = new ItemPickWindow(picker);
            picker.Tick(TimeSpan.FromSeconds(28));
            picker.Arm();
            picker.Picked = pick is { } index ? picker.Rows[index] : null;
            window.Show();
            Save(window, output, name);
            window.Close();
        }
    }

    private static void DrawWindow(PromptWindow window, string output, string name)
    {
        var prompt = (PromptViewModel)window.DataContext!;
        prompt.Tick(TimeSpan.FromSeconds(28));
        prompt.Arm();
        window.Show();
        Save(window, output, name);
        window.Close();
    }

    /// <summary>The trash with two deleted entries in it; drawn last, because it changes the demo vault.</summary>
    private static void DrawTrashWithRows(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        session.Unlocked!.RemoveEntry(new EntryName("Personal", "home wifi"));
        session.Unlocked.RemoveEntry(_openAi);
        session.Unlocked.Save();

        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start));
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        // Settings whole, tall enough that nothing is below the fold, once the unlock's recommendations are in.
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!shell.Recommendations.Checking.IsCompleted && DateTime.UtcNow < until)
        {
            WindowInput.Drain();
            Thread.Sleep(1);
        }

        window.Height = 2200;
        shell.Current = Destinations.Of(DestinationKind.Settings);
        Save(window, output, "95-settings-whole");
        var settingsWidth = window.Width;
        window.Width = 720;
        Save(window, output, "95a-settings-720");
        shell.Current = Destinations.Of(DestinationKind.Recommendations);
        Save(window, output, "96a-recommendations-720");
        window.Width = settingsWidth;
        Save(window, output, "96-recommendations");
        window.Height = _height;

        shell.Current = Destinations.Of(DestinationKind.Diagnostics);
        Save(window, output, "95b-settings-diagnostics");

        shell.Current = Destinations.Of(DestinationKind.Trash);
        var trash = (TrashViewModel)shell.Content!;
        Save(window, output, "93a-trash-no-selection");
        trash.Selected = trash.Rows[0];
        Save(window, output, "93-trash-rows");
        SaveSorted(window, shell, "93-trash-rows", output);

        trash.PurgeCommand.Press();
        Save(window, output, "94-trash-confirm");

        window.Close();
    }

    private static void DrawUnlock(DemoVault demo, string output)
    {
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(demo.Home),
            [new Core.Recent.RecentVault(demo.Path, DateTimeOffset.UtcNow)]);

        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using var unlock = new UnlockViewModel(session, demo.Home, new FakeVaultFilePicker(), () => { });
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = unlock };
        window.Show();
        Save(window, output, "00-unlock");

        window.Close();
        DrawFirstRun(session, demo, output);
    }

    /// <summary>The first run: the welcome with nothing found and with KeePassXC's databases, and a locked vault alone, with recent vaults and with Windows Hello, after the terms screen at the default window, then the terms window its link opens.</summary>
    private static void DrawFirstRun(AppVaultSession session, DemoVault demo, string output)
    {
        using var empty = new TempHome();
        var database = Path.Combine(empty.Path, "personal.kdbx");
        using (var kept = Vault.Create(database, "first-run-demo"))
        {
            kept.Save();
        }

        var ini = Path.Combine(empty.Path, "keepassxc.ini");
        File.WriteAllText(ini, $"[General]\nLastActiveDatabase={database.Replace('\\', '/')}\n");

        using var nothing = new UnlockViewModel(session, empty.Path, new FakeVaultFilePicker(), () => { });
        using var fromKeePassXc = new UnlockViewModel(session, empty.Path, new FakeVaultFilePicker(), () => { }, keePassXcConfigs: () => [ini]);
        using var locked = new UnlockViewModel(session, demo.Home, new FakeVaultFilePicker(), () => { });

        using var withRecents = new TempVault();
        var others = Enumerable.Range(1, 5).Select(number => Path.Combine(withRecents.Home, $"vault-{number}.kdbx")).ToList();
        others.ForEach(other => File.Copy(withRecents.Path_, other));
        Core.Recent.RecentVaults.Save(
            Core.Audit.KeypasteHome.RecentPath(withRecents.Home),
            [new Core.Recent.RecentVault(withRecents.Path_, DateTimeOffset.UtcNow), .. others.Select(other => new Core.Recent.RecentVault(other, DateTimeOffset.UtcNow.AddDays(-1)))]);
        using var recents = new UnlockViewModel(session, withRecents.Home, new FakeVaultFilePicker(), () => { });

        using var helloSession = new AppVaultSession(new ManualClock(AppClock.Start), home: withRecents.Home, hardwareKeys: new SoftwareYubiKey(new byte[20]), quickUnlock: new FakeQuickUnlockSigner());

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, helloSession.TryUnlock(withRecents.Path_, master.Value));
        }

        Assert.Equal(QuickUnlockOutcome.Done, helloSession.KeepForQuickUnlock());
        helloSession.Lock(VaultLockReason.Manual);
        using var hello = new UnlockViewModel(helloSession, withRecents.Home, new FakeVaultFilePicker(), () => { }, lockedBy: VaultLockReason.Manual);

        var firstLaunch = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
        firstLaunch.FindControl<ContentControl>("Root")!.Content = new FirstLaunchView { DataContext = new FirstLaunchViewModel(Terms.TermsOfUse.Carried, () => { }) };
        firstLaunch.Show();
        Save(firstLaunch, output, "00-first-launch");
        firstLaunch.Width = 720;
        Save(firstLaunch, output, "00-first-launch-720");
        firstLaunch.Close();

        foreach (var (name, model) in new[]
        {
            ("00a-first-run-welcome", nothing),
            ("00b-first-run-keepassxc", fromKeePassXc),
            ("00c-first-run-locked", locked),
            ("00f-first-run-recents", recents),
            ("00g-first-run-windows-hello", hello),
        })
        {
            var window = new MainWindow { Width = WindowChrome.DefaultWidth, Height = WindowChrome.DefaultHeight };
            window.FindControl<ContentControl>("Root")!.Content = new UnlockView { DataContext = model };
            window.Show();
            Save(window, output, name);

            // Tall enough for the whole of it, however far the zoom scrolls it.
            window.Width = 720;
            window.Height = 2600;
            Save(window, output, name + "-720-whole");
            window.Close();
        }

        DrawTerms(output);

        // Settings' plain switch, and a waiting report's Delete asking first.
        var preferences = new DesktopPreferences(empty.Path);
        using var crashes = new CrashReporting(empty.Path, preferences, endpoint: null);
        crashes.Agree();
        crashes.Crash(new InvalidOperationException("demo"));
        using var settings = new SettingsViewModel(session, empty.Path, preferences, _ => { }) { Crashes = crashes };
        var settingsWindow = new MainWindow { Width = _width, Height = 2600 };
        settingsWindow.FindControl<ContentControl>("Root")!.Content = new SettingsView { DataContext = settings };
        settingsWindow.Show();
        Save(settingsWindow, output, "00d-settings-crash-switch-whole");
        var diagnostics = new DiagnosticsViewModel(session, empty.Path, crashes);
        settingsWindow.FindControl<ContentControl>("Root")!.Content = new DiagnosticsView { DataContext = diagnostics };
        settingsWindow.Height = _height;
        Save(settingsWindow, output, "95c-diagnostics-report");
        diagnostics.Reports[0].AskDeleteCommand.Press();
        Save(settingsWindow, output, "95c-diagnostics-report-delete");
        settingsWindow.Close();
    }

    private static void DrawShell(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        var clock = new ManualClock(AppClock.Start);
        using var shares = DemoShares(session.Unlocked!, clock);
        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: clock, picker: new FakeVaultFilePicker()) { ShareTransport = shares };
        var window = new MainWindow { Width = _width, Height = _height };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        foreach (var destination in Destinations.All)
        {
            shell.Current = destination;

            if (shell.Content is SharingViewModel sharing)
            {
                DrawSharing(window, output, shell, sharing, shares, ScreenName(destination));
                continue;
            }

            if (shell.Content is EntriesViewModel entries)
            {
                entries.Selected = entries.Rows.FirstOrDefault(row => row.Title == "github");
            }

            if (shell.Content is EnvSetsViewModel env)
            {
                env.OpenCommand.Press("acme-api");
            }

            Save(window, output, ScreenName(destination));
        }

        window.Width = 720;
        window.Height = 520;

        foreach (var destination in Destinations.All)
        {
            shell.Current = destination;

            if (shell.Content is EnvSetsViewModel env)
            {
                env.OpenCommand.Press("acme-api");
            }

            if (shell.Content is not SharingViewModel)
            {
                Save(window, output, $"{ScreenName(destination)}-rail");
                SaveSorted(window, shell, ScreenName(destination), output);
            }
        }

        window.Width = _width;
        window.Height = _height;

        DrawVaultStates(window, shell, output);
        DrawActivityStates(shell, window, demo.Home, output);

        shell.Current = Destinations.Of(DestinationKind.Entries);
        shell.ShowToast("Copied the username", ToastTone.Success);
        Save(window, output, "90-toast");

        window.Close();
    }

    /// <summary>
    /// The Activity screen under each filter, with the chain's verdict open, at the window's narrowest, and after a
    /// careless edit; then a machine with no log yet, and a filter nothing matches.
    /// </summary>
    /// <remarks>The edit comes last, because it leaves the demo log broken.</remarks>
    private static void DrawActivityStates(ShellViewModel shell, Window window, string home, string output)
    {
        shell.Current = Destinations.Of(DestinationKind.Log);
        var log = (LogViewModel)shell.Content!;

        log.Filter = log.Filters.Single(option => option.Filter == LogFilter.Denied);
        Save(window, output, "04-settings-activity-log-denied");

        log.Filter = log.Filters.Single(option => option.Filter == LogFilter.You);
        Save(window, output, "04-settings-activity-log-you");

        log.Filter = log.Filters[0];
        log.VerifyCommand.Press();
        Save(window, output, "04-settings-activity-log-verify");
        log.VerifyCommand.Press();

        window.Width = 960;
        Save(window, output, "04-settings-activity-log-narrow");
        window.Width = 720;
        SaveSorted(window, shell, "04-settings-activity-log", output);
        window.Width = _width;

        var path = KeypasteHome.AuditPath(home);
        var lines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var denied = Array.FindIndex(lines, line => line.Contains("the person denied it", StringComparison.Ordinal));
        lines[denied] = lines[denied].Replace("the person denied it", "the person okayed it", StringComparison.Ordinal);
        File.WriteAllText(path, string.Join('\n', lines) + '\n');
        log.Refresh();
        Save(window, output, "04-settings-activity-log-broken");

        log.VerifyCommand.Press();
        Save(window, output, "04-settings-activity-log-broken-verify");

        var fresh = Directory.CreateTempSubdirectory("keypaste-screens-empty-").FullName;
        try
        {
            DrawLogAlone(new LogViewModel(fresh, new ManualClock(AppClock.Start)), output, "04-settings-activity-log-empty");

            var clock = new ManualClock(new DateTimeOffset(2026, 7, 28, 8, 1, 53, TimeSpan.Zero));
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(fresh), clock, out var agentsOnly, out var error), error);
            using (agentsOnly)
            {
                Assert.True(agentsOnly.TryAppend(
                    new AuditRecord
                    {
                        Tool = "list_entry_names",
                        Client = new AuditClient("claude-code", "1.0.43", null),
                        Decision = AuditDecision.Granted,
                        Method = AuditMethod.Exposure,
                        Reason = "listed 3 item names",
                    },
                    out var failure), failure);
            }

            var agents = new LogViewModel(fresh, new ManualClock(AppClock.Start));
            agents.Filter = agents.Filters.Single(option => option.Filter == LogFilter.You);
            DrawLogAlone(agents, output, "04-settings-activity-log-you-empty");
        }
        finally
        {
            Directory.Delete(fresh, recursive: true);
        }
    }

    /// <summary>The Activity view on its own, the width of the shell's content area, for a state the demo home cannot reach.</summary>
    private static void DrawLogAlone(LogViewModel model, string output, string name)
    {
        var window = new Window { Width = (_width - 232) * InterfaceZoom.Factor, Height = 520 * InterfaceZoom.Factor, Content = new ZoomFrame(new LogView { DataContext = model }) };
        window.Show();
        Save(window, output, name);
        window.Close();
    }

    /// <summary>A project in its own shell, with a file picker so Import and Export draw enabled.</summary>
    private static void DrawEnv(DemoVault demo, string output)
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(demo.Path, master.Value));
        }

        using var shell = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var window = new MainWindow { Width = _width, Height = 1000 };
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();

        shell.Current = Destinations.Of(DestinationKind.EnvSets);
        var project = Assert.IsType<EnvSetsViewModel>(shell.Content).OpenProject!;
        project.SelectedProfile = "staging";
        Save(window, output, "40-env-staging");

        Revealing(window, "REDIS_URL", () => Save(window, output, "42-env-revealed"));

        window.Width = 960;
        Save(window, output, "44-env-narrow");
        Revealing(window, "DATABASE_URL", () => Save(window, output, "45-env-narrow-revealed"));
        window.Width = _width;

        project.BeginAddCommand.Press();
        Save(window, output, "41-env-add");
        project.ConfirmAddCommand.Press();
        Save(window, output, "41c-env-key-needs-a-name");
        window.Width = 720 * InterfaceZoom.Factor;
        Save(window, output, "41b-env-add-720");
        window.Width = _width;
        project.CancelAddCommand.Press();

        project.Environments[0].AddEntry.Press();
        project.ChosenEntry = project.EntryCandidates[0];
        Save(window, output, "46-env-add-entry");
        project.CancelAddEntryCommand.Press();

        project.BeginReplace(project.Variables.First(row => row.Key == "DATABASE_URL"));
        Save(window, output, "46b-env-replace");
        project.CancelReplaceCommand.Press();

        project.Environments.Single(environment => environment.Name == "staging").Members[^1].Remove.Press();
        Save(window, output, "47-env-remove-entry");
        project.CancelTagChangeCommand.Press();

        // A tag KeePassXC or a phone app wrote with a capital, left unsaved.
        session.Unlocked!.AddTag(project.Environments[0].Members[0].Entry, "env:acme-api:Prod");
        var screen = Assert.IsType<EnvSetsViewModel>(shell.Content);
        screen.Reload();
        Save(window, output, "48-env-ignored-tag");

        // A copy of keypaste that does not run as keypaste.app on macOS, whose caption says why Run is off.
        using (var copyOnly = new ShellViewModel(session, demo.Home, null, clipboard: new FakeClipboard(), clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker())
        {
            Launching = new ProjectLaunching(new TerminalLaunch(launcher: null), new DetachedProcessLauncher(), EnvironmentMerge.Inherited),
        })
        {
            window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = copyOnly };
            copyOnly.Current = Destinations.Of(DestinationKind.EnvSets);
            ((EnvSetsViewModel)copyOnly.Content!).OpenCommand.Press("acme-api");
            WindowInput.Drain();
            window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("caption") && text.IsEffectivelyVisible && text.Text?.Contains("Terminal", StringComparison.OrdinalIgnoreCase) == true).BringIntoView();
            Save(window, output, "49-env-terminal-unavailable");
        }

        window.Close();

        static void Revealing(Window window, string key, Action draw)
        {
            var cell = window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.DataContext is EnvVariableRow row && row.Key == key);
            cell.BeginReveal();
            draw();
            cell.EndReveal();
        }
    }

    /// <summary>A value shown in its cell, with the toggle saying Hide, then hidden again.</summary>
    private static void DrawRevealed(Window window, string output, string cellName, string frame)
    {
        var cell = window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.Name == cellName);
        cell.BeginReveal();
        Save(window, output, frame);
        cell.EndReveal();
    }

    /// <summary>
    /// Four links as the vault would remember them, one per status, and a share server that knows
    /// the two still open: one untouched, one opened once.
    /// </summary>
    private static FakeShareServer DemoShares(Vault vault, ManualClock clock)
    {
        var now = clock.GetUtcNow();
        var server = new FakeShareServer { Now = now };
        var store = new Core.Sharing.ShareStore(vault);

        void Add(string what, string field, string? to, TimeSpan ttl, TimeSpan age, int views, bool passphrase, int? viewsLeft)
        {
            var id = System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            var created = now - age;
            store.Add(new Core.Sharing.ShareInfo(id, what, field, to, created, created + ttl, views, passphrase, "https://keypaste.com"), "demo-revoke-" + id);

            if (viewsLeft is { } left)
            {
                server.Shares[id] = new FakeShareServer.Share("{}", left, created + ttl, FakeShareServer.Sha256("demo-revoke-" + id));
            }
        }

        Add("env/acme-api/.env", "STRIPE_SECRET_KEY", "maya@acme.dev", TimeSpan.FromHours(24), TimeSpan.FromHours(2), 1, true, viewsLeft: null);
        Add("Work/aws-console", "login", null, TimeSpan.FromDays(7), TimeSpan.FromDays(1), 3, true, viewsLeft: 2);
        Add("Personal/home wifi", "password", "sam", TimeSpan.FromHours(24), TimeSpan.FromMinutes(20), 1, false, viewsLeft: 1);
        Add("Work/github", "password", "jordan@acme.dev", TimeSpan.FromHours(1), TimeSpan.FromHours(3), 1, false, viewsLeft: null);
        vault.Save();

        return server;
    }

    /// <summary>The Sharing screen as it opens, with statuses not yet asked for; then checked with the form filled; then once the server answers that it takes no shares.</summary>
    /// <summary>Settings › Advanced's share links, then Share… from an item, filled in and then refused by the server.</summary>
    private static void DrawSharing(Window window, string output, ShellViewModel shell, SharingViewModel sharing, FakeShareServer shares, string name)
    {
        var opened = DateTime.UtcNow;
        while (sharing.IsEmpty && DateTime.UtcNow - opened < TimeSpan.FromSeconds(5))
        {
            WindowInput.Drain();
            Thread.Sleep(1);
        }

        Save(window, output, name + "-unchecked");

        var checking = new TaskCompletionSource();
        shares.Held = checking.Task;
        var refreshed = sharing.RefreshCommand.PressAsync();
        Save(window, output, name + "-checking");
        checking.SetResult();
        Wait(refreshed);
        shares.Held = null;
        Save(window, output, name);

        sharing.RevokeRowCommand.Press(sharing.Rows.First(row => row.Opens));
        Save(window, output, name + "-revoke");
        var wideForRevoke = window.Width;
        window.Width = 720;
        Save(window, output, name + "-revoke-720");
        window.Width = wideForRevoke;
        sharing.CancelRevokeCommand.Press();

        shell.Current = Destinations.Of(DestinationKind.Entries);
        shell.ShareCommand.Press(new EntryName("Work", "aws-console"));
        var dialog = shell.Share!;
        dialog.Recipient = "sam@acme.dev";
        dialog.RequirePassphrase = true;
        Save(window, output, "12-items-share");

        shares.Answer = _ => FakeShareServer.Json(System.Net.HttpStatusCode.NotFound, "{\"error\":\"not found\"}");
        dialog.RequirePassphrase = false;
        var creating = new TaskCompletionSource();
        shares.Held = creating.Task;
        var created = dialog.CreateCommand.PressAsync();
        Save(window, output, "12-items-share-creating");
        creating.SetResult();
        Wait(created);
        shares.Held = null;
        Save(window, output, "12-items-share-unavailable");
        shares.Answer = null;
        dialog.CancelCommand.Press();
    }

    /// <summary>A place is named by its digit; a screen under one by its place's digit and both names.</summary>
    private static string ScreenName(Destination destination) =>
        destination.IsPlace
            ? $"{destination.Shortcut:00}-{Slug(destination.Title)}"
            : $"{Destinations.PlaceOf(destination).Shortcut:00}-{Slug(Destinations.PlaceOf(destination).Title)}-{Slug(destination.Title)}";

    private static void DrawApproval(string output, string name = "91-approval")
    {
        var prompt = ApprovalPrompt.For(
            "claude-code",
            _apiHome,
            "DATABASE_URL",
            "Run the database migration for the api service.",
            "work laptop");
        var model = new ApprovalViewModel(prompt);
        model.Tick(TimeSpan.FromSeconds(28));
        model.Arm();
        var window = new ApprovalWindow(model);
        window.Show();
        Save(window, output, name);
        window.Close();
    }

    /// <summary>Appends a granted credential line written <paramref name="ago"/> before now, as a bridge wrote it then.</summary>
    private static void Granted(string home, ManualClock clock, EntryName entry, string client, string vaultKey, TimeSpan ago)
    {
        var then = new ManualClock(clock.GetUtcNow() - ago);
        Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(home), then, out var log, out var error), error);

        using (log)
        {
            Assert.True(log.TryAppend(
                new AuditRecord
                {
                    Tool = "request_credential",
                    Client = new AuditClient(client, null, null),
                    Args = AuditArgs.ForCredentialRequest(EntryActivity.KeyOf(entry), "password", "demo"),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = "a person approved this request",
                    Exposure = ["**"],
                    Vault = vaultKey,
                },
                out var failure), failure);
        }
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
            Row(new BrandWordmark { Width = 104 }, new BrandMark { Width = 56 }, new BrandMark { Width = 22, Classes = { "mono" } }),
            Row(Text("Agents", "h1"), Text("New share link", "dialog-title"), Text("All items", "list-title"), Text("Always allowed", "dialog-title")),
            Row(Text("AI tools and tokens that can ask for secrets.", "subtitle"), Text("Value", "label"), Text("TIME   ACTOR   RESULT", "th"), Text("kp://acme-api/dev/DATABASE_URL", "mono")),
            Row(Button("Connect", ""), Button("Allow once", "primary"), Button("Deny", "ghost"), Button("End", "danger"), Button("New token", "link"), Button("Disabled", "primary", enabled: false), Button("Small", "sm"), Button("Large", "lg primary"), new Button { Classes = { "icon" }, Content = new KpIcon { Icon = "ellipsis", Size = 14 } }),
            Row(new TextBox { PlaceholderText = "Filter 7 secrets", Width = 260 }, focused, new TextBox { PlaceholderText = "Search secrets", Width = 300, Classes = { "search" } }),
            Row(new ComboBox { ItemsSource = new[] { "As the vault's settings say", "Ask every time", "No values, only commands" }, SelectedIndex = 1, Width = 240 }, new CheckBox { Content = "Require a passphrase", IsChecked = true }, new CheckBox { Content = "Unchecked" }, new ToggleSwitch { IsChecked = true, Content = "Keep editing in place" }, new ToggleSwitch { IsChecked = false }),
            Row(segments, Box("chip", Text("dev", "")), Box("chip outline", Text("inject-only", "")), Box("chip amber", Text("approval required", "")), Box("keycap", Text("Ctrl K", ""))),
            Row(Box("dot ok"), Text("Allowed", "ok medium"), Box("dot danger"), Text("Denied", "danger medium"), Box("dot info"), Text("Token", "info medium"), Box("dot idle"), Text("Idle", "muted"), Box("dot waiting"), Text("0:28", "mono size-12 amber"), new ProgressBar { Classes = { "thin" }, Value = 70, Width = 150 }),
            Row(
                new Border { Classes = { "card" }, Width = 260, Child = Text("card: hairline, no fill", "") },
                new Border { Classes = { "card", "filled" }, Width = 260, Child = Text("card filled", "") },
                new Border { Classes = { "terminal" }, Width = 300, Padding = new Thickness(12, 10), Child = Text("› keypaste run -p dev -- npm start", "") },
                new Border { Classes = { "field" }, Width = 300, Child = Text("kp://acme-api/dev/STRIPE_SECRET_KEY", "mono secondary") }),
            Row(
                new Border { Classes = { "dialog" }, Width = 360, Child = new StackPanel { Spacing = 8, Children = { Text("claude-code wants 2 secrets", "dialog-title"), Text("via MCP · ~/acme/api · environment dev", "subtitle") } } },
                new Border { Classes = { "toast" }, Child = Row(new KpIcon { Icon = "circle-check", Size = 15, Foreground = (IBrush)Application.Current.FindResource(Application.Current.ActualThemeVariant, "KpOk")! }, Text("Allowed claude-code for 1 hour", "")) }),
            icons,
        ]);

        var window = new Window { Width = _width * InterfaceZoom.Factor, Height = 900 * InterfaceZoom.Factor, Content = new ZoomFrame(new ScrollViewer { Content = page }) };
        window.Show();
        focused.Focus();
        Save(window, output, "92-components", sheet: true);
        window.Close();
    }

    /// <summary>A table sorted by its second head with its first edge moved, narrow and then wide, then with the head and the edge in focus, then by its last head, then with every edge at its least.</summary>
    private static void SaveSorted(Window window, ShellViewModel shell, string screen, string output)
    {
        if (shell.TableInView is not { } table)
        {
            return;
        }

        var heads = window.GetVisualDescendants().OfType<ColumnHead>().Where(head => head.IsEffectivelyVisible).ToList();

        if (heads.Count == 0)
        {
            return;
        }

        var grip = window.GetVisualDescendants().OfType<ColumnGrip>().First(candidate => candidate.IsEffectivelyVisible);
        var widths = grip.FindAncestorOfType<TableColumns>()!.DrawnWidths;

        table.Press(heads[1].Column);
        table.Resize(grip.Column, 24, widths);
        Save(window, output, $"{screen}-sorted-rail");

        var width = window.Width;
        window.Width = _width;
        Save(window, output, $"{screen}-sorted");
        heads[1].Focus(NavigationMethod.Tab);
        Save(window, output, $"{screen}-sorted-head-focused");
        grip.Focus(NavigationMethod.Tab);
        Save(window, output, $"{screen}-sorted-grip-focused");

        table.Reset();
        heads[0].Focus(NavigationMethod.Pointer);
        WindowInput.Drain();

        var showing = window.GetVisualDescendants().OfType<ColumnHead>().Where(head => head.IsEffectivelyVisible).ToList();
        table.Press(showing[^1].Column);
        Save(window, output, $"{screen}-sorted-by-last");
        table.Reset();

        // Each edge in turn, so the columns right of it have room left to give.
        table.Press(showing[1].Column);
        foreach (var edge in window.GetVisualDescendants().OfType<ColumnGrip>().Where(candidate => candidate.IsEffectivelyVisible).ToList())
        {
            table.Resize(edge.Column, -_width, edge.FindAncestorOfType<TableColumns>()!.DrawnWidths);
            WindowInput.Drain();
        }

        Save(window, output, $"{screen}-least");

        window.Width = width;
        table.Reset();
    }

    /// <param name="window">The window to draw.</param>
    /// <param name="output">The folder.</param>
    /// <param name="name">The file's name.</param>
    /// <param name="sheet">Whether the frame is a sheet of components side by side rather than one view, which the amber rule does not apply to.</param>
    private static void Save(TopLevel window, string output, string name, bool sheet = false)
    {
        WindowInput.Drain();

        // Enough render ticks for every 120-200ms transition to finish, so no frame is mid-fade.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        WindowInput.Drain();
        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
        }

        if (!sheet)
        {
            AmberElements.AssertAtMostOne(window, $"{Path.GetFileName(output)}/{name}");
            _cutOff?.AddRange(ReachableControls.Problems(window).Concat(LabelledFields.Problems(window)).Select(problem => $"{name}: {problem}"));
        }
    }

    // A wait or an error a frame cannot hold still by running it: drawn from the state its view reads.
    private static void SetDrawnState(object model, string property, object? value) =>
        model.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, value);

    private static void DrawCheckWorking(Window window, string output, ConnectClientViewModel pane)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var starting = typeof(ConnectClientViewModel).GetMethod("SetStartingCheck", Private)!;
        var setCheck = typeof(ConnectClientViewModel).GetMethod("SetCheck", Private)!;

        SetDrawnState(pane, nameof(pane.CheckMessage), "Starting keypaste mcp as Cursor would…");
        starting.Invoke(pane, [true]);
        Save(window, output, "24b-agents-connect-checking");
        starting.Invoke(pane, [false]);

        // Not a bridge that could run: the view reads only that a check is held, and it is let go below without being disposed.
        typeof(ConnectClientViewModel).GetField("_checkEntries", Private)!.SetValue(pane, new McpListedEntry[] { new("1", "env/ci/DEPLOY_KEY", "value"), new("2", "env/ci/OTHER", "value") });
        setCheck.Invoke(pane, [RuntimeHelpers.GetUninitializedObject(typeof(McpConnectionCheck))]);
        SetDrawnState(pane, nameof(pane.CheckMessage), "keypaste mcp lists 2 items. Pick the one to ask for.");
        Save(window, output, "24c-agents-connect-picking");
        typeof(ConnectClientViewModel).GetField("_checkEntries", Private)!.SetValue(pane, Array.Empty<McpListedEntry>());
        setCheck.Invoke(pane, [null]);
        SetDrawnState(pane, nameof(pane.CheckMessage), string.Empty);
    }

    private static string Slug(string title) => title.ToLowerInvariant().Replace(' ', '-');

    /// <summary>A vault that looks lived in: logins in groups, and projects whose keys are fields of tagged entries.</summary>
    private sealed class DemoVault : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-screens-").FullName;

        internal DemoVault()
        {
            Path = System.IO.Path.Combine(_directory, "acme.kdbx");

            using var vault = Vault.Create(Path, _master);
            vault.AddEntry(new VaultEntry { Title = "github", Username = "maya@acme.dev", Password = "demo-gh-7Hq2x", Url = "https://github.com", GroupPath = "Work" });
            vault.SetFields(new EntryName("Work", "github"), [new FieldWrite("GITHUB_TOKEN", "demo-ghp-4f2a9c"), new FieldWrite("Region", "eu-west-1", Protect: false)]);
            vault.AddTag(new EntryName("Work", "github"), "env:acme-api:prod");
            vault.AddTag(new EntryName("Work", "github"), "work");
            vault.AddEntry(new VaultEntry { Title = "aws-console", Username = "maya.ortiz", Password = "demo-aws-51Nf", Url = "https://console.aws.amazon.com", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "linear", Username = "maya@acme.dev", Password = "demo-lin-a91K", Url = "https://linear.app", GroupPath = "Work", Notes = "Workspace admin.\nLINEAR_API_KEY=demo-lin-api-5c2e" });
            vault.AddEntry(new VaultEntry { Title = "gmail", Username = "maya.ortiz@gmail.com", Password = "demo-gm-3a11", Url = "https://mail.google.com", GroupPath = "Personal" });
            vault.AddEntry(new VaultEntry { Title = "home wifi", Password = "demo-wifi-f3a9", Notes = "Router in the hall cupboard.", GroupPath = "Personal" });

            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "OPENAI_API_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                ProjectVariables.Set(vault, "acme-api", key, "demo-" + key.ToLowerInvariant());
            }

            foreach (var key in new[] { "NEXT_PUBLIC_API", "VERCEL_TOKEN" })
            {
                ProjectVariables.Set(vault, "acme-web", key, "demo-" + key.ToLowerInvariant());
            }

            // A project's environments: staging and prod beside dev, with every state the matrix draws: an expired
            // staging key on an entry of its own, a staging value reused from dev, and a key prod lacks.
            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                ProjectVariables.Set(vault, "acme-api", "staging", key, key switch
                {
                    "REDIS_URL" => "demo-redis_url",
                    "DATABASE_URL" => "postgres://api:demo-staging-pass@db.staging.acme.internal:5432/api",
                    _ => "demo-staging-" + key.ToLowerInvariant(),
                });
            }

            vault.AddEntry(new VaultEntry { Title = _openAi.Title, Username = "maya@acme.dev", Password = "demo-openai-login", Url = "https://platform.openai.com", GroupPath = _openAi.GroupPath });
            vault.SetFields(_openAi, [new FieldWrite("OPENAI_API_KEY", "demo-staging-openai_api_key")]);
            vault.AddTag(_openAi, "env:acme-api:staging");
            vault.SetExpiryUnchecked(_openAi, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

            foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY", "REDIS_URL", "JWT_SIGNING_KEY", "SENTRY_DSN" })
            {
                ProjectVariables.Set(vault, "acme-api", "prod", key, "demo-prod-" + key.ToLowerInvariant());
            }

            vault.Save();
            WriteActivity(_directory);
        }

        internal string Path { get; }

        internal string Home => _directory;

        /// <summary>A morning of audit records, written through the real writer so the chain verifies.</summary>
        private static void WriteActivity(string home)
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 7, 27, 18, 2, 17, TimeSpan.Zero));
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

                static AuditRecord Credential(AuditClient client, string entry, string field, AuditDecision decision, AuditMethod method, string reason, int? seconds = null, string asked = "Run the database migration for the api service.") => new()
                {
                    Tool = "request_credential",
                    Client = client,
                    Args = AuditArgs.ForCredentialRequest(entry, field, asked),
                    Decision = decision,
                    Method = method,
                    Reason = reason,
                };

                Write(TimeSpan.Zero, Credential(cursor, "Work/linear", "password", AuditDecision.Granted, AuditMethod.Prompt, "approved once", 0, "Sign in to Linear to file the bug."));
                Write(new TimeSpan(13, 37, 55), new AuditRecord
                {
                    Tool = "list_entry_names",
                    Client = claude,
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Exposure,
                    Reason = "listed 3 item names",
                });
                Write(TimeSpan.FromSeconds(41), Credential(claude, "env/acme-api/.env", "DATABASE_URL", AuditDecision.Granted, AuditMethod.Prompt, "approved for 1 hour", 3600));
                Write(TimeSpan.FromMinutes(17), new AuditRecord
                {
                    Tool = "run",
                    Client = claude,
                    Args = AuditArgs.ForRun(null, "Run the migration."),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.AlwaysAllowed,
                    Reason = "released without asking: a person always allowed this item for claude-code",
                    Entries = ["env/acme-api/.env"],
                    Command = "npm run migrate",
                });
                Write(TimeSpan.FromMinutes(2), Credential(claude, "env/acme-api/.env", "DATABASE_URL", AuditDecision.Granted, AuditMethod.AlwaysAllowed, "released without asking: a person always allowed this item for claude-code", asked: "Print the connection string so I can check it."));
                Write(TimeSpan.FromMinutes(2), Credential(cursor, "Work/aws-console", "password", AuditDecision.Denied, AuditMethod.Prompt, "the person denied it"));
                Write(TimeSpan.FromMinutes(18), new AuditRecord
                {
                    Tool = "run",
                    Client = new AuditClient("keypaste run --token", "1.0.0", null),
                    Args = new AuditArgs { Entry = "env/acme-api" },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Token,
                    Reason = Core.Tokens.TokenAuditReason.Format("t7d2e", "ci-github-actions", "6 variable(s)"),
                    Entries = ["env/acme-api/.env"],
                });
                Write(TimeSpan.FromMinutes(11), new AuditRecord
                {
                    Tool = "share",
                    Client = new AuditClient("keypaste share", "1.0.0", null),
                    Args = new AuditArgs { Entry = "env/acme-api/.env", Field = "STRIPE_SECRET_KEY" },
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.ShareCreated,
                    Reason = "share 3a11: 1 view, expires 2026-07-29T08:30:53Z, to maya@acme.dev, passphrase",
                });
                Write(TimeSpan.FromMinutes(14), Credential(cursor, "env/acme-web/.env", "VERCEL_TOKEN", AuditDecision.Denied, AuditMethod.TimedOut, "nobody answered within 60 seconds"));
                Write(TimeSpan.FromMinutes(20), Credential(claude, "Work/github", "username", AuditDecision.Granted, AuditMethod.Automatic, "released without asking: claude-code's mode for this item is Automatic"));
                Write(TimeSpan.FromMinutes(3), new AuditRecord
                {
                    Tool = "run",
                    Client = new AuditClient("windsurf-cascade-background-agent", "0.9.2", null),
                    Args = AuditArgs.ForRun("env/acme-api-background-worker-queue/staging", "Run the nightly re-index."),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = "approved once",
                    Entries = ["env/acme-api/OPENAI_API_KEY_FOR_EMBEDDINGS", "env/acme-api/JWT_SIGNING_KEY", "env/acme-api/SENTRY_DSN"],
                });
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
