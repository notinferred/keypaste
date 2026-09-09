namespace Keypaste.Core.Audit;

/// <summary>The authorization answer a bridge gave.</summary>
public enum AuditDecision
{
    /// <summary>Nothing was released.</summary>
    Denied = 0,

    /// <summary>The request was allowed.</summary>
    Granted = 1,
}

/// <summary>How a decision was reached.</summary>
/// <remarks>
/// An agent reads a refusal and decides whether to try again: <see cref="OutOfScope"/> means never,
/// <see cref="Prompt"/> with <see cref="AuditDecision.Denied"/> means a person said no, and only
/// <see cref="Busy"/> and <see cref="TimedOut"/> mean nobody got to it.
/// Only <see cref="Exposure"/>, <see cref="Prompt"/>, <see cref="GrantCache"/> and
/// <see cref="Policy"/> ever accompany <see cref="AuditDecision.Granted"/>.
/// <see cref="Policy"/> is the one grant no person saw, so it must never be logged as
/// <see cref="Prompt"/> or <see cref="GrantCache"/>, which both claim a person acted.
/// Adding a member means adding a case to <c>AuditLog.Wire</c>.
/// </remarks>
public enum AuditMethod
{
    /// <summary>The vault could not be opened, so there was nothing to answer with.</summary>
    VaultLocked = 0,

    /// <summary>No approval path existed. Written by keypaste 2.1; kept because the log is
    /// append-only and old lines still mean what they meant when written.</summary>
    NotImplemented = 1,

    /// <summary>The entry named lies outside what this server was told it may expose.</summary>
    OutOfScope = 2,

    /// <summary>The arguments did not satisfy the tool's schema.</summary>
    InvalidRequest = 3,

    /// <summary>Allowed because everything named lay inside the configured exposure. Listing names
    /// only; releasing a credential always needs a person or a policy.</summary>
    Exposure = 4,

    /// <summary>A person was shown this specific request and answered it (docs/PRODUCT.md law 3.2).</summary>
    Prompt = 5,

    /// <summary>Served from a grant a person had already given, inside its TTL. Still a line of its
    /// own, and it records the reason given for <em>this</em> request — the one nobody read.</summary>
    GrantCache = 6,

    /// <summary>Nobody answered inside the window. Silence is a denial.</summary>
    TimedOut = 7,

    /// <summary>The client gave up on the request, or went away, before anyone answered.</summary>
    Cancelled = 8,

    /// <summary>No approver was reachable, so there was nobody to ask.</summary>
    NoApprover = 9,

    /// <summary>Another request was already in front of a person. Refused rather than queued.</summary>
    Busy = 10,

    /// <summary>The same request was refused a moment ago and has not served its cooldown.</summary>
    Cooldown = 11,

    /// <summary>Asking, resolving or reading went wrong. Fail closed (docs/PRODUCT.md law 3.7).</summary>
    Failed = 12,

    /// <summary>Pre-authorized by a rule in the user's policy file, so nobody was asked. The
    /// record's reason names which rule.</summary>
    Policy = 13,

    /// <summary>A policy rule covered this request but had spent its hourly allowance.</summary>
    /// <remarks>
    /// Denied rather than escalated to a person: falling through to a prompt turns a quota into a
    /// prompt generator, one per request once the allowance is burned (THREATS.md T-11), and makes
    /// <c>keypaste policy ls</c> lie about what <c>max_per_hour</c> means.
    /// </remarks>
    PolicyLimit = 14,

    /// <summary>A tool was called before the <c>initialize</c> handshake completed, so nothing is
    /// known about who is calling.</summary>
    /// <remarks>
    /// A client that ignores the protocol still gets its request read. Answering would put it to a
    /// person, or match it against a rule, with the caller missing from both the dialog and the log —
    /// the one field the human judges by.
    /// </remarks>
    NotInitialized = 15,

    /// <summary>The request was authorized and the reply carrying the field would not fit one
    /// message, so nothing was returned.</summary>
    /// <remarks>
    /// A word of its own rather than a reuse: <see cref="Failed"/> says asking went wrong and here it
    /// went right, while <see cref="Prompt"/> beside <see cref="AuditDecision.Denied"/> already means
    /// a person said no. The reason names which authority the release had, so a rule's release is
    /// never written up as a human act. The only method where a denial follows an authorization.
    /// </remarks>
    Undeliverable = 16,
}

/// <summary>Who asked.</summary>
/// <remarks>
/// <paramref name="Name"/> and <paramref name="Version"/> are <b>unauthenticated</b>: any process
/// that can spawn the server can claim any name, so they are audit fields and never authorization
/// inputs (THREATS.md T-3). <paramref name="Label"/> cannot be chosen by whoever connects, but
/// whoever spawns the server does choose it, so it identifies a configuration rather than a caller.
/// </remarks>
/// <param name="Name">The client's self-declared name, sanitized and capped.</param>
/// <param name="Version">The client's self-declared version.</param>
/// <param name="Label">The name a human gave this server via <c>--client-label</c>.</param>
public sealed record AuditClient(string? Name, string? Version, string? Label)
{
    /// <summary>A client that said nothing about itself.</summary>
    public static AuditClient Unknown { get; } = new(null, null, null);
}

/// <summary>The arguments a tool was called with, reduced to what is safe to keep.</summary>
/// <remarks>
/// Nothing here is ever a field <em>value</em>: <see cref="Field"/> records which field was asked
/// for, never its contents, and no property on this type can hold a secret.
/// </remarks>
public sealed record AuditArgs
{
    /// <summary>The longest agent-written reason kept verbatim in the log.</summary>
    public const int ReasonExcerptLength = 200;

    /// <summary>The longest <c>entry</c> argument kept.</summary>
    public const int EntryLength = 128;

    /// <summary>A call that takes no arguments, such as <c>list_entry_names</c>.</summary>
    public static AuditArgs None { get; } = new();

    /// <summary>The <c>entry</c> argument as the agent wrote it, sanitized and capped.</summary>
    public string? Entry { get; init; }

    /// <summary>Whether that argument was a handle, a path, or neither.</summary>
    public EntryAddressKind? EntryKind { get; init; }

    /// <summary>Which field was requested, or <c>invalid</c> when it was not one keypaste knows.</summary>
    public string? Field { get; init; }

    /// <summary>The lifetime the agent asked for, or <c>-1</c> when it was missing or unparseable.</summary>
    public int? TtlSeconds { get; init; }

    /// <summary>The opening of the agent's stated reason, sanitized and capped.</summary>
    public string? ReasonExcerpt { get; init; }

    /// <summary>The reason's true length, so truncation is visible rather than silent.</summary>
    public int? ReasonLength { get; init; }

    /// <summary>Lowercase hex SHA-256 of the raw reason.</summary>
    /// <remarks>
    /// It carries its weight on a <see cref="AuditMethod.GrantCache"/> line, where nobody was shown
    /// anything: comparing that hash with the earlier <see cref="AuditMethod.Prompt"/> line's is how
    /// a reason that changed after approval becomes visible (THREATS.md T-12).
    /// </remarks>
    public string? ReasonSha256 { get; init; }

    /// <summary>Reduces a credential request to what the log keeps.</summary>
    /// <remarks>
    /// The reason is unbounded text written by the agent whose purpose is to persuade a person, which
    /// makes it the likeliest injection payload in the protocol (THREATS.md T-2), and <c>keypaste
    /// log</c> renders it in a table a human reads. Keeping all three of an excerpt, the true length
    /// and a hash serves that reader without letting the log quietly lie about what was cut.
    /// </remarks>
    public static AuditArgs ForCredentialRequest(string entry, string? field, int ttlSeconds, string reason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(reason);

        // Prose, so the slashes survive: a reason impersonates nothing.
        var excerpt = EntryNameSanitizer.SanitizeProse(reason, ReasonExcerptLength).Text;

        // Segment-wise, so the separators survive. An audit line whose whole job is to say *which
        // entry* was asked for must not render env/dev/STRIPE_KEY as "env dev STRIPE_KEY".
        var entryText = EntryNameSanitizer.SanitizePath(entry, maximumLength: EntryLength).Text;

        return new AuditArgs
        {
            Entry = entryText,
            EntryKind = EntryHandle.Classify(entry),
            Field = field ?? "invalid",
            TtlSeconds = ttlSeconds,
            ReasonExcerpt = excerpt,
            ReasonLength = reason.Length,
            ReasonSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reason))),
        };
    }
}

/// <summary>One line of the audit trail: who asked for what, and what keypaste did about it.</summary>
/// <remarks>
/// <b>Never present, at any schema version:</b> a password, user name, URL, notes, the master
/// password, or any entry title read out of the vault. The only entry text recorded is the argument
/// the agent itself supplied.
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>The schema version, written on every line from the first.</summary>
    /// <remarks>
    /// Version 2 adds <c>prev</c> and <c>hash</c> and redefines <c>seq</c> as a line's position in the
    /// chain rather than a count of what one process wrote. Older lines are reported as "predates the
    /// chain" rather than as "tampered with", which is what keeps <c>log verify</c> from crying wolf.
    /// </remarks>
    public const int SchemaVersion = 2;

    /// <summary>The name of the tool that was called.</summary>
    public required string Tool { get; init; }

    /// <summary>Who called it.</summary>
    public required AuditClient Client { get; init; }

    /// <summary>What they asked for.</summary>
    public AuditArgs Args { get; init; } = AuditArgs.None;

    /// <summary>Whether anything was released.</summary>
    public required AuditDecision Decision { get; init; }

    /// <summary>How that was decided.</summary>
    public required AuditMethod Method { get; init; }

    /// <summary>keypaste's own one-line explanation. Trusted text, unlike the agent's reason.</summary>
    public required string Reason { get; init; }

    /// <summary>The globs this server was permitted to name, as configured.</summary>
    /// <remarks>
    /// On every line, including listings: what this server could ever have named cannot be recovered
    /// from a configuration file that has been edited since.
    /// </remarks>
    public IReadOnlyList<string> Exposure { get; init; } = [];
}
