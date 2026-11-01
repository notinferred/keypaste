using Avalonia.Controls;
using Avalonia.Input;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;

namespace Keypaste.App;

/// <summary>An entry in one of the app's menus.</summary>
internal abstract record AppMenuEntry;

/// <summary>The line between two groups of a menu's commands.</summary>
internal sealed record AppMenuSeparator : AppMenuEntry;

/// <summary>A submenu whose entries are read each time it opens, as Open recent's are.</summary>
internal sealed record AppSubmenu(string Header, Func<IReadOnlyList<AppMenuEntry>> Entries) : AppMenuEntry;

/// <summary>One menu of the menu bar, or of the menu button on Windows and Linux.</summary>
internal sealed record AppMenuGroup(string Header, IReadOnlyList<AppMenuEntry> Entries);

/// <summary>One command of the menus and the window's chords.</summary>
/// <param name="Header">What its menu item says.</param>
/// <param name="Keys">The chords that run it on this platform; its menu item shows the first.</param>
/// <param name="CanRun">Whether it can run now; its menu item is off otherwise.</param>
/// <param name="Run">What it does.</param>
internal sealed record AppCommand(string Header, IReadOnlyList<KeyGesture> Keys, Func<bool> CanRun, Action Run) : AppMenuEntry
{
    /// <summary>Whether a chord runs it, where that differs from <see cref="CanRun"/>: the text commands leave a field's own keys to the field.</summary>
    internal Func<bool>? ChordCanRun { get; init; }

    /// <summary>The chord its menu item shows, if any.</summary>
    internal KeyGesture? Shown => Keys.Count > 0 ? Keys[0] : null;
}

/// <summary>What the commands act on: the running app's objects, or a test's.</summary>
internal sealed class AppCommandHost
{
    internal required AppVaultSession Session { get; init; }

    internal required Func<ShellViewModel?> Shell { get; init; }

    /// <summary>The unlock screen while it is the one showing; null behind the first run's terms.</summary>
    internal required Func<UnlockViewModel?> Unlock { get; init; }

    internal Func<Window?> Window { get; init; } = static () => null;

    /// <summary>The control the Edit commands act on; the window's focus unless the window keeps it while a menu is open.</summary>
    internal Func<IInputElement?>? Focused { get; init; }

    internal string? Home { get; init; }

    internal Action? About { get; init; }

    internal Action? Quit { get; init; }

    /// <summary>With a vault open, locks it and then opens the picker, or offers the file named, on the unlock screen.</summary>
    internal Action<string?>? OpenAnother { get; init; }

    internal Action? OpenWindow { get; init; }

    /// <summary>Brings a waiting prompt forward through its own disarming path (D-0456); false when none waits.</summary>
    internal Func<bool>? ShowPrompt { get; init; }

    internal Action<Uri>? OpenWeb { get; init; }

    internal Action? Hide { get; init; }

    internal Action? HideOthers { get; init; }

    internal Action? ShowAll { get; init; }
}

/// <summary>
/// The app's commands, each written once, for the macOS menu bar and Dock menu, the menu button on Windows and Linux,
/// and the window's chords (Q.2).
/// </summary>
/// <remarks>
/// On macOS an enabled menu item takes its chord before any control sees it, and a disabled one lets it through, so
/// the Edit commands act on the focused control themselves and are on only while it can act, and every command a
/// chord also reaches asks the same question here as there.
/// </remarks>
internal sealed class AppCommands
{
    internal const string ImportHeader = "Import from another app…";
    internal const string HelpAddress = "https://keypaste.com/docs/";
    internal const string ProblemAddress = "https://github.com/notinferred/keypaste/issues";

    private static readonly AppMenuSeparator _line = new();

    private readonly AppCommandHost _host;
    private readonly AppCommand _openVault;

    internal AppCommands(AppCommandHost host, bool? mac = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        Mac = mac ?? OperatingSystem.IsMacOS();
        var command = Mac ? KeyModifiers.Meta : KeyModifiers.Control;
        KeyGesture[] Chord(Key key, KeyModifiers also = KeyModifiers.None) => [new(key, command | also)];

        var about = new AppCommand("About keypaste", [], () => host.About is not null, () => host.About?.Invoke());
        var settings = new AppCommand("Settings…", Chord(Key.OemComma), () => Unmodal() is not null, () => Go(DestinationKind.Settings));
        var quit = new AppCommand("Quit keypaste", Chord(Key.Q), () => host.Quit is not null, () => host.Quit?.Invoke());
        AppMenu = Mac
            ?
            [
                about, _line, settings, _line,
                new AppCommand("Hide keypaste", Chord(Key.H), () => host.Hide is not null, () => host.Hide?.Invoke()),
                new AppCommand("Hide others", Chord(Key.H, KeyModifiers.Alt), () => host.HideOthers is not null, () => host.HideOthers?.Invoke()),
                new AppCommand("Show all", [], () => host.ShowAll is not null, () => host.ShowAll?.Invoke()),
                _line, quit,
            ]
            : [about, _line, settings, _line, quit];

        _openVault = new AppCommand("Open vault…", Chord(Key.O), CanOpenVault, () => OpenVault(null));
        var lockNow = new AppCommand("Lock", Chord(Key.L), () => host.Session.IsUnlocked, () => _ = host.Session.LockAsync(VaultLockReason.Manual));
        var file = new AppMenuGroup("File",
        [
            new AppCommand("New item", Chord(Key.N), () => Unmodal() is not null, () => OnItems(items => items.BeginAddCommand.Press())),
            new AppCommand("New group", [], () => Unmodal() is not null, () => OnItems(items => items.BeginCreateGroupCommand.Press())),
            _line,
            _openVault,
            new AppSubmenu("Open recent", RecentEntries),
            _line,
            new AppCommand("Close window", Chord(Key.W), () => host.Window() is { IsVisible: true }, () => host.Window()?.Close()),
            lockNow,
            _line,
            new AppCommand(ImportHeader, [], CanImport, () => Unmodal()?.ImportCommand.Press()),
            new AppCommand("Import .env…", [], () => Unmodal() is not null, () => Unmodal()?.ImportEnvCommand.Press()),
            new AppCommand("Export…", [], () => Unmodal() is not null, Export),
        ]);

        var edit = new AppMenuGroup("Edit",
        [
            new AppCommand("Undo", Chord(Key.Z), () => CanUndo(text: true), Undo) { ChordCanRun = () => CanUndo(text: false) },
            new AppCommand("Redo", Chord(Key.Z, KeyModifiers.Shift), () => Text() is { CanRedo: true }, () => Text()?.Redo()) { ChordCanRun = static () => false },
            _line,
            new AppCommand("Cut", Chord(Key.X), () => Text() is { CanCut: true }, () => Text()?.Cut()) { ChordCanRun = static () => false },
            new AppCommand("Copy", Chord(Key.C), () => CanCopy(text: true), Copy) { ChordCanRun = () => CanCopy(text: false) },
            new AppCommand("Paste", Chord(Key.V), CanPaste, Paste) { ChordCanRun = static () => false },
            new AppCommand("Select all", Chord(Key.A), () => Text() is { Text.Length: > 0 }, () => Text()?.SelectAll()) { ChordCanRun = static () => false },
            new AppCommand("Delete", Mac ? [new(Key.Back, KeyModifiers.Meta)] : [new(Key.Delete)], CanDelete, Delete),
            _line,
            new AppCommand(
                "Find",
                Mac ? [new(Key.F, command), new(Key.F, command | KeyModifiers.Alt), new(Key.K, command)] : [new(Key.F, command), new(Key.K, command)],
                () => Unmodal() is not null,
                () => Unmodal()?.FocusSearch()),
        ]);

        var view = new AppMenuGroup("View",
        [
            .. Destinations.Places.Select(place => new AppCommand(
                place.Title,
                [new(DigitKey(place.Shortcut), command), new(NumPadKey(place.Shortcut), command)],
                () => Unmodal() is not null,
                () => Unmodal()?.GoTo(place.Shortcut))),
            _line,
            new AppCommand(
                "Enter full screen",
                Mac ? [new(Key.F, KeyModifiers.Control | KeyModifiers.Meta)] : [new(Key.F11)],
                () => host.Window() is { IsVisible: true },
                ToggleFullScreen),
        ]);

        var window = new AppMenuGroup("Window",
        [
            new AppCommand("Minimize", Chord(Key.M), () => host.Window() is { IsVisible: true }, () => SetState(WindowState.Minimized)),
            new AppCommand("Zoom", [], () => host.Window() is { IsVisible: true }, Zoom),
            _line,
            new AppCommand("Bring all to front", [], () => host.OpenWindow is not null, BringAllToFront),
        ]);

        var help = new AppMenuGroup("Help",
        [
            new AppCommand("keypaste help", [], () => host.OpenWeb is not null, () => host.OpenWeb?.Invoke(new Uri(HelpAddress))),
            new AppCommand("Report a problem", [], () => host.OpenWeb is not null, () => host.OpenWeb?.Invoke(new Uri(ProblemAddress))),
        ]);

        Menus = [file, edit, view, window, help];
        Dock = [lockNow, new AppCommand(_openVault.Header, [], CanOpenVault, OpenVaultFromDock)];
    }

    /// <summary>Whether these are macOS's commands and chords, with ⌘, or every other platform's, with Ctrl.</summary>
    internal bool Mac { get; }

    /// <summary>The app's own menu: keypaste's in the macOS menu bar, the first submenu of the menu button elsewhere.</summary>
    internal IReadOnlyList<AppMenuEntry> AppMenu { get; }

    /// <summary>File, Edit, View, Window and Help.</summary>
    internal IReadOnlyList<AppMenuGroup> Menus { get; }

    /// <summary>The macOS Dock icon's menu.</summary>
    internal IReadOnlyList<AppMenuEntry> Dock { get; }

    /// <summary>Every command with a chord, in menu order.</summary>
    internal IEnumerable<AppCommand> All =>
        AppMenu.Concat(Menus.SelectMany(menu => menu.Entries)).OfType<AppCommand>();

    /// <summary>The command a chord runs now, matched on its key and exactly its modifiers, or null for none.</summary>
    internal AppCommand? ForChord(Key key, KeyModifiers modifiers) =>
        All.FirstOrDefault(command =>
            command.Keys.Any(chord => chord.Key == key && chord.KeyModifiers == modifiers) && (command.ChordCanRun ?? command.CanRun)());

    private IInputElement? Focus() =>
        _host.Focused is { } focused ? focused() : _host.Window()?.FocusManager?.GetFocusedElement();

    private TextBox? Text() => Focus() as TextBox;

    private bool InField() => Focus() is TextBox or MaskedInput;

    // The import, share and YubiKey-touch dialogs are modal: nothing may move focus or the page out from under them.
    private ShellViewModel? Unmodal() =>
        _host.Shell() is { HasImport: false, HasShare: false, IsWaitingForTouch: false } shell ? shell : null;

    private EntriesViewModel? Items() => Unmodal()?.Content as EntriesViewModel;

    private void Go(DestinationKind kind)
    {
        if (Unmodal() is { } shell)
        {
            shell.Current = Destinations.Of(kind);
        }
    }

    private void OnItems(Action<EntriesViewModel> act)
    {
        Go(DestinationKind.Entries);

        if (Items() is { } items)
        {
            act(items);
        }
    }

    private void Export()
    {
        Go(DestinationKind.Settings);
        (Unmodal()?.Content as SettingsViewModel)?.ExportCommand.Press();
    }

    private bool CanImport() => Unmodal() is { } shell && shell.ImportCommand.CanExecute(null);

    private bool CanOpenVault() =>
        _host.Shell() is null
            ? _host.Unlock() is { } unlock && unlock.BrowseCommand.CanExecute(null)
            : Unmodal() is not null && _host.OpenAnother is not null;

    private void OpenVault(string? path)
    {
        if (_host.Shell() is not null)
        {
            _host.OpenAnother?.Invoke(path);
            return;
        }

        if (_host.Unlock() is { } unlock)
        {
            if (path is null)
            {
                unlock.BrowseCommand.Press();
            }
            else
            {
                _ = unlock.Offer(path);
            }
        }
    }

    private void OpenVaultFromDock()
    {
        _host.OpenWindow?.Invoke();
        OpenVault(null);
    }

    private IReadOnlyList<AppMenuEntry> RecentEntries()
    {
        var vaults = RecentVaults.Load(KeypasteHome.RecentPath(_host.Home));
        var clear = new AppCommand("Clear menu", [], () => vaults.Count > 0, ClearRecent);

        return
        [
            .. vaults.Select(vault => new AppCommand(Path.GetFileName(vault.Path), [], CanOpenVault, () => OpenVault(vault.Path))),
            _line,
            clear,
        ];
    }

    private void ClearRecent()
    {
        if (_host.Unlock() is { } unlock)
        {
            unlock.ForgetAll();
        }
        else
        {
            RecentVaults.Save(KeypasteHome.RecentPath(_host.Home), []);
        }
    }

    private bool CanUndo(bool text) =>
        Focus() is TextBox field ? text && field.CanUndo : !InField() && Items() is { CanUndoDelete: true };

    private void Undo()
    {
        if (Text() is { } field)
        {
            field.Undo();
        }
        else
        {
            Items()?.UndoDeleteCommand.Press();
        }
    }

    // A TextBox copies its selection even behind a password character unless CanCopy is asked first.
    private bool CanCopy(bool text) =>
        Focus() is TextBox field ? text && field.CanCopy
        : !InField() && Items()?.Detail is { } detail && detail.CopyPasswordCommand.CanExecute(null);

    private void Copy()
    {
        if (Text() is { } field)
        {
            field.Copy();
        }
        else
        {
            Items()?.Detail?.CopyPasswordCommand.Press();
        }
    }

    private bool CanPaste() => Focus() switch
    {
        TextBox field => field.CanPaste,
        MaskedInput secret => secret.CanPaste,
        _ => false,
    };

    private void Paste()
    {
        switch (Focus())
        {
            case TextBox field:
                field.Paste();
                break;
            case MaskedInput secret:
                secret.Paste();
                break;
            default:
                break;
        }
    }

    private bool CanDelete() => !InField() && Items() is { } items && items.DeleteCommand.CanExecute(null);

    // Delete asks first; the menu never reaches the confirming command.
    private void Delete() => Items()?.DeleteCommand.Press();

    private void SetState(WindowState state)
    {
        if (_host.Window() is { } window)
        {
            window.WindowState = state;
        }
    }

    private void Zoom() =>
        SetState(_host.Window()?.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);

    private void ToggleFullScreen() =>
        SetState(_host.Window()?.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen);

    private void BringAllToFront()
    {
        _host.OpenWindow?.Invoke();
        _ = _host.ShowPrompt?.Invoke();
    }

    private static Key DigitKey(int digit) => Key.D0 + digit;

    private static Key NumPadKey(int digit) => Key.NumPad0 + digit;
}
