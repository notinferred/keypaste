using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Controls;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Internal;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The commands of the menus and the window's chords (Q.2): each does what its item says, is off where it cannot
/// act, and on macOS the Edit commands act on the focused field themselves, because there the menu takes their chord
/// before the field sees it.
/// </summary>
public sealed class AppCommandsTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly AppVaultSession _session;
    private readonly FakeClipboard _clipboard = new();

    public AppCommandsTests()
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
    public void Each_platform_has_its_menus_and_writes_its_chords_with_its_own_key()
    {
        var mac = Commands(shell: null, mac: true);
        var other = Commands(shell: null, mac: false);

        Assert.Equal(["File", "Edit", "View", "Window", "Help"], mac.Menus.Select(menu => menu.Header));
        Assert.Equal(["File", "Edit", "View", "Window", "Help"], other.Menus.Select(menu => menu.Header));
        Assert.Equal(["About keypaste", "Settings…", "Hide keypaste", "Hide others", "Show all", "Quit keypaste"], Headers(mac.AppMenu));
        Assert.Equal(["About keypaste", "Settings…", "Quit keypaste"], Headers(other.AppMenu));

        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), Find(mac, "Settings…").Shown);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Control), Find(other, "Settings…").Shown);
        Assert.Equal(new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt), Find(mac, "Hide others").Shown);
        Assert.Equal(new KeyGesture(Key.Back, KeyModifiers.Meta), Find(mac, "Delete").Shown);
        Assert.Equal(new KeyGesture(Key.Delete), Find(other, "Delete").Shown);
        Assert.Equal(new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Meta), Find(mac, "Enter full screen").Shown);
        Assert.Equal(new KeyGesture(Key.F11), Find(other, "Enter full screen").Shown);
        Assert.Contains(new KeyGesture(Key.K, KeyModifiers.Meta), Find(mac, "Find").Keys);
        Assert.Contains(new KeyGesture(Key.K, KeyModifiers.Control), Find(other, "Find").Keys);

        // Windows reports AltGr as Ctrl+Alt, so no chord there holds Alt (Q.1).
        Assert.DoesNotContain(other.All.SelectMany(command => command.Keys), chord => chord.KeyModifiers.HasFlag(KeyModifiers.Alt));
        Assert.Equal(["Lock", "Open vault…"], Headers(mac.Dock));
    }

    [Fact]
    public Task Each_command_does_what_its_item_says() => HeadlessSession.On(() =>
    {
        using var shell = NewShell();
        var opened = new List<string?>();
        var front = 0;
        var commands = Commands(shell, openAnother: opened.Add, openWindow: () => front++);
        var searched = 0;
        shell.SearchFocusRequested += (_, _) => searched++;

        Find(commands, "Settings…").Run();
        Assert.Equal(DestinationKind.Settings, shell.Current.Kind);

        Find(commands, "New item").Run();
        Assert.NotNull(Assert.IsType<EntriesViewModel>(shell.Content).NewItem);

        Find(commands, "Agents").Run();
        Assert.Equal(DestinationKind.AgentActivity, shell.Current.Kind);

        Find(commands, "Find").Run();
        Assert.Equal(1, searched);

        Find(commands, "Open vault…").Run();
        Assert.Equal(new string?[] { null }, opened);

        Find(commands, "Bring all to front").Run();
        Assert.Equal(1, front);

        Find(commands, "Lock").Run();
        Assert.True(SpinWait.SpinUntil(() => !_session.IsUnlocked, TimeSpan.FromSeconds(10)), "Lock did not lock");
        Assert.False(Find(commands, "Lock").CanRun());
    });

    [Fact]
    public Task The_vault_commands_are_off_while_locked_and_behind_a_dialog() => HeadlessSession.On(() =>
    {
        var locked = Commands(shell: null);
        Assert.False(Find(locked, "Settings…").CanRun());
        Assert.False(Find(locked, "New item").CanRun());
        Assert.False(Find(locked, "Open vault…").CanRun());

        using var shell = NewShell();
        var commands = Commands(shell);
        shell.OpenImport(ForeignVault());

        Assert.True(shell.HasImport);

        foreach (var header in new[] { "Settings…", "New item", "Find", "Items", AppCommands.ImportHeader, "Export…", "Open vault…" })
        {
            Assert.False(Find(commands, header).CanRun(), $"{header} is on behind the import dialog");
        }

        Assert.True(Find(commands, "Lock").CanRun());
    });

    [Fact]
    public Task On_an_item_Copy_copies_its_password_Delete_asks_first_and_Undo_puts_it_back() => HeadlessSession.On(() =>
    {
        using var shell = NewShell();
        var row = new Button();
        IInputElement? focused = row;
        var commands = Commands(shell, focused: () => focused);
        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        items.Selected = items.Rows.Single();

        Find(commands, "Copy").Run();

        Assert.True(SpinWait.SpinUntil(() => Drained(_clipboard.Content is not null), TimeSpan.FromSeconds(10)), "Copy copied nothing");
        Assert.Equal(TempVault.ExamplePassword, _clipboard.Content);

        Find(commands, "Delete").Run();

        Assert.True(items.IsConfirmingDelete);
        Assert.Single(items.Rows);

        items.ConfirmDeleteCommand.Press();
        Assert.Empty(items.Rows);
        Assert.True(Find(commands, "Undo").CanRun());

        Find(commands, "Undo").Run();
        Assert.Single(items.Rows);

        // In a text field the field's own Copy and Delete are left to it.
        focused = new TextBox();
        items.Selected = items.Rows.Single();
        Assert.False(Find(commands, "Copy").CanRun());
        Assert.False(Find(commands, "Delete").CanRun());
    });

    [Fact]
    public Task A_password_text_field_copies_nothing() => HeadlessSession.On(() =>
    {
        var field = new TextBox { PasswordChar = '•', Text = "hunter2" };
        var window = new Window { Content = field };
        window.Show();
        field.Focus();
        field.SelectAll();

        var commands = Commands(shell: null, window: window);

        Assert.False(Find(commands, "Copy").CanRun());
        Assert.False(Find(commands, "Cut").CanRun());
        Assert.True(Find(commands, "Select all").CanRun());
        window.Close();
    });

    [Fact]
    public Task Paste_into_a_masked_field_pastes_once_and_exposes_nothing() => HeadlessSession.On(async () =>
    {
        const string fixture = "pasted-master-7c41";

        using var locked = new AppVaultSession(new ManualClock(AppClock.Start));
        var clipboard = new FakeClipboard();
        using var unlock = new UnlockViewModel(locked, _vault.Home, new FakeVaultFilePicker(), () => { }, paste: clipboard.TryPasteIntoAsync);
        var window = new Window { Content = new UnlockView { DataContext = unlock } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(unlock.Offer(_vault.Path_));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var field = window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == "Password");
        field.Focus();
        clipboard.Plant(fixture);

        var commands = new AppCommands(new AppCommandHost { Session = locked, Shell = () => null, Unlock = () => unlock, Window = () => window }, mac: true);
        var paste = Find(commands, "Paste");

        Assert.True(paste.CanRun());
        paste.Run();
        await field.PasteCompleted;

        Assert.Equal(fixture.Length, unlock.MaskedLength);
        AutomationSurface.AssertNothingExposes(window, fixture);
        window.Close();
    });

    [Fact]
    public Task Open_recent_lists_the_vaults_by_name_offers_one_and_clears() => HeadlessSession.On(() =>
    {
        _vault.RememberSelf();
        using var locked = new AppVaultSession(new ManualClock(AppClock.Start));
        using var unlock = new UnlockViewModel(locked, _vault.Home, new FakeVaultFilePicker(), () => { });
        unlock.SelectedPath = null;
        var commands = new AppCommands(new AppCommandHost { Session = locked, Shell = () => null, Unlock = () => unlock, Home = _vault.Home });
        var recent = commands.Menus.Single(menu => menu.Header == "File").Entries.OfType<AppSubmenu>().Single();

        var vault = recent.Entries().OfType<AppCommand>().First();
        Assert.Equal("test.kdbx", vault.Header);

        vault.Run();
        Assert.Equal(Path.GetFullPath(_vault.Path_), unlock.SelectedPath);

        recent.Entries().OfType<AppCommand>().Single(command => command.Header == "Clear menu").Run();

        Assert.Equal(["Clear menu"], recent.Entries().OfType<AppCommand>().Select(command => command.Header));
        Assert.False(recent.Entries().OfType<AppCommand>().Single().CanRun());
        Assert.Empty(unlock.Recent);
    });

    [Fact]
    public Task About_names_keypaste_and_its_version() => HeadlessSession.On(() =>
    {
        var about = new AboutWindow();
        about.Show();

        var texts = about.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();

        Assert.Equal("About keypaste", about.Title);
        Assert.Contains("keypaste", texts);
        Assert.Contains($"Version {CoreInfo.Version}", texts);
        Assert.Contains(AboutWindow.Copyright, texts);
        about.Close();
    });

    private AppCommands Commands(
        ShellViewModel? shell,
        bool mac = true,
        Window? window = null,
        Func<IInputElement?>? focused = null,
        Action<string?>? openAnother = null,
        Action? openWindow = null) =>
        new(
            new AppCommandHost
            {
                Session = _session,
                Shell = () => shell,
                Unlock = () => null,
                Window = () => window,
                Focused = focused,
                Home = _vault.Home,
                OpenAnother = openAnother,
                OpenWindow = openWindow,
            },
            mac);

    private ShellViewModel NewShell() =>
        new(_session, _vault.Home, null, clipboard: _clipboard, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());

    private string ForeignVault()
    {
        var path = Path.Combine(_vault.Home, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(path, Encoding.UTF8.GetBytes("foreign-source"), null, "Argon2id", "ChaCha20");
        return path;
    }

    private static bool Drained(bool done)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return done;
    }

    private static AppCommand Find(AppCommands commands, string header) =>
        commands.AppMenu.Concat(commands.Menus.SelectMany(menu => menu.Entries)).OfType<AppCommand>().Single(command => command.Header == header);

    private static IEnumerable<string> Headers(IEnumerable<AppMenuEntry> entries) =>
        entries.OfType<AppCommand>().Select(command => command.Header);
}
