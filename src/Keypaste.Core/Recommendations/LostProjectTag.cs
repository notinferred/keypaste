using System.Globalization;

namespace Keypaste.Core.Recommendations;

/// <summary>
/// A project environment an entry was in and is no longer, lost in a version that changed more than its tags (V.11).
/// </summary>
/// <param name="Entry">The entry that lost it.</param>
/// <param name="EntryUuid">The entry's KDBX identifier as hex, which survives a rename.</param>
/// <param name="Tag">The tag that put it there, as the last version holding it wrote it.</param>
/// <param name="Project">The project the tag named.</param>
/// <param name="Environment">The environment the tag named.</param>
/// <param name="Protects">Whether the tag made every agent request for the entry ask live.</param>
/// <param name="LostUtc">When the version that dropped it was written.</param>
public sealed record LostProjectTag(
    EntryName Entry,
    string EntryUuid,
    string Tag,
    string Project,
    string Environment,
    bool Protects,
    DateTime LostUtc)
{
    /// <summary>What a dismissal remembers: the tag and when it was lost, so the same tag lost again is listed again.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Tag}@{LostUtc:O}");
}

/// <summary>Finds project tags an entry lost while something else about it changed (V.11).</summary>
/// <remarks>
/// A version that changed tags alone was a tag edit in keypaste, KeePassXC or another app that keeps
/// tags, and is not reported; one that dropped a tag while changing anything else, as an app that does
/// not keep tags does when it saves an edit, is. It reads tags and compares versions, and never returns
/// a value.
/// </remarks>
public static class LostProjectTagCheck
{
    /// <summary>Checks every live entry outside keypaste's own groups.</summary>
    /// <param name="vault">The unlocked vault.</param>
    /// <returns>The findings, by entry in tree order.</returns>
    public static IReadOnlyList<LostProjectTag> Scan(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return [.. vault.ReadLostProjectTags().Where(lost => !ReservedGroups.IsReserved(lost.Entry.GroupPath))];
    }
}
