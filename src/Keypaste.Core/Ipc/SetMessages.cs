using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>One field of a linked set as the agent named it: an item, by handle or path, and one field of it.</summary>
/// <param name="Entry">The <c>entry</c> argument exactly as the agent wrote it.</param>
/// <param name="Field">The field, as <see cref="CredentialRequest.Field"/> takes it; never an attachment.</param>
public sealed record SetItem(string Entry, string Field);

/// <summary>
/// An agent's request for a linked set: fields of one item it may see, its anchor, and of the items <c>find_access</c> links to it,
/// which the person allows once or denies whole (SECURITY.md law 2).
/// </summary>
/// <remarks>The exposure travels with the request, as <see cref="CredentialRequest"/>'s does, so the owner re-checks every item after it resolves it.</remarks>
public sealed record SetRequest
{
    /// <summary>The fields asked for, the anchor's first.</summary>
    public required IReadOnlyList<SetItem> Items { get; init; }

    /// <summary>The agent's stated reason, verbatim and untrusted (THREATS.md T-2).</summary>
    public required string Reason { get; init; }

    /// <summary>The globs the bridge was configured with.</summary>
    public required IReadOnlyList<string> Exposure { get; init; }

    /// <summary>What the client called itself. Unauthenticated, so display and audit only (T-3).</summary>
    public string? ClientName { get; init; }

    /// <summary>What version the client claimed. Same caveat.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>The bridge's <c>--client-label</c>, raw, as <see cref="CredentialRequest.ClientLabel"/> is.</summary>
    public string? ClientLabel { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>One field a linked set released, in the place the request named it.</summary>
/// <remarks><see cref="ToString"/> leaves the value out, for the reason <see cref="CredentialReply"/>'s does.</remarks>
public sealed record SetField
{
    /// <summary>The item, sanitized as every prompt shows it.</summary>
    public required string Entry { get; init; }

    /// <summary>The field, as the request named it.</summary>
    public required string Field { get; init; }

    /// <summary>The released value.</summary>
    public required string Value { get; init; }

    /// <summary>How many seconds a released TOTP code has left in its period; zero for any other field.</summary>
    public int CodeSeconds { get; init; }

    /// <summary>The item's UUID as hex, which its audit line names; never on the wire.</summary>
    internal string? Item { get; init; }

    /// <summary>A description with the value left out.</summary>
    /// <returns>The item and field, never the value.</returns>
    public override string ToString() => $"SetField {{ Entry = {Entry}, Field = {Field}, Value = <redacted> }}";
}

/// <summary>The owner's answer to a <see cref="SetRequest"/>: every field, or none and why.</summary>
public sealed record SetReply
{
    /// <summary>Whether the set was released.</summary>
    public required AuditDecision Decision { get; init; }

    /// <summary>How the decision was reached, as its audit lines say.</summary>
    public required AuditMethod Method { get; init; }

    /// <summary>keypaste's own one-line explanation. Trusted text.</summary>
    public required string Reason { get; init; }

    /// <summary>Each field released, in the order asked; empty unless <see cref="Decision"/> is granted.</summary>
    public IReadOnlyList<SetField> Fields { get; init; } = [];

    /// <summary>Each item the owner resolved, as every prompt shows it, for a refusal's line; never on the wire.</summary>
    internal IReadOnlyList<string> Entries { get; init; } = [];

    /// <summary>The session that answered, or null when none did.</summary>
    public string? Session { get; init; }

    /// <summary>Whether the process that answered recorded this request in its audit log before replying.</summary>
    /// <remarks>A frame without it reads as not recorded, the fail-closed reading.</remarks>
    public bool Recorded { get; init; }

    /// <summary>A description with every value left out.</summary>
    /// <returns>The decision, method and how many fields, never a value.</returns>
    public override string ToString() => $"SetReply {{ Decision = {Decision}, Method = {Method}, Fields = {Fields.Count} }}";
}
