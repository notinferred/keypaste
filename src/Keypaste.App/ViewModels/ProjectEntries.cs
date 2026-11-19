using Keypaste.Core;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One environment of the open project and the entries its tag puts in it, as the Entries section lists them.</summary>
/// <param name="Name">The environment.</param>
/// <param name="IsProtected">Whether every release of it is asked live.</param>
/// <param name="Members">Its entries, in ordinal order of path.</param>
/// <param name="AddEntry">Opens the form that tags another entry into it.</param>
internal sealed record EnvEnvironmentEntries(string Name, bool IsProtected, IReadOnlyList<EnvMemberRow> Members, RelayCommand AddEntry)
{
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    /// <summary>The section's heading, in capitals as the design writes table headers.</summary>
    internal string Heading => DisplayName.ToUpperInvariant();

    internal bool HasMembers => Members.Count > 0;

    internal string AddName => $"Add an item to {DisplayName}";

    internal string EmptyNote => $"No item is tagged into {DisplayName} yet.";
}

/// <summary>One entry of an environment. Holds the names of its variables, never a value.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Environment">The environment it is listed under.</param>
/// <param name="Keys">Its fields a project releases, in ordinal order.</param>
/// <param name="Also">The other environments it serves, as <see cref="EnvProjectViewModel.Elsewhere"/> names them.</param>
/// <param name="Remove">Asks to take it out of <paramref name="Environment"/>.</param>
internal sealed record EnvMemberRow(EntryName Entry, string Environment, IReadOnlyList<string> Keys, IReadOnlyList<string> Also, RelayCommand Remove)
{
    internal string Display => EntryNameSanitizer.Sanitize(Entry.Title).Text;

    /// <summary>The item's group path and title, for its tooltip.</summary>
    internal string DisplayPath => ApprovalPrompt.Shown(Entry);

    internal bool HasKeys => Keys.Count > 0;

    internal string KeysText => Keys.Count == 0
        ? "No variables"
        : string.Join(", ", Keys.Select(key => EntryNameSanitizer.Sanitize(key).Text));

    /// <summary>Whether another of its stage's items is listed above it, so it draws the hairline between them.</summary>
    internal bool FollowsAnother { get; init; }

    internal bool HasAlso => Also.Count > 0;

    internal string AlsoText => HasAlso ? "also " + string.Join(", ", Also) : string.Empty;

    internal string RemoveName => $"Remove {Display} from {EntryNameSanitizer.Sanitize(Environment).Text}";
}

/// <summary>An entry the add form offers to tag into an environment.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Display">Its title.</param>
internal sealed record EnvEntryCandidate(EntryName Entry, string Display)
{
    /// <summary>The item's group path and title, for its tooltip.</summary>
    internal string DisplayPath => ApprovalPrompt.Shown(Entry);

    /// <inheritdoc/>
    public override string ToString() => Display;
}

/// <summary>A tag that starts like a project tag and puts its entry in no project, as <c>keypaste env ls</c> warns of it, with the fix when only its case is wrong.</summary>
/// <param name="Problem">The tag, its entry and why it is ignored.</param>
/// <param name="Open">Shows the item in Items.</param>
/// <param name="Change">Asks to change the tag to <see cref="Fixed"/>.</param>
internal sealed record EnvTagProblemRow(ProjectTagProblem Problem, RelayCommand Open, RelayCommand Change)
{
    /// <summary>The same tag in lower case, when that puts the item in a stage, or null.</summary>
    internal string? Fixed => ProjectTag.Read(Problem.Tag.ToLowerInvariant()) is { Kind: ProjectTagKind.Member } member ? member.Tag : null;

    internal bool HasFix => Fixed is not null;

    /// <summary>The stage the fixed tag names, as its button says it.</summary>
    internal string FixLabel => Fixed is { } tag ? UseText.ChangeTo(EntryNameSanitizer.Sanitize(ProjectTag.Read(tag).Environment!).Text) : string.Empty;

    internal string Title => EntryNameSanitizer.Sanitize(Problem.Entry.Title).Text;

    /// <summary>What the notice says: the tag's wrong stage when only its case is wrong, otherwise why the tag is ignored.</summary>
    internal string Sentence => HasFix
        ? UseText.TagIsNotAStage(Title, EntryNameSanitizer.Sanitize(Problem.Tag.Split(':')[^1]).Text)
        : EntryNameSanitizer.SanitizeProse(
            $"{Title} has the tag {Problem.Tag}, which puts it in no project: {Problem.Problem}."
                + (Problem.Protects ? " Every agent request for the item is still asked live." : string.Empty),
            1024).Text;
}
