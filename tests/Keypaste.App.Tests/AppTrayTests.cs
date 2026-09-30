using Avalonia.Controls;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>The tray menu's Open, Lock and Quit, and Lock offered only while a vault is open (G.4a).</summary>
public sealed class AppTrayTests
{
    [Fact]
    public Task Each_item_does_its_one_thing_and_Lock_follows_the_session() => HeadlessSession.On(() =>
    {
        var open = false;
        var unlocked = true;
        var quit = false;

        using var tray = new AppTray(() => open = true, () => unlocked = false, () => unlocked, () => quit = true);
        var items = tray.Menu.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).ToDictionary(item => item.Header!, StringComparer.Ordinal);

        Assert.Equal(new[] { AppTray.OpenHeader, AppTray.LockHeader, AppTray.QuitHeader }, items.Keys.ToArray());
        Assert.True(items[AppTray.LockHeader].IsEnabled);

        items[AppTray.LockHeader].Command!.Execute(null);
        tray.Refresh();
        Assert.False(unlocked);
        Assert.False(items[AppTray.LockHeader].IsEnabled);

        items[AppTray.OpenHeader].Command!.Execute(null);
        items[AppTray.QuitHeader].Command!.Execute(null);
        Assert.True(open);
        Assert.True(quit);

        Assert.False(tray.IsShown);
        tray.Show(true);
        Assert.True(tray.IsShown);
        tray.Show(false);
        Assert.False(tray.IsShown);
    });
}
