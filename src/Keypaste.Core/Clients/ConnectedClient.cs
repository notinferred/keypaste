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
    DateTimeOffset LastRequestAt);
