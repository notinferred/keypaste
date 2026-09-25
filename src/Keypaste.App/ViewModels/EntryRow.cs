using System.ComponentModel;
using Keypaste.Core;
using Keypaste.Core.Activity;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One line of the entry list: a name and where it lives, and nothing else.
/// </summary>
/// <param name="Title">The entry's title.</param>
/// <param name="GroupPath">Its group, or empty at the root.</param>
/// <param name="Fields">
/// Which of the entry's fields the current query was found in, or
/// <see cref="MatchedFields.None"/> when there is no query.
/// </param>
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
/// <para>
/// <b><see cref="Fields"/> is the one thing here that came from a search, and it is not a value.</b>
/// V.5b matches usernames and URLs, so a row can be in a result for a reason that is nowhere on it,
/// and saying which field matched is how the list answers "why is this here?". The matching happens
/// in <see cref="Core.Vault.Search"/> and what comes back is a member of a four-value enum, so the
/// username that matched never reaches this assembly — which is what keeps the paragraph above true
/// of a filtered list as well as an unfiltered one.
/// </para>
/// </remarks>
internal sealed record EntryRow(string Title, string GroupPath, MatchedFields Fields = MatchedFields.None) : INotifyPropertyChanged
{
    private EntryUseState _use;
    private string _lastUsedText = string.Empty;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether agents use this entry now, recently, or not: the row's dot.</summary>
    internal EntryUseState Use
    {
        get => _use;
        private set
        {
            if (_use != value)
            {
                _use = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Use)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInUse)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRecent)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIdle)));
            }
        }
    }

    internal bool IsInUse => _use == EntryUseState.InUse;

    internal bool IsRecent => _use == EntryUseState.Recent;

    internal bool IsIdle => _use == EntryUseState.Idle;

    /// <summary>"in use", "4m ago", "3h ago", "2d ago" or "never"; empty until activity is read.</summary>
    internal string LastUsedText
    {
        get => _lastUsedText;
        private set
        {
            if (!string.Equals(_lastUsedText, value, StringComparison.Ordinal))
            {
                _lastUsedText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUsedText)));
            }
        }
    }

    /// <summary>Takes this entry's use from the latest picture, without rebuilding the list.</summary>
    /// <param name="activity">The picture.</param>
    /// <param name="now">The time to measure from.</param>
    internal void Apply(EntryActivity activity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var use = activity.Use(Name);
        Use = use.State;
        LastUsedText = UseText.LastUsed(use, now);
    }

    /// <summary>Rows are the same row when they name the same entry for the same reason; their use is not identity.</summary>
    /// <param name="other">The other row.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(EntryRow? other) =>
        other is not null
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(GroupPath, other.GroupPath, StringComparison.Ordinal)
        && Fields == other.Fields;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Title, GroupPath, Fields);

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

    /// <summary>What sort of entry this is, read from which fields are filled in.</summary>
    internal EntryKind Kind { get; init; }

    /// <summary>The icon the row draws for its kind.</summary>
    internal string Icon => EntryKinds.Icon(Kind);

    /// <summary>The row's second line: its kind, then where it lives.</summary>
    /// <remarks>A variable says its project and profile rather than the <c>env/…</c> group path they are stored under.</remarks>
    internal string Summary =>
        EnvPlace.Of(GroupPath, Title) is { } place
            ? $"{EntryKinds.Label(Kind)} · {EntryNameSanitizer.Sanitize(place.Project).Text} · {place.Profile}"
            : GroupPath.Length == 0
                ? EntryKinds.Label(Kind)
                : $"{EntryKinds.Label(Kind)} · {Where}";

    /// <summary>
    /// Which fields a person cannot see on this row the query was found in, worded for the list.
    /// </summary>
    /// <remarks>
    /// Only the two that are not already on the row. A title or a group match shows its own evidence
    /// in the two columns beside this one, and labelling those would be noise on every row of an
    /// ordinary search. The strings are constants: nothing from the vault is interpolated here.
    /// </remarks>
    internal string Why =>
        (Fields.HasFlag(MatchedFields.Username), Fields.HasFlag(MatchedFields.Url)) switch
        {
            (true, true) => "username, URL",
            (true, false) => "username",
            (false, true) => "URL",
            _ => string.Empty,
        };
}
