using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One line of the item preview's Environments card: a project and environment the item is in, and whether agents ask.</summary>
/// <remarks>
/// A name and a fixed word, never a value. "approval required" means no agent's settings let it have the item
/// without a person's answer (D-0441, D-0371); otherwise the item is simply set there. Nothing is said about
/// approval when this computer's ceilings cannot be read, since every agent request is refused until they can.
/// </remarks>
internal sealed class EnvironmentRow
{
    internal EnvironmentRow(EntryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        Label = EntryNameSanitizer.Sanitize($"{environment.Project} · {environment.Environment}").Text;
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

    /// <summary>Whether an agent gets the item here only once a person answers.</summary>
    internal bool RequiresApproval { get; }

    /// <summary>What the card says beside the environment.</summary>
    internal string Status { get; }

    /// <summary>The line's name for a screen reader.</summary>
    /// <returns>The environment, then what the card says of it.</returns>
    public override string ToString() => Status.Length == 0 ? Label : $"{Label}, {Status}";
}
