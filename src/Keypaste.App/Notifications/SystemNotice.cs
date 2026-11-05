namespace Keypaste.App.Notifications;

/// <summary>
/// What keypaste may put on a surface other apps, the lock screen and notification history can read: fixed words,
/// with no place for an item, field, group, value, vault or anything an agent wrote, its name included.
/// </summary>
internal sealed class SystemNotice
{
    /// <summary>Says a prompt is up, for a request that arrived while keypaste was in the background.</summary>
    internal static readonly SystemNotice RequestWaiting = new(
        "request-waiting",
        "A request is waiting for your answer",
        "Show the prompt to see who is asking and for what.",
        "Show the prompt",
        timeSensitive: true,
        comesWithPrompt: true);

    /// <summary>Says a bridge found the app locked.</summary>
    internal static readonly SystemNotice VaultLocked = new(
        "vault-locked",
        "An agent asked for a locked vault",
        "It was refused. Unlock the vault in keypaste, then ask the agent to try again.",
        "Open keypaste",
        timeSensitive: false,
        comesWithPrompt: false);

    private SystemNotice(string kind, string title, string body, string action, bool timeSensitive, bool comesWithPrompt)
    {
        Kind = kind;
        Title = title;
        Body = body;
        Action = action;
        TimeSensitive = timeSensitive;
        ComesWithPrompt = comesWithPrompt;
    }

    internal static IReadOnlyList<SystemNotice> All { get; } = [RequestWaiting, VaultLocked];

    /// <summary>Names the notice to the platform, which groups its action under this.</summary>
    internal string Kind { get; }

    internal string Title { get; }

    internal string Body { get; }

    /// <summary>What its one action says. No notice answers a request: its action only brings keypaste forward.</summary>
    internal string Action { get; }

    internal bool TimeSensitive { get; }

    /// <summary>Whether a prompt on screen says the same thing, so that a notice the person never sees loses nothing.</summary>
    internal bool ComesWithPrompt { get; }
}
