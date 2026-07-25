namespace Keypaste.Core;

/// <summary>
/// A single credential stored in a vault.
/// </summary>
/// <remarks>
/// This is a plain value carrying secret material in managed strings. It is deliberately
/// simple: the protection that matters is the encrypted container on disk (CORE.md law 3.4),
/// not an in-memory ceremony that .NET cannot honour. Callers should not persist instances
/// beyond the operation that needed them.
/// </remarks>
public sealed record VaultEntry
{
    /// <summary>
    /// The entry title. Together with <see cref="GroupPath"/> this forms the entry's path,
    /// which is how KeePassXC and the keypaste CLI address an entry.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>The user name, or an empty string when the entry has none.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The secret. Stored protected in the KDBX file.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>The associated URL, or an empty string when the entry has none.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Free-form notes. May contain line breaks.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// Slash-separated path of the containing group, relative to the root group and excluding
    /// it — for example <c>servers/production</c>. An empty string places the entry in the
    /// root group. Intermediate groups are created on demand.
    /// </summary>
    public string GroupPath { get; init; } = string.Empty;

    /// <summary>
    /// The entry's full path, as accepted by <see cref="Vault.Find(string)"/> and by
    /// <c>keepassxc-cli show</c>.
    /// </summary>
    public string Path => GroupPath.Length == 0 ? Title : GroupPath + "/" + Title;
}
