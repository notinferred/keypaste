using System.ComponentModel;
using Keypaste.Core;
using Keypaste.Core.Activity;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One line of the entry list: a name, where it lives, its username and its web address, and nothing else.
/// </summary>
/// <param name="Title">The entry's title.</param>
/// <param name="GroupPath">Its group, or empty at the root.</param>
/// <param name="Fields">
/// Which of the entry's fields the current query was found in, or
/// <see cref="MatchedFields.None"/> when there is no query.
/// </param>
/// <remarks>
/// <para>
/// <b>The list shows title, kind, group, agent use, username and URL, and never a password or a
/// custom field's value (D-0488).</b> A username and a web address are what KeePassXC's table shows
/// too; the password, the notes and every custom field stay out of the row, so "the list holds no
/// secret" is a claim about the list rather than about which entry happens to be selected. An
/// implementation that read every password into every row would fail <c>SecretHygieneTests</c> on the
/// entry it never selected.
/// </para>
/// <para>
/// <b><see cref="Fields"/> and <see cref="Tags"/> are what came from a search, and neither is a
/// value.</b> The matching happens in <see cref="Core.Vault.Search"/>: what comes back is a member of a
/// fixed enum and the entry's own tags the query was in, which are labels rather than values (D-0485).
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

    /// <summary>When it was last used, for sorting: in use is the latest there is, and never answered has no time.</summary>
    internal long? LastUsedTicks { get; private set; }

    /// <summary>Takes this entry's use from the latest picture, without rebuilding the list.</summary>
    /// <param name="activity">The picture.</param>
    /// <param name="now">The time to measure from.</param>
    internal void Apply(EntryActivity activity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activity);

        // A path names both items of a pair, so of one of them only what names its UUID is its own.
        var use = activity.Use(Name, byItem: SharesItsName);
        Use = use.State;
        LastUsedText = UseText.LastUsed(use, now);
        LastUsedTicks = use.State == EntryUseState.InUse ? long.MaxValue : use.LastUsed?.UtcTicks;
    }

    /// <summary>Rows are the same row when they are the same item, show the same fields and are found for the same reason; their use is not identity.</summary>
    /// <param name="other">The other row.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(EntryRow? other) =>
        other is not null
        && string.Equals(Uuid, other.Uuid, StringComparison.Ordinal)
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(GroupPath, other.GroupPath, StringComparison.Ordinal)
        && string.Equals(Username, other.Username, StringComparison.Ordinal)
        && string.Equals(Url, other.Url, StringComparison.Ordinal)
        && string.Equals(SharedNameTell, other.SharedNameTell, StringComparison.Ordinal)
        && Fields == other.Fields
        && Tags.SequenceEqual(other.Tags, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Title, GroupPath, Fields);

    /// <summary>The item's KDBX UUID, which tells this row from another of the same name.</summary>
    internal required string Uuid { get; init; }

    /// <summary>What core reads and writes this item through.</summary>
    /// <remarks>
    /// Carries the title the vault holds and is never sanitized. <see cref="Path"/> is the two
    /// joined, and joining is lossy: a title containing a separator and a group of that name
    /// produce the same string, so a mutation addressed by path can reach the wrong entry.
    /// </remarks>
    internal EntryName Name => new(GroupPath, Title) { Uuid = Uuid };

    /// <summary>Whether another item holds this UUID too, which KeePass's format forbids and a damaged file can do.</summary>
    internal bool SharesItsUuid { get; init; }

    /// <summary>Whether <paramref name="item"/> reaches this row's item: its UUID, and its name too where the UUID alone cannot say.</summary>
    /// <param name="item">A name carrying a UUID, or null.</param>
    /// <returns>Whether it is this item.</returns>
    internal bool Is(EntryName? item) =>
        item is not null
        && string.Equals(item.Uuid, Uuid, StringComparison.Ordinal)
        && (!SharesItsUuid || item == Name);

    /// <summary>Whether another item in this group has this title.</summary>
    internal bool SharesItsName => SharedNameTell is not null;

    /// <summary>What tells this item from the others of its name, empty when nothing does, or null when no other has it.</summary>
    internal string? SharedNameTell { get; init; }

    /// <summary>The path as a sentence names this item, with its tell when another item has the path too.</summary>
    internal string DisplayPath => SameNameTells.WithTell(EntryNameSanitizer.SanitizePath(Path).Text, SharedNameTell);

    /// <summary>The entry's full path, as a person reads and types it.</summary>
    /// <remarks>
    /// <b>A label and a lookup, not an identity</b> — see <see cref="Name"/>. It carries the title
    /// the vault holds and is never sanitized, because selection matching goes through it. What the
    /// screen shows is <see cref="DisplayTitle"/> and <see cref="Where"/>.
    /// </remarks>
    internal string Path => GroupPath.Length == 0 ? Title : GroupPath + "/" + Title;

    /// <summary>The title as the list draws it, scrubbed of anything that misrepresents it.</summary>
    internal string DisplayTitle { get; } = EntryNameSanitizer.Sanitize(Title).Text;

    /// <summary>The row's name for a screen reader, which reads a list item by its content's text.</summary>
    /// <returns>The row's columns, as it draws them, which hold no secret.</returns>
    public override string ToString() =>
        string.Join(", ", new[] { DisplayTitle, Why, DisplayUsername, DisplayUrl, KindLabel, Where, LastUsedText }.Where(column => !string.IsNullOrEmpty(column)));

    /// <summary>The group, for a list that is not grouped by one. Display only, so scrubbed.</summary>
    internal string Where { get; } =
        GroupPath.Length == 0 ? "—" : EntryNameSanitizer.SanitizePath(GroupPath).Text;

    /// <summary>The entry's username, as the vault holds it. Never drawn; see <see cref="DisplayUsername"/>.</summary>
    internal string Username { get; init; } = string.Empty;

    /// <summary>The entry's web address, as the vault holds it. Never drawn; see <see cref="DisplayUrl"/>.</summary>
    internal string Url { get; init; } = string.Empty;

    /// <summary>The username column, scrubbed as the item's preview scrubs it.</summary>
    internal string DisplayUsername => DisplayTextSanitizer.Sanitize(Username, DisplayTextSanitizer.MaximumLength).Text;

    /// <summary>The URL column, scrubbed as the item's preview scrubs it.</summary>
    internal string DisplayUrl => DisplayTextSanitizer.Sanitize(Url, DisplayTextSanitizer.MaximumLength).Text;

    /// <summary>The username whole, for a cell that cuts it short; null with none, which a tooltip then does not open for.</summary>
    internal string? UsernameTip => DisplayUsername.Length > 0 ? DisplayUsername : null;

    /// <summary>The URL whole, as <see cref="UsernameTip"/>.</summary>
    internal string? UrlTip => DisplayUrl.Length > 0 ? DisplayUrl : null;

    /// <summary>The entry's own tags the current query was in, or none when there is no query or it matched no tag.</summary>
    internal IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>What sort of entry this is, read from which fields are filled in.</summary>
    internal EntryKind Kind { get; init; }

    /// <summary>The icon the row draws for its kind.</summary>
    internal string Icon => EntryKinds.Icon(Kind);

    /// <summary>The kind column: login, note and so on.</summary>
    internal string KindLabel => EntryKinds.Label(Kind);

    /// <summary>
    /// Which fields beside the title and the group the query was found in, worded for the list, with the
    /// tags it was in by name.
    /// </summary>
    /// <remarks>
    /// A title or a group match shows its own evidence in the columns beside this one, and labelling
    /// those would be noise on every row of an ordinary search. A username or a URL column can be cut
    /// short, so a match there is still named. Field names are constants; a tag is a label, scrubbed as
    /// a name is (D-0485).
    /// </remarks>
    internal string Why
    {
        get
        {
            List<string> reasons = [];

            if (Fields.HasFlag(MatchedFields.Username))
            {
                reasons.Add("username");
            }

            if (Fields.HasFlag(MatchedFields.Url))
            {
                reasons.Add("URL");
            }

            if (Fields.HasFlag(MatchedFields.Tag) && Tags.Count > 0)
            {
                var tags = string.Join(", ", Tags.Select(tag => EntryNameSanitizer.Sanitize(tag).Text));
                reasons.Add(Tags.Count == 1 ? $"tag {tags}" : $"tags {tags}");
            }

            return string.Join(", ", reasons);
        }
    }
}
