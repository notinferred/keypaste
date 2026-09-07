using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One line of the entry list: a name and where it lives, and nothing else.
/// </summary>
/// <param name="Title">The entry's title.</param>
/// <param name="GroupPath">Its group, or empty at the root.</param>
/// <remarks>
/// <para>
/// <b>No field value is materialised here, and that is the decision 4.1's hygiene gate existed to
/// force.</b> A username column is a disclosure surface no CLI verb has: <c>keypaste ls</c> prints
/// titles and groups, and that is what a listing is. It would also be readable over a shoulder and
/// in the marketing screenshots <c>the Ideas table in DECISIONS.md</c> plans and THREATS.md T-24 already worries about.
/// The detail pane widens to username, URL and notes for the one entry a person selected, which is
/// <c>keypaste get</c>'s scope minus the password.
/// </para>
/// <para>
/// The narrower consequence is what the test relies on: because a row cannot carry a password, "the
/// list holds no field value" is a claim about the list rather than about which entry happens to be
/// selected. An implementation that read every password into every row would fail
/// <c>SecretHygieneTests</c> on the entry it never selected.
/// </para>
/// </remarks>
internal sealed record EntryRow(string Title, string GroupPath)
{
    /// <summary>The row's identity, which is what core reads and writes through.</summary>
    /// <remarks>
    /// Carries the title the vault holds and is never sanitized. <see cref="Path"/> is the two
    /// joined, and joining is lossy: a title containing a separator and a group of that name
    /// produce the same string, so a mutation addressed by path can reach the wrong entry.
    /// </remarks>
    internal EntryName Name => new(GroupPath, Title);

    /// <summary>The entry's full path, as a person reads and types it.</summary>
    /// <remarks>
    /// <b>A label and a lookup, not an identity</b> — see <see cref="Name"/>. It carries the title
    /// the vault holds and is never sanitized, because selection matching goes through it. What the
    /// screen shows is <see cref="DisplayTitle"/> and <see cref="Where"/>.
    /// </remarks>
    internal string Path => GroupPath.Length == 0 ? Title : GroupPath + "/" + Title;

    /// <summary>The title as the list draws it, scrubbed of anything that misrepresents it.</summary>
    internal string DisplayTitle { get; } = EntryNameSanitizer.Sanitize(Title).Text;

    /// <summary>The group, for a list that is not grouped by one. Display only, so scrubbed.</summary>
    internal string Where { get; } =
        GroupPath.Length == 0 ? "—" : EntryNameSanitizer.SanitizePath(GroupPath).Text;
}
