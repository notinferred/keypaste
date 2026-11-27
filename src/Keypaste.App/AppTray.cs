using Avalonia.Controls;
using Avalonia.Platform;

namespace Keypaste.App;

/// <summary>The icon in the macOS menu bar or the Windows and Linux tray, with Open, Run again, Lock and Quit.</summary>
internal sealed class AppTray : IDisposable
{
    internal const string OpenHeader = "Open keypaste";
    internal const string RunAgainHeader = "Run again";
    internal const string LockHeader = "Lock";
    internal const string QuitHeader = "Quit keypaste";

    private readonly Func<bool> _canLock;
    private readonly Func<bool> _canRunAgain;
    private readonly NativeMenuItem _open;
    private readonly NativeMenuItem _runAgain;
    private readonly NativeMenuItem _lock;
    private readonly NativeMenuItem _quit;
    private TrayIcon? _icon;

    internal AppTray(Action open, Action runAgain, Func<bool> canRunAgain, Action lockNow, Func<bool> canLock, Action quit)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(runAgain);
        ArgumentNullException.ThrowIfNull(canRunAgain);
        ArgumentNullException.ThrowIfNull(lockNow);
        ArgumentNullException.ThrowIfNull(canLock);
        ArgumentNullException.ThrowIfNull(quit);

        _canLock = canLock;
        _canRunAgain = canRunAgain;
        _open = new NativeMenuItem(OpenHeader) { Command = new RelayCommand(open) };
        _runAgain = new NativeMenuItem(RunAgainHeader) { Command = new RelayCommand(runAgain, canRunAgain) };
        _lock = new NativeMenuItem(LockHeader) { Command = new RelayCommand(lockNow, canLock) };
        _quit = new NativeMenuItem(QuitHeader) { Command = new RelayCommand(quit) };

        Menu = new NativeMenu();
        Menu.Add(_open);
        Menu.Add(_runAgain);
        Menu.Add(_lock);
        Menu.Add(new NativeMenuItemSeparator());
        Menu.Add(_quit);
        Menu.NeedsUpdate += (_, _) => Refresh();
        Refresh();
    }

    internal NativeMenu Menu { get; }

    internal bool IsShown => _icon?.IsVisible == true;

    /// <summary>The icon, once it has been shown.</summary>
    internal TrayIcon? Icon => _icon;

    /// <summary>Shows or hides the icon; it is made only when first shown, so a Linux desktop that never opts in never registers one.</summary>
    internal void Show(bool shown)
    {
        if (shown && _icon is null)
        {
            using var image = AssetLoader.Open(new Uri(
                OperatingSystem.IsWindows() ? "avares://keypaste-app/Assets/keypaste.ico"
                : OperatingSystem.IsMacOS() ? "avares://keypaste-app/Assets/keypaste-menu-bar.png"
                : "avares://keypaste-app/Assets/keypaste-256.png"));

            _icon = new TrayIcon
            {
                Icon = new WindowIcon(image),
                ToolTipText = "keypaste",
                Menu = Menu,
            };

            // The menu bar draws a template image in its own ink, light or dark.
            MacOSProperties.SetIsTemplateIcon(_icon, OperatingSystem.IsMacOS());
            _icon.Clicked += (_, _) => _open.Command?.Press();
        }

        if (_icon is not null)
        {
            _icon.IsVisible = shown;
        }
    }

    /// <summary>Lock is offered only while a vault is open, and Run again only while the vault of the last command a Run started is.</summary>
    internal void Refresh()
    {
        _lock.IsEnabled = _canLock();
        _runAgain.IsEnabled = _canRunAgain();
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
