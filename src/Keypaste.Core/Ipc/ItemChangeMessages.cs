using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>
/// An agent's request to change an item it may see: that the person allow moving it to Trash, marking it expired or deleting
/// for good an item the same agent created through a save the person approved, or adding or removing the needs-update tag unasked.
/// </summary>
public sealed record ItemChangeRequest
{
    /// <summary>The item, by its handle or its path.</summary>
    public required string Entry { get; init; }

    /// <summary>The change as the tool names it, such as <c>move_to_recycle_bin</c>.</summary>
    public required string Change { get; init; }

    /// <summary>With <c>mark_expired</c>, the day it expires as <c>yyyy-MM-dd</c>, or empty for now.</summary>
    public string Expires { get; init; } = string.Empty;

    /// <summary>The agent's stated reason, verbatim and untrusted.</summary>
    public required string Reason { get; init; }

    /// <summary>The bridge's <c>--expose</c> globs.</summary>
    public required IReadOnlyList<string> Exposure { get; init; }

    /// <summary>The client's asserted name, or null.</summary>
    public string? ClientName { get; init; }

    /// <summary>The client's asserted version, or null.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>The bridge's <c>--client-label</c>, or null.</summary>
    public string? ClientLabel { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>Whether the change was made.</summary>
/// <param name="Decision">Granted once the vault holds the change.</param>
/// <param name="Method">How it was decided.</param>
/// <param name="Reason">keypaste's own words.</param>
public sealed record ItemChangeReply(AuditDecision Decision, AuditMethod Method, string Reason)
{
    /// <summary>Whether the process that answered recorded this request in its audit log before replying.</summary>
    /// <remarks>A frame without it reads as not recorded, the fail-closed reading.</remarks>
    public bool Recorded { get; init; }

    /// <summary>The item changed, by the path it had, or null when nothing was changed.</summary>
    public string? Item { get; init; }

    /// <summary>The session that answered.</summary>
    public string? Session { get; init; }
}
