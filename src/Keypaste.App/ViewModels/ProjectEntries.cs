using Keypaste.Core;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One environment of the open project and the entries its tag puts in it, as the Entries section lists them.</summary>
/// <param name="Name">The environment.</param>
/// <param name="IsProtected">Whether every release of it is asked live (D-0348).</param>
/// <param name="Members">Its entries, in ordinal order of path.</param>
/// <param name="AddEntry">Opens the form that tags another entry into it.</param>
internal sealed record EnvEnvironmentEntries(string Name, bool IsProtected, IReadOnlyList<EnvMemberRow> Members, RelayCommand AddEntry)
{
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    /// <summary>The section's heading, in capitals as the design writes table headers.</summary>
    internal string Heading => DisplayName.ToUpperInvariant();

    internal bool HasMembers => Members.Count > 0;

    internal string AddName => $"Add an entry to {DisplayName}";

    internal string EmptyNote => $"No entry is tagged into {DisplayName} yet.";
}

/// <summary>One entry of an environment. Holds the names of its variables, never a value.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Environment">The environment it is listed under.</param>
/// <param name="Keys">Its fields a project releases, in ordinal order.</param>
/// <param name="Also">The other environments it serves, as <see cref="EnvProjectViewModel.Elsewhere"/> names them.</param>
/// <param name="Remove">Asks to take it out of <paramref name="Environment"/>.</param>
internal sealed record EnvMemberRow(EntryName Entry, string Environment, IReadOnlyList<string> Keys, IReadOnlyList<string> Also, RelayCommand Remove)
{
    internal string Display => ApprovalPrompt.Shown(Entry);

    internal string KeysText => Keys.Count == 0
        ? "No variables"
        : string.Join(", ", Keys.Select(key => EntryNameSanitizer.Sanitize(key).Text));

    internal bool HasAlso => Also.Count > 0;

    internal string AlsoText => HasAlso ? "also " + string.Join(", ", Also) : string.Empty;

    internal string RemoveName => $"Remove {Display} from {EntryNameSanitizer.Sanitize(Environment).Text}";
}

/// <summary>An entry the add form offers to tag into an environment.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Display">Its path, as a prompt shows it.</param>
internal sealed record EnvEntryCandidate(EntryName Entry, string Display)
{
    /// <inheritdoc/>
    public override string ToString() => Display;
}

/// <summary>A tag that starts like a project tag and puts its entry in no project, as <c>keypaste env ls</c> warns of it.</summary>
/// <param name="Problem">The tag, its entry and why it is ignored.</param>
internal sealed record EnvTagProblemRow(ProjectTagProblem Problem)
{
    internal string Sentence => EntryNameSanitizer.SanitizeProse(
        $"{ApprovalPrompt.Shown(Problem.Entry)} has the tag {Problem.Tag}, which puts it in no project: {Problem.Problem}."
            + (Problem.Protects ? " Every agent request for the entry is still asked live." : string.Empty),
        1024).Text;
}
