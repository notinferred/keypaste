namespace Keypaste.Core.Approval;

/// <summary>
/// What came back from asking a human. Two values release a credential.
/// </summary>
/// <remarks>
/// Seven of the nine mean deny, and they are distinct only so the audit line and the refusal an
/// agent reads can say <em>why</em> — never so any of them can be treated as a maybe. docs/PRODUCT.md law
/// 3.2 makes deny the default and law 3.7 makes every error path a denial, so the safe way to read
/// this enum is: two values release, <see cref="Approved"/> and <see cref="ApprovedOnce"/>; anything
/// else is a no. <see cref="ApprovalAnswers.Releases"/> reads it that way.
/// </remarks>
public enum ApprovalAnswer
{
    /// <summary>Nobody was asked, because nothing could ask them. The default, and a denial.</summary>
    NoChannel = 0,

    /// <summary>A human allowed this request and its reuse for the timed grant the prompt offered.</summary>
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

    /// <summary>A human allowed this one request and nothing after it.</summary>
    ApprovedOnce = 8,
}
