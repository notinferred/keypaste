namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown when an agent asks them to save a value: who asks, where it would go and why, already sanitized,
/// and never a value, which only the person enters.
/// </summary>
/// <remarks>It has no member for a value, a default button or a layout (THREATS.md T-2).</remarks>
public sealed record SavePrompt
{
    /// <summary>The client's asserted name, sanitized.</summary>
    public required string Client { get; init; }

    /// <summary>The bridge's label, sanitized, or null.</summary>
    public string? Label { get; init; }

    /// <summary>Where the value goes: the item's path, or the project and environment, sanitized.</summary>
    public required string Where { get; init; }

    /// <summary>The field or variable it is saved as, sanitized.</summary>
    public required string Field { get; init; }

    /// <summary>Whether it is a project's variable rather than an item's field.</summary>
    public bool ForProject { get; init; }

    /// <summary>The agent's reason, sanitized and capped. Untrusted text.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether the reason was cut short.</summary>
    public bool ReasonWasTruncated { get; init; }

    /// <summary>Whether the reason or the place had to be scrubbed.</summary>
    public bool WasAltered { get; init; }

    /// <summary>Whether saving creates the item that holds it.</summary>
    public bool Creates { get; init; }

    /// <summary>Whether saving replaces a value the place already holds, which the person confirms before it is saved.</summary>
    public bool Replaces { get; init; }

    /// <summary>The prompt for one request, sanitizing everything untrusted on the way in.</summary>
    /// <param name="client">The client's asserted name, or null.</param>
    /// <param name="label">The bridge's <c>--client-label</c>, or null.</param>
    /// <param name="where">Where the value goes, as a path.</param>
    /// <param name="field">The field or variable.</param>
    /// <param name="reason">The agent's stated reason, verbatim.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    public static SavePrompt For(string? client, string? label, string where, string field, string reason)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(reason);

        var asked = ApprovalPrompt.For(client, EntryName.Parse(where), field, reason, label);
        var shownField = EntryNameSanitizer.Sanitize(field, CredentialFields.MaximumFieldLength);

        return new SavePrompt
        {
            Client = asked.Client,
            Label = asked.Label,
            Where = asked.Entry,
            Field = shownField.Text,
            Reason = asked.Reason,
            ReasonWasTruncated = asked.ReasonWasTruncated,
            WasAltered = asked.EntryWasAltered || asked.ReasonWasAltered || shownField.WasAltered,
        };
    }
}

/// <summary>The person's answer to a save prompt: whether to save, and the value they entered when they did.</summary>
/// <param name="Answer">What they said, or why they were not asked; only an allow with a value saves.</param>
/// <param name="Value">The value they typed, pasted or generated, or null.</param>
public readonly record struct SaveAnswer(ApprovalAnswer Answer, string? Value)
{
    /// <summary>A save prompt nobody could be shown.</summary>
    public static SaveAnswer NoChannel { get; } = new(ApprovalAnswer.NoChannel, null);

    /// <summary>Names the answer, never the value.</summary>
    /// <returns>The answer.</returns>
    public override string ToString() => Answer.ToString();
}
