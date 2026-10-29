namespace Keypaste.Core.AutoType;

/// <summary>
/// An item's Auto-Type settings and its groups', as KeePass 2 and KeePassXC keep them in a KDBX file, which keypaste
/// reads and never writes (D-0605).
/// </summary>
/// <param name="Enabled">The item's <c>AutoType/Enabled</c>, on when absent.</param>
/// <param name="Obfuscated">Whether the item's <c>AutoType/DataTransferObfuscation</c> asks for KeePass 2's two-channel obfuscation.</param>
/// <param name="Sequence">The item's <c>AutoType/DefaultSequence</c>; empty inherits.</param>
/// <param name="Associations">The item's window associations, in order.</param>
/// <param name="Groups">Each group from the item's own up to the root, the nearest first.</param>
/// <param name="HasUsername">Whether the item's stored username is not empty.</param>
/// <param name="HasPassword">Whether the item's stored password is not empty.</param>
/// <param name="Expires">When the item expires, or null when it does not.</param>
/// <remarks>
/// KeePassXC 2.8's rules decide (<c>Entry::effectiveAutoTypeSequence</c>, <c>Group::resolveAutoTypeEnabled</c>,
/// <c>Group::effectiveAutoTypeSequence</c>): an item types only when its own setting is on, the nearest group that says
/// on or off says on, and the walk up to the first group holding a sequence meets no group that says off. Its
/// sequence is its own, else the nearest group's, else <see cref="DefaultSequence"/>, which is shortened to
/// <c>{PASSWORD}{ENTER}</c> when the username is empty and to <c>{USERNAME}{ENTER}</c> when the password is.
/// </remarks>
internal sealed record AutoTypeSettings(
    bool Enabled,
    bool Obfuscated,
    string Sequence,
    IReadOnlyList<AutoTypeAssociation> Associations,
    IReadOnlyList<GroupAutoType> Groups,
    bool HasUsername,
    bool HasPassword,
    DateTimeOffset? Expires)
{
    /// <summary>KeePass's and KeePassXC's sequence for an item no group gives one.</summary>
    internal const string DefaultSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}";

    private const string _passwordOnly = "{PASSWORD}{ENTER}";
    private const string _usernameOnly = "{USERNAME}{ENTER}";

    /// <summary>Why the item types nothing at all, or null when Auto-Type is on for it.</summary>
    internal string? Off =>
        !Enabled ? "Auto-Type is off for this item."
        : Groups.FirstOrDefault(group => group.Enabled is not null)?.Enabled == false || UpToSequence().Any(group => group.Enabled == false)
            ? "Auto-Type is off for a group this item is in."
            : null;

    /// <summary>The sequence the item types where no association decides, and where it comes from.</summary>
    internal (string Sequence, AutoTypeSource Source) Effective()
    {
        if (Sequence.Length > 0)
        {
            return (Sequence, AutoTypeSource.Item);
        }

        var inherited = Groups.FirstOrDefault(group => group.Sequence.Length > 0)?.Sequence;
        var source = inherited is null ? AutoTypeSource.Default : AutoTypeSource.Group;
        var sequence = inherited ?? DefaultSequence;

        if (string.Equals(sequence, DefaultSequence, StringComparison.Ordinal) && HasUsername != HasPassword)
        {
            return (HasUsername ? _usernameOnly : _passwordOnly, source);
        }

        return (sequence, source);
    }

    /// <summary>
    /// The sequence for a window's title: the first association whose pattern, its placeholders filled in, fits the
    /// title, else <see cref="Effective"/>; and why each pattern that could not be matched matched nothing.
    /// </summary>
    /// <param name="title">The window's title; an empty one takes <see cref="Effective"/>, as KeePassXC does.</param>
    /// <param name="fill">Fills in a pattern's placeholders from the item, as D-0465 fills a value.</param>
    /// <param name="timeout">How long one pattern may take to match.</param>
    /// <returns>The choice.</returns>
    internal AutoTypeChoice Choose(string title, Func<string, PlaceholderFill> fill, TimeSpan timeout)
    {
        var (effective, source) = Effective();
        List<string> unmatched = [];

        for (var index = 0; index < Associations.Count && title.Length > 0; index++)
        {
            var association = Associations[index];
            var number = index + 1;

            if (association.Window.Length == 0)
            {
                continue;
            }

            var filled = fill(association.Window);

            if (filled.Value is not { } pattern)
            {
                unmatched.Add($"association {number}'s window pattern {filled.Refusal}");
                continue;
            }

            switch (WindowPattern.Match(pattern, title, timeout))
            {
                case PatternMatch.Fits:
                    return new AutoTypeChoice(
                        association.Sequence.Length > 0 ? association.Sequence : effective, AutoTypeSource.Association, number, unmatched);
                case PatternMatch.Unreadable:
                    unmatched.Add($"association {number}'s window pattern is a regular expression keypaste cannot read");
                    break;
                case PatternMatch.TimedOut:
                    unmatched.Add($"association {number}'s window pattern took too long to match this title");
                    break;
            }
        }

        return new AutoTypeChoice(effective, source, null, unmatched);
    }

    /// <summary>The item's group, and each group above it up to and including the first that holds a sequence, or to the root.</summary>
    private IEnumerable<GroupAutoType> UpToSequence()
    {
        foreach (var group in Groups)
        {
            yield return group;

            if (group.Sequence.Length > 0)
            {
                yield break;
            }
        }
    }
}

/// <summary>One window association: a pattern for a window's title and the sequence it types.</summary>
/// <param name="Window">The pattern as the vault holds it; empty matches no title of its own.</param>
/// <param name="Sequence">The sequence; empty types the item's own.</param>
internal sealed record AutoTypeAssociation(string Window, string Sequence);

/// <summary>A group's Auto-Type settings.</summary>
/// <param name="Enabled">Its <c>EnableAutoType</c>: on, off, or null to inherit.</param>
/// <param name="Sequence">Its <c>DefaultAutoTypeSequence</c>; empty inherits.</param>
internal sealed record GroupAutoType(bool? Enabled, string Sequence);

/// <summary>The sequence chosen for one title.</summary>
/// <param name="Sequence">The sequence as the vault holds it.</param>
/// <param name="Source">Where it comes from.</param>
/// <param name="Association">The association that chose it, counting from 1, or null.</param>
/// <param name="Unmatched">Why each pattern that could not be matched matched nothing.</param>
internal sealed record AutoTypeChoice(string Sequence, AutoTypeSource Source, int? Association, IReadOnlyList<string> Unmatched);

/// <summary>Where the sequence an Auto-Type types comes from.</summary>
public enum AutoTypeSource
{
    /// <summary>A window association whose pattern fits the title.</summary>
    Association,

    /// <summary>The item's own sequence.</summary>
    Item,

    /// <summary>The nearest group's sequence.</summary>
    Group,

    /// <summary>KeePass's default, <c>{USERNAME}{TAB}{PASSWORD}{ENTER}</c>, or its shortening for an empty username or password.</summary>
    Default,
}
