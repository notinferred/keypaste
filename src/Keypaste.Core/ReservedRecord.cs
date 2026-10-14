namespace Keypaste.Core;

/// <summary>One entry of a reserved group as a record store reads it: its UUID, its title and one of its string fields.</summary>
/// <param name="Id">The entry's UUID, its 16 bytes in RFC 9562's order.</param>
/// <param name="Title">The entry's title.</param>
/// <param name="Value">The field's value, or null when the entry has no such field.</param>
internal sealed record ReservedRecord(Guid Id, string Title, string? Value);
