using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Xunit;
using static Keypaste.App.Tests.Session.JourneyDriver;

namespace Keypaste.App.Tests.Views;

/// <summary>The window opens as it was left and on a screen, and a narrow one lays Items out whole (Q.10).</summary>
public sealed class WindowLayoutTests
{
    private static readonly Size _minimum = new(WindowChrome.MinimumWidth, WindowChrome.MinimumHeight);
    private static readonly ScreenArea _main = new(new PixelRect(0, 25, 1440, 875), 1);
    private static readonly ScreenArea _left = new(new PixelRect(-1920, 0, 1920, 1040), 1);

    private static WindowLayout Saved(int x, int y, int width = 1000, int height = 680) => new(x, y, width, height, WindowMode.Normal, null, false);

    [Fact]
    public void A_window_on_a_screen_stays_where_it_was()
    {
        var (position, size) = WindowPlacement.Fit(Saved(100, 60), _minimum, [_main, _left]);

        Assert.Equal(new PixelPoint(100, 60), position);
        Assert.Equal(new Size(1000, 680), size);
    }

    [Fact]
    public void A_window_on_a_screen_left_of_the_main_one_stays_there()
    {
        var (position, _) = WindowPlacement.Fit(Saved(-1500, 40), _minimum, [_main, _left]);

        Assert.Equal(new PixelPoint(-1500, 40), position);
    }

    [Theory]
    [InlineData(-1500, 40)]
    [InlineData(5000, 40)]
    [InlineData(100, 5000)]
    [InlineData(100, -5000)]
    public void A_window_on_no_screen_comes_back_onto_the_main_one(int x, int y)
    {
        var (position, size) = WindowPlacement.Fit(Saved(x, y), _minimum, [_main]);

        Assert.True(_main.WorkingArea.Contains(new PixelRect(position, new PixelSize((int)size.Width, (int)size.Height))), $"{position} {size}");
    }

    [Fact]
    public void A_window_hanging_off_the_edge_is_brought_wholly_inside()
    {
        var (position, _) = WindowPlacement.Fit(Saved(900, 500), _minimum, [_main]);

        Assert.Equal(new PixelPoint(440, 220), position);
    }

    [Fact]
    public void A_window_larger_than_the_screen_fits_it_and_one_under_the_minimum_grows_to_it()
    {
        Assert.Equal(new Size(1440, 875), WindowPlacement.Fit(Saved(0, 0, 3000, 2000), _minimum, [_main]).Size);
        Assert.Equal(_minimum, WindowPlacement.Fit(Saved(0, 25, 300, 200), _minimum, [_main]).Size);
    }

    [Fact]
    public void A_scaled_screen_counts_its_own_units_against_the_window_size()
    {
        var scaled = new ScreenArea(new PixelRect(0, 0, 2000, 1200), 2);

        var (position, size) = WindowPlacement.Fit(Saved(1800, 900), _minimum, [scaled]);

        Assert.Equal(new Size(1000, 600), size);
        Assert.Equal(new PixelPoint(0, 0), position);
    }

    [Fact]
    public Task Saved_bounds_are_restored_and_read_back_as_they_were() => HeadlessSession.On(() =>
    {
        var window = new MainWindow();
        var area = window.Screens!.Primary!.WorkingArea;
        var saved = new WindowLayout(area.X + 40, area.Y + 30, 800, 600, WindowMode.Normal, 400, false);

        window.Restore(saved);
        window.Show();
        WindowInput.Drain();

        Assert.Equal(new PixelPoint(area.X + 40, area.Y + 30), window.Position);
        Assert.Equal(new Size(800, 600), window.ClientSize);
        Assert.Equal(saved, window.Layout(400, false));
        window.Close();
    });

    [Fact]
    public Task Bounds_off_the_screen_come_back_onto_it_and_a_maximized_window_opens_maximized() => HeadlessSession.On(() =>
    {
        var window = new MainWindow();
        var area = window.Screens!.Primary!.WorkingArea;

        window.Restore(new WindowLayout(area.Right + 4000, area.Bottom + 4000, 800, 600, WindowMode.Maximized, null, false));
        window.Show();
        WindowInput.Drain();

        Assert.Equal(WindowState.Maximized, window.WindowState);
        var kept = window.Layout(null, false)!;
        Assert.Equal(WindowMode.Maximized, kept.Mode);
        Assert.True(area.Contains(new PixelPoint(kept.X, kept.Y)), $"kept at {kept.X},{kept.Y}, outside {area}");
        Assert.Equal((800, 600), (kept.Width, kept.Height));
        window.Close();
    });

    [Fact]
    public Task Neither_a_maximize_nor_a_minimize_replaces_the_bounds_kept_under_it() => HeadlessSession.On(() =>
    {
        var window = new MainWindow { Width = 800, Height = 600 };
        window.Show();
        WindowInput.Drain();
        var before = window.Layout(null, false)!;

        window.WindowState = WindowState.Maximized;
        WindowInput.Drain();
        window.Position = new PixelPoint(before.X + 300, before.Y + 300);
        WindowInput.Drain();
        Assert.Equal(before with { Mode = WindowMode.Maximized }, window.Layout(null, false));

        window.WindowState = WindowState.Minimized;
        WindowInput.Drain();
        Assert.Equal(before with { Mode = WindowMode.Maximized }, window.Layout(null, false));
        window.Close();
    });

    [Fact]
    public Task A_window_that_never_showed_keeps_nothing() => HeadlessSession.On(() =>
    {
        var window = new MainWindow();

        Assert.Null(window.Layout(null, false));
    });

    [Fact]
    public Task The_window_can_go_down_to_720_wide() => HeadlessSession.On(() =>
    {
        var window = new MainWindow();

        Assert.Equal(720, window.MinWidth);
        Assert.Equal(520, window.MinHeight);
    });

    [Fact]
    public Task A_narrow_window_collapses_the_sidebar_and_a_wider_one_brings_it_back() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-window-narrow-");
        var sidebar = shell.Named<Border>("Sidebar");
        shell.Window.Width = 1000;
        shell.Frame();
        Assert.True(sidebar.IsEffectivelyVisible);

        shell.Window.Width = 720;
        shell.Frame();
        Assert.False(sidebar.IsEffectivelyVisible);
        Assert.True(shell.Shell.IsNarrow);
        Assert.Equal(720, shell.Root.Bounds.Width);

        shell.Window.Width = 840;
        shell.Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
    });

    [Fact]
    public Task Show_sidebar_opens_it_over_a_narrow_window_and_Escape_or_a_press_outside_closes_it() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-window-overlay-");
        shell.Window.Width = 720;
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        shell.Frame();
        var sidebar = shell.Named<Border>("Sidebar");
        var pane = shell.Named<ContentControl>("EntryPane");
        var widthBefore = pane.Bounds.Width;

        shell.Shell.ToggleSidebar();
        shell.Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        Assert.Equal(WindowChrome.SidebarWidth, sidebar.Bounds.Width);
        Assert.Equal(widthBefore, pane.Bounds.Width);

        WindowInput.Escape(shell.Window);
        shell.Frame();
        Assert.False(sidebar.IsEffectivelyVisible);

        shell.Shell.ToggleSidebar();
        shell.Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        WindowInput.Press(shell.Window, pane);
        WindowInput.Release(shell.Window, pane);
        shell.Frame();
        Assert.False(sidebar.IsEffectivelyVisible);
        Assert.False(shell.Shell.SidebarHidden, "closing it over a narrow window is not the person's choice to hide it");
        Assert.NotNull(entries);
    });

    [Fact]
    public Task A_group_can_still_be_chosen_from_the_sidebar_over_a_narrow_window() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-window-overlay-group-");
        shell.Window.Width = 720;
        shell.Frame();

        shell.Shell.ToggleSidebar();
        shell.Frame();
        var nav = shell.Named<ListBox>("Nav");
        var row = nav.Items.OfType<GroupRow>().First();
        WindowInput.Press(shell.Window, nav.ContainerFromItem(row)!);
        WindowInput.Release(shell.Window, nav.ContainerFromItem(row)!);

        Assert.Equal(row.Path, Assert.IsType<GroupRow>(shell.Shell.SelectedSidebarRow).Path);
    });

    [Fact]
    public Task Hide_sidebar_gives_its_width_to_the_content_at_any_width_and_the_menu_says_the_way_back() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-window-hide-");
        shell.Window.Width = 1280;
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        shell.Frame();
        var head = shell.Named<TextBlock>("UsernameHead");
        var before = shell.Named<ContentControl>("EntryPane").Bounds.Width;
        var commands = shell.Commands;
        var hide = commands.All.Single(command => command.Header == "Hide sidebar");
        var drawn = AppMenu.Bar(commands).Items.OfType<NativeMenuItem>().Single(item => item.Header == "View").Menu!
            .Items.OfType<NativeMenuItem>().Single(item => item.Header == "Hide sidebar");

        hide.Run();
        shell.Frame();

        Assert.False(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.True(shell.Named<ContentControl>("EntryPane").Bounds.Width > before);
        Assert.True(head.IsEffectivelyVisible, "a wide window keeps the Username column with the sidebar hidden");
        Assert.Equal("Show sidebar", drawn.Header);

        hide.Run();
        shell.Frame();

        Assert.True(shell.Named<Border>("Sidebar").IsEffectivelyVisible);
        Assert.Equal("Hide sidebar", drawn.Header);
    });

    [Fact]
    public Task Focus_inside_a_sidebar_that_hides_moves_to_its_button_and_focus_elsewhere_stays() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-window-focus-");
        shell.Frame();
        var button = shell.Named<Button>("ShowSidebar");

        shell.Window.Width = 720;
        shell.Frame();
        WindowInput.Drain();
        Assert.False(button.IsFocused, "narrowing the window moved the keyboard to the sidebar's button");

        shell.Window.Width = 1000;
        shell.Frame();
        shell.Named<ListBox>("Nav").ContainerFromIndex(0)!.Focus();
        WindowInput.Drain();

        shell.Shell.ToggleSidebar();
        shell.Frame();
        WindowInput.Drain();

        Assert.True(button.IsFocused);
    });

    [Fact]
    public Task The_split_is_held_across_a_trip_away_from_Items() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-window-split-");
        shell.Window.Height = 800;
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        shell.Frame();
        var before = shell.Named<ListBox>("Rows").Bounds.Height;

        DragDivider(shell.Window, shell.Named<GridSplitter>("PaneDivider"), 60);
        var dragged = shell.Named<ListBox>("Rows").Bounds.Height;
        Assert.InRange(dragged, before + 50, before + 70);
        Assert.NotNull(shell.Shell.ListShare);

        shell.Show<TrashViewModel>(DestinationKind.Trash);
        shell.Frame();
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        shell.Frame();

        Assert.InRange(shell.Named<ListBox>("Rows").Bounds.Height, dragged - 2, dragged + 2);
    });

    [Fact]
    public Task A_lock_keeps_the_layout_and_the_next_unlock_opens_with_it() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var vault = Path.Combine(fixture.Home, "layout.kdbx");

        using (var created = Vault.Create(vault, TempVault.Password))
        {
            created.AddEntry(new VaultEntry { GroupPath = "Work", Title = "github", Username = "me", Password = "layout-secret" });
            created.Save();
        }

        var settingsHome = Path.Combine(fixture.Home, "layout-home");
        Directory.CreateDirectory(settingsHome);
        var settingsPath = KeypasteHome.SettingsPath(settingsHome);
        File.WriteAllText(settingsPath, "[[settings]]\nidle_timeout_seconds = 137\n");
        var preferences = new DesktopPreferences(settingsHome);

        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        Attach(lifetime);
        using var app = new App { Preferences = preferences };
        app.Launch(lifetime);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

        var main = lifetime.MainWindow!;
        main.Width = 1100;
        main.Height = 800;
        main.Show();
        var root = main.FindControl<ContentControl>("Root")!;

        var shell = await Unlock(main, root, vault);
        var split = Named<Grid>(main, "Split");
        var before = ListShareOf(split);
        DragDivider(main, Named<GridSplitter>(main, "PaneDivider"), 60);
        var dragged = ListShareOf(split);
        Assert.True(dragged > before, $"{dragged} after the drag, {before} before it");
        shell.ToggleSidebar();
        DrawnFrame.Capture(main);

        app.Menus!.All.Single(command => command.Header == "Lock").Run();
        await Until(() => root.Content is UnlockView);

        // The file holds the layout and the hand edit, and it reads whole for the CLI's reader.
        Assert.True(AppSettings.TryLoad(settingsPath, out var kept));
        Assert.Equal(137, kept.IdleTimeoutSeconds);
        Assert.Equal((1100, 800), (kept.Window!.Width, kept.Window.Height));
        Assert.True(kept.Window.SidebarHidden);
        Assert.NotNull(kept.Window.ListShare);

        // A lock with nothing changed writes nothing.
        var written = File.ReadAllBytes(settingsPath);
        var stamp = File.GetLastWriteTimeUtc(settingsPath);
        var reopened = await Unlock(main, root, vault);
        Assert.True(reopened.SidebarHidden);
        Assert.DoesNotContain(main.GetVisualDescendants().OfType<Border>(), border => border.Name == "Sidebar" && border.IsEffectivelyVisible);
        Assert.Equal(dragged, ListShareOf(Named<Grid>(main, "Split")), 0.01);

        app.Menus!.All.Single(command => command.Header == "Lock").Run();
        await Until(() => root.Content is UnlockView);
        Assert.Equal(written, File.ReadAllBytes(settingsPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(settingsPath));

        main.Close();
    });

    [Fact]
    public Task The_zoom_is_written_as_it_changes_and_comes_back_clamped_at_the_next_launch() => HeadlessSession.On(() =>
    {
        var home = Directory.CreateTempSubdirectory("keypaste-zoom-launch-").FullName;
        var settingsPath = KeypasteHome.SettingsPath(home);

        // Closing into the tray, as Moving_the_window_is_kept_on_closing_it does: a real close ends the shared headless session where the tray is off, Linux's default, and every later test then waits on it forever.
        File.WriteAllText(settingsPath, "[[settings]]\nstay_in_tray = 1\n");
        var untouched = File.ReadAllBytes(settingsPath);

        try
        {
            using (var lifetime = new ClassicDesktopStyleApplicationLifetime())
            {
                Attach(lifetime);
                using var app = new App { Preferences = new DesktopPreferences(home) };
                app.Launch(lifetime);
                lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
                var main = lifetime.MainWindow!;
                main.Show();
                WindowInput.Drain();
                Assert.Equal(untouched, File.ReadAllBytes(settingsPath));

                app.Menus!.All.Single(command => command.Header == "Zoom in").Run();
                WindowInput.Drain();
                Assert.Equal(125, AppSettings.Load(settingsPath).Window!.ZoomPercent);
                main.Close();
            }

            File.WriteAllText(settingsPath, File.ReadAllText(settingsPath).Replace("125%", "9000%", StringComparison.Ordinal));

            using (var lifetime = new ClassicDesktopStyleApplicationLifetime())
            {
                Attach(lifetime);
                using var app = new App { Preferences = new DesktopPreferences(home) };
                app.Launch(lifetime);
                lifetime.ShutdownRequested += (_, e) => e.Cancel = true;
                var main = lifetime.MainWindow!;

                Assert.Equal(200, InterfaceZoom.Percent);
                Assert.Equal(1440, main.MinWidth);
                main.Close();
            }
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    });

    [Fact]
    public Task A_window_nobody_touched_writes_nothing_on_closing() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        var settingsHome = Path.Combine(fixture.Home, "untouched-home");
        Directory.CreateDirectory(settingsHome);
        var settingsPath = KeypasteHome.SettingsPath(settingsHome);
        var written = "[[settings]]\nidle_timeout_seconds = 137\nstay_in_tray = 1\n"u8.ToArray();
        File.WriteAllBytes(settingsPath, written);

        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        Attach(lifetime);
        using var app = new App { Preferences = new DesktopPreferences(settingsHome) };
        app.Launch(lifetime);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

        var main = lifetime.MainWindow!;
        main.Show();
        WindowInput.Drain();
        main.Close();
        WindowInput.Drain();

        Assert.Equal(written, File.ReadAllBytes(settingsPath));
    });

    [Fact]
    public Task Moving_the_window_is_kept_on_closing_it() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        var settingsHome = Path.Combine(fixture.Home, "moved-home");
        Directory.CreateDirectory(settingsHome);
        var settingsPath = KeypasteHome.SettingsPath(settingsHome);
        File.WriteAllText(settingsPath, "[[settings]]\nstay_in_tray = 1\n");

        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        Attach(lifetime);
        using var app = new App { Preferences = new DesktopPreferences(settingsHome) };
        app.Launch(lifetime);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

        var main = lifetime.MainWindow!;
        main.Show();
        WindowInput.Drain();
        main.Width = 900;
        WindowInput.Drain();
        main.Close();
        WindowInput.Drain();

        Assert.Equal(900, AppSettings.Load(settingsPath).Window!.Width);
        Assert.True(AppSettings.Load(settingsPath).StayInTray);
    });

    [Fact]
    public Task A_lock_leaves_alone_a_file_it_cannot_read() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var vault = Path.Combine(fixture.Home, "unreadable.kdbx");

        using (var created = Vault.Create(vault, TempVault.Password))
        {
            created.Save();
        }

        var settingsHome = Path.Combine(fixture.Home, "unreadable-home");
        Directory.CreateDirectory(settingsHome);
        var settingsPath = KeypasteHome.SettingsPath(settingsHome);
        var handEdit = "[[settings]]\nnot a pair\n"u8.ToArray();
        File.WriteAllBytes(settingsPath, handEdit);

        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        Attach(lifetime);
        using var app = new App { Preferences = new DesktopPreferences(settingsHome) };
        app.Launch(lifetime);
        lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

        var main = lifetime.MainWindow!;
        main.Show();
        var root = main.FindControl<ContentControl>("Root")!;

        _ = await Unlock(main, root, vault);
        main.Width = 1100;
        DrawnFrame.Capture(main);
        app.Menus!.All.Single(command => command.Header == "Lock").Run();
        await Until(() => root.Content is UnlockView);

        Assert.Equal(handEdit, File.ReadAllBytes(settingsPath));
        main.Close();
    });

    [Fact]
    public void Keeping_the_window_takes_nothing_else_from_the_running_copy()
    {
        using var home = new TempHome();
        var path = KeypasteHome.SettingsPath(home.Path);
        AppSettings.Save(path, AppSettings.Default with { IdleTimeoutSeconds = 600 });
        var preferences = new DesktopPreferences(home.Path);

        // A hand edit while the app runs takes effect at the next launch, so nothing may write the running copy's value over it.
        AppSettings.Save(path, AppSettings.Default with { IdleTimeoutSeconds = 900 });
        preferences.KeepWindow(Saved(1, 2));

        var read = AppSettings.Load(path);
        Assert.Equal(900, read.IdleTimeoutSeconds);
        Assert.Equal(Saved(1, 2), read.Window);
        Assert.Equal(600, preferences.Current.IdleTimeoutSeconds);

        // A choice in Settings keeps it too.
        Assert.True(preferences.Update(preferences.Current with { Theme = AppTheme.Dark }));
        Assert.Equal(Saved(1, 2), AppSettings.Load(path).Window);
        Assert.Equal(Saved(1, 2), preferences.Current.Window);
    }

    private static double ListShareOf(Grid split) =>
        split.RowDefinitions[0].ActualHeight / (split.RowDefinitions[0].ActualHeight + split.RowDefinitions[2].ActualHeight);

    private static void DragDivider(TopLevel window, Control divider, int down)
    {
        DrawnFrame.Capture(window);
        var from = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window)!.Value;
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        WindowInput.Drain();
        window.MouseMove(from + new Point(0, down));
        WindowInput.Drain();
        window.MouseUp(from + new Point(0, down), MouseButton.Left);
        WindowInput.Drain();
        DrawnFrame.Capture(window);
    }

    private static async Task<ShellViewModel> Unlock(Window main, ContentControl root, string vault)
    {
        var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
        Assert.True(unlock.Offer(vault, null));
        WindowInput.Drain();
        WindowInput.Click(main, Named<MaskedInput>(main, "Password"));
        WindowInput.Type(main, TempVault.Password);
        Key(main, PhysicalKey.Enter, RawInputModifiers.None);
        await Until(() => root.Content is ShellView);
        var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);
        DrawnFrame.Capture(main);
        return shell;
    }
}
