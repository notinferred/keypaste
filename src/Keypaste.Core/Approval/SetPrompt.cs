namespace Keypaste.Core.Approval;

/// <summary>One field of a linked set as the person is shown it.</summary>
/// <param name="Entry">The item, sanitized as <see cref="ApprovalPrompt.Shown"/> writes it.</param>
/// <param name="Field">The field, scrubbed for display.</param>
/// <param name="EntryWasAltered">Whether <paramref name="Entry"/> differs from the name the vault holds.</param>
public sealed record SetPromptField(string Entry, string Field, bool EntryWasAltered)
{
    /// <summary>The item's own title, sanitized, which a prompt names it by; <see cref="Entry"/> is shown under Details.</summary>
    public string ItemTitle
    {
        get => _itemTitle ?? Entry[(Entry.LastIndexOf('/') + 1)..];
        init => _itemTitle = value;
    }

    private readonly string? _itemTitle;
}

/// <summary>
/// A linked set in front of a person: the agent, each item and field, and the agent's reason, already sanitized and capped,
/// with nowhere to express a default, a deadline or a layout, as <see cref="ApprovalPrompt"/> has none (THREATS.md T-2).
/// </summary>
/// <remarks>It offers Deny and Allow once and nothing else: no answer is ever kept for a set (PRODUCT §2).</remarks>
public sealed record SetPrompt
{
    /// <summary>What the requesting client calls itself, sanitized. Never proof of anything.</summary>
    public required string Client { get; init; }

    /// <summary>The bridge's <c>--client-label</c>, sanitized, or null when it was started without one.</summary>
    public string? Label { get; init; }

    /// <summary>Who is asking, as the requester line says it: verified only from what the system said of the connection, never from the label.</summary>
    public RequesterIdentity Identity
    {
        get => _identity ?? new RequesterIdentity(Client, null, false);
        init => _identity = value;
    }

    private readonly RequesterIdentity? _identity;

    /// <summary>Each field asked for, the anchor's first.</summary>
    public required IReadOnlyList<SetPromptField> Fields { get; init; }

    /// <summary>The agent's stated reason, sanitized and capped at <see cref="ApprovalPrompt.MaximumReasonLength"/>. Untrusted text.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether the reason shown is shorter than the one the agent sent.</summary>
    public required bool ReasonWasTruncated { get; init; }

    /// <summary>Whether <see cref="Reason"/> had anything scrubbed out of it.</summary>
    public required bool ReasonWasAltered { get; init; }

    /// <summary>Whether any item shown differs from the name the vault holds.</summary>
    public bool EntryWasAltered => Fields.Any(shown => shown.EntryWasAltered);

    /// <summary>Builds the prompt for one set, sanitizing everything untrusted on the way in.</summary>
    /// <param name="client">The client's asserted name, or null.</param>
    /// <param name="clientLabel">The bridge's <c>--client-label</c>, or null.</param>
    /// <param name="fields">Each item the set resolved to and the field asked of it, the anchor's first.</param>
    /// <param name="reason">The agent's stated reason, verbatim.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    public static SetPrompt For(string? client, string? clientLabel, IEnumerable<(EntryName Entry, string Field)> fields, string reason)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(reason);

        var shownReason = EntryNameSanitizer.SanitizeProse(reason, ApprovalPrompt.MaximumReasonLength);

        var shownClient = client is { Length: > 0 } ? EntryNameSanitizer.Sanitize(client, ApprovalPrompt.MaximumClientLength).Text : "an unnamed agent";
        var shownLabel = clientLabel is { Length: > 0 } ? EntryNameSanitizer.Sanitize(clientLabel, ApprovalPrompt.MaximumClientLength).Text : null;

        return new SetPrompt
        {
            Client = shownClient,
            Label = shownLabel,
            Identity = RequesterIdentity.Current(shownClient, shownLabel),
            Fields =
            [
                .. fields.Select(field => Shown(field.Entry, field.Field)),
            ],
            Reason = shownReason.Text,
            ReasonWasTruncated = reason.Length > ApprovalPrompt.MaximumReasonLength,
            ReasonWasAltered = shownReason.WasAltered,
        };
    }

    private static SetPromptField Shown(EntryName entry, string field)
    {
        var shown = ApprovalPrompt.Display(entry);
        return new SetPromptField(shown.Text, EntryNameSanitizer.Sanitize(field).Text, shown.WasAltered) { ItemTitle = EntryNameSanitizer.Sanitize(entry.Title).Text };
    }
}
