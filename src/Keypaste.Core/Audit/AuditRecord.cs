namespace Keypaste.Core.Audit;

/// <summary>The authorization answer a bridge gave.</summary>
public enum AuditDecision
{
    /// <summary>Nothing was released. Every decision in keypaste 2.1 is this one.</summary>
    Denied = 0,

    /// <summary>The request was allowed. Reachable from Stage 2.2 onwards.</summary>
    Granted = 1,
}

/// <summary>How a decision was reached.</summary>
/// <remarks>
/// The distinction between <see cref="OutOfScope"/> and <see cref="NotImplemented"/> is worth more
/// than it looks: the first means "keypaste will never give you that", the second means "keypaste
/// cannot ask yet". An agent that can tell them apart stops retrying the first.
/// </remarks>
public enum AuditMethod
{
    /// <summary>The vault could not be opened, so there was nothing to answer with.</summary>
    VaultLocked = 0,

    /// <summary>There is no approval path in this version, so the default deny stands.</summary>
    NotImplemented = 1,

    /// <summary>The entry named lies outside what this server was told it may expose.</summary>
    OutOfScope = 2,

    /// <summary>The arguments did not satisfy the tool's schema.</summary>
    InvalidRequest = 3,

    /// <summary>
    /// Allowed because everything named lay inside the exposure the user configured. Applies to
    /// listing names, which is the only thing this version can allow — releasing a credential
    /// always needs a person, and that is Stage 2.2's <c>prompt</c> and Stage 2.3's <c>policy</c>.
    /// </summary>
    Exposure = 4,
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
    /// Recorded alongside the excerpt so Stage 2.2 can check that the reason shown to the human in
    /// the approval dialog is the reason that was recorded, even when the excerpt was truncated.
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
    /// protocol (THREATS.md T-2) — and 2.4 renders the log as a table a human reads. Keeping all
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
/// CORE.md law 3.3 requires every agent access to be logged locally, immutably, and readably —
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
    /// Present from day one so Stage 2.4 can add the hash-chain fields and report older lines as
    /// "predates the chain" rather than as "tampered with" — the distinction that keeps the first
    /// <c>keypaste log verify</c> from crying wolf.
    /// </remarks>
    public const int SchemaVersion = 1;

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
