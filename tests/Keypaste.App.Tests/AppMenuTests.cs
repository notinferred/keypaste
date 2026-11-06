using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The commands drawn as menus (Q.2): the macOS menu bar, its app menu and the Dock's, and the menu button's flyout on
/// Windows and Linux, each item running its command, off while the command cannot run, and showing its chord.
/// </summary>
public sealed class AppMenuTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly AppVaultSession _session;

    public AppMenuTests()
    {
        _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _vault.Home);
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public Task The_mac_menu_bar_draws_each_menu_and_its_items_run_their_commands() => HeadlessSession.On(() =>
    {
        var foreign = Path.Combine(_vault.Home, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(foreign, Encoding.UTF8.GetBytes("foreign-source"), null, "Argon2id", "ChaCha20");
        using var shell = new ShellViewModel(_session, _vault.Home, null, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker { ExistingPath = foreign });
        var commands = Commands(shell, mac: true);

        var bar = AppMenu.Bar(commands);
        var menus = bar.Items.OfType<NativeMenuItem>().ToDictionary(item => item.Header!, item => item.Menu!, StringComparer.Ordinal);
        Assert.Equal(["File", "Edit", "Item", "View", "Window", "Help"], menus.Keys);

        var newItem = Item(menus["File"], "New item…");
        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Meta), newItem.Gesture);
        Assert.NotNull(menus["File"].Items.OfType<NativeMenuItem>().Single(item => item.Header == "Open recent").Menu);
        Assert.NotEmpty(menus["File"].Items.OfType<NativeMenuItemSeparator>());

        var searched = 0;
        shell.SearchFocusRequested += (_, _) => searched++;
        Item(menus["Edit"], "Find").Command!.Press();
        Assert.Equal(1, searched);

        var import = Item(menus["File"], AppCommands.ImportHeader);
        Assert.True(import.Command!.CanExecute(null));
        import.Command.Press();
        Assert.True(shell.HasImport);
        Assert.False(import.Command.CanExecute(null), "Import is on while an import is open");
        Assert.False(newItem.Command!.CanExecute(null), "New item is on while an import is open");

        var app = AppMenu.Native(commands.AppMenu);
        Assert.Equal(["About keypaste", "Settings…", "Hide keypaste", "Hide others", "Show all", "Quit keypaste"],
            app.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).Select(item => item.Header));
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta), Item(app, "Quit keypaste").Gesture);
    });

    [Fact]
    public Task The_menu_button_opens_the_same_menus_with_their_chords_shown() => HeadlessSession.On(() =>
    {
        using var shell = new ShellViewModel(_session, _vault.Home, null, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var commands = Commands(shell, mac: false);

        var flyout = AppMenu.Flyout(commands);
        var groups = flyout.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header!, StringComparer.Ordinal);
        Assert.Equal(["keypaste", "File", "Edit", "Item", "View", "Window", "Help"], groups.Keys);

        var settings = Child(groups["keypaste"], "Settings…");
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Control), settings.InputGesture);
        Assert.Null(settings.HotKey);

        settings.Command!.Press();
        Assert.Equal(Navigation.DestinationKind.Settings, shell.Current.Kind);

        Assert.Equal(new KeyGesture(Key.F11), Child(groups["View"], "Enter full screen").InputGesture);
        Assert.Contains(groups["File"].Items, item => item is Separator);
    });

    [Fact]
    public Task The_Dock_menu_locks_and_opens_a_vault() => HeadlessSession.On(() =>
    {
        var opened = 0;
        var commands = new AppCommands(
            new AppCommandHost { Session = _session, Shell = () => null, Unlock = () => null, OpenWindow = () => opened++ },
            mac: true);

        var dock = AppMenu.Native(commands.Dock);
        var lockNow = Item(dock, "Lock");

        Assert.True(lockNow.Command!.CanExecute(null));
        lockNow.Command.Press();
        Assert.True(SpinWait.SpinUntil(() => !_session.IsUnlocked, TimeSpan.FromSeconds(10)), "the Dock's Lock did not lock");
        Assert.False(lockNow.Command.CanExecute(null));
        Assert.NotNull(Item(dock, "Open vault…"));
    });

    private AppCommands Commands(ShellViewModel shell, bool mac) =>
        new(new AppCommandHost { Session = _session, Shell = () => shell, Unlock = () => null, Home = _vault.Home }, mac);

    private static NativeMenuItem Item(NativeMenu menu, string header) =>
        menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);

    private static MenuItem Child(MenuItem group, string header) =>
        group.Items.OfType<MenuItem>().Single(item => (string?)item.Header == header);
}
