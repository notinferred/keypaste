using Keypaste.Core.Agents;
using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>
/// What adding or removing project tags on one entry does, shown to the person before it is written
/// (D-0415): the environment it reaches, the fields that join or leave it and, for each field that joins
/// holding a KeePass placeholder, what the placeholder fills in (D-NEW-W21), never a value.
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
    private static readonly EntryExposure _everything =
        EntryExposure.TryCreate(["**"], out var everything, out var error) ? everything : throw new InvalidOperationException(error);

    /// <summary>
    /// The agents the change would let get the entry without asking, which before it they would not: their
    /// settings make where it then is Automatic, they see it and are given values. Saying so blocks nothing.
    /// </summary>
    public IReadOnlyList<string> Automatic { get; init; } = [];

    /// <summary>
    /// Whether this computer's <c>clients.toml</c> could not be read, so <see cref="Automatic"/> names the
    /// agents the vault's settings alone would release it to once the file reads again, since the file
    /// can only lower them; until then every agent request is refused.
    /// </summary>
    public bool CeilingsUnread { get; init; }

    /// <summary>
    /// For each of <see cref="Fields"/> whose value holds a KeePass placeholder, what it fills in from the entry
    /// when it leaves or the first placeholder that refuses it, by keyword and never by value (D-0465); judged only
    /// for a change that puts the entry in an environment, and empty for any other.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, PlantedPlaceholders>> Placeholders { get; init; } = [];

    /// <summary>
    /// What adding or removing tags on an entry would do, naming the agents the vault's settings, beneath
    /// this computer's ceilings, would then give it to without asking, or null for tags that do not start
    /// <c>env:</c>, which reach no project.
    /// </summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="tags">The tags to add or remove, which read alike.</param>
    /// <param name="adding">Whether they are added rather than removed.</param>
    /// <param name="clientsPath">This computer's <c>clients.toml</c>, from <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="now">What a limit on Automatic is judged against.</param>
    /// <returns>The change, or null when it needs no confirmation.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static ProjectTagChange? Preview(Vault vault, EntryName entry, IReadOnlyList<string> tags, bool adding, string clientsPath, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(clientsPath);

        if (tags.Count == 0 || ProjectTag.Read(tags[0]).Kind == ProjectTagKind.None)
        {
            return null;
        }

        var unread = !AccessCaps.TryLoad(clientsPath, out var caps, out _);
        var fields = Variables(vault.Fields(entry));

        return Of(vault, entry, tags, adding, [.. vault.Tags(entry) ?? []], fields, fields.Count > 0, revision: null, caps ?? AccessCaps.Empty, unread, now);
    }

    /// <summary>
    /// What restoring one of an entry's revisions does to the projects it is in, since the restore puts back the
    /// revision's tags: the change D-0415 asks about for each environment the revision's tags starting <c>env:</c>
    /// put the entry in, then for each they take it out of, each judged after the ones before it, so an environment
    /// a tag the restore drops and a tag it puts back both reach is said to stay as it is (D-NEW-W22).
    /// </summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="revision">The revision, from a reading of <see cref="Vault.ReadHistory"/> taken since the vault last changed.</param>
    /// <param name="clientsPath">This computer's <c>clients.toml</c>, from <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="now">What a limit on Automatic is judged against.</param>
    /// <returns>The changes in the order a front end shows them; empty when the revision's tags starting <c>env:</c> are the entry's own.</returns>
    /// <remarks>
    /// An environment the entry joins gets the revision's variables, which the restore puts back; one it leaves loses the
    /// entry's own. Every change judges Automatic with the revision's variables, which the entry holds once restored.
    /// </remarks>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static IReadOnlyList<ProjectTagChange> Restoring(Vault vault, EntryName entry, EntryRevision revision, string clientsPath, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(clientsPath);

        List<string> held = [.. vault.Tags(entry) ?? []];
        var leaving = Alike(held.Except(revision.Tags, StringComparer.Ordinal));
        var joining = Alike(revision.Tags.Except(held, StringComparer.Ordinal));

        if (leaving.Count == 0 && joining.Count == 0)
        {
            return [];
        }

        var unread = !AccessCaps.TryLoad(clientsPath, out var loaded, out _);
        var caps = loaded ?? AccessCaps.Empty;
        var current = Variables(vault.Fields(entry));
        var restored = Variables(revision.CustomFields);
        List<ProjectTagChange> changes = [];

        foreach (var tags in joining)
        {
            changes.Add(Of(vault, entry, tags, adding: true, held, restored, restored.Count > 0, revision.Index, caps, unread, now));
            held = [.. held.Union(tags, StringComparer.Ordinal)];
        }

        foreach (var tags in leaving)
        {
            changes.Add(Of(vault, entry, tags, adding: false, held, current, restored.Count > 0, revision: null, caps, unread, now));
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
    /// <param name="hasVariables">Whether the entry has a field a project releases once the change is made, which Automatic is judged with.</param>
    /// <param name="revision">The revision <paramref name="fields"/> come from, or null for the entry's own.</param>
    /// <param name="caps">This computer's ceilings.</param>
    /// <param name="unread">Whether <paramref name="caps"/> stands in for a <c>clients.toml</c> that could not be read.</param>
    /// <param name="now">What a limit on Automatic is judged against.</param>
    private static ProjectTagChange Of(
        Vault vault,
        EntryName entry,
        IReadOnlyList<string> tags,
        bool adding,
        List<string> held,
        List<string> fields,
        bool hasVariables,
        int? revision,
        AccessCaps caps,
        bool unread,
        DateTimeOffset now)
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
            Automatic = Released(vault, caps, entry, held, after, hasVariables, now),
            CeilingsUnread = unread,
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

    /// <summary>The named agents with settings of their own that get the entry without asking with its tags after, and not before.</summary>
    private static List<string> Released(
        Vault vault,
        AccessCaps caps,
        EntryName entry,
        List<string> before,
        List<string> after,
        bool hasVariables,
        DateTimeOffset now)
    {
        var store = new AgentAccessStore(vault);
        List<string> released = [];

        foreach (var name in store.List().Select(record => record.Name).Where(AgentNames.IsNamed).Distinct(StringComparer.Ordinal))
        {
            var access = EffectiveAccess.Create(name, store.Read(name), caps);

            if (Releases(access, entry, after, hasVariables, now) && !Releases(access, entry, before, hasVariables, now))
            {
                released.Add(name);
            }
        }

        return released;
    }

    /// <summary>
    /// Whether an agent gets an entry without asking: its settings make where the entry's tags put it
    /// Automatic, it sees the entry through any bridge, and it is given values.
    /// </summary>
    /// <param name="access">What applies to the agent.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="carried">The entry's own tags.</param>
    /// <param name="hasVariables">Whether the entry has a field a project releases.</param>
    /// <param name="now">What a limit on Automatic is judged against.</param>
    /// <returns><see langword="true"/> when a request for the entry is released without a person's answer.</returns>
    internal static bool Releases(EffectiveAccess access, EntryName entry, IReadOnlyList<string> carried, bool hasVariables, DateTimeOffset now)
    {
        var reach = access.Reach(_everything, entry, carried);

        return access.Values
            && (reach == ExposureReach.Entry || (reach == ExposureReach.ProjectFields && hasVariables))
            && access.Item(carried, now).Mode == AgentMode.Automatic;
    }

    /// <summary>The environment the tags name, as <c>project/environment</c>; null for a malformed tag.</summary>
    public string? Environment => Read.Kind == ProjectTagKind.Member ? $"{Read.Project}/{Read.Environment}" : null;

    /// <summary>What the change does, in sentences a front end shows before asking; names are sanitized and no value appears.</summary>
    public IReadOnlyList<string> Describe()
    {
        var sentences = Sentences();

        if (Automatic.Count == 0)
        {
            return sentences;
        }

        var agents = string.Join(", ", Automatic.Select(agent => EntryNameSanitizer.Sanitize(agent, ApprovalPrompt.MaximumClientLength).Text));

        return
        [
            .. sentences,
            CeilingsUnread
                ? $"Automatic: by the vault's settings, {agents} would get {ApprovalPrompt.Shown(Entry)} without asking, each release recorded in history, once this computer's clients.toml can be read again; until then every agent request is refused."
                : $"Automatic: {agents} would get {ApprovalPrompt.Shown(Entry)} without asking, each release recorded in history.",
        ];
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
