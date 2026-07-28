namespace Keypaste.Core.Approval;

/// <summary>
/// What came back from asking a human. Exactly one value releases a credential.
/// </summary>
/// <remarks>
/// Seven of the eight mean deny, and they are distinct only so the audit line and the refusal an
/// agent reads can say <em>why</em> — never so any of them can be treated as a maybe. docs/PRODUCT.md law
/// 3.2 makes deny the default and law 3.7 makes every error path a denial, so the safe way to read
/// this enum is: anything that is not <see cref="Approved"/> is a no.
/// </remarks>
public enum ApprovalAnswer
{
    /// <summary>Nobody was asked, because nothing could ask them. The default, and a denial.</summary>
    NoChannel = 0,

    /// <summary>A human said yes to this specific request. The only value that releases anything.</summary>
    Approved = 1,

    /// <summary>A human said no.</summary>
    Denied = 2,

    /// <summary>Nobody answered inside the window. Silence is a denial, not a maybe.</summary>
    TimedOut = 3,

    /// <summary>The client gave up on the request, or the connection went away, before an answer.</summary>
    Cancelled = 4,

    /// <summary>
    /// Another request was already in front of a human. Refused rather than queued: a queue is a
    /// pipeline that eventually shows every prompt, which is the storm it was meant to prevent.
    /// </summary>
    Busy = 5,

    /// <summary>
    /// The same request was refused a moment ago and has not served its cooldown. Stops "the human
    /// said no, ask again immediately".
    /// </summary>
    Cooldown = 6,

    /// <summary>Asking went wrong. Fail closed (docs/PRODUCT.md law 3.7).</summary>
    Failed = 7,
}
