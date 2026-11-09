namespace Keypaste.Core.Approval;

/// <summary>
/// What came back from asking a human. One value releases a credential.
/// </summary>
/// <remarks>
/// All but two mean deny, and they are distinct only so the audit line and the refusal an
/// agent reads can say <em>why</em> — never so any of them can be treated as a maybe. docs/PRODUCT.md law
/// 3.2 makes deny the default and law 3.7 makes every error path a denial, so the safe way to read
/// this enum is: <see cref="ApprovedOnce"/> releases; anything else is a no.
/// <see cref="ApprovalAnswers.Releases"/> reads it that way. <see cref="AlwaysAllowed"/> releases only on the
/// credential path that offered it, which reads it itself; the gate turns either always into a denial on any
/// prompt that did not offer it.
/// </remarks>
public enum ApprovalAnswer
{
    /// <summary>Nobody was asked, because nothing could ask them. The default, and a denial.</summary>
    NoChannel = 0,

    /// <summary>A human said no.</summary>
    Denied = 2,

    /// <summary>Nobody answered inside the window. Silence is a denial, not a maybe.</summary>
    TimedOut = 3,

    /// <summary>The client gave up on the request, or the connection went away, before an answer.</summary>
    Cancelled = 4,

    /// <summary>
    /// Another request was in front of a human and this one could not wait its turn: its agent already had
    /// <see cref="ApprovalLimits.MaximumWaiting"/> waiting, or it names no agent and so never waits (F.80).
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

    /// <summary>A human allowed this request and confirmed that this agent always gets this item's field, or the item they picked, without asking.</summary>
    AlwaysAllowed = 9,

    /// <summary>A human refused this request and confirmed that this agent is always refused this item's field without asking.</summary>
    AlwaysBlocked = 10,
}
