namespace Keypaste.App.Notifications;

/// <summary>
/// Decides whether a request whose prompt is about to appear also earns a notification, posted once the prompt is up: only while keypaste is in the
/// background, and no more than one every <see cref="Spacing"/>, since a time-sensitive notice can break through
/// the person's Focus and an agent chooses when it asks. The prompt appears either way.
/// </summary>
/// <param name="notifications">The platform's notifications.</param>
/// <param name="clock">The clock the spacing runs on.</param>
/// <param name="inFront">Whether one of keypaste's windows has the person's focus.</param>
internal sealed class RequestNotices(ISystemNotifications notifications, TimeProvider clock, Func<bool> inFront)
{
    internal static readonly TimeSpan Spacing = TimeSpan.FromSeconds(30);

    // Read and written on the UI thread only.
    private long? _lastPosted;

    /// <summary>Whether this request earns a notice, which spends the spacing whether or not the post then succeeds.</summary>
    internal bool Earned()
    {
        if (inFront() || (_lastPosted is { } last && clock.GetElapsedTime(last) < Spacing))
        {
            return false;
        }

        _lastPosted = clock.GetTimestamp();
        return true;
    }

    /// <summary>Asks the system to post the notice.</summary>
    /// <param name="pressed">Called on the UI thread when the person presses it, until it is removed.</param>
    /// <returns>What takes it down.</returns>
    internal RaisedNotice Raise(Action pressed) => RaisedNotice.Raise(notifications, SystemNotice.RequestWaiting, pressed);
}
