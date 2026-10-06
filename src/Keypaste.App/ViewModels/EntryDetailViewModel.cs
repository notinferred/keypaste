using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Activity;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The one entry somebody selected: its fields, its copy buttons, its inline edit, and its custom
/// fields and tags.
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
/// <para>
/// Custom fields are listed by name and protection only, and each value, plain or protected, is read
/// the same way as the password (V.7b). Every change to a field or a tag is one core call and one
/// save, so it is one revision in the entry's history.
/// </para>
/// </remarks>
internal sealed class EntryDetailViewModel : ObservableObject, IRevealSource, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
    private readonly Action<EntryName> _restored;
    private readonly IWebLauncher? _web;
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
    private bool _isConfirmingRotate;
    private string _created = string.Empty;
    private string _rotated = string.Empty;
    private string? _uuid;
    private EntryAgentAccess? _agentAccess;
    private bool _showsAgentAccess;
    private string _agentAccessSummary = "None active";
    private string _lastUsedText = "never";
    private EntryKind _kind;
    private IReadOnlyList<string> _agentLines = [];
    private IReadOnlyList<EntryFieldRow> _fields = [];
    private IReadOnlyList<EntryTagChip> _tags = [];
    private bool _isAddingField;
    private string _draftFieldName = string.Empty;
    private bool _newFieldProtected = true;
    private EntryFieldRow? _replacingField;
    private EntryFieldRow? _removingField;
    private string _draftTag = string.Empty;
    private ProjectTagChange? _pendingTagChange;

    internal EntryDetailViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        VaultEntry entry,
        Action<string?> report,
        Action<EntryName> restored,
        IWebLauncher? web = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(restored);

        _session = session;
        _clipboard = clipboard;
        _entryPath = entry.Path;
        _web = web;
        Report = report;

        _title = entry.Title;
        _groupPath = entry.GroupPath;
        _username = entry.Username;
        _url = entry.Url;
        _notes = entry.Notes;
        PasswordLength = entry.Password.Length;
        _kind = EntryKinds.Of(entry);
        VaultName = session.VaultPath is { } vaultPath ? System.IO.Path.GetFileName(vaultPath) : string.Empty;

        Reference = KpReferences.ForEntry(Name);

        NewPassword = new SecretField(clipboard);
        NewFieldValue = new SecretField(clipboard);
        ReplacementFieldValue = new SecretField(clipboard);
        _restored = restored;
        History = new EntryHistoryViewModel(session, clipboard, this, Restored);

        CopyPasswordCommand = new AsyncRelayCommand(CopyPasswordAsync, () => PasswordLength > 0);
        CopyUsernameCommand = new AsyncRelayCommand(CopyUsernameAsync, () => Username.Length > 0);
        CopyReferenceCommand = new AsyncRelayCommand(CopyReferenceAsync, () => Reference is not null);
        OpenUrlCommand = new AsyncRelayCommand(OpenUrlAsync, () => OpensUrl);
        CopyUrlCommand = new AsyncRelayCommand(CopyUrlAsync, () => Url.Length > 0);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsEditing) or nameof(IsConfirmingRotate) or nameof(IsAddingField) or nameof(IsReplacingField))
            {
                Raise(nameof(HasOwnPrimary));
            }

            if (e.PropertyName == nameof(IsEditing))
            {
                Raise(nameof(TakesWholeView));
            }
        };
        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EntryHistoryViewModel.IsOpen))
            {
                Raise(nameof(TakesWholeView));
            }
        };
        EditCommand = new RelayCommand(BeginEdit, () => !IsEditing);
        CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);
        SaveCommand = new RelayCommand(SaveEdit, () => IsEditing);
        RotateCommand = new RelayCommand(() => IsConfirmingRotate = true, () => !IsConfirmingRotate && !IsEditing);
        ConfirmRotateCommand = new RelayCommand(ConfirmRotate, () => IsConfirmingRotate);
        CancelRotateCommand = new RelayCommand(() => IsConfirmingRotate = false, () => IsConfirmingRotate);
        BeginAddFieldCommand = new RelayCommand(BeginAddField, () => !IsAddingField);
        ConfirmAddFieldCommand = new RelayCommand(ConfirmAddField, () => IsAddingField && DraftFieldName.Trim().Length > 0);
        CancelAddFieldCommand = new RelayCommand(CancelAddField, () => IsAddingField);
        ConfirmReplaceFieldCommand = new RelayCommand(ConfirmReplaceField, () => ReplacingField is not null);
        CancelReplaceFieldCommand = new RelayCommand(() => ReplacingField = null, () => ReplacingField is not null);
        ConfirmRemoveFieldCommand = new RelayCommand(ConfirmRemoveField, () => RemovingField is not null);
        CancelRemoveFieldCommand = new RelayCommand(() => RemovingField = null, () => RemovingField is not null);
        AddTagCommand = new RelayCommand(AddTag, () => DraftTag.Trim().Length > 0);
        ConfirmTagChangeCommand = new RelayCommand(ConfirmTagChange, () => PendingTagChange is not null);
        CancelTagChangeCommand = new RelayCommand(() => PendingTagChange = null, () => PendingTagChange is not null);

        ReadTimes();
        ReadFieldsAndTags();
    }

    /// <summary>The countdown every copy on this pane goes through.</summary>
    internal ClipboardCountdown Clipboard => _clipboard;

    /// <summary>The entry's custom fields, by name, in ordinal order.</summary>
    internal IReadOnlyList<EntryFieldRow> Fields
    {
        get => _fields;
        private set
        {
            if (Set(ref _fields, value))
            {
                Raise(nameof(HasFields));
            }
        }
    }

    internal bool HasFields => _fields.Count > 0;

    /// <summary>The entry's own tags, never its group's.</summary>
    internal IReadOnlyList<EntryTagChip> Tags
    {
        get => _tags;
        private set
        {
            if (Set(ref _tags, value))
            {
                Raise(nameof(HasTags));
            }
        }
    }

    internal bool HasTags => _tags.Count > 0;

    /// <summary>A new field's value, while its form is open.</summary>
    internal SecretField NewFieldValue { get; }

    /// <summary>A replacement for one field's value, while its form is open.</summary>
    internal SecretField ReplacementFieldValue { get; }

    internal bool IsAddingField
    {
        get => _isAddingField;
        private set
        {
            if (Set(ref _isAddingField, value))
            {
                BeginAddFieldCommand.RaiseCanExecuteChanged();
                ConfirmAddFieldCommand.RaiseCanExecuteChanged();
                CancelAddFieldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal string DraftFieldName
    {
        get => _draftFieldName;
        set
        {
            if (Set(ref _draftFieldName, value))
            {
                ConfirmAddFieldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Whether the new field is protected; on unless the person turns it off.</summary>
    internal bool NewFieldProtected
    {
        get => _newFieldProtected;
        set => Set(ref _newFieldProtected, value);
    }

    /// <summary>The field whose value the replace form is for, or null.</summary>
    internal EntryFieldRow? ReplacingField
    {
        get => _replacingField;
        private set
        {
            if (Set(ref _replacingField, value))
            {
                ReplacementFieldValue.Clear();
                Raise(nameof(IsReplacingField));
                Raise(nameof(ReplaceFieldPrompt));
                ConfirmReplaceFieldCommand.RaiseCanExecuteChanged();
                CancelReplaceFieldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsReplacingField => _replacingField is not null;

    /// <summary>Whether a form in the pane shows its own primary button: an edit, a rotate, or adding or replacing a field.</summary>
    internal bool HasOwnPrimary => IsEditing || IsConfirmingRotate || IsAddingField || IsReplacingField;

    /// <summary>Whether the item takes the whole of Items, as KeePassXC's editor does: while it is edited or two revisions are compared.</summary>
    internal bool TakesWholeView => IsEditing || History.IsOpen;

    internal string ReplaceFieldPrompt => $"New value for {_replacingField?.DisplayName}";

    /// <summary>The field the remove confirmation is for, or null.</summary>
    internal EntryFieldRow? RemovingField
    {
        get => _removingField;
        private set
        {
            if (Set(ref _removingField, value))
            {
                Raise(nameof(IsRemovingField));
                Raise(nameof(RemoveFieldPrompt));
                ConfirmRemoveFieldCommand.RaiseCanExecuteChanged();
                CancelRemoveFieldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsRemovingField => _removingField is not null;

    internal string RemoveFieldPrompt => $"Remove {_removingField?.DisplayName} from this entry? Its value stays in the entry's history.";

    /// <summary>A tag being typed, added on Add tag.</summary>
    internal string DraftTag
    {
        get => _draftTag;
        set
        {
            if (Set(ref _draftTag, value))
            {
                AddTagCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal RelayCommand BeginAddFieldCommand { get; }

    internal RelayCommand ConfirmAddFieldCommand { get; }

    internal RelayCommand CancelAddFieldCommand { get; }

    internal RelayCommand ConfirmReplaceFieldCommand { get; }

    internal RelayCommand CancelReplaceFieldCommand { get; }

    internal RelayCommand ConfirmRemoveFieldCommand { get; }

    internal RelayCommand CancelRemoveFieldCommand { get; }

    internal RelayCommand AddTagCommand { get; }

    /// <summary>A project tag being added or removed, held until the person confirms what it reaches (D-0415); null otherwise.</summary>
    internal ProjectTagChange? PendingTagChange
    {
        get => _pendingTagChange;
        private set
        {
            if (Set(ref _pendingTagChange, value))
            {
                Raise(nameof(IsConfirmingTagChange));
                Raise(nameof(TagChangeLines));
                Raise(nameof(TagChangeAction));
                ConfirmTagChangeCommand.RaiseCanExecuteChanged();
                CancelTagChangeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsConfirmingTagChange => _pendingTagChange is not null;

    /// <summary>What the pending tag change reaches: its environment and the fields joining or leaving it, never a value.</summary>
    internal IReadOnlyList<string> TagChangeLines => _pendingTagChange?.Describe() ?? [];

    /// <summary>What the confirming button says.</summary>
    internal string TagChangeAction => _pendingTagChange?.Adding == false ? "Remove tag" : "Add tag";

    /// <summary>Writes the pending tag change.</summary>
    internal RelayCommand ConfirmTagChangeCommand { get; }

    /// <summary>Drops the pending tag change, writing nothing.</summary>
    internal RelayCommand CancelTagChangeCommand { get; }

    /// <summary>Reads one custom field out of the open vault, for a hold or a copy.</summary>
    internal string? ReadField(string field)
    {
        try
        {
            return _session.Unlocked?.ReadField(Name, field);
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return null;
        }
    }

    /// <summary>Opens the form that writes a new value over one field's.</summary>
    internal void BeginReplaceField(EntryFieldRow row)
    {
        IsAddingField = false;
        RemovingField = null;
        ReplacingField = row;
        Report(null);
    }

    /// <summary>Asks whether to remove one field.</summary>
    internal void BeginRemoveField(EntryFieldRow row)
    {
        IsAddingField = false;
        ReplacingField = null;
        RemovingField = row;
        Report(null);
    }

    /// <summary>Protects a plain field or stops protecting a protected one, keeping its value.</summary>
    internal void ToggleProtection(EntryFieldRow row) =>
        Write(vault => vault.SetFields(Name, [new FieldWrite(row.Name, Value: null, Protect: !row.IsProtected)]), null);

    /// <summary>Removes one of the entry's tags, first asking about a project tag.</summary>
    internal void RemoveTag(EntryTagChip chip)
    {
        if (!Ask(chip.Tag, adding: false))
        {
            Write(vault => vault.RemoveTags(Name, [chip.Tag]), "That tag is no longer on this entry.");
        }
    }

    /// <summary>Replaces the password with a generated one, after asking.</summary>
    internal RelayCommand RotateCommand { get; }

    internal RelayCommand ConfirmRotateCommand { get; }

    internal RelayCommand CancelRotateCommand { get; }

    /// <summary>Whether the pane is asking whether to rotate.</summary>
    internal bool IsConfirmingRotate
    {
        get => _isConfirmingRotate;
        private set
        {
            if (Set(ref _isConfirmingRotate, value))
            {
                RotateCommand.RaiseCanExecuteChanged();
                ConfirmRotateCommand.RaiseCanExecuteChanged();
                CancelRotateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>What the confirm row asks; an instance property because a binding needs one.</summary>
#pragma warning disable CA1822
    internal string RotatePrompt =>
        $"Replace the saved password with a new {PasswordGenerator.DefaultLength}-character one? keypaste only changes its copy: set the new password on the site or service too, or you will need the old one from history to sign in.";
#pragma warning restore CA1822

    /// <summary>When the entry was created, as the metadata row shows it.</summary>
    internal string Created
    {
        get => _created;
        private set => Set(ref _created, value);
    }

    /// <summary>When its current password was set, as the metadata row shows it.</summary>
    internal string Rotated
    {
        get => _rotated;
        private set => Set(ref _rotated, value);
    }

    /// <summary>
    /// Whether the Agent access card shows: an agent attached here or a standing rule can name this
    /// entry, or it has left the vault or is being asked for (N.5). False until the first reading,
    /// and always when nothing answers agents.
    /// </summary>
    internal bool ShowsAgentAccess
    {
        get => _showsAgentAccess;
        private set => Set(ref _showsAgentAccess, value);
    }

    /// <summary>What agents did with this entry, or null before the first reading.</summary>
    internal EntryAgentAccess? AgentAccess
    {
        get => _agentAccess;
        private set => Set(ref _agentAccess, value);
    }

    /// <summary>The Agent access card's one line, when <see cref="AgentLines"/> does not list the same agents.</summary>
    internal string AgentAccessSummary
    {
        get => _agentAccessSummary;
        private set => Set(ref _agentAccessSummary, value);
    }

    internal bool ShowsAgentAccessSummary => _agentLines.Count == 0;

    /// <summary>When an agent last received it: "in use", "4m ago" or "never".</summary>
    internal string LastUsedText
    {
        get => _lastUsedText;
        private set
        {
            if (Set(ref _lastUsedText, value))
            {
                Raise(nameof(HasBeenUsed));
            }
        }
    }

    /// <summary>Whether an agent ever received it, which is when the card says when.</summary>
    internal bool HasBeenUsed => _lastUsedText != "never";

    /// <summary>Takes the latest picture of what agents did, never a value.</summary>
    /// <param name="activity">The picture.</param>
    internal void Apply(EntryActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (_entryPath.Length == 0)
        {
            return;
        }

        var now = _session.Clock.GetUtcNow();
        var access = activity.Access(Name);
        ShowsAgentAccess = activity.AgentsCanSee(Name, [.. _tags.Select(chip => chip.Tag)]);
        AgentAccess = access;
        AgentAccessSummary = UseText.Summary(access);
        LastUsedText = UseText.LastUsed(access.Use, now);
        AgentLines = UseText.Lines(access, now);
    }

    /// <summary>The Agent access card's detail: each grant in force, then each client that received this entry.</summary>
    internal IReadOnlyList<string> AgentLines
    {
        get => _agentLines;
        private set
        {
            if (Set(ref _agentLines, value))
            {
                Raise(nameof(ShowsAgentAccessSummary));
            }
        }
    }

    /// <summary>What sort of entry this is.</summary>
    internal EntryKind Kind
    {
        get => _kind;
        private set
        {
            if (Set(ref _kind, value))
            {
                Raise(nameof(KindLabel));
                Raise(nameof(Location));
            }
        }
    }

    internal string KindLabel => EntryKinds.Label(_kind);

    /// <summary>The vault's file name.</summary>
    internal string VaultName { get; }

    /// <summary>The header's second line: the kind, then the vault file and each group down to this entry.</summary>
    internal string Location
    {
        get
        {
            var trail = _groupPath.Length == 0
                ? VaultName
                : string.Join(" › ", _groupPath.Split('/').Select(group => EntryNameSanitizer.Sanitize(group).Text).Prepend(VaultName));

            return trail.Length == 0 ? KindLabel : $"{KindLabel} · {trail}";
        }
    }

    /// <summary>Where the value lives in the KDBX file: the entry's UUID, abbreviated, and its field.</summary>
    /// <remarks>The path is on the location line already; the UUID is what KeePassXC shows nowhere else.</remarks>
    internal string KdbxEntry => _uuid is { Length: > 8 } uuid
        ? $"uuid {uuid[..4].ToLowerInvariant()}…{uuid[^4..].ToLowerInvariant()} · field Password"
        : $"{DisplayPath} · field Password";

    /// <summary>Whether the URL is a link that opens in the browser: http or https, or a bare address as https (D-0378).</summary>
    internal bool OpensUrl => WebAddress.TryOpenable(Url, out _);

    /// <summary>Whether the URL is shown as text, with a line saying why it opens nothing.</summary>
    internal bool ShowsUrlNote => Url.Length > 0 && !OpensUrl;

    /// <summary>Opens the URL in the default browser.</summary>
    internal AsyncRelayCommand OpenUrlCommand { get; }

    /// <summary>Copies the URL as it is stored, which is not a secret.</summary>
    internal AsyncRelayCommand CopyUrlCommand { get; }

    internal bool ShowsNotes => Notes.Length > 0;

    internal bool HasReference => Reference is not null;

    /// <summary>Copies <see cref="Reference"/>, which names the entry and holds no value.</summary>
    internal AsyncRelayCommand CopyReferenceCommand { get; }

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

    /// <summary>The <c>kp://</c> reference that names this entry, or null for one no reference resolves.</summary>
    internal string? Reference { get; private set; }

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
                Raise(nameof(OpensUrl));
                Raise(nameof(ShowsUrlNote));
                OpenUrlCommand.RaiseCanExecuteChanged();
                CopyUrlCommand.RaiseCanExecuteChanged();
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
                Raise(nameof(ShowsNotes));
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
        Kind = EntryKinds.Of(entry);
        Raise(nameof(PasswordLength));
        Raise(nameof(MaskedLength));
        Raise(nameof(PasswordMask));
        CopyPasswordCommand.RaiseCanExecuteChanged();
        ReadFieldsAndTags();
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
        _uuid = null;
        Reference = null;
        AgentLines = [];
        Fields = [];
        Tags = [];
        ReplacingField = null;
        RemovingField = null;
        DraftFieldName = string.Empty;
        DraftTag = string.Empty;
        NewPassword.Dispose();
        NewFieldValue.Dispose();
        ReplacementFieldValue.Dispose();
        History.Dispose();

        Raise(nameof(Reference));
        Raise(nameof(HasReference));
        Raise(nameof(Location));
        Raise(nameof(KdbxEntry));
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

    private async Task OpenUrlAsync()
    {
        if (!WebAddress.TryOpenable(Url, out var address))
        {
            return;
        }

        if (_web is null || !await _web.OpenAsync(address).ConfigureAwait(true))
        {
            Report($"Your system didn't open {DisplayUrl}. Copy it and paste it into your browser.");
        }
    }

    private async Task CopyUrlAsync() =>
        await _clipboard.CopyPlainAsync(Url, "Web address").ConfigureAwait(true);

    private async Task CopyReferenceAsync()
    {
        if (Reference is { } reference)
        {
            await _clipboard.CopyPlainAsync(reference, "Reference").ConfigureAwait(true);
        }
    }

    private void ReadTimes()
    {
        if (_session.Unlocked is not { } vault)
        {
            return;
        }

        try
        {
            var now = _session.Clock.GetUtcNow();
            Created = UseText.Dated(vault.ReadTimes(Name)?.Created, now);
            Rotated = UseText.Dated(EntryRotation.LastRotated(vault, Name), now);
            _uuid = vault.EntryUuid(Name);
            Raise(nameof(KdbxEntry));
        }
        catch (VaultException e)
        {
            Report(e.Message);
        }
    }

    private void ConfirmRotate()
    {
        IsConfirmingRotate = false;

        if (_session.Unlocked is not { } vault)
        {
            Report("The vault is locked.");
            return;
        }

        try
        {
            if (EntryRotation.Rotate(vault, Name, SecretRecipe.Default) != RotateOutcome.Rotated)
            {
                Report($"'{_entryPath}' could not be rotated here.");
                return;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Report("Something else changed this vault since you opened it. Reload to see it, then make your change again.");
            return;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return;
        }

        Reload();
        ReadTimes();
        History.Refresh();
        Report(null);
    }

    private void ReadFieldsAndTags()
    {
        if (_session.Unlocked is not { } vault)
        {
            return;
        }

        try
        {
            Fields = [.. (vault.Fields(Name) ?? []).Select(field => new EntryFieldRow(this, field))];
            Tags = [.. (vault.Tags(Name) ?? []).Select(tag => new EntryTagChip(tag, RemoveTag))];
        }
        catch (VaultException e)
        {
            Report(e.Message);
        }
    }

    /// <summary>Makes one change to this entry through core and saves it, as one revision.</summary>
    /// <param name="change">The change; false when it found nothing to change.</param>
    /// <param name="unchanged">What to say when it found nothing, or null to say the entry is gone.</param>
    /// <returns>Whether the change was saved.</returns>
    private bool Write(Func<Vault, bool> change, string? unchanged)
    {
        if (_session.Unlocked is not { } vault)
        {
            Report("The vault is locked.");
            return false;
        }

        try
        {
            if (!change(vault))
            {
                Report(unchanged ?? $"'{_entryPath}' is no longer in this vault.");
                return false;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Report("Something else changed this vault since you opened it. Reload to see it, then make your change again.");
            return false;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return false;
        }

        ReadFieldsAndTags();

        // The change is a revision now, and a list read before it would name the wrong one at every index (D-0229).
        History.Refresh();
        Report(null);
        return true;
    }

    private void BeginAddField()
    {
        ReplacingField = null;
        RemovingField = null;
        DraftFieldName = string.Empty;
        NewFieldValue.Clear();
        NewFieldProtected = true;
        IsAddingField = true;
        Report(null);
    }

    private void CancelAddField()
    {
        IsAddingField = false;
        NewFieldValue.Clear();
        Report(null);
    }

    private void ConfirmAddField()
    {
        var name = DraftFieldName.Trim();

        if (!FieldNameRules.IsWritable(name, out var error))
        {
            Report(string.Concat(char.ToUpperInvariant(error[0]).ToString(), error.AsSpan(1), "."));
            return;
        }

        if (Fields.Any(field => string.Equals(field.Name, name, StringComparison.Ordinal)))
        {
            Report($"This entry already has {EntryNameSanitizer.Sanitize(name).Text}. Replace its value instead.");
            return;
        }

        if (Write(vault => vault.SetFields(Name, [new FieldWrite(name, NewFieldValue.Compose(), NewFieldProtected)]), null))
        {
            IsAddingField = false;
            NewFieldValue.Clear();
            DraftFieldName = string.Empty;
        }
    }

    private void ConfirmReplaceField()
    {
        if (ReplacingField is not { } row)
        {
            return;
        }

        if (Write(vault => vault.SetFields(Name, [new FieldWrite(row.Name, ReplacementFieldValue.Compose())]), null))
        {
            ReplacingField = null;
        }
    }

    private void ConfirmRemoveField()
    {
        if (RemovingField is not { } row)
        {
            return;
        }

        if (Write(vault => vault.RemoveField(Name, row.Name), $"{row.DisplayName} is no longer on this entry."))
        {
            RemovingField = null;
        }
    }

    private void AddTag()
    {
        var tag = DraftTag.Trim();

        if (Tags.Any(chip => string.Equals(chip.Tag, tag, StringComparison.Ordinal)) || !Ask(tag, adding: true))
        {
            WriteTag(tag, adding: true);
        }
    }

    /// <summary>Holds a project tag change for the person to confirm.</summary>
    /// <returns>Whether the tag reaches a project and is now waiting; false for a tag to write at once.</returns>
    private bool Ask(string tag, bool adding)
    {
        if (_session.Unlocked is not { } vault)
        {
            return false;
        }

        try
        {
            PendingTagChange = ProjectTagChange.Preview(vault, Name, [tag], adding);
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return true;
        }

        Report(null);
        return PendingTagChange is not null;
    }

    private void ConfirmTagChange()
    {
        if (PendingTagChange is not { } change)
        {
            return;
        }

        PendingTagChange = null;
        WriteTag(change.Tags[0], change.Adding);
    }

    private void WriteTag(string tag, bool adding)
    {
        if (adding)
        {
            if (Write(vault => vault.AddTag(Name, tag), "This entry already has that tag."))
            {
                DraftTag = string.Empty;
            }
        }
        else
        {
            Write(vault => vault.RemoveTags(Name, [tag]), "That tag is no longer on this entry.");
        }
    }

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
            Report("Something else changed this vault since you opened it. Reload to see it, then make your change again.");
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
