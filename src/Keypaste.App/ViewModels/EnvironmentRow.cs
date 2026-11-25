using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One line of the item preview's Environments card: a project and environment the item is in, and whether agents ask.</summary>
/// <remarks>
/// A name and a fixed word, never a value. "approval required" means no agent's settings let it have the item
/// without a person's answer; otherwise the item is simply set there. Nothing is said about
/// approval when this computer's ceilings cannot be read, since every agent request is refused until they can.
/// </remarks>
internal sealed class EnvironmentRow
{
    /// <param name="environment">The project and environment.</param>
    /// <param name="tags">The item's tags, one of which puts it there and is removed from this line.</param>
    internal EnvironmentRow(EntryEnvironment environment, IEnumerable<EntryTagChip> tags)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(tags);

        Label = EntryNameSanitizer.Sanitize($"{environment.Project} · {environment.Environment}").Text;
        Tag = tags.FirstOrDefault(chip => chip.IsProject && chip.Display == Label);
        RequiresApproval = environment.ApprovalRequired == true;
        Status = environment.ApprovalRequired switch
        {
            true => "approval required",
            false => "set",
            null => string.Empty,
        };
    }

    /// <summary>The project and environment, as a project tag's chip names them.</summary>
    internal string Label { get; }

    /// <summary>The tag that puts the item here, which the Tags row leaves to this line, or null.</summary>
    internal EntryTagChip? Tag { get; }

    internal bool HasTag => Tag is not null;

    /// <summary>Whether an agent gets the item here only once a person answers.</summary>
    internal bool RequiresApproval { get; }

    /// <summary>What the card says beside the environment.</summary>
    internal string Status { get; }

    /// <summary>The line's name for a screen reader.</summary>
    /// <returns>The environment, then what the card says of it.</returns>
    public override string ToString() => Status.Length == 0 ? Label : $"{Label}, {Status}";
}
