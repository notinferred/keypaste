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
/// <para>
/// The distinctions here are worth more than they look, because an agent reads a refusal and
/// decides whether to try again. <see cref="OutOfScope"/> means "keypaste will never give you
/// that"; <see cref="Prompt"/> alongside <see cref="AuditDecision.Denied"/> means a person
/// considered this one and said no; <see cref="Busy"/> and <see cref="TimedOut"/> mean nobody got
/// to it at all, and are the only two where trying again later is reasonable. Only
/// <see cref="Exposure"/>, <see cref="Prompt"/>, <see cref="GrantCache"/> and <see cref="Policy"/>
/// ever accompany <see cref="AuditDecision.Granted"/>.
/// </para>
/// <para>
/// <b><see cref="Policy"/> is the one that means no person was involved at all.</b> Every other
/// grant traces back to somebody looking at that specific request; a policy grant traces back to a
/// rule they wrote in advance, and the audit line is the only record that the release happened.
/// That is why it must never be logged as <see cref="Prompt"/> or <see cref="GrantCache"/>, both of
/// which claim a person acted.
/// </para>
/// <para>
/// <b>Adding a member means adding a case to <c>AuditLog.Wire</c>.</b> The switch there used to
/// fall back to <c>vault-locked</c>, so a new member would have been logged as something it was
/// not — the quietest possible way to corrupt the record law 3.3 requires. It now falls back to
/// <c>unknown</c>, and <c>AuditLogTests.EveryAuditMethod_HasItsOwnWireString</c> is what actually
/// stops the next member slipping through.
/// </para>
/// </remarks>
public enum AuditMethod
{
    /// <summary>The vault could not be opened, so there was nothing to answer with.</summary>
    VaultLocked = 0,

    /// <summary>
    /// No approval path existed. Written by keypaste 2.1, which denied everything; kept because the
    /// audit log is append-only and old lines still have to mean what they meant when written.
    /// </summary>
    NotImplemented = 1,

    /// <summary>The entry named lies outside what this server was told it may expose.</summary>
    OutOfScope = 2,

    /// <summary>The arguments did not satisfy the tool's schema.</summary>
    InvalidRequest = 3,

    /// <summary>
    /// Allowed because everything named lay inside the exposure the user configured. Applies to
    /// listing names; releasing a credential always needs a person or a policy.
    /// </summary>
    Exposure = 4,

    /// <summary>A person was shown this specific request and answered it (docs/PRODUCT.md law 3.2).</summary>
    Prompt = 5,

    /// <summary>
    /// Served from a grant a person had already given, inside its TTL. Still an agent access, so
    /// still a line of its own (law 3.3) — and the line records the reason given for <em>this</em>
    /// request, which is the one nobody read.
    /// </summary>
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

    /// <summary>
    /// Pre-authorized by a rule in the user's policy file, so nobody was asked. The record's reason
    /// names which rule (Stage 2.3, DECISIONS.md D-0028).
    /// </summary>
    Policy = 13,

    /// <summary>
    /// A policy rule covered this request but had spent its hourly allowance.
    /// </summary>
    /// <remarks>
    /// Denied rather than escalated to a person. Falling through to a prompt looks safer — a human
    /// still decides — but it turns a quota into a prompt generator: an agent that has burned a
    /// rule's allowance would start producing one prompt per request, which is THREATS.md T-11 with
    /// a lever attached. It would also make <c>keypaste policy ls</c> lie about what
    /// <c>max_per_hour</c> means.
    /// </remarks>
    PolicyLimit = 14,

    /// <summary>
    /// A tool was called before the <c>initialize</c> handshake completed, so nothing is known
    /// about who is calling.
    /// </summary>
    /// <remarks>
    /// The protocol forbids this, but a client that ignores the rule still gets its request read.
    /// Answering it would mean putting a request to a person, or matching it against a policy rule,
    /// with the caller's identity missing from both the dialog and the log — the one field the
    /// human has to judge the request by. Fail closed (docs/PRODUCT.md law 3.7) rather than serve a
    /// credential to nobody in particular.
    /// </remarks>
    NotInitialized = 15,
}

/// <summary>Who asked.</summary>
/// <param name="Name">
/// The client's self-declared name, sanitized and capped. <b>Unauthenticated</b>: any process that
/// can spawn the server can claim any name, so this is an audit field and never an authorization
/// input (THREATS.md T-3). Null when the client did not say.
/// </param>
/// <param name="Version">The client's self-declared version, on the same terms. Null when absent.</param>
/// <param name="Label">
/// The name a human gave this server in its configuration, via <c>--client-label</c>. Null when
/// unset. Unlike <paramref name="Name"/> it cannot be chosen by whoever connects — though whoever
/// spawns the server does choose it, so it identifies a configuration rather than a caller.
/// </param>
public sealed record AuditClient(string? Name, string? Version, string? Label)
{
    /// <summary>A client that said nothing about itself.</summary>
    public static AuditClient Unknown { get; } = new(null, null, null);
}

/// <summary>
/// The arguments a tool was called with, reduced to what is safe and useful to keep.
/// </summary>
/// <remarks>
/// Nothing here is ever a field <em>value</em>. <see cref="Field"/> records which field was asked
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
    /// Recorded alongside the excerpt so the reason a person was shown can be compared with the one
    /// that was recorded, even when the excerpt was truncated. It carries most of its weight on a
    /// <see cref="AuditMethod.GrantCache"/> line, where nobody was shown anything: comparing that
    /// line's hash with the earlier <see cref="AuditMethod.Prompt"/> line's is how a reason that
    /// changed after approval becomes visible (THREATS.md T-12).
    /// </remarks>
    public string? ReasonSha256 { get; init; }

    /// <summary>Reduces a credential request to what the log keeps.</summary>
    /// <param name="entry">The <c>entry</c> argument, as written.</param>
    /// <param name="field">The requested field name, or null when it was missing or unrecognised.</param>
    /// <param name="ttlSeconds">The requested lifetime, or <c>-1</c> when missing or unparseable.</param>
    /// <param name="reason">The agent's stated reason, as written.</param>
    /// <returns>The arguments, redacted and capped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> or <paramref name="reason"/> is null.</exception>
    /// <remarks>
    /// The reason is the hardest call in the schema. It is unbounded text written by the agent whose
    /// whole purpose is to persuade a person, which makes it the likeliest injection payload in the
    /// protocol (THREATS.md T-2) — and <c>keypaste log</c> renders it in a table a human reads. Keeping all
    /// three of an excerpt, the true length, and a hash serves the human without letting the log
    /// quietly lie about what was cut.
    /// </remarks>
    public static AuditArgs ForCredentialRequest(string entry, string? field, int ttlSeconds, string reason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(reason);

        var excerpt = EntryNameSanitizer.Sanitize(reason, ReasonExcerptLength).Text;

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

/// <summary>
/// One line of the audit trail: who asked for what, and what keypaste did about it.
/// </summary>
/// <remarks>
/// <para>
/// docs/PRODUCT.md law 3.3 requires every agent access to be logged locally, immutably, and readably —
/// who, which entry, when, granted or denied. This type is that line.
/// </para>
/// <para>
/// It does not conflict with law 3.5's ban on telemetry over entry names, because the two laws
/// govern different verbs: 3.5 is about data <em>leaving</em> the machine and 3.3 is about a record
/// <em>staying</em> on it. See THREATS.md T-9, which states the separation as a checkable property
/// rather than a promise.
/// </para>
/// <para>
/// <b>Never present, at any schema version:</b> a password, user name, URL, notes, the master
/// password, or any entry title read out of the vault. The only entry text recorded is the
/// argument the agent itself supplied.
/// </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>The schema version, written on every line from the first.</summary>
    /// <remarks>
    /// Present from day one so that Stage 2.4 could add the hash-chain fields and have older lines
    /// reported as "predates the chain" rather than as "tampered with" — the distinction that keeps
    /// the first <c>keypaste log verify</c> from crying wolf. Version 2 is that stage: it adds
    /// <c>prev</c> and <c>hash</c>, and redefines <c>seq</c> as a line's position in the chain
    /// rather than a count of what one process happened to write.
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
    /// On every line, including listings. "What could this server ever have named?" is the first
    /// question a post-incident reader asks, and it cannot be recovered from a configuration file
    /// that has been edited since.
    /// </remarks>
    public IReadOnlyList<string> Exposure { get; init; } = [];
}
