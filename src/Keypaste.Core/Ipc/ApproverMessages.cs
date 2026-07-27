using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>What the bridge is asking the approver for.</summary>
public enum ApproverMessageKind
{
    /// <summary>Not a message this version understands.</summary>
    Unknown = 0,

    /// <summary>The entry names the bridge may show an agent.</summary>
    Names = 1,

    /// <summary>One field of one entry, subject to a human saying yes.</summary>
    Credential = 2,
}

/// <summary>
/// A request for the entry names an agent may be shown. It carries nothing, deliberately.
/// </summary>
/// <remarks>
/// <c>list_entry_names</c> takes no arguments (THREATS.md T-4), so there is nothing agent-controlled
/// to forward and no parameter that could be coaxed into widening the listing. The exposure comes
/// from the bridge's own configuration, not from the call.
/// </remarks>
/// <param name="Exposure">The globs the bridge was configured with, applied again by the approver.</param>
public sealed record NamesRequest(IReadOnlyList<string> Exposure);

/// <summary>The names the approver is willing to have shown, or why there are none.</summary>
/// <param name="VaultUnlocked">Whether a vault was open at all.</param>
/// <param name="Names">The raw, unsanitized names inside the exposure. Sanitizing is the bridge's job.</param>
/// <param name="Reason">Why the list is empty, when it is. keypaste's own words, not an agent's.</param>
public sealed record NamesReply(bool VaultUnlocked, IReadOnlyList<EntryName> Names, string Reason);

/// <summary>An agent's credential request, forwarded to whoever can ask a human about it.</summary>
/// <remarks>
/// <b>The exposure travels with the request</b> rather than being configured on the approver, and
/// that is deliberate. The bridge can check a path-shaped entry against its globs before forwarding,
/// but it cannot check a handle, because resolving one needs the vault it does not have. So the
/// approver re-checks after it resolves, using the same globs — otherwise a handle would be a way
/// around the exposure rule (THREATS.md T-4). Sending them is not a weakening: whoever spawns the
/// bridge already chooses its argv, which THREATS.md assumption 2 says outright.
/// </remarks>
public sealed record CredentialRequest
{
    /// <summary>The <c>entry</c> argument exactly as the agent wrote it, handle or path.</summary>
    public required string Entry { get; init; }

    /// <summary>Which field, already checked against <see cref="Approval.CredentialFields"/>.</summary>
    public required string Field { get; init; }

    /// <summary>The agent's stated reason, verbatim and untrusted (THREATS.md T-2).</summary>
    public required string Reason { get; init; }

    /// <summary>How long the agent asked for. The approver decides what it actually gets.</summary>
    public required int TtlSeconds { get; init; }

    /// <summary>The globs the bridge was configured with.</summary>
    public required IReadOnlyList<string> Exposure { get; init; }

    /// <summary>What the client called itself. Unauthenticated, so display and audit only (T-3).</summary>
    public string? ClientName { get; init; }

    /// <summary>What version the client claimed. Same caveat.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>
    /// The name a human gave this bridge with <c>--client-label</c>, raw and exactly as written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="ClientName"/> this cannot be chosen by whoever <em>connects</em> — whoever
    /// spawns the bridge chooses it — which is the entire reason a policy rule keys on this and on
    /// nothing else (THREATS.md T-3). Null when the operator set none, and a null label matches no
    /// rule, including one written <c>client = "*"</c>.
    /// </para>
    /// <para>
    /// <b>Raw, not sanitized.</b> The operator writes <c>--client-label claude-code</c> in one file
    /// and <c>client = "claude-code"</c> in another, and those two strings have to compare as
    /// written. <c>EntryNameSanitizer</c> is lossy, and two distinct labels collapsing into one
    /// identical display string would be a widening. The audit line keeps using the sanitized form,
    /// because that one is for reading.
    /// </para>
    /// </remarks>
    public string? ClientLabel { get; init; }
}

/// <summary>The approver's answer, and — on exactly one path — the field value itself.</summary>
/// <remarks>
/// <para>
/// <b><see cref="ToString"/> is overridden, and that is not cosmetic.</b> A positional or default
/// record prints every member, so one interpolated string in a log line, an exception message or a
/// debugger-friendly trace would put a live credential somewhere it can never be taken back from.
/// The override is the only thing standing between that and a stray <c>$"{reply}"</c>.
/// </para>
/// <para>
/// <see cref="Value"/> is a <see cref="string"/> because it crossed a process boundary as bytes and
/// there is nowhere else for it to land. It cannot be zeroed. SECURITY.md says so rather than
/// implying otherwise.
/// </para>
/// </remarks>
public sealed record CredentialReply
{
    /// <summary>Whether anything was released.</summary>
    public required AuditDecision Decision { get; init; }

    /// <summary>How the decision was reached, for the audit line the bridge writes.</summary>
    public required AuditMethod Method { get; init; }

    /// <summary>keypaste's own one-line explanation. Trusted text, unlike the agent's reason.</summary>
    public required string Reason { get; init; }

    /// <summary>The entry the request resolved to, sanitized, or null when it resolved to none.</summary>
    public string? Entry { get; init; }

    /// <summary>The TTL that was actually granted, or zero.</summary>
    public int TtlSeconds { get; init; }

    /// <summary>The released field value. Present only when <see cref="Decision"/> is granted.</summary>
    public string? Value { get; init; }

    /// <summary>A description with the credential left out.</summary>
    /// <returns>The decision and method, and never the value.</returns>
    public override string ToString() =>
        $"CredentialReply {{ Decision = {Decision}, Method = {Method}, Value = {(Value is null ? "none" : "<redacted>")} }}";
}
