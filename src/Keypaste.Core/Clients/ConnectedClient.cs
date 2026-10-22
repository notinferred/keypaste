using Keypaste.Core.Approval;

namespace Keypaste.Core.Clients;

/// <summary>A bridge attached to the live session, as it described its client (display only, THREATS.md T-3).</summary>
/// <param name="ConnectionId">The owner's id for the connection.</param>
/// <param name="Name">What the client called itself, or null.</param>
/// <param name="Version">What version it claimed, or null.</param>
/// <param name="Label">The bridge's <c>--client-label</c>, or null.</param>
/// <param name="AttachedAt">When it attached.</param>
/// <param name="LastRequestAt">When it last asked for anything.</param>
public sealed record ConnectedClient(
    string ConnectionId,
    string? Name,
    string? Version,
    string? Label,
    DateTimeOffset AttachedAt,
    DateTimeOffset LastRequestAt)
{
    /// <summary>The globs it said it answers with when it attached, or empty when it said none (N.5).</summary>
    public IReadOnlyList<string> Exposure { get; init; } = [];

    /// <summary>How a screen and a narration name a client: its label, else its name, sanitized, else <c>an unnamed agent</c>.</summary>
    /// <param name="label">Its label, or null.</param>
    /// <param name="name">What it called itself, or null.</param>
    /// <returns>The name to show.</returns>
    public static string Named(string? label, string? name) =>
        label is { Length: > 0 } ? EntryNameSanitizer.Sanitize(label, ApprovalPrompt.MaximumClientLength).Text
        : name is { Length: > 0 } ? EntryNameSanitizer.Sanitize(name, ApprovalPrompt.MaximumClientLength).Text
        : "an unnamed agent";
}
