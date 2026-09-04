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
    /// <summary>The entry's full path, the way core addresses it.</summary>
    internal string Path => GroupPath.Length == 0 ? Title : GroupPath + "/" + Title;

    /// <summary>The group, for a list that is not grouped by one.</summary>
    internal string Where => GroupPath.Length == 0 ? "—" : GroupPath;
}
