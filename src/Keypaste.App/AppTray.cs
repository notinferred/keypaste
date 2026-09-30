using Avalonia.Controls;
using Avalonia.Platform;
using Keypaste.App.ViewModels;

namespace Keypaste.App;

/// <summary>The icon in the macOS menu bar or the Windows and Linux tray, with Open, Lock and Quit.</summary>
internal sealed class AppTray : IDisposable
{
    internal const string OpenHeader = "Open keypaste";
    internal const string LockHeader = "Lock";
    internal const string QuitHeader = "Quit keypaste";

    private readonly Func<bool> _canLock;
    private readonly NativeMenuItem _open;
    private readonly NativeMenuItem _lock;
    private readonly NativeMenuItem _quit;
    private TrayIcon? _icon;

    internal AppTray(Action open, Action lockNow, Func<bool> canLock, Action quit)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(lockNow);
        ArgumentNullException.ThrowIfNull(canLock);
        ArgumentNullException.ThrowIfNull(quit);

        _canLock = canLock;
        _open = new NativeMenuItem(OpenHeader) { Command = new RelayCommand(open) };
        _lock = new NativeMenuItem(LockHeader) { Command = new RelayCommand(lockNow, canLock) };
        _quit = new NativeMenuItem(QuitHeader) { Command = new RelayCommand(quit) };

        Menu = new NativeMenu();
        Menu.Add(_open);
        Menu.Add(_lock);
        Menu.Add(new NativeMenuItemSeparator());
        Menu.Add(_quit);
        Menu.NeedsUpdate += (_, _) => Refresh();
        Refresh();
    }

    internal NativeMenu Menu { get; }

    /// <summary>Whether the icon is showing.</summary>
    internal bool IsShown => _icon?.IsVisible == true;

    /// <summary>Shows or hides the icon; it is made only when first shown, so a Linux desktop that never opts in never registers one.</summary>
    internal void Show(bool shown)
    {
        if (shown && _icon is null)
        {
            using var image = AssetLoader.Open(new Uri(OperatingSystem.IsWindows()
                ? "avares://keypaste-app/Assets/keypaste.ico"
                : "avares://keypaste-app/Assets/keypaste-256.png"));

            _icon = new TrayIcon
            {
                Icon = new WindowIcon(image),
                ToolTipText = "keypaste",
                Menu = Menu,
            };
            _icon.Clicked += (_, _) => _open.Command?.Execute(null);
        }

        if (_icon is not null)
        {
            _icon.IsVisible = shown;
        }
    }

    /// <summary>Lock is offered only while a vault is open.</summary>
    internal void Refresh() => _lock.IsEnabled = _canLock();

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
