namespace Keypaste.App.Notifications;

/// <summary>The operating system's notifications, on whichever platform this is.</summary>
internal interface ISystemNotifications : IDisposable
{
    /// <summary>Posts a notice, tagged only with a random identifier of the platform's or this object's.</summary>
    /// <param name="notice">What it says.</param>
    /// <param name="pressed">Called on any thread when the person presses it.</param>
    /// <returns>What takes it down, or null when the system cannot or may not post it.</returns>
    Task<IPostedNotice?> PostAsync(SystemNotice notice, Action pressed);
}

/// <summary>A notice the system accepted.</summary>
internal interface IPostedNotice
{
    /// <summary>Takes it off the screen and out of the system's history.</summary>
    void Remove();
}

/// <summary>A platform that posts nothing: every notice falls back to keypaste's own window.</summary>
internal sealed class NoSystemNotifications : ISystemNotifications
{
    public Task<IPostedNotice?> PostAsync(SystemNotice notice, Action pressed) => Task.FromResult<IPostedNotice?>(null);

    public void Dispose()
    {
    }
}
