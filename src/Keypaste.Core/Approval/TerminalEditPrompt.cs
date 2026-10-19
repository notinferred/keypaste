using Keypaste.Core.Editing;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown before a terminal's edit is saved in the vault: the command, the item and
/// what changes, by name only, already sanitized (D-0464).
/// </summary>
/// <remarks>
/// It has no member for a value, a default button or a layout (THREATS.md T-2). A terminal command can
/// be typed by any program the person runs, an agent with a shell included, so the prompt says where
/// the request came from as a claim and never that the person asked.
/// </remarks>
public sealed record TerminalEditPrompt
{
    /// <summary>The command, as a person types it.</summary>
    public required string Command { get; init; }

    /// <summary>The item, or the environment, sanitized.</summary>
    public required string Item { get; init; }

    /// <summary>What changes, in keypaste's own words, holding no value.</summary>
    public required IReadOnlyList<string> Changes { get; init; }

    /// <summary>Where the request came from, as the prompt says it.</summary>
    public string OriginText => AccessChangePrompt.FromTerminalText;

    /// <summary>The prompt for one edit, from the preview the holder checked it into.</summary>
    /// <param name="edit">The edit.</param>
    /// <param name="preview">What it would do.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    public static TerminalEditPrompt For(TerminalEdit edit, EditPreview preview)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(preview);

        return new TerminalEditPrompt
        {
            Command = $"keypaste {edit.Verb}",
            Item = preview.Item,
            Changes = preview.Changes,
        };
    }
}
