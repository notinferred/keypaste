
namespace Keypaste.App.Notifications;

/// <summary>
/// One notice from the moment it is asked for until it is removed. Once removed its press does nothing, however late
/// the system delivers one, so a notice outliving its request, lock or session reaches nothing.
/// </summary>
internal sealed class RaisedNotice
{
    private readonly Lock _gate = new();
    private readonly Action _pressed;
    private IPostedNotice? _posted;
    private bool _removed;

    private RaisedNotice(Action pressed) => _pressed = pressed;

    /// <summary>Whether the system took it. Never faults: a platform that fails has only failed to post.</summary>
    private Task<bool> Accepted { get; set; } = Task.FromResult(false);

    /// <summary>Asks the system to post a notice, returning before it answers.</summary>
    /// <param name="notifications">The platform's notifications.</param>
    /// <param name="notice">What it says.</param>
    /// <param name="pressed">Called on the UI thread when the person presses it, until it is removed.</param>
    internal static RaisedNotice Raise(ISystemNotifications notifications, SystemNotice notice, Action pressed)
    {
        var raised = new RaisedNotice(pressed);
        raised.Accepted = raised.PostAsync(notifications, notice);
        return raised;
    }

    /// <summary>Whether the system took it within a time. A system still deciding after that counts as having refused.</summary>
    internal async Task<bool> AcceptedWithinAsync(TimeSpan time, TimeProvider clock)
    {
        try
        {
            return await Accepted.WaitAsync(time, clock).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Takes it down, or, if the system has not answered yet, as soon as it does. Doing it twice is not an error.</summary>
    internal void Remove()
    {
        IPostedNotice? posted;

        lock (_gate)
        {
            _removed = true;
            posted = _posted;
            _posted = null;
        }

        TakeDown(posted);
    }

    private async Task<bool> PostAsync(ISystemNotifications notifications, SystemNotice notice)
    {
        try
        {
            // Off the UI thread, whatever the platform's call does before it first waits: a stalled system holds back no prompt.
            if (await Task.Run(() => notifications.PostAsync(notice, Press)).ConfigureAwait(false) is not { } posted)
            {
                return false;
            }

            lock (_gate)
            {
                if (!_removed)
                {
                    _posted = posted;
                    return true;
                }
            }

            TakeDown(posted);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Press() => UiThread.Post(() =>
    {
        lock (_gate)
        {
            if (_removed)
            {
                return;
            }
        }

        _pressed();
    });

    private static void TakeDown(IPostedNotice? posted)
    {
        try
        {
            posted?.Remove();
        }
        catch (Exception)
        {
            // The system kept a notice whose press now reaches nothing, and nothing else is waiting on it.
        }
    }
}
