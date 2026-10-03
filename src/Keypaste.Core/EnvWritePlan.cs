namespace Keypaste.Core;

/// <summary>What a write does to one key of an environment.</summary>
public enum EnvWriteChange
{
    /// <summary>The environment has no such key, and it would be added as a field.</summary>
    New = 0,

    /// <summary>The environment has it with another value, which would be replaced where it lives and kept in history.</summary>
    Replaces = 1,

    /// <summary>The environment already has it with the same value, and it would not be written.</summary>
    Unchanged = 2,
}

/// <summary>One key of a write plan: its name, what happens to it and where it lives or goes, never its value.</summary>
/// <param name="Key">The variable's name.</param>
/// <param name="Change">What the write does to it.</param>
/// <param name="Entry">The entry holding it, or the one it is added to.</param>
/// <param name="Field">The field holding it: the key itself on a tagged entry, or <see cref="EnvSource.LegacyField"/> for a legacy variable.</param>
public sealed record EnvKeyWrite(string Key, EnvWriteChange Change, EntryName Entry, string Field)
{
    /// <summary>Whether the key is a legacy <c>env/&lt;project&gt;</c> variable, updated in place.</summary>
    public bool IsLegacy => string.Equals(Field, EnvSource.LegacyField, StringComparison.Ordinal);
}

/// <summary>What writing keys to one environment of a project would do, planned before anything is written.</summary>
/// <remarks>
/// <see cref="EnvStore.Plan"/> checks everything that could refuse a key, so a plan without a
/// <see cref="Refusal"/> either writes whole or not at all. It holds the values it would write and
/// never shows one.
/// </remarks>
public sealed class EnvWritePlan
{
    private readonly IReadOnlyDictionary<string, string> _values;

    internal EnvWritePlan(
        string project,
        string environment,
        IReadOnlyList<EnvKeyWrite> keys,
        IReadOnlyDictionary<string, string> values,
        string tag,
        bool createsHome,
        string? refusal)
    {
        Project = project;
        Environment = environment;
        Keys = keys;
        _values = values;
        Tag = tag;
        CreatesHome = createsHome;
        Refusal = refusal;
    }

    /// <summary>The project written to.</summary>
    public string Project { get; }

    /// <summary>The environment written to.</summary>
    public string Environment { get; }

    /// <summary>Every key, ordinal-sorted by name.</summary>
    public IReadOnlyList<EnvKeyWrite> Keys { get; }

    /// <summary>Why nothing can be written, naming each key and entry concerned; null when the plan can be applied.</summary>
    public string? Refusal { get; }

    /// <summary>Whether applying creates the environment's home entry (<see cref="EnvStore.HomeEntry"/>).</summary>
    public bool CreatesHome { get; }

    /// <summary>The environment's home entry.</summary>
    public EntryName Home => EnvStore.HomeEntry(Project, Environment);

    /// <summary>The names that would be added, ordinal-sorted.</summary>
    public IReadOnlyList<string> Created => Named(EnvWriteChange.New);

    /// <summary>The names whose value would be replaced, ordinal-sorted.</summary>
    public IReadOnlyList<string> Updated => Named(EnvWriteChange.Replaces);

    /// <summary>How many keys already hold the value.</summary>
    public int Unchanged => Keys.Count(key => key.Change == EnvWriteChange.Unchanged);

    /// <summary>Whether applying would write anything.</summary>
    public bool WritesAnything => Refusal is null && Keys.Any(key => key.Change != EnvWriteChange.Unchanged);

    /// <summary>The environment's member tag, which a created home entry carries.</summary>
    internal string Tag { get; }

    /// <summary>The keys to write, grouped by the entry they are written on.</summary>
    internal IEnumerable<IGrouping<EntryName, EnvKeyWrite>> ToWrite =>
        Keys.Where(key => key.Change != EnvWriteChange.Unchanged).GroupBy(key => key.Entry);

    internal string Value(string key) => _values[key];

    internal static EnvWritePlan Refused(string project, string environment, string refusal) =>
        new(project, environment, [], new Dictionary<string, string>(), string.Empty, false, refusal);

    private List<string> Named(EnvWriteChange change) =>
        [.. Keys.Where(key => key.Change == change).Select(key => key.Key)];
}

/// <summary>What removing one key of an environment did.</summary>
public enum EnvRemoveOutcome
{
    /// <summary>The environment has no such key on the entry asked for, and nothing changed.</summary>
    NothingMatched = 0,

    /// <summary>The key is on more than one entry and none was named; nothing changed.</summary>
    Ambiguous = 1,

    /// <summary>The key cannot be removed by keypaste, as <see cref="EnvRemoval.Refusal"/> says; nothing changed.</summary>
    Refused = 2,

    /// <summary>The field was removed from its entry, whose history keeps it.</summary>
    FieldRemoved = 3,

    /// <summary>The legacy variable's entry went to the recycle bin.</summary>
    Recycled = 4,

    /// <summary>The legacy variable's entry was deleted, the vault having no recycle bin.</summary>
    Deleted = 5,
}

/// <summary>What <see cref="EnvStore.Remove"/> did and where.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Source">Where the key lived, when one entry held it.</param>
/// <param name="Refusal">Why nothing was removed, naming the entries concerned; empty otherwise.</param>
public sealed record EnvRemoval(EnvRemoveOutcome Outcome, EnvSource? Source, string Refusal);
