using Keypaste.App.Notifications;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Xunit;

namespace Keypaste.App.Windows.Tests;

/// <summary>Toasts from the app's Windows build (Q.14). A hosted runner has no person to see one, so these read what Windows is told and how it answers.</summary>
public sealed class WindowsNotificationsTests
{
    [Fact]
    public void The_Windows_build_chooses_toasts_only_on_Windows()
    {
        using var chosen = SystemNotifications.ForThisProcess();

        Assert.Equal(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041), chosen is WindowsNotifications);
    }

    [Fact]
    public async Task A_copy_whose_ID_no_shortcut_registers_posts_nothing_and_stays_quiet()
    {
        Assert.SkipUnless(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041), "Toasts are Windows's.");

        using var notifications = new WindowsNotifications("com.keypaste.tests.never-registered");

        Assert.Null(await notifications.PostAsync(SystemNotice.RequestWaiting, () => { }));
    }

    /// <summary>A toast with no COM activator may leave with its banner, and this notice has no prompt behind it: the window shows it.</summary>
    [Fact]
    public async Task The_locked_notice_is_left_to_the_window_even_under_a_registered_ID()
    {
        Assert.SkipUnless(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041), "Toasts are Windows's.");

        const string id = "com.keypaste.tests.registered";
        var key = $@"Software\Classes\AppUserModelId\{id}";

        using (var registered = Registry.CurrentUser.CreateSubKey(key))
        {
            registered.SetValue("DisplayName", "keypaste tests");
        }

        try
        {
            using var notifications = new WindowsNotifications(id);

            Assert.Null(await notifications.PostAsync(SystemNotice.VaultLocked, () => { }));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        }
    }

    [Theory]
    [MemberData(nameof(Notices))]
    public void A_toast_holds_a_notices_words_and_nothing_else(string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041), "Windows reads a toast's XML with its own parser.");

        var notice = SystemNotice.All.Single(known => known.Kind == kind);
        var xml = new XmlDocument();
        xml.LoadXml(WindowsNotifications.ToastXml(notice));

        var texts = xml.GetElementsByTagName("text").Select(text => text.InnerText);
        Assert.Equal([notice.Title, notice.Body], texts);

        var action = Assert.Single(xml.GetElementsByTagName("action"));
        Assert.Equal(notice.Action, action.Attributes.GetNamedItem("content").InnerText);
        Assert.Equal(notice.Kind, action.Attributes.GetNamedItem("arguments").InnerText);

        var scenario = xml.DocumentElement.GetAttribute("scenario");
        Assert.Equal(notice.TimeSensitive ? "urgent" : string.Empty, scenario);
    }

    public static TheoryData<string> Notices => [.. SystemNotice.All.Select(notice => notice.Kind)];
}
