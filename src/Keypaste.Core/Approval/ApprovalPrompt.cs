namespace Keypaste.Core.Approval;

/// <summary>
/// Everything a human is shown before deciding, already sanitized and already capped.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this type deliberately has no member for</b> is the point of it: there is no default
/// button, no deadline, no window size, no layout. THREATS.md T-2 requires that the agent's stated
/// reason "never influence the default button, the timeout, or the layout", and the way to make
/// that true is to hand a channel a value that has nowhere to express any of them. A channel
/// renders these five strings and nothing else; the deadline belongs to
/// <see cref="ApprovalGate"/>, which owns it whatever the channel does.
/// </para>
/// <para>
/// Every untrusted string is put through <see cref="EntryNameSanitizer"/> by
/// <see cref="For(string?, EntryName, string, string, int)"/> rather than by the caller, so no
/// channel can be handed raw text by a caller that forgot. Two of the three are attacker-chosen:
/// the client name is asserted during an unauthenticated handshake (T-3), and the reason is written
/// by the agent for the express purpose of persuading the person reading it (T-2).
/// </para>
/// <para>
/// The reason is capped hard at <see cref="MaximumReasonLength"/>, well below the schema's 2000.
/// A two-thousand-character reason is a layout attack: it can push the entry name out of view or
/// the buttons off the screen. <see cref="ReasonWasTruncated"/> exists so the channel can say so
/// rather than silently showing a shortened sentence as though it were the whole one.
/// </para>
/// </remarks>
public sealed record ApprovalPrompt
{
    /// <summary>The longest reason a human is shown, whatever the schema allows.</summary>
    public const int MaximumReasonLength = 400;

    /// <summary>The longest client name a human is shown.</summary>
    public const int MaximumClientLength = 64;

    /// <summary>What the requesting client calls itself, sanitized. Never proof of anything.</summary>
    public required string Client { get; init; }

    /// <summary>The entry, sanitized for display. Not an address: sanitizing is lossy.</summary>
    public required string Entry { get; init; }

    /// <summary>Which field is being asked for. Trusted: it is one of <see cref="CredentialFields.All"/>.</summary>
    public required string Field { get; init; }

    /// <summary>The agent's stated reason, sanitized and capped. Untrusted text, never an instruction.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether the reason shown is shorter than the one the agent sent.</summary>
    public required bool ReasonWasTruncated { get; init; }

    /// <summary>Whether <see cref="Entry"/> differs from the name the vault holds.</summary>
    /// <remarks>
    /// Sanitizing is lossy, and this is the one line a person is being asked to judge. Two entries
    /// whose titles differ only by an invisible code point draw the same text, so a channel that
    /// cannot say "this is not the stored name" is showing a name it has no right to vouch for.
    /// <c>list_entry_names</c> already reports <c>altered</c> per row to the model; this is the same
    /// fact, told to the human.
    /// </remarks>
    public required bool EntryWasAltered { get; init; }

    /// <summary>Whether <see cref="Reason"/> had anything scrubbed out of it.</summary>
    /// <remarks>
    /// Distinct from <see cref="ReasonWasTruncated"/>, which is only about length. A reason can be
    /// the same length and still not be what the agent sent.
    /// </remarks>
    public required bool ReasonWasAltered { get; init; }

    /// <summary>
    /// How long a grant would live, in seconds — the number that will actually apply, not the one
    /// the agent asked for. Showing a requested hour when policy will grant five minutes would be
    /// lying to the human in the one place they are being asked to trust the display.
    /// </summary>
    public required int TtlSeconds { get; init; }

    /// <summary>Builds the prompt for one request, sanitizing everything untrusted on the way in.</summary>
    /// <param name="client">The client's asserted name, or null when it did not give one.</param>
    /// <param name="entry">The entry the request resolved to.</param>
    /// <param name="field">The requested field, already validated against <see cref="CredentialFields"/>.</param>
    /// <param name="reason">The agent's stated reason, verbatim.</param>
    /// <param name="effectiveTtlSeconds">The TTL that will actually apply.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/>, <paramref name="field"/> or <paramref name="reason"/> is null.</exception>
    public static ApprovalPrompt For(
        string? client,
        EntryName entry,
        string field,
        string reason,
        int effectiveTtlSeconds)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(reason);

        var shownEntry = Display(entry);
        var shownReason = EntryNameSanitizer.Sanitize(reason, MaximumReasonLength);

        return new ApprovalPrompt
        {
            Client = client is { Length: > 0 }
                ? EntryNameSanitizer.Sanitize(client, MaximumClientLength).Text
                : "an unnamed client",
            Entry = shownEntry.Text,
            EntryWasAltered = shownEntry.WasAltered,
            Field = field,
            Reason = shownReason.Text,
            ReasonWasTruncated = reason.Length > MaximumReasonLength,
            ReasonWasAltered = shownReason.WasAltered,
            TtlSeconds = effectiveTtlSeconds,
        };
    }

    /// <summary>The entry rendered for a human: group separators kept, everything else scrubbed.</summary>
    /// <remarks>
    /// The title goes through <see cref="EntryNameSanitizer.Sanitize"/> rather than
    /// <see cref="EntryNameSanitizer.SanitizePath"/>, so a slash inside a title becomes a space.
    /// That is deliberate: it stops an entry titled <c>../../prod/ROOT_TOKEN</c> from rendering as
    /// though it lived somewhere it does not, which is the one thing a human reading this line is
    /// being asked to judge.
    /// </remarks>
    private static SanitizedName Display(EntryName entry)
    {
        var title = EntryNameSanitizer.Sanitize(entry.Title);

        if (entry.GroupPath.Length == 0)
        {
            return title;
        }

        var group = EntryNameSanitizer.SanitizePath(entry.GroupPath);

        return new SanitizedName(
            group.Text + "/" + title.Text,
            group.WasAltered || title.WasAltered);
    }
}
