using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One of an entry's own tags, as a chip the pane draws and can remove.</summary>
/// <remarks>
/// A project tag (D-0370) reads as its project and environment, with a shield when the environment is
/// protected; a tag that starts <c>env:</c> and breaks the grammar is drawn as written, flagged, with
/// the reason as its tip, and still carries the shield when it protects (D-0371).
/// </remarks>
internal sealed class EntryTagChip
{
    internal EntryTagChip(EntryDetailViewModel owner, string tag)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(tag);

        Tag = tag;

        var read = ProjectTag.Read(tag);
        IsProject = read.Kind == ProjectTagKind.Member;
        IsMalformed = read.Kind == ProjectTagKind.Malformed;
        IsProtected = read.Protects;
        Display = EntryNameSanitizer.Sanitize(IsProject ? $"{read.Project} · {read.Environment}" : tag).Text;
        Tip = IsMalformed
            ? $"{EntryNameSanitizer.Sanitize(tag).Text} puts this entry in no project: {read.Problem}"
            : IsProject ? EntryNameSanitizer.Sanitize(tag).Text : null;

        RemoveCommand = new RelayCommand(() => owner.RemoveTag(this));
    }

    /// <summary>The tag as the entry holds it. Addresses the tag on removal.</summary>
    internal string Tag { get; }

    /// <summary>What the chip says.</summary>
    internal string Display { get; }

    internal bool IsProject { get; }

    internal bool IsMalformed { get; }

    /// <summary>Whether this tag makes every agent request for the entry ask.</summary>
    internal bool IsProtected { get; }

    /// <summary>The chip's tip: the tag as written, and for a malformed one why it is ignored; null for an ordinary tag.</summary>
    internal string? Tip { get; }

    /// <summary>What the remove button is called. Names the tag.</summary>
    internal string RemoveLabel => $"Remove the tag {Display}";

    internal RelayCommand RemoveCommand { get; }
}
