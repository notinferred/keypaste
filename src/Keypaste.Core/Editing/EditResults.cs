namespace Keypaste.Core.Editing;

/// <summary>Why an edit is refused, which a CLI turns into its exit code.</summary>
public enum EditRefusalKind
{
    /// <summary>The edit cannot be made as asked: a usage error.</summary>
    Invalid = 0,

    /// <summary>The vault disagrees with the edit, or more than one item answers.</summary>
    Conflict = 1,

    /// <summary>The item, field or variable is not there.</summary>
    NotFound = 2,
}

/// <summary>An edit refused, in keypaste's words without the verb that asked.</summary>
/// <param name="Kind">Which kind of refusal.</param>
/// <param name="Reason">The sentence, as the CLI prints it after <c>keypaste &lt;verb&gt;: </c>.</param>
public sealed record EditRefusal(EditRefusalKind Kind, string Reason)
{
    /// <summary>Lines printed after the reason, unprefixed.</summary>
    public IReadOnlyList<string> Then { get; init; } = [];
}

/// <summary>What an applied edit did.</summary>
public enum EditOutcome
{
    /// <summary>Nothing needed writing.</summary>
    Unchanged = 0,

    /// <summary>An item was created.</summary>
    Created = 1,

    /// <summary>An item's password was replaced.</summary>
    Updated = 2,

    /// <summary>Custom fields were set.</summary>
    FieldsSet = 3,

    /// <summary>An item went to the recycle bin.</summary>
    Recycled = 4,

    /// <summary>An item was removed for good.</summary>
    Deleted = 5,

    /// <summary>A password was replaced with a generated one.</summary>
    Rotated = 6,

    /// <summary>A custom field was removed.</summary>
    FieldRemoved = 7,

    /// <summary>A variable was added or replaced.</summary>
    KeySet = 8,

    /// <summary>A variable was removed.</summary>
    KeyRemoved = 9,

    /// <summary>A <c>.env</c> file's variables were written.</summary>
    Imported = 10,

    /// <summary>An item was tagged into an environment.</summary>
    Tagged = 11,

    /// <summary>An item was untagged out of one.</summary>
    Untagged = 12,
}

/// <summary>What an edit did, by name only, for the line the CLI prints; every name is raw, sanitized where it is printed.</summary>
/// <param name="Action">The edit.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Item">The item's path, as typed or as the vault holds it.</param>
public sealed record EditDone(EditAction Action, EditOutcome Outcome, string Item)
{
    /// <summary>The fields, tags or variable named.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>The variables written and where, never a value.</summary>
    public IReadOnlyList<EnvKeyWrite> Keys { get; init; } = [];

    /// <summary>The item a variable was written on or removed from.</summary>
    public EntryName? Entry { get; init; }

    /// <summary>Whether the new item's path now names two items.</summary>
    public bool SharesPath { get; init; }

    /// <summary>Whether an environment's home item was created and tagged.</summary>
    public bool CreatesHome { get; init; }
}

/// <summary>An edit as it would be made, by name only: what the person who allows it is shown.</summary>
/// <param name="Action">The edit.</param>
/// <param name="Item">The item, or the environment, sanitized.</param>
/// <param name="Changes">What changes, in sentences that hold no value.</param>
public sealed record EditPreview(EditAction Action, string Item, IReadOnlyList<string> Changes)
{
    /// <summary>Whether a removed item goes to the recycle bin.</summary>
    public bool Recycles { get; init; }

    /// <summary>Whether <c>set</c> creates the item rather than replacing its password.</summary>
    public bool Creates { get; init; }

    /// <summary>Whether a new item's path would then name two items.</summary>
    public bool SharesPath { get; init; }

    /// <summary>The item a variable is on.</summary>
    public EntryName? Entry { get; init; }

    /// <summary>The variables an <c>env set</c> or <c>env pull</c> writes; it holds values and never leaves the process.</summary>
    public EnvWritePlan? Plan { get; init; }

    /// <summary>The tags an <c>env tag</c> or <c>untag</c> changes.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The tag change's own sentences, in the order the CLI prints them.</summary>
    public IReadOnlyList<string> TagSentences { get; init; } = [];

    /// <summary>Whether <paramref name="other"/> shows a person the same change.</summary>
    /// <param name="other">The preview to compare.</param>
    /// <returns>True when the item and every sentence match.</returns>
    public bool Shows(EditPreview other) =>
        other is not null
        && other.Action == Action
        && string.Equals(other.Item, Item, StringComparison.Ordinal)
        && other.Changes.SequenceEqual(Changes, StringComparer.Ordinal);
}

/// <summary>An edit checked against a vault: refused, needing nothing, or ready to ask about.</summary>
public sealed record EditCheck
{
    /// <summary>Why it is refused, or null.</summary>
    public EditRefusal? Refusal { get; init; }

    /// <summary>What it would do, when it is ready.</summary>
    public EditPreview? Preview { get; init; }

    /// <summary>What the vault already holds, when nothing needs writing.</summary>
    public EditDone? Unchanged { get; init; }
}

/// <summary>An edit applied, or refused at the last moment.</summary>
public sealed record EditResult
{
    /// <summary>Why nothing was changed, or null.</summary>
    public EditRefusal? Refusal { get; init; }

    /// <summary>What was changed.</summary>
    public EditDone? Done { get; init; }
}

/// <summary>What an edit is checked against besides the vault.</summary>
/// <param name="ClientsPath">This computer's <c>clients.toml</c>, which says which agents a tag reaches unasked.</param>
/// <param name="Now">The time a tag change is judged at.</param>
public sealed record EditScene(string ClientsPath, DateTimeOffset Now);
