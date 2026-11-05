#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Keypaste.App.Notifications;

/// <summary>A toast through WinRT's <c>ToastNotificationManager</c>, under the AppUserModelID the MSI's Start menu shortcut registers.</summary>
/// <remarks>
/// A copy not installed by the MSI has no registered ID, which Windows reports by throwing as its setting is read, and
/// a person's or a policy's turning keypaste's notifications off reads as anything but Enabled; both count as unable to
/// post. Windows answers nothing about Focus Assist.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsNotifications : ISystemNotifications
{
    /// <summary>What <c>packaging/windows/Package.wxs</c> gives the shortcut.</summary>
    internal const string AppId = "com.keypaste.app";

    private const string _group = "keypaste";

    private readonly string _appId;
    private readonly ConcurrentDictionary<string, ToastNotification> _live = new(StringComparer.Ordinal);
    private bool _cleared;

    /// <param name="appId">The AppUserModelID.</param>
    internal WindowsNotifications(string appId) => _appId = appId;

    public Task<IPostedNotice?> PostAsync(SystemNotice notice, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(notice);

        // Microsoft describes a press from Notification Center only through a COM activator named by the shortcut's ToastActivatorCLSID, which keypaste lacks, so a toast may leave with its banner; a notice with no prompt behind it keeps its window.
        if (!notice.ComesWithPrompt)
        {
            return Task.FromResult<IPostedNotice?>(null);
        }

        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(_appId);

            if (notifier.Setting != NotificationSetting.Enabled)
            {
                return Task.FromResult<IPostedNotice?>(null);
            }

            // Whatever a run that died left in Notification Center goes first.
            if (!Interlocked.Exchange(ref _cleared, true))
            {
                ToastNotificationManager.History.Clear(_appId);
            }

            var xml = new XmlDocument();
            xml.LoadXml(ToastXml(notice));

            var tag = Guid.NewGuid().ToString("N");
            var toast = new ToastNotification(xml)
            {
                Tag = tag,
                Group = _group,
                Priority = notice.TimeSensitive ? ToastNotificationPriority.High : ToastNotificationPriority.Default,
            };
            toast.Activated += (_, _) => pressed();
            _live[tag] = toast;
            notifier.Show(toast);

            return Task.FromResult<IPostedNotice?>(new Posted(this, tag));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult<IPostedNotice?>(null);
        }
    }

    public void Dispose() => _live.Clear();

    /// <summary>The toast as Windows reads it, whose press and action both activate this process.</summary>
    internal static string ToastXml(SystemNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var toast = new XElement(
            "toast",
            new XAttribute("activationType", "foreground"),
            new XElement(
                "visual",
                new XElement(
                    "binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", notice.Title),
                    new XElement("text", notice.Body))),
            new XElement(
                "actions",
                new XElement(
                    "action",
                    new XAttribute("content", notice.Action),
                    new XAttribute("arguments", notice.Kind),
                    new XAttribute("activationType", "foreground"))));

        if (notice.TimeSensitive)
        {
            toast.SetAttributeValue("scenario", "urgent");
        }

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    private sealed class Posted(WindowsNotifications owner, string tag) : IPostedNotice
    {
        public void Remove()
        {
            owner._live.TryRemove(tag, out _);

            try
            {
                ToastNotificationManager.History.Remove(tag, _group, owner._appId);
            }
            catch (COMException)
            {
                // Windows no longer holds it, or never showed it; its press now reaches nothing either way.
            }
        }
    }
}
#endif
