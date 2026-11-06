namespace Keypaste.App.Notifications;

/// <summary>
/// Asks once a run for the system's permission to post notifications, when the person opens a place where its dialog interrupts nothing and no prompt waits.
/// </summary>
/// <param name="notifications">The platform's notifications.</param>
/// <param name="promptWaits">Whether a prompt is on screen waiting for an answer, which a system dialog must not land beside.</param>
internal sealed class CalmPermission(ISystemNotifications notifications, Func<bool> promptWaits)
{
    private bool _asked;

    /// <summary>Called on the UI thread when the person opens such a place.</summary>
    internal void Offer()
    {
        if (!_asked && !promptWaits())
        {
            _asked = true;
            notifications.AskPermission();
        }
    }
}
