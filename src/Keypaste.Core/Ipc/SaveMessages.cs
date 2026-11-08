using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>
/// An agent's request that the person save a value it never sees, at a place it names: an item's password or custom field,
/// or a variable of a project's environment.
/// </summary>
public sealed record SaveRequest
{
    /// <summary>The item, by its path, when the value belongs to an item; empty for a project's variable.</summary>
    public string Item { get; init; } = string.Empty;

    /// <summary>With <see cref="Item"/>, the field: <c>password</c> or a custom field's name.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>The project, when the value is one of its variables; empty for an item's field.</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>With <see cref="Project"/>, the environment.</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>With <see cref="Project"/>, the variable's name.</summary>
    public string Key { get; init; } = string.Empty;

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

    /// <summary>Whether the value is a project's variable rather than an item's field.</summary>
    public bool ForProject => Project.Length > 0;

    /// <summary>The field or variable the value is saved as.</summary>
    public string Named => ForProject ? Key : Field;

    /// <summary>Where the value goes, as the agent named it: the item's path, or the project and environment.</summary>
    public string Where => ForProject ? $"{Project}/{Environment}" : Item;
}

/// <summary>Whether the person saved the value; never the value.</summary>
/// <param name="Decision">Granted once the vault holds the value.</param>
/// <param name="Method">How it was decided.</param>
/// <param name="Reason">keypaste's own words.</param>
public sealed record SaveReply(AuditDecision Decision, AuditMethod Method, string Reason)
{
    /// <summary>Whether the process that answered recorded this request in its audit log before replying.</summary>
    /// <remarks>A frame without it reads as not recorded, the fail-closed reading.</remarks>
    public bool Recorded { get; init; }

    /// <summary>Where the value was saved, by path, or null when nothing was.</summary>
    public string? Where { get; init; }

    /// <summary>The session that answered.</summary>
    public string? Session { get; init; }
}
