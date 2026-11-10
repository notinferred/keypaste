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
/// of the open vault at the moment Copy is pressed or its cell is held, and handed straight
/// to the clipboard or to the <see cref="Controls.RevealedValue"/> that draws it, so it is never in a
/// view model and never in a binding.
/// </para>
/// <para>
/// Custom fields are listed by name and protection only, and each value, plain or protected, is read
/// the same way as the password (V.7b). Every change to a field or a tag is one core call and one
/// save, so it is one revision in the entry's history.
/// </para>
/// <para>
/// A copy or an open hands on a value as it leaves keypaste, its KeePass placeholders filled in from
/// the item's own fields, and refuses one it cannot fill; a reveal, a mask and the edit form
/// keep the stored text.
/// </para>
/// <para>
/// An item with a TOTP gets <see cref="Totp"/>, whose code is computed the same way, at the press
///; one keypaste cannot compute says why in <see cref="TotpProblem"/> instead.
/// </para>
/// </remarks>
internal sealed partial class EntryDetailViewModel : ObservableObject, IRevealSource, IDisposable
{
    private SecretField? _generatedInto;
    private GeneratePasswordViewModel? _generatorPanel;
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
    private readonly Action<EntryName> _restored;
    private readonly IWebLauncher? _web;
    private readonly Action<Action> _post;
    private EntryTotpRow? _totp;
    private string _entryPath;
    private string _title;
    private string _groupPath;
    private (string Username, string Url, string Notes) _editOpenedWith = (string.Empty, string.Empty, string.Empty);

    private string? _uuid;
    private EntryFieldRow? _replacingField;
    private EntryFieldRow? _renamingField;
    private string _draftFieldRename = string.Empty;

    internal EntryDetailViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        VaultEntry entry,
        Action<string?> report,
        Action<EntryName> restored,
        IWebLauncher? web = null,
        Action<Action>? post = null,
        IVaultFilePicker? picker = null)
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
        _post = post ?? (run => run());
        Report = report;

        CopyPasswordCommand = new AsyncRelayCommand(CopyPasswordAsync, () => PasswordLength > 0);
        CopyUsernameCommand = new AsyncRelayCommand(CopyUsernameAsync, () => Username.Length > 0);
        CopyReferenceCommand = new AsyncRelayCommand(CopyReferenceAsync, () => Reference is not null);
        CopyUrlCommand = new AsyncRelayCommand(CopyUrlAsync, () => Url.Length > 0);
        EditCommand = new RelayCommand(BeginEdit, () => !IsEditing);
        CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);
        SaveCommand = new RelayCommand(SaveEdit, () => IsEditing);
        RotateCommand = new RelayCommand(() => IsConfirmingRotate = true, () => HasPassword && !IsConfirmingRotate && !IsEditing);
        ConfirmRotateCommand = new RelayCommand(ConfirmRotate, () => IsConfirmingRotate);
        CancelRotateCommand = new RelayCommand(() => IsConfirmingRotate = false, () => IsConfirmingRotate);
        BeginAddFieldCommand = new RelayCommand(() => LeavingFieldForm(BeginAddField), () => !IsAddingField);
        ConfirmAddFieldCommand = new RelayCommand(ConfirmAddField, () => IsAddingField && DraftFieldName.Trim().Length > 0);
        CancelAddFieldCommand = new RelayCommand(CancelAddField, () => IsAddingField);
        ConfirmReplaceFieldCommand = new RelayCommand(ConfirmReplaceField, () => ReplacingField is not null);
        CancelReplaceFieldCommand = new RelayCommand(
            () =>
            {
                ReplacingField = null;
                FormClosed?.Invoke();
            },
            () => ReplacingField is not null);
        GenerateIntoCommand = new RelayCommand<SecretField>(GenerateInto);
        OpenGeneratorCommand = new RelayCommand(OpenGenerator, () => IsEditing);
        CloseGeneratorCommand = new RelayCommand(CloseGenerator);
        GeneratePasswordCommand = new RelayCommand(GeneratePassword, () => HasPassword && !IsEditing);
        Generator.PropertyChanged += (_, _) =>
        {
            if (_generatedInto is { IsGenerated: true } field)
            {
                GenerateInto(field);
            }
        };
        ConfirmRemoveFieldCommand = new RelayCommand(ConfirmRemoveField, () => RemovingField is not null);
        CancelRemoveFieldCommand = new RelayCommand(() => RemovingField = null, () => RemovingField is not null);
        ConfirmRenameFieldCommand = new RelayCommand(ConfirmRenameField, () => RenamingField is not null && DraftFieldRename.Trim().Length > 0);
        CancelRenameFieldCommand = new RelayCommand(
            () =>
            {
                RenamingField = null;
                FormClosed?.Invoke();
            },
            () => RenamingField is not null);
        AddTagCommand = new RelayCommand(AddTag, () => DraftTag.Trim().Length > 0);
        ConfirmTagChangeCommand = new RelayCommand(ConfirmTagChange, () => PendingTagChange is not null);
        CancelTagChangeCommand = new RelayCommand(() => PendingTagChange = null, () => PendingTagChange is not null);

        _title = entry.Title;
        _groupPath = entry.GroupPath;
        _uuid = entry.Uuid;
        Username = entry.Username;
        Url = entry.Url;
        Notes = entry.Notes;
        PasswordLength = entry.Password.Length;
        Kind = EntryKinds.Of(entry);

        // A reference names a path, and one two items answer to resolves to neither.
        Reference = session.Unlocked?.NameIsShared(Name) == true ? null : KpReferences.ForEntry(Name);

        NewPassword = new SecretField(clipboard);
        NewFieldValue = new SecretField(clipboard);
        ReplacementFieldValue = new SecretField(clipboard);
        _restored = restored;
        History = new EntryHistoryViewModel(session, clipboard, this, Restored);
        Attachments = new EntryAttachmentsViewModel(session, Name, picker, report, History.Refresh);

        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EntryHistoryViewModel.IsOpen))
            {
                OnPropertyChanged(nameof(TakesWholeView));
            }
        };

        ReadTimes();
        ReadFieldsAndTags();
    }

    /// <summary>The countdown every copy on this pane goes through.</summary>
    internal ClipboardCountdown Clipboard => _clipboard;

    /// <summary>The entry's custom fields, by name, in ordinal order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFields), nameof(FieldsSummary))]
    internal partial IReadOnlyList<EntryFieldRow> Fields { get; private set; } = [];

    internal bool HasFields => Fields.Count > 0;

    /// <summary>The item's TOTP, when keypaste can compute one; null otherwise.</summary>
    internal EntryTotpRow? Totp
    {
        get => _totp;
        private set
        {
            var previous = _totp;

            if (SetProperty(ref _totp, value))
            {
                OnPropertyChanged(nameof(ShowsTotp));
                OnPropertyChanged(nameof(HasStandardRows));
                previous?.Dispose();
            }
        }
    }

    internal bool ShowsTotp => !IsEditing && _totp is not null;

    /// <summary>Why the item's TOTP cannot be computed, naming a field and never a value; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTotpProblem), nameof(HasStandardRows))]
    internal partial string? TotpProblem { get; private set; }

    internal bool ShowsTotpProblem => !IsEditing && TotpProblem is not null;

    /// <summary>The entry's own tags, never its group's.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTags), nameof(TagsSummary))]
    internal partial IReadOnlyList<EntryTagChip> Tags { get; private set; } = [];

    internal bool HasTags => Tags.Count > 0;

    /// <summary>A new field's value, while its form is open.</summary>
    internal SecretField NewFieldValue { get; }

    /// <summary>A replacement for one field's value, while its form is open.</summary>
    internal SecretField ReplacementFieldValue { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnPrimary))]
    [NotifyCanExecuteChangedFor(nameof(BeginAddFieldCommand), nameof(ConfirmAddFieldCommand), nameof(CancelAddFieldCommand))]
    internal partial bool IsAddingField { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmAddFieldCommand))]
    [NotifyPropertyChangedFor(nameof(NewFieldNameProblem), nameof(HasNewFieldNameProblem))]
    internal partial string DraftFieldName { get; set; } = string.Empty;

    /// <summary>Why the field name typed cannot be used, or null; none while it is empty, which Add waits on.</summary>
    internal string? NewFieldNameProblem => FieldNameProblem(DraftFieldName);

    internal bool HasNewFieldNameProblem => NewFieldNameProblem is not null;

    /// <summary>Why the new name typed for a field cannot be used, or null; none while it is empty, which Rename waits on.</summary>
    internal string? RenamedFieldProblem => FieldNameProblem(DraftFieldRename);

    internal bool HasRenamedFieldProblem => RenamedFieldProblem is not null;

    private static string? FieldNameProblem(string typed) =>
        typed.Trim() is { Length: > 0 } name && !FieldNameRules.IsWritable(name, out var error)
            ? string.Concat(char.ToUpperInvariant(error[0]).ToString(), error.AsSpan(1), ".")
            : null;

    /// <summary>Whether the new field is protected; on unless the person turns it off.</summary>
    [ObservableProperty]
    internal partial bool NewFieldProtected { get; set; } = true;

    /// <summary>What leaving would lose: the first form here holding what was typed, or a tag being typed; null when none does.</summary>
    internal string? UnsavedDraft =>
        EditDraft() ?? AddFieldDraft() ?? ReplaceFieldDraft() ?? RenameFieldDraft()
        ?? (DraftTag.Trim().Length > 0 ? "this tag" : null);

    internal Func<string?> EditDraft => () =>
        IsEditing && (DraftUsername != Username || DraftUrl != Url || DraftNotes != Notes || NewPassword.HasValue)
            ? "your changes to this item"
            : null;

    internal Func<string?> AddFieldDraft => () =>
        IsAddingField && (DraftFieldName.Trim().Length > 0 || (NewFieldValue.HasValue && !NewFieldValue.IsGenerated))
            ? "this new field"
            : null;

    internal Func<string?> ReplaceFieldDraft => () =>
        IsReplacingField && ReplacementFieldValue.HasValue && !ReplacementFieldValue.IsGenerated ? "this new value" : null;

    internal Func<string?> RenameFieldDraft => () =>
        IsRenamingField && DraftFieldRename.Trim() != _renamingField?.Name ? "this new name" : null;

    /// <summary>The field whose value the replace form is for, or null.</summary>
    internal EntryFieldRow? ReplacingField
    {
        get => _replacingField;
        private set
        {
            if (SetProperty(ref _replacingField, value))
            {
                OnPropertyChanged(nameof(IsReplacingField));
                OnPropertyChanged(nameof(ReplaceFieldPrompt));
                OnPropertyChanged(nameof(ReplacingFieldName));
                OnPropertyChanged(nameof(HasOwnPrimary));
                ConfirmReplaceFieldCommand.NotifyCanExecuteChanged();
                CancelReplaceFieldCommand.NotifyCanExecuteChanged();
                ReplacementFieldValue.Clear();
            }
        }
    }

    internal bool IsReplacingField => _replacingField is not null;

    /// <summary>Whether a form in the pane shows its own primary button: an edit, a rotate, or adding, replacing or renaming a field.</summary>
    internal bool HasOwnPrimary => IsEditing || IsConfirmingRotate || IsAddingField || IsReplacingField || IsRenamingField;

    /// <summary>Whether the item takes the whole of Items rather than sharing it with the list: while two revisions are compared.</summary>
    internal bool TakesWholeView => History.IsOpen;

    internal string ReplaceFieldPrompt => $"New value for {_replacingField?.DisplayName}";

    internal string? ReplacingFieldName => _replacingField?.DisplayName;

    /// <summary>The field the remove confirmation is for, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemovingField), nameof(RemoveFieldPrompt))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveFieldCommand), nameof(CancelRemoveFieldCommand))]
    internal partial EntryFieldRow? RemovingField { get; private set; }

    internal bool IsRemovingField => RemovingField is not null;

    internal string RemoveFieldPrompt => $"Remove {RemovingField?.DisplayName} from this item? Its value stays in the item's history.";

    /// <summary>The field the rename form is for, or null.</summary>
    internal EntryFieldRow? RenamingField
    {
        get => _renamingField;
        private set
        {
            if (SetProperty(ref _renamingField, value))
            {
                OnPropertyChanged(nameof(IsRenamingField));
                OnPropertyChanged(nameof(RenameFieldPrompt));
                OnPropertyChanged(nameof(RenamingFieldName));
                OnPropertyChanged(nameof(HasOwnPrimary));
                ConfirmRenameFieldCommand.NotifyCanExecuteChanged();
                CancelRenameFieldCommand.NotifyCanExecuteChanged();
                DraftFieldRename = value?.Name ?? string.Empty;
                FieldRenameLines = [];
            }
        }
    }

    internal bool IsRenamingField => _renamingField is not null;

    internal string RenameFieldPrompt => $"New name for {_renamingField?.DisplayName}";

    internal string? RenamingFieldName => _renamingField?.DisplayName;

    /// <summary>The name being typed for the field; a change of it asks again about what the rename breaks.</summary>
    internal string DraftFieldRename
    {
        get => _draftFieldRename;
        set
        {
            if (SetProperty(ref _draftFieldRename, value))
            {
                ConfirmRenameFieldCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RenamedFieldProblem));
                OnPropertyChanged(nameof(HasRenamedFieldProblem));
                FieldRenameLines = [];
            }
        }
    }

    /// <summary>
    /// What renaming the field breaks, shown before it is written as a project tag change is: the
    /// project variable it is, the <c>kp://</c> references that name it and the item's fields whose
    /// <c>{S:…}</c> names it. Names only, never a value; empty until the rename needs asking about.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingFieldRename))]
    internal partial IReadOnlyList<string> FieldRenameLines { get; private set; } = [];

    internal bool IsConfirmingFieldRename => FieldRenameLines.Count > 0;

    /// <summary>Renames the field, or first says what the rename breaks.</summary>
    internal RelayCommand ConfirmRenameFieldCommand { get; }

    internal RelayCommand CancelRenameFieldCommand { get; }

    /// <summary>The custom fields by name and protection, for the history's comparison; never a value.</summary>
    internal string FieldsSummary => EntryRevisionRow.Describe(Fields.Select(row => (row.Name, row.IsProtected)));

    /// <summary>The item's own tags, for the history's comparison.</summary>
    internal string TagsSummary => EntryRevisionRow.Describe(Tags.Select(chip => chip.Tag));

    /// <summary>The project environments the item's own tags put it in, for the Environments card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEnvironments))]
    internal partial IReadOnlyList<EnvironmentRow> Environments { get; private set; } = [];

    /// <summary>Whether the Environments card shows: the item's own tags put it in a project.</summary>
    internal bool ShowsEnvironments => !IsEditing && Environments.Count > 0;

    /// <summary>A tag being typed, added on Add tag.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTagCommand))]
    internal partial string DraftTag { get; set; } = string.Empty;

    partial void OnDraftTagChanged(string value)
    {
        if (value.Trim().Length == 0)
        {
            FormClosed?.Invoke();
        }
    }

    /// <summary>The vault's project tags this item does not carry, which the tag box offers as a tag is typed; choosing one only fills <see cref="DraftTag"/>.</summary>
    [ObservableProperty]
    internal partial IReadOnlyList<string> TagSuggestions { get; private set; } = [];

    internal RelayCommand BeginAddFieldCommand { get; }

    internal RelayCommand ConfirmAddFieldCommand { get; }

    internal RelayCommand CancelAddFieldCommand { get; }

    internal RelayCommand ConfirmReplaceFieldCommand { get; }

    internal RelayCommand CancelReplaceFieldCommand { get; }

    internal RelayCommand ConfirmRemoveFieldCommand { get; }

    internal RelayCommand CancelRemoveFieldCommand { get; }

    internal RelayCommand AddTagCommand { get; }

    /// <summary>A project tag being added or removed, held until the person confirms what it reaches; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingTagChange), nameof(TagChangeLines), nameof(TagChangeAction))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmTagChangeCommand), nameof(CancelTagChangeCommand))]
    internal partial ProjectTagChange? PendingTagChange { get; private set; }

    internal bool IsConfirmingTagChange => PendingTagChange is not null;

    /// <summary>What the pending tag change reaches: its environment and the fields joining or leaving it, never a value.</summary>
    internal IReadOnlyList<string> TagChangeLines => PendingTagChange?.Describe() ?? [];

    /// <summary>What the confirming button says.</summary>
    internal string TagChangeAction => PendingTagChange?.Adding == false ? "Remove tag" : "Add tag";

    /// <summary>Writes the pending tag change.</summary>
    internal RelayCommand ConfirmTagChangeCommand { get; }

    /// <summary>Drops the pending tag change, writing nothing.</summary>
    internal RelayCommand CancelTagChangeCommand { get; }

    /// <summary>Reads one custom field out of the open vault as it holds it, for a reveal.</summary>
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

    /// <summary>The item's current TOTP code, computed from the open vault for a reveal or a copy; the seed is zeroed before this returns.</summary>
    /// <returns>The code, or null once what went wrong was reported.</returns>
    internal string? ReadTotpCode()
    {
        try
        {
            string? refusal = null;
            using var spec = _session.Unlocked?.ReadTotp(Name, out refusal);

            if (spec is null)
            {
                Report(refusal is null
                    ? "This item's TOTP could not be read. The vault may have locked: unlock it, then try again."
                    : TotpRefusal(refusal));
                return null;
            }

            return spec.Code(_session.Clock).Value;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return null;
        }
    }

    /// <summary>Reads one custom field out of the open vault as it leaves keypaste, for a copy: its placeholders filled in, or why it cannot leave reported.</summary>
    /// <returns>The value, or null once what went wrong was reported.</returns>
    internal string? ReadFilledField(EntryFieldRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (ReadField(row.Name) is not { Length: > 0 } value)
        {
            Report($"{row.DisplayName} is empty, or could not be read. The vault may have locked: unlock it, then try again.");
            return null;
        }

        return Filled(vault => vault.FillPlaceholders(Name, value), row.DisplayName, "copied");
    }

    /// <summary>A value of this item as it leaves keypaste, its placeholders filled in from the item's own fields.</summary>
    /// <param name="fill">Fills the value in from the open vault.</param>
    /// <param name="what">What the value is, as a sentence about it starts, such as <c>The password</c>.</param>
    /// <param name="refused">What does not happen to it when it cannot leave, such as <c>copied</c>.</param>
    /// <returns>The filled value, never empty, or null once why it cannot leave was reported.</returns>
    internal string? Filled(Func<Vault, PlaceholderFill?> fill, string what, string refused)
    {
        ArgumentNullException.ThrowIfNull(fill);

        PlaceholderFill? filled;
        try
        {
            filled = _session.Unlocked is { } vault ? fill(vault) : null;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return null;
        }

        if (filled is null)
        {
            Report("That item could not be read. The vault may have locked: unlock it, then try again.");
            return null;
        }

        return Leaving(filled, what, refused);
    }

    /// <summary>A value of this item filled in as it leaves keypaste, or null once why it cannot leave was reported.</summary>
    /// <param name="filled">The value with its placeholders filled in, or why it cannot leave.</param>
    /// <param name="what">What the value is, as a sentence about it starts, such as <c>The password</c>.</param>
    /// <param name="refused">What does not happen to it when it cannot leave, such as <c>copied</c>.</param>
    /// <returns>The filled value, never empty, or null.</returns>
    internal string? Leaving(PlaceholderFill filled, string what, string refused)
    {
        ArgumentNullException.ThrowIfNull(filled);

        if (filled.Value is { Length: > 0 } value)
        {
            return value;
        }

        Report(filled.Refusal is { } refusal
            ? DisplayTextSanitizer.Sanitize($"{what} {refusal}, so it was not {refused}.").Text + " Edit the item so keypaste can fill it in."
            : $"{what} fills in as empty, so it was not {refused}.");
        return null;
    }

    /// <summary>What a value generated in the pane's forms is made of (V.12).</summary>
    internal GeneratorViewModel Generator { get; } = new();

    /// <summary>Generate, beside a form's secret field: a generated value in place of what the field holds.</summary>
    internal RelayCommand<SecretField> GenerateIntoCommand { get; }

    /// <summary>Generate, on the saved password: Edit fields with a generated password, which Save writes and history keeps the old one of.</summary>
    internal RelayCommand GeneratePasswordCommand { get; }

    /// <summary>Adds a generated value as a new protected field, as Generate password's Add as a field asks, through Add a field's own write.</summary>
    internal void AddGeneratedField(string name, SecretField value)
    {
        ArgumentNullException.ThrowIfNull(value);

        BeginAddField();
        DraftFieldName = name;
        NewFieldValue.TakeFrom(value);
        NewFieldProtected = true;
        ConfirmAddFieldCommand.Press();
    }

    /// <summary>Generate, on a protected field: its replace form with a generated value, which Save writes and history keeps the old one of.</summary>
    internal void GenerateField(EntryFieldRow row) =>
        LeavingFieldForm(() =>
        {
            OpenReplaceField(row);
            GenerateInto(ReplacementFieldValue);
        });

    private void GeneratePassword()
    {
        BeginEdit();
        OpenGenerator();
    }

    /// <summary>Generate, beside the new password: draws a value into the field and opens the generator anchored to it.</summary>
    internal RelayCommand OpenGeneratorCommand { get; }

    internal RelayCommand CloseGeneratorCommand { get; }

    /// <summary>Starts a new item with a generated value, set by the screen that holds this pane; the second argument closes the panel.</summary>
    internal Action<SecretField, Action>? StartNewItemWith { get; set; }

    /// <summary>The generator anchored to the new password field, or null while it is closed.</summary>
    internal GeneratePasswordViewModel? GeneratorPanel
    {
        get => _generatorPanel;
        private set
        {
            if (SetProperty(ref _generatorPanel, value))
            {
                OnPropertyChanged(nameof(HasGeneratorPanel));
            }
        }
    }

    internal bool HasGeneratorPanel
    {
        get => _generatorPanel is not null;
        set
        {
            if (!value)
            {
                CloseGenerator();
            }
        }
    }

    private void OpenGenerator()
    {
        GenerateInto(NewPassword);

        if (GeneratorPanel is not null)
        {
            return;
        }

        GeneratorPanel = new GeneratePasswordViewModel(
            _clipboard,
            Generator,
            NewPassword,
            () => GenerateInto(NewPassword),
            value => StartNewItemWith?.Invoke(value, CloseGenerator),
            (value, name) => AddGeneratedField(name, value),
            Name.Title,
            CloseGenerator);
    }

    private void CloseGenerator()
    {
        var panel = GeneratorPanel;
        GeneratorPanel = null;
        panel?.Dispose();
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (!value)
        {
            CloseGenerator();
        }
    }

    private void GenerateInto(SecretField? field)
    {
        if (field is null)
        {
            return;
        }

        if (Generator.Recipe is { } recipe)
        {
            field.Generate(recipe);
            _generatedInto = field;
        }
        else
        {
            Report(Generator.Error);
        }
    }

    /// <summary>Opens the form that writes a new value over one field's.</summary>
    internal void BeginReplaceField(EntryFieldRow row)
    {
        if (ReplacingField != row)
        {
            LeavingFieldForm(() => OpenReplaceField(row));
        }
    }

    private void OpenReplaceField(EntryFieldRow row)
    {
        IsAddingField = false;
        RemovingField = null;
        RenamingField = null;
        ReplacingField = row;
        Report(null);
    }

    /// <summary>Asks whether to remove one field.</summary>
    internal void BeginRemoveField(EntryFieldRow row) =>
        LeavingFieldForm(() =>
        {
            IsAddingField = false;
            ReplacingField = null;
            RenamingField = null;
            RemovingField = row;
            Report(null);
        });

    /// <summary>Opens the form that gives one field another name.</summary>
    internal void BeginRenameField(EntryFieldRow row)
    {
        if (RenamingField == row)
        {
            return;
        }

        LeavingFieldForm(() =>
        {
            IsAddingField = false;
            ReplacingField = null;
            RemovingField = null;
            RenamingField = row;
            Report(null);
        });
    }

    /// <summary>Opens another of the pane's field forms, first asking when the one open holds what was typed.</summary>
    private void LeavingFieldForm(Action open)
    {
        if ((AddFieldDraft() ?? ReplaceFieldDraft() ?? RenameFieldDraft()) is { } draft && AskDiscard is { } ask)
        {
            ask(draft, open, null);
        }
        else
        {
            open();
        }
    }

    /// <summary>Protects a plain field or stops protecting a protected one, keeping its value; the rows are rebuilt, so the view hands the keyboard to its new row.</summary>
    internal void ToggleProtection(EntryFieldRow row)
    {
        if (Write(vault => vault.SetFields(Name, [new FieldWrite(row.Name, Value: null, Protect: !row.IsProtected)]), null))
        {
            FieldRebuilt?.Invoke(row.Name);
        }
    }

    /// <summary>Raised with a field's name when its row was replaced under the keyboard.</summary>
    internal event Action<string>? FieldRebuilt;

    /// <summary>How leaving a form holding what was typed is asked about, or null to leave without asking.</summary>
    internal Action<string, Action, Action?>? AskDiscard { get; init; }

    /// <summary>Called last when a form here closes, which may replace this pane.</summary>
    internal Action? FormClosed { get; set; }

    /// <summary>Removes one of the entry's tags, first asking about a project tag.</summary>
    internal void RemoveTag(EntryTagChip chip)
    {
        if (!Ask(chip.Tag, adding: false))
        {
            Write(vault => vault.RemoveTags(Name, [chip.Tag]), "That tag is no longer on this item.");
        }
    }

    /// <summary>Replaces the password with a generated one, after asking.</summary>
    internal RelayCommand RotateCommand { get; }

    internal RelayCommand ConfirmRotateCommand { get; }

    internal RelayCommand CancelRotateCommand { get; }

    /// <summary>Whether the pane is asking whether to rotate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnPrimary))]
    [NotifyCanExecuteChangedFor(nameof(RotateCommand), nameof(ConfirmRotateCommand), nameof(CancelRotateCommand))]
    internal partial bool IsConfirmingRotate { get; private set; }

    /// <summary>What the confirm row asks; an instance property because a binding needs one.</summary>
#pragma warning disable CA1822
    internal string RotatePrompt =>
        $"Replace the saved password with a new {PasswordGenerator.DefaultLength}-character one? keypaste only changes its copy: set the new password on the site or service too, or you will need the old one from history to sign in.";
#pragma warning restore CA1822

    /// <summary>When the entry was created, as the metadata row shows it.</summary>
    [ObservableProperty]
    internal partial string Created { get; private set; } = string.Empty;

    /// <summary>When its current password was set, as the metadata row shows it.</summary>
    [ObservableProperty]
    internal partial string Rotated { get; private set; } = string.Empty;

    /// <summary>When the item expires or expired, its local date and time as the metadata row shows it; empty when it never expires.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpiry))]
    internal partial string Expiry { get; private set; } = string.Empty;

    /// <summary>Whether the item has an expiry, so its metadata row shows.</summary>
    internal bool HasExpiry => Expiry.Length > 0;

    /// <summary>Whether its expiry has passed by the session's clock, as a run judges it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpiryLabel))]
    internal partial bool IsExpired { get; private set; }

    /// <summary>The expiry row's label.</summary>
    internal string ExpiryLabel => IsExpired ? "Expired" : "Expires";

    /// <summary>
    /// Whether the Agent access card shows: an agent attached here whose settings reach this entry
    /// can name it, or it has left the vault or is being asked for (N.5). False until the first reading,
    /// and always when nothing answers agents.
    /// </summary>
    [ObservableProperty]
    internal partial bool ShowsAgentAccess { get; private set; }

    /// <summary>What agents did with this entry, or null before the first reading.</summary>
    [ObservableProperty]
    internal partial EntryAgentAccess? AgentAccess { get; private set; }

    /// <summary>The Agent access card's one line, when <see cref="AgentLines"/> does not list the same agents.</summary>
    [ObservableProperty]
    internal partial string AgentAccessSummary { get; private set; } = "None active";

    internal bool ShowsAgentAccessSummary => AgentLines.Count == 0;

    /// <summary>When an agent last received it: "in use", "4m ago" or "never".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBeenUsed))]
    internal partial string LastUsedText { get; private set; } = "never";

    /// <summary>Whether an agent ever received it, which is when the card says when.</summary>
    internal bool HasBeenUsed => LastUsedText != "never";

    /// <summary>Takes the latest picture of what agents did, never a value.</summary>
    /// <param name="activity">The picture.</param>
    internal void Apply(EntryActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (_entryPath.Length == 0)
        {
            return;
        }

        _activity = activity;

        // Of an item another shares its name with, only what names this item's UUID is its own.
        var now = _session.Clock.GetUtcNow();
        var access = activity.Access(Name, byItem: SharesItsName);
        IReadOnlyList<string> tags = [.. Tags.Select(chip => chip.Tag)];
        ShowsAgentAccess = activity.AgentsCanSee(Name, tags, byItem: SharesItsName);
        AgentAccess = access;
        AgentAccessSummary = UseText.Summary(access, activity.Seeing(Name, tags));
        LastUsedText = UseText.LastUsed(access.Use, now);
        AgentLines = UseText.Lines(access, now);
    }

    /// <summary>The Agent access card's detail: each client that received this entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAgentAccessSummary))]
    internal partial IReadOnlyList<string> AgentLines { get; private set; } = [];

    /// <summary>What sort of entry this is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    internal partial EntryKind Kind { get; private set; }

    internal string KindLabel => EntryKinds.Label(Kind);

    /// <summary>Each group down to this entry.</summary>
    internal string Location => string.Join(" › ", _groupPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(group => EntryNameSanitizer.Sanitize(group).Text));

    /// <summary>Back to the preview from an edit or a comparison.</summary>
    internal void LeaveWholeView()
    {
        if (IsEditing)
        {
            CancelEdit();
        }

        if (History.IsOpen)
        {
            History.ToggleCommand.Press();
        }
    }

    /// <summary>Where the value lives in the KDBX file: the entry's UUID, abbreviated, and its field.</summary>
    /// <remarks>The list's row says where it is; the UUID is what KeePassXC shows nowhere else.</remarks>
    internal string KdbxEntry => _uuid is { Length: > 8 } uuid
        ? $"uuid {uuid[..4].ToLowerInvariant()}…{uuid[^4..].ToLowerInvariant()} · field Password"
        : $"{DisplayPath} · field Password";

    /// <summary>Whether the URL is a link that opens in the browser: http or https, or a bare address as https.</summary>
    internal bool OpensUrl => WebAddress.TryOpenable(Url, out _);

    /// <summary>Whether the URL is shown as text, with a line saying why it opens nothing.</summary>
    internal bool ShowsUrlNote => Url.Length > 0 && !OpensUrl;

    /// <summary>Copies the URL, which is not a secret, with the countdown only once it filled in another field.</summary>
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
    /// The item this pane is looking at, reached by its UUID.
    /// </summary>
    /// <remarks>
    /// The pane used to reach for <see cref="Path"/>, and two entries can answer to one of those —
    /// so a copy served a secret the person had not selected and an edit wrote to its neighbour
    /// (docs/STEPS.md F.1e). Every vault access below goes through this instead.
    /// </remarks>
    internal EntryName Name => new(_groupPath, _title) { Uuid = _uuid };

    private EntryActivity? _activity;

    /// <summary>Whether another item has this name now, so the card reads only what is recorded of this item by its UUID.</summary>
    [ObservableProperty]
    internal partial bool SharesItsName { get; private set; }

    /// <summary>Whether another item has this name and neither its username nor its URL tells them apart, which only a rename can then do.</summary>
    [ObservableProperty]
    internal partial bool NothingTellsItApart { get; private set; }

    /// <summary>Takes what the list knows of this item's name, as another item arriving or leaving changes it.</summary>
    /// <param name="shared">Whether another item has the name.</param>
    /// <param name="nothingTells">Whether nothing the list draws tells them apart.</param>
    internal void ShowSharedName(bool shared, bool nothingTells)
    {
        var changed = SharesItsName != shared;
        SharesItsName = shared;

        if (changed && _activity is { } activity)
        {
            Apply(activity);
        }

        NothingTellsItApart = nothingTells;
        Reference = shared ? null : KpReferences.ForEntry(Name);
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(HasReference));
        CopyReferenceCommand.NotifyCanExecuteChanged();
    }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayUsername), nameof(HasUsername), nameof(HasStandardRows))]
    [NotifyCanExecuteChangedFor(nameof(CopyUsernameCommand))]
    internal partial string Username { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpensUrl), nameof(ShowsUrlNote), nameof(DisplayUrl), nameof(HasUrl), nameof(HasStandardRows))]
    [NotifyCanExecuteChangedFor(nameof(CopyUrlCommand), nameof(OpenUrlCommand))]
    internal partial string Url { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNotes), nameof(DisplayNotes), nameof(HasStandardRows))]
    internal partial string Notes { get; private set; }

    /// <summary>
    /// How long the password is, which is all this object knows about it.
    /// </summary>
    /// <remarks>
    /// A length rather than a value, for the mask — the same trade
    /// <see cref="Controls.MaskedInput.MaskedLength"/> makes. It is a disclosure, and a small one:
    /// it is visible to anyone who can already see that an entry exists.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskedLength), nameof(PasswordMask), nameof(HasPassword), nameof(HasStandardRows))]
    [NotifyCanExecuteChangedFor(nameof(CopyPasswordCommand), nameof(RotateCommand), nameof(GeneratePasswordCommand))]
    internal partial int PasswordLength { get; private set; }

    internal bool HasUsername => Username.Length > 0;

    internal bool HasUrl => Url.Length > 0;

    internal bool HasPassword => PasswordLength > 0;

    /// <summary>Whether the pane draws any of the rows every item may have, so an item with none leaves no gap for them.</summary>
    internal bool HasStandardRows => HasUsername || HasPassword || HasUrl || ShowsNotes || _totp is not null || TotpProblem is not null;

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

    /// <summary>The item's attachments by name and size, and the way to add, replace, remove and save them (V.8b).</summary>
    internal EntryAttachmentsViewModel Attachments { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTotp), nameof(ShowsTotpProblem), nameof(HasOwnPrimary), nameof(TakesWholeView), nameof(ShowsEnvironments))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(CancelCommand), nameof(SaveCommand), nameof(RotateCommand), nameof(GeneratePasswordCommand), nameof(OpenGeneratorCommand))]
    internal partial bool IsEditing { get; private set; }

    [ObservableProperty]
    internal partial string DraftUsername { get; set; } = string.Empty;

    [ObservableProperty]
    internal partial string DraftUrl { get; set; } = string.Empty;

    [ObservableProperty]
    internal partial string DraftNotes { get; set; } = string.Empty;

    /// <summary>Copies the password, with the auto-clearing countdown.</summary>
    internal AsyncRelayCommand CopyPasswordCommand { get; }

    /// <summary>Copies the username, which is not a secret, with the countdown only once it filled in another field.</summary>
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
        Changed?.Invoke(this, EventArgs.Empty);
        Kind = EntryKinds.Of(entry);
        ReadFieldsAndTags();
        Attachments.Read();
    }

    /// <summary>Reads again what a change this pane did not make can reach: its values, its times and an open history.</summary>
    internal void ReadAgain()
    {
        Reload();
        ReadTimes();
        History.Refresh();
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

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
        CloseGenerator();
        FormClosed = null;
        _editOpenedWith = (string.Empty, string.Empty, string.Empty);
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
        Totp = null;
        TotpProblem = null;
        Tags = [];
        TagSuggestions = [];
        ReplacingField = null;
        RemovingField = null;
        RenamingField = null;
        Environments = [];
        DraftFieldName = string.Empty;
        DraftTag = string.Empty;
        NewPassword.Dispose();
        NewFieldValue.Dispose();
        ReplacementFieldValue.Dispose();
        History.Dispose();
        Attachments.Dispose();

        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(KdbxEntry));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(GroupPath));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(HasReference));
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
            Report(e.Message);
            return;
        }

        if (found is not { Length: > 0 } stored)
        {
            Report("That item could not be read. The vault may have locked: unlock it, then try again.");
            return;
        }

        if (Filled(vault => vault.FillPlaceholders(Name, stored), "The password", "copied") is { } password)
        {
            await _clipboard.CopyAsync(password, "Password").ConfigureAwait(true);
        }
    }

    private async Task CopyUsernameAsync() =>
        await CopyFilledAsync(Username, "The username", "Username").ConfigureAwait(true);

    /// <summary>Opens the URL in the default browser, its placeholders filled in.</summary>
    [RelayCommand(CanExecute = nameof(OpensUrl))]
    private async Task OpenUrlAsync()
    {
        if (Filled(vault => vault.FillPlaceholders(Name, Url), "The URL", "opened") is not { } url)
        {
            return;
        }

        if (!WebAddress.TryOpenable(url, out var address))
        {
            Report("Filled in, the URL is not one keypaste opens, so it was not opened. Copy it to use it.");
            return;
        }

#pragma warning disable RS0030 // The item's web address, which the person asked to open.
        if (_web is null || !await _web.OpenAsync(address).ConfigureAwait(true))
#pragma warning restore RS0030
        {
            Report($"Your system didn't open {DisplayUrl}. Copy it and paste it into your browser.");
        }
    }

    private async Task CopyUrlAsync() =>
        await CopyFilledAsync(Url, "The URL", "URL").ConfigureAwait(true);

    /// <summary>Copies a field that is not a secret as it leaves keypaste; once it filled in another field it may carry one, so it gets the countdown.</summary>
    private async Task CopyFilledAsync(string stored, string what, string label)
    {
        if (Filled(vault => vault.FillPlaceholders(Name, stored), what, "copied") is not { } value)
        {
            return;
        }

        await (string.Equals(value, stored, StringComparison.Ordinal)
            ? _clipboard.CopyPlainAsync(value, label)
            : _clipboard.CopyAsync(value, label)).ConfigureAwait(true);
    }

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
            var times = vault.ReadTimes(Name);
            Created = UseText.Dated(times?.Created, now);
            Expiry = UseText.LocalMinute(times?.Expires);
            IsExpired = times?.Expires <= now;
            Rotated = UseText.Dated(EntryRotation.LastRotated(vault, Name), now);
            _uuid = vault.EntryUuid(Name);
            OnPropertyChanged(nameof(KdbxEntry));
        }
        catch (VaultException e)
        {
            Report(e.Message);
        }
    }

    private void ConfirmRotate()
    {
        IsConfirmingRotate = false;

        var write = _session.Write(vault => EntryRotation.Rotate(vault, Name, SecretRecipe.Default), outcome => outcome == RotateOutcome.Rotated);
        var problem = write.Outcome == WriteOutcome.NothingToSave
            ? $"'{_entryPath}' could not be rotated here. Generate a new password in its edit form instead."
            : write.Problem("make your change again");

        if (problem is not null)
        {
            Report(problem);
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
            TagSuggestions = [.. ProjectCatalog.MemberTags(vault).Except(Tags.Select(chip => chip.Tag), StringComparer.Ordinal)];
            Environments = [.. (_session.ReadEnvironments(vault, Name) ?? []).Select(environment => new EnvironmentRow(environment))];
            ReadTotp(vault);
        }
        catch (VaultException e)
        {
            Report(e.Message);
        }
    }

    /// <summary>Takes the TOTP's shape, never its seed: the spec is zeroed here, and each code is computed again at its press.</summary>
    private void ReadTotp(Vault vault)
    {
        using var spec = vault.ReadTotp(Name, out var refusal);

        Totp = spec is null ? null : new EntryTotpRow(this, _session.Clock, _post, spec.Digits, spec.Period);
        TotpProblem = refusal is null ? null : TotpRefusal(refusal);
    }

    /// <summary>What the pane says of a TOTP keypaste refuses, from the refusal, which names a field and never a value.</summary>
    private static string TotpRefusal(string refusal) =>
        DisplayTextSanitizer.Sanitize($"keypaste can't show this item's TOTP code. {refusal} Correct that field to see the code here.").Text;

    /// <summary>Makes one change to this entry through core and saves it, as one revision.</summary>
    /// <param name="change">The change; false when it found nothing to change.</param>
    /// <param name="unchanged">What to say when it found nothing, or null to say the entry is gone.</param>
    /// <returns>Whether the change was saved.</returns>
    private bool Write(Func<Vault, bool> change, string? unchanged)
    {
        var write = _session.Write(change, changed => changed);
        var problem = write.Outcome == WriteOutcome.NothingToSave
            ? unchanged ?? $"'{_entryPath}' is no longer in this vault. Choose another item."
            : write.Problem("make your change again");

        if (problem is not null)
        {
            Report(problem);
            return false;
        }

        ReadFieldsAndTags();

        // The change is a revision now, and a list read before it would name the wrong one at every index.
        History.Refresh();
        Report(null);
        return true;
    }

    private void BeginAddField()
    {
        ReplacingField = null;
        RemovingField = null;
        RenamingField = null;
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
        FormClosed?.Invoke();
    }

    private void ConfirmAddField()
    {
        var name = DraftFieldName.Trim();

        if (HasNewFieldNameProblem)
        {
            return;
        }

        if (Write(vault => vault.AddField(Name, new FieldWrite(name, NewFieldValue.Compose(), NewFieldProtected)), null))
        {
            IsAddingField = false;
            NewFieldValue.Clear();
            DraftFieldName = string.Empty;
            FormClosed?.Invoke();
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
            FormClosed?.Invoke();
        }
    }

    /// <summary>
    /// Renames the field in one core write, first saying what the rename breaks when the field is a project
    /// variable or the item's own <c>{S:…}</c> names it.
    /// </summary>
    private void ConfirmRenameField()
    {
        if (RenamingField is not { } row)
        {
            return;
        }

        var name = DraftFieldRename.Trim();

        if (HasRenamedFieldProblem)
        {
            return;
        }

        if (!IsConfirmingFieldRename && !string.Equals(name, row.Name, StringComparison.Ordinal) && Breaks(row, name) is { Count: > 0 } lines)
        {
            FieldRenameLines = lines;
            Report(null);
            return;
        }

        if (Write(vault => vault.RenameField(Name, row.Name, name), $"{row.DisplayName} is no longer on this item."))
        {
            RenamingField = null;
            FormClosed?.Invoke();
        }
    }

    /// <summary>What renaming a field breaks, in sentences, or nothing when it needs no question.</summary>
    private List<string> Breaks(EntryFieldRow row, string newName)
    {
        FieldRenameChange? change;
        try
        {
            change = _session.Unlocked is { } vault ? FieldRenameChange.Preview(vault, Name, row.Name) : null;
        }
        catch (VaultException e)
        {
            Report(e.Message);
            return [];
        }

        if (change is null || (!EnvConvention.IsEnvNamedField(row.Name) && change.Placeholders.Count == 0))
        {
            return [];
        }

        var old = row.DisplayName;
        var renamed = EntryNameSanitizer.Sanitize(newName).Text;
        var environments = Tags
            .Select(chip => ProjectTag.Read(chip.Tag))
            .Where(tag => tag.Kind == ProjectTagKind.Member)
            .Select(tag => EntryNameSanitizer.SanitizePath($"{tag.Project}/{tag.Environment}").Text)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        List<string> lines = [];

        if (EnvConvention.IsEnvNamedField(row.Name) && environments.Count > 0)
        {
            var where = string.Join(", ", environments);
            lines.Add(EnvConvention.IsEnvNamedField(newName)
                ? $"{old} is a variable of {where}. Renamed, those environments give {renamed} instead, and nothing there gives {old}."
                : $"{old} is a variable of {where}. {renamed} is not a variable name, so those environments stop giving {old}.");
        }

        if (change.References.Count > 0)
        {
            lines.Add($"These references name {old} and will find nothing: {string.Join(", ", change.References)}.");
        }

        if (change.Placeholders.Count > 0)
        {
            var fields = string.Join(", ", change.Placeholders.Select(field => EntryNameSanitizer.Sanitize(field).Text));
            lines.Add($"{{S:{old}}} in {fields} will fill in nothing.");
        }

        return [.. lines.Select(line => DisplayTextSanitizer.Sanitize(line).Text)];
    }

    private void ConfirmRemoveField()
    {
        if (RemovingField is not { } row)
        {
            return;
        }

        if (Write(vault => vault.RemoveField(Name, row.Name), $"{row.DisplayName} is no longer on this item."))
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
            if (Write(vault => vault.AddTag(Name, tag), "This item already has that tag."))
            {
                DraftTag = string.Empty;
            }
        }
        else
        {
            Write(vault => vault.RemoveTags(Name, [tag]), "That tag is no longer on this item.");
        }
    }

    private void BeginEdit()
    {
        _editOpenedWith = (Username, Url, Notes);
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
        FormClosed?.Invoke();
    }

    /// <summary>What Save writes to a field: the person's value only where they changed it, so a merge since the form opened keeps the rest.</summary>
    private static string Edited(string draft, string openedWith, string current) =>
        string.Equals(draft, openedWith, StringComparison.Ordinal) ? current : draft;

    private void SaveEdit()
    {
        var write = _session.Write(
            vault =>
            {
                if (vault.Find(Name) is not { } existing)
                {
                    return false;
                }

                // An untouched password is carried across rather than read and written back, which
                // would put it in a local for no reason. A replacement is applied in the same
                // UpdateEntry as the field edits, so the whole change costs one history item rather
                // than two.
                var updated = existing with
                {
                    Username = Edited(DraftUsername, _editOpenedWith.Username, existing.Username),
                    Url = Edited(DraftUrl, _editOpenedWith.Url, existing.Url),
                    Notes = Edited(DraftNotes, _editOpenedWith.Notes, existing.Notes),
                };

                if (NewPassword.HasValue)
                {
                    updated = updated with { Password = NewPassword.Compose() };
                }

                vault.UpdateEntry(Name, updated);
                return true;
            },
            updated => updated);
        var problem = write.Outcome == WriteOutcome.NothingToSave
            ? $"'{_entryPath}' is no longer in this vault. Choose another item."
            : write.Problem("make your change again");

        if (problem is not null)
        {
            Report(problem);
            return;
        }

        IsEditing = false;
        NewPassword.Clear();
        Reload();

        // The edit it just made is a revision now, and a list read before it would name the wrong
        // one at every index.
        History.Refresh();
        Report(null);

        // The list shows the username and the URL too, so it reads them again.
        _restored(Name);
    }
}
