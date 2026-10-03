using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>
/// What adding or removing project tags on one entry does, shown to the person before it is written
/// (D-0415): the environment it reaches and the fields that join or leave it, never a value.
/// </summary>
/// <param name="Entry">The entry.</param>
/// <param name="Tags">The tags added or removed, which all read alike (<see cref="ProjectTag.Read"/>).</param>
/// <param name="Adding">Whether the tags are added rather than removed.</param>
/// <param name="Read">What the first tag says about projects.</param>
/// <param name="Moves">Whether the change puts the entry in the environment or takes it out, rather than leaving it as it was through another tag.</param>
/// <param name="Fields">The entry's fields a project releases (<see cref="EnvConvention.IsEnvNamedField"/>), in ordinal order.</param>
public sealed record ProjectTagChange(EntryName Entry, IReadOnlyList<string> Tags, bool Adding, ProjectTag Read, bool Moves, IReadOnlyList<string> Fields)
{
    /// <summary>What adding or removing tags on an entry would do, or null for tags that do not start <c>env:</c>, which reach no project.</summary>
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

        if (tags.Count == 0 || ProjectTag.Read(tags[0]) is not { Kind: not ProjectTagKind.None } read)
        {
            return null;
        }

        var held = vault.Tags(entry) ?? [];
        var after = adding ? held.Union(tags, StringComparer.Ordinal) : held.Except(tags, StringComparer.Ordinal);

        bool InEnvironment(IEnumerable<string> carried) =>
            read.Kind == ProjectTagKind.Member
            && carried.Select(ProjectTag.Read).Any(tag => tag.Kind == ProjectTagKind.Member
                && string.Equals(tag.Project, read.Project, StringComparison.Ordinal)
                && string.Equals(tag.Environment, read.Environment, StringComparison.Ordinal));

        var fields = (vault.Fields(entry) ?? [])
            .Select(field => field.Name)
            .Where(EnvConvention.IsEnvNamedField)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new ProjectTagChange(entry, tags, adding, read, InEnvironment(held) != InEnvironment(after), fields);
    }

    /// <summary>The environment the tags name, as <c>project/environment</c>; null for a malformed tag.</summary>
    public string? Environment => Read.Kind == ProjectTagKind.Member ? $"{Read.Project}/{Read.Environment}" : null;

    /// <summary>What the change does, in sentences a front end shows before asking; names are sanitized and no value appears.</summary>
    public IReadOnlyList<string> Describe()
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
                        ? "While the entry carries it, every agent request for the entry is asked live."
                        : "It stops making agent requests for the entry ask live, unless another tag or the entry's group still does."
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
        ];
    }
}
