using Avalonia.Controls;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>The tray menu's Open, Run again, Lock and Quit, Run again and Lock offered only while they can act, and a template image in the macOS menu bar.</summary>
public sealed class AppTrayTests
{
    [Fact]
    public Task Each_item_does_its_one_thing_and_Run_again_and_Lock_follow_the_session() => HeadlessSession.On(() =>
    {
        var open = false;
        var unlocked = true;
        var ranBefore = false;
        var ranAgain = 0;
        var quit = false;

        using var tray = new AppTray(() => open = true, () => ranAgain++, () => unlocked && ranBefore, () => unlocked = false, () => unlocked, () => quit = true);
        var items = tray.Menu.Items.OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).ToDictionary(item => item.Header!, StringComparer.Ordinal);

        Assert.Equal(new[] { AppTray.OpenHeader, AppTray.RunAgainHeader, AppTray.LockHeader, AppTray.QuitHeader }, items.Keys.ToArray());
        Assert.True(items[AppTray.LockHeader].IsEnabled);

        // Nothing has run yet.
        Assert.False(items[AppTray.RunAgainHeader].IsEnabled);
        Assert.False(items[AppTray.RunAgainHeader].Command!.CanExecute(null));

        ranBefore = true;
        tray.Refresh();
        Assert.True(items[AppTray.RunAgainHeader].IsEnabled);
        items[AppTray.RunAgainHeader].Command!.Press();
        Assert.Equal(1, ranAgain);

        items[AppTray.LockHeader].Command!.Press();
        tray.Refresh();
        Assert.False(unlocked);
        Assert.False(items[AppTray.LockHeader].IsEnabled);
        Assert.False(items[AppTray.RunAgainHeader].IsEnabled);

        items[AppTray.OpenHeader].Command!.Press();
        items[AppTray.QuitHeader].Command!.Press();
        Assert.True(open);
        Assert.True(quit);

        Assert.False(tray.IsShown);
        tray.Show(true);
        Assert.True(tray.IsShown);
        Assert.Equal(OperatingSystem.IsMacOS(), MacOSProperties.GetIsTemplateIcon(tray.Icon!));
        tray.Show(false);
        Assert.False(tray.IsShown);
    });
}
