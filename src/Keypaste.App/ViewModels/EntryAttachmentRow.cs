#pragma warning disable RS0030 // One of the item pane's two attachment files (D-0534).
using System.Buffers;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One attachment in the item pane: its name and size, never its content.</summary>
/// <remarks>
/// Another KeePass program can give an attachment any name, so the pane draws <see cref="DisplayName"/> and the save
/// dialog starts from <see cref="SuggestedFileName"/>, while <see cref="Name"/> addresses the attachment unchanged.
/// </remarks>
internal sealed class EntryAttachmentRow
{
    private static readonly SearchValues<char> _notInFileName = SearchValues.Create("\"*:<>?|\\/");

    internal EntryAttachmentRow(EntryAttachmentsViewModel owner, EntryAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(attachment);

        Name = attachment.Name;
        SizeText = attachment.SizeText;
        SaveCommand = new AsyncRelayCommand(() => owner.SaveAsync(this), () => owner.CanPick);
        ReplaceCommand = new AsyncRelayCommand(() => owner.ReplaceAsync(this), () => owner.CanPick);
        RemoveCommand = new RelayCommand(() => owner.BeginRemove(this));
    }

    /// <summary>The attachment's name as the vault holds it, which addresses it on every act.</summary>
    internal string Name { get; }

    /// <summary>The name as the pane draws it.</summary>
    internal string DisplayName => EntryNameSanitizer.Sanitize(Name).Text;

    /// <summary>Its size as a person reads it, such as <c>4.0 KiB</c>.</summary>
    internal string SizeText { get; }

    /// <summary>The name the save dialog starts with: one file name, whatever the vault holds.</summary>
    internal string SuggestedFileName => SafeFileName(Name);

    internal string SaveLabel => $"Save {DisplayName}";

    internal string ReplaceLabel => $"Replace {DisplayName}";

    internal string RemoveLabel => $"Remove {DisplayName}";

    /// <summary>Asks where to save a copy, and writes one there only where nothing is.</summary>
    internal AsyncRelayCommand SaveCommand { get; }

    /// <summary>Asks for another file and puts what it holds in place of what this attachment holds.</summary>
    internal AsyncRelayCommand ReplaceCommand { get; }

    /// <summary>Asks whether to remove this attachment.</summary>
    internal RelayCommand RemoveCommand { get; }

    /// <summary>
    /// An attachment's name as a file name a save dialog can start with: drawn as the pane draws it, with every character
    /// a file name cannot hold on some platform made an underscore, and no trailing dot or space.
    /// </summary>
    /// <param name="name">The name as the vault holds it.</param>
    internal static string SafeFileName(string name)
    {
        var shown = EntryNameSanitizer.Sanitize(name).Text;
        var safe = string.Create(shown.Length, shown, static (span, text) =>
        {
            for (var i = 0; i < text.Length; i++)
            {
                span[i] = _notInFileName.Contains(text[i]) ? '_' : text[i];
            }
        }).TrimEnd(' ', '.');

        return safe.Length == 0 ? "attachment" : safe;
    }
}
