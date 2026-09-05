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
/// of the open vault at the moment Copy is pressed and handed straight to the clipboard, so it is
/// never in a view model, never in a binding, and never in the visual tree.
/// </para>
/// <para>
/// <b>Why there is no reveal here when <c>Env Sets</c> has one.</b> The two are used differently. An
/// environment value gets compared by eye against a <c>.env</c> file or a provider's dashboard, so
/// reading it is the task. An entry password gets pasted into a login form, so copying it is the
/// task, and <c>keypaste get --show</c> is there for the times it genuinely has to be read. The
/// asymmetry is a decision, not an oversight; adding a reveal here later means widening the hygiene
/// gate's allow-set, which is where the argument belongs.
/// </para>
/// </remarks>
internal sealed class EntryDetailViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
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
        Action<string?> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(report);

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

        CopyPasswordCommand = new AsyncRelayCommand(CopyPasswordAsync, () => PasswordLength > 0);
        CopyUsernameCommand = new AsyncRelayCommand(CopyUsernameAsync, () => Username.Length > 0);
        EditCommand = new RelayCommand(BeginEdit, () => !IsEditing);
        CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);
        SaveCommand = new RelayCommand(SaveEdit, () => IsEditing);
    }

    /// <summary>The longest single-line field drawn.</summary>
    private const int _displayLength = 512;

    /// <summary>The longest notes body drawn. Notes is the largest free-text field in a vault.</summary>
    private const int _displayNotesLength = 8192;

    /// <summary>Where a failure goes. Owned by the entries screen, which draws the banner.</summary>
    internal Action<string?> Report { get; }

    /// <summary>The entry's title. Read-only: a new title is a different entry (core's rule).</summary>
    internal string Title => _title;

    /// <summary>The entry's group.</summary>
    internal string GroupPath => _groupPath;

    /// <summary>The entry's path, for the header and for the CLI hint.</summary>
    /// <remarks>An address: it is what <c>Find</c> and the save path use. Drawn as
    /// <see cref="DisplayPath"/>.</remarks>
    internal string Path => _entryPath;

    /// <summary>The title as the pane draws it.</summary>
    internal string DisplayTitle => EntryNameSanitizer.Sanitize(_title).Text;

    /// <summary>The path as the pane draws it.</summary>
    internal string DisplayPath => EntryNameSanitizer.SanitizePath(_entryPath).Text;

    /// <summary>The username as the pane draws it.</summary>
    /// <remarks>
    /// <b>Separate from <see cref="Username"/> on purpose.</b> That one seeds
    /// <c>DraftUsername</c> when editing begins and is what the Copy button puts on the clipboard,
    /// so scrubbing it in place would write scrubbed text back into the vault, or paste it. Only
    /// the <c>TextBlock</c> reads this.
    /// </remarks>
    internal string DisplayUsername => EntryNameSanitizer.Sanitize(Username, _displayLength).Text;

    /// <summary>The URL as the pane draws it. Separate from <see cref="Url"/> for the same reason.</summary>
    internal string DisplayUrl => EntryNameSanitizer.Sanitize(Url, _displayLength).Text;

    /// <summary>The notes as the pane draws it. Separate from <see cref="Notes"/> likewise.</summary>
    internal string DisplayNotes => EntryNameSanitizer.Sanitize(Notes, _displayNotesLength).Text;

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
        if (_session.Unlocked?.Find(_entryPath) is not { } entry)
        {
            return;
        }

        Username = entry.Username;
        Url = entry.Url;
        Notes = entry.Notes;
        PasswordLength = entry.Password.Length;
        Raise(nameof(PasswordLength));
        Raise(nameof(PasswordMask));
        CopyPasswordCommand.RaiseCanExecuteChanged();
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

        Raise(nameof(Title));
        Raise(nameof(GroupPath));
        Raise(nameof(Path));
        Raise(nameof(PasswordMask));
    }

    private async Task CopyPasswordAsync()
    {
        // Read at the moment of the press, from the open vault, and handed straight on. The value
        // is a local for the length of this method and is in no field of this object.
        if (_session.Unlocked?.Find(_entryPath)?.Password is not { Length: > 0 } password)
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
        IsEditing = true;
        Report(null);
    }

    private void CancelEdit()
    {
        IsEditing = false;
        Report(null);
    }

    private void SaveEdit()
    {
        if (_session.Unlocked is not { } vault)
        {
            Report("The vault is locked.");
            return;
        }

        if (vault.Find(_entryPath) is not { } existing)
        {
            Report($"'{_entryPath}' is no longer in this vault.");
            return;
        }

        // The password is carried across untouched rather than re-typed into the record: this pane
        // does not edit it, and reading it here only to write it back would put it in a local for
        // no reason. `keypaste get` and the generator are how a password changes.
        var updated = existing with
        {
            Username = DraftUsername,
            Url = DraftUrl,
            Notes = DraftNotes,
        };

        try
        {
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
        Reload();
        Report(null);
    }
}
