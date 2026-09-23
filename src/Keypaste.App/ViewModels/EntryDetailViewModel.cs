using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The one entry somebody selected: its fields, its copy buttons, and its inline edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The password is not a property of this object, in any state.</b> Title, group, username, URL
/// and notes are read once on selection and held, which is a deliberate widening bounded to one
/// entry a person chose — <c>keypaste get</c>'s scope minus the password. The password is read out
/// of the open vault at the moment Copy is pressed or its cell is held (D-0300), and handed straight
/// to the clipboard or to the <see cref="Controls.RevealedValue"/> that draws it, so it is never in a
/// view model and never in a binding.
/// </para>
/// </remarks>
internal sealed class EntryDetailViewModel : ObservableObject, IRevealSource, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
    private readonly Action<EntryName> _restored;
    private string _entryPath;
    private string _title;
    private string _groupPath;

    private string _username;
    private string _url;
    private string _notes;
    private bool _isEditing;
    private string _draftUsername = string.Empty;
    private string _draftUrl = string.Empty;
    private string _draftNotes = string.Empty;

    internal EntryDetailViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        VaultEntry entry,
        Action<string?> report,
        Action<EntryName> restored)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(restored);

        _session = session;
        _clipboard = clipboard;
        _entryPath = entry.Path;
        Report = report;

        _title = entry.Title;
        _groupPath = entry.GroupPath;
        _username = entry.Username;
        _url = entry.Url;
        _notes = entry.Notes;
        PasswordLength = entry.Password.Length;

        NewPassword = new SecretField(clipboard);
        _restored = restored;
        History = new EntryHistoryViewModel(session, clipboard, this, Restored);

        CopyPasswordCommand = new AsyncRelayCommand(CopyPasswordAsync, () => PasswordLength > 0);
        CopyUsernameCommand = new AsyncRelayCommand(CopyUsernameAsync, () => Username.Length > 0);
        EditCommand = new RelayCommand(BeginEdit, () => !IsEditing);
        CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);
        SaveCommand = new RelayCommand(SaveEdit, () => IsEditing);
    }

    /// <summary>Where a failure goes. Owned by the entries screen, which draws the banner.</summary>
    internal Action<string?> Report { get; }

    /// <summary>The entry's title. Read-only: a new title is a different entry (core's rule).</summary>
    internal string Title => _title;

    /// <summary>The entry's group.</summary>
    internal string GroupPath => _groupPath;

    /// <summary>
    /// The entry this pane is looking at: its group and its title, which is what addresses it.
    /// </summary>
    /// <remarks>
    /// The pane used to reach for <see cref="Path"/>, and two entries can answer to one of those —
    /// so a copy served a secret the person had not selected and an edit wrote to its neighbour
    /// (docs/STEPS.md F.1e). Every vault access below goes through this instead.
    /// </remarks>
    internal EntryName Name => new(_groupPath, _title);

    /// <summary>The entry's path, for the header and for the CLI hint.</summary>
    /// <remarks>A label, not an identity: joining is lossy, so nothing here looks an entry up by
    /// it. Drawn as <see cref="DisplayPath"/>.</remarks>
    internal string Path => _entryPath;

    /// <summary>The title as the pane draws it.</summary>
    internal string DisplayTitle => EntryNameSanitizer.Sanitize(_title).Text;

    /// <summary>The path as the pane draws it.</summary>
    internal string DisplayPath => EntryNameSanitizer.SanitizePath(_entryPath).Text;

    /// <summary>The username as the pane draws it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="Username"/> on purpose.</b> That one seeds
    /// <c>DraftUsername</c> when editing begins and is what the Copy button puts on the clipboard,
    /// so scrubbing it in place would write scrubbed text back into the vault, or paste it. Only
    /// the <c>TextBlock</c> reads this.
    /// </para>
    /// <para>
    /// <b><see cref="DisplayTextSanitizer"/> and not <see cref="EntryNameSanitizer"/>.</b> These
    /// three are read by a person and address nothing — <see cref="Name"/> addresses the entry — so
    /// the name rule's structural set has no argument here and cost a great deal: a URL lost its
    /// slashes, a note its brackets and line breaks, and this field the backslash in a Windows
    /// login. What still goes is everything that can make text misrepresent itself, which
    /// <c>HostileNameRenderingTests</c> holds and <c>FaithfulFieldRenderingTests</c> holds the other
    /// side of. <see cref="DisplayTitle"/> and <see cref="DisplayPath"/> stay on the name rule,
    /// because they are names.
    /// </para>
    /// </remarks>
    internal string DisplayUsername =>
        DisplayTextSanitizer.Sanitize(Username, DisplayTextSanitizer.MaximumLength).Text;

    /// <summary>The URL as the pane draws it. Separate from <see cref="Url"/> for the same reason.</summary>
    internal string DisplayUrl =>
        DisplayTextSanitizer.Sanitize(Url, DisplayTextSanitizer.MaximumLength).Text;

    /// <summary>The notes as the pane draws it. Separate from <see cref="Notes"/> likewise.</summary>
    internal string DisplayNotes =>
        DisplayTextSanitizer.Sanitize(Notes, DisplayTextSanitizer.MaximumNotesLength).Text;

    internal string Username
    {
        get => _username;
        private set
        {
            if (Set(ref _username, value))
            {
                Raise(nameof(DisplayUsername));
                CopyUsernameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal string Url
    {
        get => _url;
        private set
        {
            if (Set(ref _url, value))
            {
                Raise(nameof(DisplayUrl));
            }
        }
    }

    internal string Notes
    {
        get => _notes;
        private set
        {
            if (Set(ref _notes, value))
            {
                Raise(nameof(DisplayNotes));
            }
        }
    }

    /// <summary>
    /// How long the password is, which is all this object knows about it.
    /// </summary>
    /// <remarks>
    /// A length rather than a value, for the mask — the same trade
    /// <see cref="Controls.MaskedInput.MaskedLength"/> makes. It is a disclosure, and a small one:
    /// it is visible to anyone who can already see that an entry exists.
    /// </remarks>
    internal int PasswordLength { get; private set; }

    /// <summary>The dots the detail pane shows where the password would be.</summary>
    internal string PasswordMask => new('•', Math.Min(PasswordLength, 24));

    /// <inheritdoc/>
    public int MaskedLength => PasswordLength;

    /// <summary>A replacement password, while editing. Empty means "leave it alone".</summary>
    /// <remarks>
    /// Empty rather than a separate "change the password" switch: the field is the switch. Somebody
    /// editing a username should not have to say they are not touching the password, and a
    /// replacement nobody typed is exactly what an empty buffer already means.
    /// </remarks>
    internal SecretField NewPassword { get; }

    /// <summary>The entry's earlier values, and the way back to one of them.</summary>
    /// <remarks>
    /// Always here and never null, so the section can bind whether or not it has been opened; it
    /// reads nothing out of the vault until somebody asks for it.
    /// </remarks>
    internal EntryHistoryViewModel History { get; }

    internal bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (Set(ref _isEditing, value))
            {
                EditCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal string DraftUsername
    {
        get => _draftUsername;
        set => Set(ref _draftUsername, value);
    }

    internal string DraftUrl
    {
        get => _draftUrl;
        set => Set(ref _draftUrl, value);
    }

    internal string DraftNotes
    {
        get => _draftNotes;
        set => Set(ref _draftNotes, value);
    }

    /// <summary>Copies the password, with the auto-clearing countdown.</summary>
    internal AsyncRelayCommand CopyPasswordCommand { get; }

    /// <summary>Copies the username, which is not a secret and gets no countdown.</summary>
    internal AsyncRelayCommand CopyUsernameCommand { get; }

    internal RelayCommand EditCommand { get; }

    internal RelayCommand CancelCommand { get; }

    internal RelayCommand SaveCommand { get; }

    /// <summary>Refreshes from the vault after a save.</summary>
    internal void Reload()
    {
        VaultEntry? found;
        try
        {
            found = _session.Unlocked?.Find(Name);
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return;
        }

        if (found is not { } entry)
        {
            return;
        }

        Username = entry.Username;
        Url = entry.Url;
        Notes = entry.Notes;
        PasswordLength = entry.Password.Length;
        Raise(nameof(PasswordLength));
        Raise(nameof(MaskedLength));
        Raise(nameof(PasswordMask));
        CopyPasswordCommand.RaiseCanExecuteChanged();
    }

    /// <inheritdoc/>
    public string? Reveal()
    {
        try
        {
            return _session.Unlocked?.Find(Name)?.Password;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>The pane has one current password, so there is no slot to give back.</remarks>
    public void Conceal()
    {
    }

    /// <summary>
    /// Lets go of everything read out of the vault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called when the selection changes and when the shell is disposed, which is what a lock does.
    /// Without it this object goes on holding a username, a URL and a notes field after the vault
    /// that produced them is gone — and it can be referenced by an in-flight continuation long
    /// after the shell has stopped pointing at it.
    /// </para>
    /// <para>
    /// <b>What this is and is not.</b> A <c>string</c> cannot be wiped, so the characters may
    /// survive in the heap until a collection; T-18 and <see cref="SecretBuffer"/>'s own remarks say
    /// so. What this buys is that no live object exposes them, which is exactly the claim
    /// <c>SecretHygieneTests.Nothing_built_while_unlocked_survives_the_lock</c> makes.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        _entryPath = string.Empty;
        _title = string.Empty;
        _groupPath = string.Empty;
        Username = string.Empty;
        Url = string.Empty;
        Notes = string.Empty;
        DraftUsername = string.Empty;
        DraftUrl = string.Empty;
        DraftNotes = string.Empty;
        PasswordLength = 0;
        NewPassword.Dispose();
        History.Dispose();

        Raise(nameof(Title));
        Raise(nameof(GroupPath));
        Raise(nameof(Path));
        Raise(nameof(MaskedLength));
        Raise(nameof(PasswordMask));
    }

    /// <summary>Picks up what a restore left, and tells the screen which entry it left it on.</summary>
    /// <remarks>
    /// A restored revision can carry an older title, and then this pane addresses an entry that no
    /// longer exists — so the pane refreshes itself only when the identity survived, and the screen
    /// rebuilds it either way.
    /// </remarks>
    private void Restored(EntryName name)
    {
        if (name == Name)
        {
            Reload();
            History.Refresh();
        }

        _restored(name);
    }

    private async Task CopyPasswordAsync()
    {
        // Read at the moment of the press, from the open vault, and handed straight on. The value
        // is a local for the length of this method and is in no field of this object.
        string? found;
        try
        {
            found = _session.Unlocked?.Find(Name)?.Password;
        }
        catch (VaultException e)
        {
            // Two entries with one title in one group. Copying either would put a secret nobody
            // chose on the clipboard, and this pane never draws one, so nothing would show it.
            Report(e.Message);
            return;
        }

        if (found is not { Length: > 0 } password)
        {
            Report("That entry could not be read. The vault may have locked.");
            return;
        }

        await _clipboard.CopyAsync(password, "Password").ConfigureAwait(true);
    }

    private async Task CopyUsernameAsync() =>
        await _clipboard.CopyPlainAsync(Username, "Username").ConfigureAwait(true);

    private void BeginEdit()
    {
        DraftUsername = Username;
        DraftUrl = Url;
        DraftNotes = Notes;
        NewPassword.Clear();
        IsEditing = true;
        Report(null);
    }

    private void CancelEdit()
    {
        IsEditing = false;
        NewPassword.Clear();
        Report(null);
    }

    private void SaveEdit()
    {
        if (_session.Unlocked is not { } vault)
        {
            Report("The vault is locked.");
            return;
        }

        try
        {
            if (vault.Find(Name) is not { } existing)
            {
                Report($"'{_entryPath}' is no longer in this vault.");
                return;
            }

            // An untouched password is carried across rather than read and written back, which
            // would put it in a local for no reason. A replacement is applied in the same
            // UpdateEntry as the field edits, so the whole change costs one history item rather
            // than two (D-0014).
            // Title and GroupPath come across too, so the read has to be the identity one — `UpdateEntry`
            // locates by them, and an `existing` from the wrong entry writes the draft into that entry.
            var updated = existing with
            {
                Username = DraftUsername,
                Url = DraftUrl,
                Notes = DraftNotes,
            };

            if (NewPassword.HasValue)
            {
                updated = updated with { Password = NewPassword.Compose() };
            }

            vault.UpdateEntry(updated);
            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Report("Something else changed this vault since you opened it. Lock and unlock to see it, then make your change again.");
            return;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return;
        }

        IsEditing = false;
        NewPassword.Clear();
        Reload();

        // The edit it just made is a revision now, and a list read before it would name the wrong
        // one at every index (D-0229).
        History.Refresh();
        Report(null);
    }
}
