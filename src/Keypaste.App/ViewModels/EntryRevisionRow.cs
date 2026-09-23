using System.Globalization;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One earlier state of an entry, as the detail pane lists it: when it was current, what it held,
/// and a way to see or copy its password.
/// </summary>
/// <remarks>
/// <para>
/// <b>The password is not a property of this object</b>, for the reason
/// <see cref="EnvVariableRow"/> gives: a row that carried its value would put every superseded
/// password of the selected entry in a view model for as long as the list is open, which is what
/// <c>SecretHygieneTests</c> exists to catch. <see cref="Reveal"/> reads it out of the open vault at
/// the moment of the press.
/// </para>
/// <para>
/// <b>It holds no <see cref="EntryRevision"/> either.</b> <see cref="VaultEntry"/> is a record, so
/// its generated <c>ToString</c> prints <see cref="VaultEntry.Password"/>, and a retained revision
/// would hand the characters to any sweep that stringifies what it finds.
/// </para>
/// <para>
/// <see cref="Index"/> is an ordinal within the reading that built this row and not an identifier
/// (DECISIONS.md D-0229). <see cref="EntryHistoryViewModel"/> re-reads before using one, and
/// <see cref="ModifiedUtc"/>, <see cref="Title"/> and <see cref="MaskedLength"/> are what it checks
/// the reading against.
/// </para>
/// </remarks>
internal sealed class EntryRevisionRow : ObservableObject, IRevealSource
{
    private readonly EntryHistoryViewModel _owner;
    private readonly string _username;
    private readonly string _url;
    private readonly string _notes;

    internal EntryRevisionRow(EntryHistoryViewModel owner, EntryRevision revision)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(revision);

        _owner = owner;
        Index = revision.Index;
        ModifiedUtc = revision.ModifiedUtc;
        Title = revision.Fields.Title;
        MaskedLength = revision.Fields.Password.Length;
        _username = revision.Fields.Username;
        _url = revision.Fields.Url;
        _notes = revision.Fields.Notes;

        CopyPasswordCommand = new AsyncRelayCommand(() => _owner.Copy(this), () => MaskedLength > 0);
    }

    /// <summary>This revision's position in the reading that built the list, newest first.</summary>
    internal int Index { get; }

    /// <summary>When this revision was last modified, as the vault recorded it.</summary>
    internal DateTime ModifiedUtc { get; }

    /// <summary>The title this revision carried. A name, and part of its fingerprint.</summary>
    /// <remarks>
    /// Not an address: KeePassXC can rename an entry, and the revisions either side of that rename
    /// answer to different paths while remaining one entry (<see cref="EntryRevision"/>'s remarks).
    /// It matters here because restoring one of them renames the entry back.
    /// </remarks>
    internal string Title { get; }

    /// <summary>When this revision was current, rendered as the Log screen renders a time.</summary>
    internal string When =>
        ModifiedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The title as the list draws it.</summary>
    internal string DisplayTitle => EntryNameSanitizer.Sanitize(Title).Text;

    /// <summary>The username this revision held, as the pane draws it.</summary>
    internal string DisplayUsername =>
        DisplayTextSanitizer.Sanitize(_username, DisplayTextSanitizer.MaximumLength).Text;

    /// <summary>The URL this revision held, as the pane draws it.</summary>
    internal string DisplayUrl =>
        DisplayTextSanitizer.Sanitize(_url, DisplayTextSanitizer.MaximumLength).Text;

    /// <summary>The notes this revision held, as the pane draws it.</summary>
    internal string DisplayNotes =>
        DisplayTextSanitizer.Sanitize(_notes, DisplayTextSanitizer.MaximumNotesLength).Text;

    /// <inheritdoc/>
    public int MaskedLength { get; }

    /// <summary>The dots the comparison shows where this revision's password would be.</summary>
    internal string PasswordMask => new('•', Math.Min(MaskedLength, 24));

    /// <summary>What a screen reader is told the hold does. Names the time, never the value.</summary>
    internal string RevealLabel => $"Hold to reveal the password from {When}";

    /// <summary>Copies this revision's password, with the auto-clearing countdown.</summary>
    internal AsyncRelayCommand CopyPasswordCommand { get; }

    /// <inheritdoc/>
    public string? Reveal() => _owner.Reveal(this);

    /// <inheritdoc/>
    public void Conceal() => _owner.Conceal(this);
}
