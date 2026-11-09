using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>
/// What adding or removing project tags on one entry does, shown to the person before it is written
/// (D-0415): the environment it reaches, the fields that join or leave it and, for each field that joins
/// holding a KeePass placeholder, what the placeholder fills in (D-0509), never a value.
/// </summary>
/// <param name="Entry">The entry.</param>
/// <param name="Tags">The tags added or removed, which all read alike (<see cref="ProjectTag.Read"/>).</param>
/// <param name="Adding">Whether the tags are added rather than removed.</param>
/// <param name="Read">What the first tag says about projects.</param>
/// <param name="Moves">Whether the change puts the entry in the environment or takes it out, rather than leaving it as it was through another tag.</param>
/// <param name="Fields">
/// The fields a project releases (<see cref="EnvConvention.IsEnvNamedField"/>) that join or leave, in ordinal order: the
/// entry's own, or for a restore that puts the entry in an environment, the revision's, which the restore puts back.
/// </param>
public sealed record ProjectTagChange(EntryName Entry, IReadOnlyList<string> Tags, bool Adding, ProjectTag Read, bool Moves, IReadOnlyList<string> Fields)
{
    /// <summary>
    /// For each of <see cref="Fields"/> whose value holds a KeePass placeholder, what it fills in from the entry
    /// when it leaves or the first placeholder that refuses it, by keyword and never by value (D-0465); judged only
    /// for a change that puts the entry in an environment, and empty for any other.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, PlantedPlaceholders>> Placeholders { get; init; } = [];

    /// <summary>
    /// What adding or removing tags on an entry would do, or null for tags that do not start <c>env:</c>,
    /// which reach no project.
    /// </summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="tags">The tags to add or remove, which read alike.</param>
    /// <param name="adding">Whether they are added rather than removed.</param>
    /// <returns>The change, or null when it needs no confirmation.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static ProjectTagChange? Preview(Vault vault, EntryName entry, IReadOnlyList<string> tags, bool adding)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(tags);

        if (tags.Count == 0 || ProjectTag.Read(tags[0]).Kind == ProjectTagKind.None)
        {
            return null;
        }

        var fields = Variables(vault.Fields(entry));

        return Of(vault, entry, tags, adding, [.. vault.Tags(entry) ?? []], fields, revision: null);
    }

    /// <summary>
    /// What restoring one of an entry's revisions does to the projects it is in, since the restore puts back the
    /// revision's tags: the change D-0415 asks about for each environment the revision's tags starting <c>env:</c>
    /// put the entry in, then for each they take it out of, each judged after the ones before it, so an environment
    /// a tag the restore drops and a tag it puts back both reach is said to stay as it is (D-0510).
    /// </summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="revision">The revision, from a reading of <see cref="Vault.ReadHistory"/> taken since the vault last changed.</param>
    /// <returns>The changes in the order a front end shows them; empty when the revision's tags starting <c>env:</c> are the entry's own.</returns>
    /// <remarks>
    /// An environment the entry joins gets the revision's variables, which the restore puts back; one it leaves loses the
    /// entry's own.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static IReadOnlyList<ProjectTagChange> Restoring(Vault vault, EntryName entry, EntryRevision revision)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(revision);

        List<string> held = [.. vault.Tags(entry) ?? []];
        var leaving = Alike(held.Except(revision.Tags, StringComparer.Ordinal));
        var joining = Alike(revision.Tags.Except(held, StringComparer.Ordinal));

        if (leaving.Count == 0 && joining.Count == 0)
        {
            return [];
        }

        var current = Variables(vault.Fields(entry));
        var restored = Variables(revision.CustomFields);
        List<ProjectTagChange> changes = [];

        foreach (var tags in joining)
        {
            changes.Add(Of(vault, entry, tags, adding: true, held, restored, revision.Index));
            held = [.. held.Union(tags, StringComparer.Ordinal)];
        }

        foreach (var tags in leaving)
        {
            changes.Add(Of(vault, entry, tags, adding: false, held, current, revision: null));
            held = [.. held.Except(tags, StringComparer.Ordinal)];
        }

        return changes;
    }

    /// <summary>What adding or removing tags that read alike does to an entry carrying <paramref name="held"/>.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="tags">The tags, which read alike.</param>
    /// <param name="adding">Whether they are added rather than removed.</param>
    /// <param name="held">The tags the entry carries before the change.</param>
    /// <param name="fields">The variables that join or leave.</param>
    /// <param name="revision">The revision <paramref name="fields"/> come from, or null for the entry's own.</param>
    private static ProjectTagChange Of(
        Vault vault,
        EntryName entry,
        IReadOnlyList<string> tags,
        bool adding,
        List<string> held,
        List<string> fields,
        int? revision)
    {
        var read = ProjectTag.Read(tags[0]);
        List<string> after = adding ? [.. held.Union(tags, StringComparer.Ordinal)] : [.. held.Except(tags, StringComparer.Ordinal)];

        bool InEnvironment(List<string> carried) =>
            read.Kind == ProjectTagKind.Member
            && carried.Select(ProjectTag.Read).Any(tag => tag.Kind == ProjectTagKind.Member
                && string.Equals(tag.Project, read.Project, StringComparison.Ordinal)
                && string.Equals(tag.Environment, read.Environment, StringComparison.Ordinal));

        var moves = InEnvironment(held) != InEnvironment(after);

        return new ProjectTagChange(entry, tags, adding, read, moves, fields)
        {
            Placeholders = adding && moves && fields.Count > 0 ? vault.PlaceholdersHeld(entry, revision) ?? [] : [],
        };
    }

    /// <summary>The names of the fields a project releases, in ordinal order.</summary>
    private static List<string> Variables(IReadOnlyList<EntryField>? fields) =>
        [.. (fields ?? []).Select(field => field.Name).Where(EnvConvention.IsEnvNamedField).Order(StringComparer.Ordinal)];

    /// <summary>The tags starting <c>env:</c> among these, in groups that read alike: one environment's tags together, and each malformed tag alone.</summary>
    private static List<List<string>> Alike(IEnumerable<string> tags) =>
    [
        .. tags.Select(ProjectTag.Read)
            .Where(tag => tag.Kind != ProjectTagKind.None)
            .GroupBy(tag => (tag.Kind == ProjectTagKind.Member ? tag.Project : tag.Tag, tag.Environment))
            .Select(group => group.Select(tag => tag.Tag).ToList()),
    ];


    /// <summary>The environment the tags name, as <c>project/environment</c>; null for a malformed tag.</summary>
    public string? Environment => Read.Kind == ProjectTagKind.Member ? $"{Read.Project}/{Read.Environment}" : null;

    /// <summary>What the change does, in sentences a front end shows before asking; names are sanitized and no value appears.</summary>
    public IReadOnlyList<string> Describe()
    {
        return Sentences();
    }

    private List<string> Sentences()
    {
        var entry = ApprovalPrompt.Shown(Entry);
        var tags = string.Join(", ", Tags.Select(tag => EntryNameSanitizer.Sanitize(tag).Text));

        if (Read.Kind == ProjectTagKind.Malformed)
        {
            var problem = EntryNameSanitizer.SanitizeProse(Read.Problem).Text;

            return
            [
                $"{tags} puts {entry} in no project: {problem}.",
                Read.Protects
                    ? Adding
                        ? "While the item carries it, every agent request for the item is asked live."
                        : "It stops making agent requests for the item ask live, unless another tag or the item's group still does."
                    : Adding ? "It changes no environment." : "Removing it changes no environment.",
            ];
        }

        var environment = EntryNameSanitizer.SanitizePath(Environment!).Text;
        var protectedNote = Read.Protects ? ", a protected environment whose every release is asked live" : string.Empty;

        if (!Moves)
        {
            return
            [
                Adding
                    ? $"{entry} is already in {environment} through another tag, so adding {tags} changes no environment."
                    : $"{entry} stays in {environment} through another tag, so removing {tags} changes no environment.",
            ];
        }

        var names = string.Join(", ", Fields.Select(field => EntryNameSanitizer.Sanitize(field).Text));

        return
        [
            Adding
                ? $"{tags} puts {entry} in {environment}{protectedNote}."
                : $"Removing {tags} takes {entry} out of {environment}{protectedNote}.",
            Fields.Count == 0
                ? $"None of its fields is a variable, so no key {(Adding ? "joins" : "leaves")} {environment}."
                : $"{(Adding ? "Joining" : "Leaving")} {environment}: {names}.",
            .. Placeholders.Select(held => Planting(held.Key, held.Value, entry)),
        ];
    }

    /// <summary>
    /// The sentence for a variable joining an environment whose value holds placeholders: what they fill in from the
    /// entry when it leaves, or the one that refuses it wherever it would, as a terminal edit says of a new value (D-0465).
    /// </summary>
    private static string Planting(string field, PlantedPlaceholders planted, string entry)
    {
        var name = EntryNameSanitizer.Sanitize(field).Text;

        return planted.Refused is { } refused
            ? $"{name} holds {refused.Placeholder}, which {refused.Ending}, so it is refused wherever it would leave."
            : $"{name} fills in {Joined(planted.Fills)} from {entry} when it leaves.";
    }

    private static string Joined(IReadOnlyList<string> items) =>
        items.Count < 2 ? string.Concat(items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";
}
