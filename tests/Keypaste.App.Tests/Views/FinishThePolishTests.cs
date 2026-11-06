using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Globalization;
using Keypaste.App.Navigation;
using Keypaste.App.Notifications;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>What Q.17 finished: titles, the hand, toasts, scroll bars, the theme's place, right to left, the wider tight widths and asking for permission calmly.</summary>
public sealed class FinishThePolishTests
{
    [Fact]
    public Task The_window_names_the_place_and_nothing_of_the_vault() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-title-", vault =>
        {
            vault.CreateGroup(string.Empty, "secret-group", out _);
            ProjectVariables.Set(vault, "secret-project", "dev", "KEY", "value");
        });

        Assert.Equal("Items - keypaste", shell.Window.Title);

        shell.Show<TrashViewModel>(DestinationKind.Trash);
        Assert.Equal("Trash - keypaste", shell.Window.Title);

        shell.Show<SettingsViewModel>(DestinationKind.Settings);
        Assert.Equal("Settings - keypaste", shell.Window.Title);

        shell.Show<LogViewModel>(DestinationKind.Log);
        Assert.Equal("Activity log - keypaste", shell.Window.Title);

        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<ProjectRow>().First(row => row.Name == "secret-project");
        shell.Frame();
        Assert.Equal("Projects - keypaste", shell.Window.Title);
    });

    [Fact]
    public Task A_title_goes_back_to_the_bare_name_when_the_shell_leaves_the_window() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-title-lock-");
        Assert.Equal("Items - keypaste", shell.Window.Title);

        ShellViewOf(shell).DataContext = null;
        var content = shell.Window.Content;
        shell.Window.Content = null;

        Assert.Equal("keypaste", shell.Window.Title);
        shell.Window.Content = content;
    });

    [Theory]
    [InlineData(nameof(DestinationKind.Settings))]
    [InlineData(nameof(DestinationKind.Tokens))]
    [InlineData(nameof(DestinationKind.AgentActivity))]
    public Task The_pointing_hand_stays_on_links(string kind) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-hand-", vault => ProjectVariables.Set(vault, "billing", "dev", "KEY", "value"));
        shell.Shell.Current = Destinations.Of(Enum.Parse<DestinationKind>(kind));
        shell.Frame();

        var controls = shell.Window.GetVisualDescendants().OfType<Control>().ToList();
        var pressable = controls.Where(control => control is ToggleSwitch or RadioButton or CheckBox or ToggleButton or Button).ToList();

        Assert.NotEmpty(pressable);
        Assert.All(pressable.Where(control => !control.Classes.Contains("link")), control => Assert.Null(control.Cursor));
    });

    [Fact]
    public Task A_link_keeps_the_pointing_hand() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-hand-link-");
        var link = new Button { Classes = { "link" }, Content = "Learn more" };
        shell.Window.Content = link;
        shell.Frame();

        Assert.NotNull(link.Cursor);
    });

    [Theory]
    [InlineData(720, 100)]
    [InlineData(720, 200)]
    [InlineData(840, 100)]
    [InlineData(1280, 100)]
    public Task Toasts_sit_under_the_titlebar_and_clear_of_the_search_and_New(int width, int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-toast-place-");
        shell.Window.Width = width * percent / 100d;
        shell.Frame();
        shell.Shell.ShowToast("Saved a very long toast that wraps and goes on for a little while so that it is tall and wide.", ToastTone.Success);
        shell.Shell.ShowToast("And a second one under it.", ToastTone.Information);
        shell.Frame();

        var stack = shell.Named<StackPanel>("ToastStack");
        var root = (Visual)shell.Window.GetVisualDescendants().OfType<ShellView>().Single();
        var toasts = BoundsIn(root, stack);

        Assert.True(toasts.Top >= WindowChrome.TitleBarHeight, $"toasts start at {toasts.Top}, in the titlebar");
        Assert.False(toasts.Intersects(BoundsIn(root, shell.Named<TextBox>("ShellSearch"))), "a toast covers the search");
        Assert.False(toasts.Intersects(BoundsIn(root, shell.Named<ToggleButton>("AddEntry"))), "a toast covers New");
    });

    [Fact]
    public Task Scroll_bars_follow_what_the_system_says() => HeadlessSession.On(() =>
    {
        var app = Assert.IsType<App>(Application.Current);
        using var shell = new RenderedShell("keypaste-scrollbars-");
        shell.Show<SettingsViewModel>(DestinationKind.Settings);
        shell.Frame();

        try
        {
            Assert.All(Bars(shell), bar => Assert.True(bar.AllowAutoHide));

            app.ScrollBarsAutoHide = false;
            shell.Frame();
            Assert.All(Bars(shell), bar => Assert.False(bar.AllowAutoHide));
        }
        finally
        {
            app.ScrollBarsAutoHide = true;
        }
    });

    [Fact]
    public async Task Reading_the_systems_scroll_bars_with_no_bus_says_they_hide()
    {
        if (OperatingSystem.IsLinux())
        {
            Assert.True(await SystemScrollBars.AutoHideAsync(null));
        }
        else
        {
            _ = await SystemScrollBars.AutoHideAsync(null);
        }
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData("fa-IR")]
    [InlineData("ur-PK")]
    [InlineData("en-US")]
    public void Windows_run_left_to_right_while_no_translation_exists(string culture) =>
        Assert.Equal(FlowDirection.LeftToRight, ReadingDirection.Of(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void A_right_to_left_language_with_a_translation_mirrors()
    {
        IReadOnlySet<string> translated = new HashSet<string>(StringComparer.Ordinal) { "ar" };

        Assert.Equal(FlowDirection.RightToLeft, ReadingDirection.Of(CultureInfo.GetCultureInfo("ar-SA"), translated));
        Assert.Equal(FlowDirection.LeftToRight, ReadingDirection.Of(CultureInfo.GetCultureInfo("he-IL"), translated));
        Assert.Equal(FlowDirection.LeftToRight, ReadingDirection.Of(CultureInfo.GetCultureInfo("en-US"), translated));
    }

    [Fact]
    public Task Appearance_shows_only_where_the_system_reports_no_light_or_dark() => HeadlessSession.On(() =>
    {
        using var hidden = new RenderedShell("keypaste-theme-hidden-");
        hidden.Show<SettingsViewModel>(DestinationKind.Settings);
        hidden.Frame();
        Assert.False(hidden.Named<ListBox>("ThemeChoice").IsEffectivelyVisible);

        using var offered = new RenderedShell("keypaste-theme-offered-", offersTheme: true);
        offered.Show<SettingsViewModel>(DestinationKind.Settings);
        offered.Frame();
        Assert.True(offered.Named<ListBox>("ThemeChoice").IsEffectivelyVisible);
    });

    [Theory]
    [InlineData("[[settings]]\ntheme = \"light\"\n", AppTheme.Light)]
    [InlineData("[[settings]]\ntheme = \"dark\"\n", AppTheme.Dark)]
    [InlineData("[[settings]]\ntheme = \"system\"\n", AppTheme.System)]
    public Task A_kept_theme_still_reads_and_gives_way_to_a_system_that_reports(string line, AppTheme kept) => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        Directory.CreateDirectory(fixture.Home);
        File.WriteAllText(Core.Audit.KeypasteHome.SettingsPath(fixture.Home), line);
        var preferences = new DesktopPreferences(fixture.Home);
        Assert.Equal(kept, preferences.Current.Theme);

        var app = Assert.IsType<App>(Application.Current);
        var reported = app.SystemReportsLightOrDark;

        try
        {
            app.SystemReportsLightOrDark = true;
            using var session = app.Compose(preferences, new ManualClock(AppClock.Start));
            PlatformTheme.Set(Avalonia.Platform.PlatformThemeVariant.Light);
            Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.ActualThemeVariant);
            PlatformTheme.Set(Avalonia.Platform.PlatformThemeVariant.Dark);
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, app.ActualThemeVariant);

            app.SystemReportsLightOrDark = false;
            Assert.Equal(kept == AppTheme.Light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark, app.ActualThemeVariant);

            Assert.Equal(line, File.ReadAllText(Core.Audit.KeypasteHome.SettingsPath(fixture.Home)));
        }
        finally
        {
            app.SystemReportsLightOrDark = reported;
            PlatformTheme.Set(Avalonia.Platform.PlatformThemeVariant.Dark);
            app.ApplyTheme(AppTheme.System);
        }
    });

    [Theory]
    [InlineData(840, 100, true)]
    [InlineData(900, 100, true)]
    [InlineData(976, 100, true)]
    [InlineData(999, 100, true)]
    [InlineData(1000, 100, true)]
    [InlineData(840, 200, true)]
    [InlineData(840, 100, false)]
    [InlineData(976, 100, false)]
    [InlineData(999, 100, false)]
    [InlineData(1000, 100, false)]
    [InlineData(840, 200, false)]
    public Task The_logs_Result_reads_whole_between_840_and_1000(int width, int percent, bool barsHide) => HeadlessSession.On(() =>
    {
        using var bars = new ScrollBarsSetting(barsHide);
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-tight-log-");
        using var home = new TempAuditHome();
        home.Append("env/dev/STRIPE_KEY");
        Directory.CreateDirectory(Path.GetDirectoryName(Core.Audit.KeypasteHome.AuditPath(shell.Home))!);
        File.Copy(home.LogPath, Core.Audit.KeypasteHome.AuditPath(shell.Home), overwrite: true);
        shell.Window.Width = width * percent / 100d;
        var log = shell.Show<LogViewModel>(DestinationKind.Log);
        log.RefreshCommand.Press();
        shell.Frame();

        var scroll = shell.Named<ScrollViewer>("TableScroll");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width, $"the table scrolls sideways at {width}");
        var row = Assert.Single(shell.Window.GetVisualDescendants().OfType<LogColumns>(), columns => columns.Parent is Border { Classes: var classes } && classes.Contains("table-row"));
        var result = row.GetVisualDescendants().OfType<TextBlock>().Last(block => block.IsEffectivelyVisible);
        Assert.All(result.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"{result.Text} is cut at {width}"));
    });

    [Theory]
    [InlineData(840, 100, true)]
    [InlineData(900, 100, true)]
    [InlineData(999, 100, true)]
    [InlineData(1000, 100, true)]
    [InlineData(1100, 100, true)]
    [InlineData(1280, 100, true)]
    [InlineData(840, 200, true)]
    [InlineData(840, 100, false)]
    [InlineData(1000, 100, false)]
    [InlineData(840, 200, false)]
    public Task An_environments_note_stays_whole_between_840_and_1000(int width, int percent, bool barsHide) => HeadlessSession.On(() =>
    {
        using var bars = new ScrollBarsSetting(barsHide);
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-tight-env-", vault =>
        {
            ProjectVariables.Set(vault, "billing", "dev", "SHARED", "value");
            ProjectVariables.Set(vault, "billing", "staging", "SHARED", "value");
            ProjectVariables.Set(vault, "billing", "prod", "SHARED", "other");
        });
        shell.Window.Width = width * percent / 100d;
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "billing");
        shell.Frame();

        var note = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(block => block.IsEffectivelyVisible && block.Text == "same as staging");
        Assert.All(note.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, $"the note is cut at {width}"));
        var overprints = ReachableControls.Problems(shell.Window).Where(problem => problem.Contains("overprints", StringComparison.Ordinal)).ToList();
        Assert.True(overprints.Count == 0, string.Join(Environment.NewLine, overprints));

        var root = (Visual)ShellViewOf(shell);
        var newProject = BoundsIn(root, Named("New project"));
        var import = BoundsIn(root, Named("Import .env"));
        Assert.True(newProject.Right <= import.Left, $"New project ends at {newProject.Right} and Import starts at {import.Left} at {width}");

        Button Named(string name) => shell.Window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name);
    });

    [Fact]
    public Task A_wide_window_keeps_the_logs_Where_and_a_hidden_sidebar_gives_it_back() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-tight-wide-");
        shell.Window.Width = 1000;
        shell.Frame();
        Assert.False(shell.Shell.ContentIsTight);

        shell.Window.Width = 999;
        shell.Frame();
        Assert.True(shell.Shell.ContentIsTight);

        shell.Shell.ToggleSidebarCommand.Execute(null);
        shell.Frame();
        Assert.False(shell.Shell.ContentIsTight);
    });

    [Fact]
    public void Permission_is_asked_once_at_a_calm_moment_and_never_beside_a_prompt()
    {
        using var notifications = new FakeNotifications();
        var waiting = true;
        var calm = new CalmPermission(notifications, () => waiting);

        calm.Offer();
        Assert.Equal(0, notifications.PermissionAsks);

        waiting = false;
        calm.Offer();
        calm.Offer();
        Assert.Equal(1, notifications.PermissionAsks);
    }

    /// <summary>A request the UI thread has not taken up yet is a prompt about to appear, and the system's dialog must not land beside it.</summary>
    [Fact]
    public Task A_request_on_its_way_to_the_screen_holds_the_permission_back() => HeadlessSession.On(async () =>
    {
        using var notifications = new FakeNotifications();
        using var withdraw = new CancellationTokenSource();
        var channel = new WindowApprovalChannel(new ManualClock(AppClock.Start), TimeSpan.FromSeconds(45));
        var calm = new CalmPermission(notifications, () => channel.IsAsking);

        var answer = channel.AskAsync(ApprovalPrompt.For("claude-code", new EntryName("env/ci", "DEPLOY_KEY"), "password", "deploying", 3600, "work laptop"), withdraw.Token);

        Assert.True(channel.IsAsking);
        calm.Offer();
        Assert.Equal(0, notifications.PermissionAsks);

        WindowInput.Drain();
        Assert.True(channel.IsAsking);
        calm.Offer();
        Assert.Equal(0, notifications.PermissionAsks);

        await withdraw.CancelAsync();
        await answer;
        WindowInput.Drain();
        Assert.False(channel.IsAsking);
        calm.Offer();
        Assert.Equal(1, notifications.PermissionAsks);
    });

    [Theory]
    [InlineData(nameof(DestinationKind.AgentActivity), true)]
    [InlineData(nameof(DestinationKind.Settings), true)]
    [InlineData(nameof(DestinationKind.Trash), false)]
    public Task Opening_Agents_or_Settings_is_a_calm_moment(string kind, bool asks) => HeadlessSession.On(() =>
    {
        var told = 0;
        using var context = new RenderedShell("keypaste-calm-");
        using var shell = new ShellViewModel(context.Session, context.Home, null, clock: new ManualClock(AppClock.Start)) { CalmMoment = () => told++ };

        shell.Current = Destinations.Of(Enum.Parse<DestinationKind>(kind));

        Assert.Equal(asks ? 1 : 0, told);
    });

    [Fact]
    public Task Learn_more_says_what_a_selector_and_a_scope_are_where_they_are_typed() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-learn-more-");
        shell.Show<ScopedTokensViewModel>(DestinationKind.Tokens).OpenFormCommand.Execute(null);
        shell.Frame();

        var learnMore = shell.Named<Button>("LearnMoreScopes");
        Assert.True(learnMore.IsEffectivelyVisible);
        Assert.Equal("Learn more about token scopes", AutomationProperties.GetName(learnMore));
        Assert.Contains("link", learnMore.Classes);
        Assert.NotNull(learnMore.Flyout);
        AssertItsExamplesAreSetAsCode(learnMore);

        var seeing = new AgentSettingsView();
        var learnMoreSeeing = seeing.FindControl<Button>("LearnMoreSeeing")!;
        Assert.Equal("Learn more about what it can see", AutomationProperties.GetName(learnMoreSeeing));
        Assert.Contains("link", learnMoreSeeing.Classes);
        AssertItsExamplesAreSetAsCode(learnMoreSeeing);

        static void AssertItsExamplesAreSetAsCode(Button link)
        {
            var content = Assert.IsType<StackPanel>(Assert.IsType<Flyout>(link.Flyout).Content);
            var examples = content.GetLogicalDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("mono")).ToList();
            Assert.Equal(2, examples.Count);
        }
    });

    /// <summary>The system's scroll bar setting, as a test sets it, restored at the end, since the app outlives the test.</summary>
    private sealed class ScrollBarsSetting : IDisposable
    {
        private readonly App _app = Assert.IsType<App>(Application.Current);

        internal ScrollBarsSetting(bool autoHide) => _app.ScrollBarsAutoHide = autoHide;

        public void Dispose() => _app.ScrollBarsAutoHide = true;
    }

    private static List<ScrollBar> Bars(RenderedShell shell) =>
        [.. shell.Window.GetVisualDescendants().OfType<ScrollBar>()];

    private static ShellView ShellViewOf(RenderedShell shell) =>
        shell.Window.GetVisualDescendants().OfType<ShellView>().Single();

    private static Rect BoundsIn(Visual root, Control control)
    {
        var at = control.TranslatePoint(default, root)!.Value;
        return new Rect(at, control.Bounds.Size);
    }
}
