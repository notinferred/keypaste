using System.Collections.ObjectModel;
using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Import;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The KDBX import dialog: another KeePass file, unlocked, and where each of its groups lands in the
/// open vault; or, with <see cref="KeepEditingInPlace"/>, that file opened as the vault instead.
/// </summary>
/// <remarks>
/// <para>
/// The file goes through the same <see cref="KdbxImport"/> as <c>keypaste import</c>, so both front
/// ends copy the same file the same way. It is opened read-only and its key is held only by the
/// <see cref="ImportSource"/>, which goes on cancel, on confirm and on lock.
/// </para>
/// <para>
/// A file that also needs a YubiKey slot is asked through the session's keys, once, by a key of the
/// dialog's own, so its touch is said here rather than as a save waiting in the shell; the CLI's import
/// cannot ask one (D-0366).
/// </para>
/// <para>
/// Nothing here names a value: rows carry group paths, titles' counts and problems, and the password
/// typed for the file stays in a buffer that is cleared once it has been tried.
/// </para>
/// </remarks>
internal sealed partial class KdbxImportViewModel : ObservableObject, ISecretSink, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly KdbxProbe? _probe;
    private readonly Action<string, string?, int?> _openInPlace;
    private readonly Action<string> _announce;
    private readonly Func<Task<string?>>? _pickKeyfile;
    private readonly Func<Task>? _chooseAnother;
    private readonly Func<SecretBuffer, Task<PasteOutcome>>? _paste;
    private readonly Action<Action>? _post;
    private readonly string _fileName;
    private readonly SecretBuffer _password = new();

    private string? _keyfilePath;
    private HardwareKey? _asking;
    private string _into = string.Empty;
    private bool _blocked;

    /// <summary>Reads the file's header and waits for its password.</summary>
    /// <param name="session">The session whose vault an import copies into.</param>
    /// <param name="path">The file to import.</param>
    /// <param name="openInPlace">
    /// Opens a file as the vault, as the unlock screen's open-another-vault flow does: the session
    /// locks the vault it has and asks for this one's password, with the keyfile and YubiKey slot chosen here.
    /// </param>
    /// <param name="announce">Says what an import did, once it is saved.</param>
    /// <param name="pickKeyfile">Asks which keyfile opens the file; null where nothing can ask.</param>
    /// <param name="chooseAnother">Asks for another file when this one cannot be read; null where nothing can ask.</param>
    /// <param name="paste">Reads the clipboard into a buffer under D-0225's rule, as for the master-password fields; null where nothing can paste.</param>
    /// <param name="post">Runs an action on the UI thread, where a lock, which may run on another, closes the dialog; null closes it where the lock runs.</param>
    /// <param name="exportFormats">The exports the import also reads, which a file that is no KeePass database is named as none of; null where only KeePass files are offered.</param>
    internal KdbxImportViewModel(
        AppVaultSession session,
        string path,
        Action<string, string?, int?> openInPlace,
        Action<string> announce,
        Func<Task<string?>>? pickKeyfile = null,
        Func<Task>? chooseAnother = null,
        Func<SecretBuffer, Task<PasteOutcome>>? paste = null,
        Action<Action>? post = null,
        IReadOnlyList<string>? exportFormats = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(openInPlace);
        ArgumentNullException.ThrowIfNull(announce);

        _session = session;
        _openInPlace = openInPlace;
        _announce = announce;
        _pickKeyfile = pickKeyfile;
        _chooseAnother = chooseAnother;
        _paste = paste;
        _post = post;
        _fileName = System.IO.Path.GetFileName(path);

        if (KdbxImport.TryProbe(path, out var probe, out var error))
        {
            _probe = probe;
            _keyfilePath = probe.SiblingKeyfile;
        }
        else
        {
            Message = exportFormats is { Count: > 0 }
                ? $"{WhyUnreadable(path, error)} Nor is it an export keypaste reads: {ExportImportViewModel.Either(exportFormats)}."
                : WhyUnreadable(path, error);
        }

        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => !Busy && _probe is not null && Source is null);
        CancelCommand = new RelayCommand(Dispose);
        ChooseKeyfileCommand = new AsyncRelayCommand(ChooseKeyfileAsync, () => !Busy && Source is null && _pickKeyfile is not null);
        ClearKeyfileCommand = new RelayCommand(() => KeyfilePath = null, () => !Busy && Source is null && _keyfilePath is not null);
        UseHardwareKeyCommand = new RelayCommand(() => HardwareKeySlot = 2, () => !Busy && Source is null && OffersHardwareKey && HardwareKeySlot is null);
        SwitchSlotCommand = new RelayCommand(() => HardwareKeySlot = HardwareKeySlot == 1 ? 2 : 1, () => !Busy && Source is null && HardwareKeySlot is not null);
        ClearHardwareKeyCommand = new RelayCommand(() => HardwareKeySlot = null, () => !Busy && Source is null && HardwareKeySlot is not null);
        CancelTouchCommand = new RelayCommand(() => Volatile.Read(ref _asking)?.CancelWait(), () => IsWaitingForTouch);
        ChooseAnotherCommand = new AsyncRelayCommand(() => _chooseAnother!(), () => _chooseAnother is not null && !Disposed);

        _session.Locked += OnLocked;
    }

    /// <summary>Raised once, when the dialog is done: cancelled, confirmed or dropped by a lock.</summary>
    internal event EventHandler? Closed;

    /// <summary>Whether the file's header was read, so there is something to unlock or open.</summary>
    internal bool IsReadable => _probe is not null;

    /// <summary>Whether the header could not be read: the card says so and only another file or Cancel is offered.</summary>
    internal bool IsUnreadable => _probe is null;

    /// <summary>Asks for another file in place of one that cannot be read.</summary>
    internal AsyncRelayCommand ChooseAnotherCommand { get; }

    internal bool CanChooseAnother => IsUnreadable && _chooseAnother is not null;

    /// <summary>The file card's second line: the header before unlock, and what unlocked it after.</summary>
    internal string CardDetail => Source is null
        ? $"{Version} · {Kdf} · {Cipher}"
        : $"{Version} · {Kdf} · {Entries(EntryCount)} · {KeyFactors}";

    /// <summary>Whether a message shows below the dialog's body rather than under the password field or on the card.</summary>
    internal bool HasTrailingMessage => HasMessage && !NeedsUnlock && IsReadable;

    /// <summary>Whether the password and keyfile are still wanted before rows can show.</summary>
    internal bool NeedsUnlock => !Disposed && _probe is not null && Source is null && !KeepEditingInPlace;

    /// <summary>Whether a paste may join the password: the field is showing and no unlock is reading it.</summary>
    private bool TakesPassword => NeedsUnlock && !Busy;

    /// <summary>Whether the confirm button shows: the file was read and nothing more is needed to unlock it.</summary>
    internal bool ShowsConfirm => IsReadable && !NeedsUnlock;

    /// <summary>Whether the group mapping shows: the file is unlocked and is being copied.</summary>
    internal bool ShowsRows => Source is not null && !KeepEditingInPlace;

    /// <summary>The unlock button's words, which say when Argon2 is running.</summary>
    internal string UnlockText => Busy ? "Unlocking…" : "Unlock";

    /// <summary>What the file holds that no import copies, such as its recycle bin.</summary>
    internal string SkippedText => Source is { Skipped.Count: > 0 } source
        ? "Not copied: " + string.Join(", ", source.Skipped.Select(skip => $"{Show(skip.SourceGroup)} ({Entries(skip.EntryCount)})"))
        : string.Empty;

    internal bool HasSkipped => SkippedText.Length > 0;

    /// <summary>What keeping the file in place does to the vault that is open now.</summary>
    /// <remarks>
    /// The file's key is not carried over from this dialog: the unlock screen asks for it, so after a
    /// decrypt here the note says it will be asked for again.
    /// </remarks>
    internal string InPlaceNote => (_session.VaultPath, Source) switch
    {
        ({ } open, null) => $"{Show(System.IO.Path.GetFileName(open))} locks and {FileName} opens as the vault, asking for its password. Nothing is copied.",
        ({ } open, _) => $"{Show(System.IO.Path.GetFileName(open))} locks and {FileName} opens as the vault; you type its password once more there. Nothing is copied.",
        _ => $"{FileName} opens as the vault. Nothing is copied.",
    };

    internal bool HasMessage => Message.Length > 0;

    /// <summary>The keyfile's name; the full path is a tooltip.</summary>
    internal string KeyfileName => _keyfilePath is null ? string.Empty : Show(System.IO.Path.GetFileName(_keyfilePath));

    internal bool HasKeyfile => _keyfilePath is not null;

    /// <summary>Asks for the file's keyfile.</summary>
    internal AsyncRelayCommand ChooseKeyfileCommand { get; }

    /// <summary>Whether this session reaches YubiKeys, so the dialog offers one.</summary>
    internal bool OffersHardwareKey => _session.HardwareKeys is not null;

    /// <summary>The YubiKey slot the file needs as well, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesHardwareKey), nameof(HardwareKeyLabel), nameof(OtherSlotLabel))]
    [NotifyCanExecuteChangedFor(nameof(UseHardwareKeyCommand), nameof(SwitchSlotCommand), nameof(ClearHardwareKeyCommand))]
    internal partial int? HardwareKeySlot { get; private set; }

    internal bool UsesHardwareKey => HardwareKeySlot is not null;

    internal string HardwareKeyLabel => HardwareKeySlot is { } slot ? $"YubiKey, slot {slot}" : string.Empty;

    internal string OtherSlotLabel => HardwareKeySlot == 1 ? "Use slot 2" : "Use slot 1";

    /// <summary>Asks for a YubiKey as well, in slot 2 until another is chosen.</summary>
    internal RelayCommand UseHardwareKeyCommand { get; }

    /// <summary>Moves between the YubiKey's two slots.</summary>
    internal RelayCommand SwitchSlotCommand { get; }

    /// <summary>Stops asking for a YubiKey.</summary>
    internal RelayCommand ClearHardwareKeyCommand { get; }

    /// <summary>Whether the YubiKey waits to be touched, when its waiting dot is the dialog's one amber element.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnlockIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(CancelTouchCommand))]
    internal partial bool IsWaitingForTouch { get; private set; }

    /// <summary>Whether Unlock is the amber action, which it is except while the YubiKey waits.</summary>
    internal bool UnlockIsPrimary => !IsWaitingForTouch;

    /// <summary>Stops waiting for the YubiKey, so nothing is opened.</summary>
    internal RelayCommand CancelTouchCommand { get; }

    /// <summary>Stops using a keyfile, including one found beside the file.</summary>
    internal RelayCommand ClearKeyfileCommand { get; }

    internal string FileName => Show(_probe?.FileName ?? _fileName);

    internal string Version => _probe?.Version ?? string.Empty;

    internal string Kdf => _probe?.Kdf ?? string.Empty;

    internal string Cipher => _probe?.Cipher ?? string.Empty;

    /// <summary>What unlocked the file, as the card words it: <c>key file + password</c>.</summary>
    internal string KeyFactors => Source is null
        ? string.Empty
        : string.Join(" + ", Source.KeyFactors.Reverse());

    /// <summary>How many entries a whole import copies.</summary>
    internal int EntryCount => Source?.EntryCount ?? 0;

    internal bool IsDecrypted => Source is not null;

    /// <summary>Whether the file was read but not yet unlocked.</summary>
    internal bool IsLocked => _probe is not null && Source is null;

    /// <summary>The unlocked file, while there is one. A test seam for when its key goes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardDetail), nameof(NeedsUnlock), nameof(ShowsRows), nameof(SkippedText), nameof(InPlaceNote))]
    [NotifyPropertyChangedFor(nameof(KeyFactors), nameof(EntryCount), nameof(IsDecrypted), nameof(IsLocked), nameof(CardText), nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(HasTrailingMessage), nameof(ShowsConfirm), nameof(HasSkipped))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand), nameof(ChooseKeyfileCommand), nameof(ClearKeyfileCommand), nameof(UseHardwareKeyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchSlotCommand), nameof(ClearHardwareKeyCommand), nameof(ConfirmCommand))]
    internal partial ImportSource? Source { get; private set; }

    /// <summary>The file card's one line.</summary>
    internal string CardText => Source is null
        ? $"{FileName} · {Version} · {Kdf} · Locked"
        : $"{FileName} · {Version} · {Kdf} · {Entries(EntryCount)} · {KeyFactors} · Decrypted";

    /// <summary>How many characters of the file's password have been typed, which is all a screen may know.</summary>
    internal int PasswordLength => Disposed ? 0 : _password.Length;

    /// <summary>The file's keyfile, prefilled from one found beside it.</summary>
    internal string? KeyfilePath
    {
        get => _keyfilePath;
        set
        {
            if (SetProperty(ref _keyfilePath, string.IsNullOrEmpty(value) ? null : value))
            {
                OnPropertyChanged(nameof(KeyfileName));
                OnPropertyChanged(nameof(HasKeyfile));
                ClearKeyfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    internal AsyncRelayCommand UnlockCommand { get; }

    /// <summary>One row per group of the file, once it is unlocked.</summary>
    internal ObservableCollection<ImportRowViewModel> Rows { get; } = [];

    /// <summary>The group everything is copied under; changing it lays every row out again.</summary>
    internal string Into
    {
        get => _into;
        set
        {
            if (SetProperty(ref _into, value ?? string.Empty) && Source is not null)
            {
                Lay(_into);
            }
        }
    }

    /// <summary>Whether confirming opens the file as the vault instead of copying it in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsUnlock), nameof(ShowsRows), nameof(ConfirmText), nameof(CanConfirm), nameof(HasTrailingMessage))]
    [NotifyPropertyChangedFor(nameof(ShowsConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    internal partial bool KeepEditingInPlace { get; set; }

    /// <summary>What is wrong with the file or the import as a whole, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage), nameof(HasTrailingMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnlockText))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand), nameof(ChooseKeyfileCommand), nameof(ClearKeyfileCommand), nameof(UseHardwareKeyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchSlotCommand), nameof(ClearHardwareKeyCommand))]
    internal partial bool Busy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsUnlock), nameof(PasswordLength), nameof(CanConfirm), nameof(HasTrailingMessage), nameof(ShowsConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ChooseAnotherCommand), nameof(ConfirmCommand))]
    private partial bool Disposed { get; set; }

    /// <summary><c>Import 142 entries</c>, or <c>Open acme.kdbx</c> when keeping the file in place.</summary>
    internal string ConfirmText => KeepEditingInPlace
        ? $"Open {FileName}"
        : $"Import {Entries(Rows.Where(row => row.Include).Sum(row => row.Count))}";

    /// <summary>False while any included row is blocked, or before there is anything to confirm.</summary>
    internal bool CanConfirm => !Disposed && _probe is not null
        && (KeepEditingInPlace || (Source is not null && _session.IsUnlocked && !_blocked && Rows.Any(row => row.Include)));

    internal RelayCommand CancelCommand { get; }

    internal void TypePassword(char c)
    {
        if (!Disposed)
        {
            _password.Append(c);
            OnPropertyChanged(nameof(PasswordLength));
        }
    }

    internal void BackspacePassword()
    {
        if (!Disposed)
        {
            _password.Backspace();
            OnPropertyChanged(nameof(PasswordLength));
        }
    }

    internal void ClearPassword()
    {
        if (!Disposed)
        {
            _password.Clear();
            OnPropertyChanged(nameof(PasswordLength));
        }
    }

    void ISecretSink.Type(char value) => TypePassword(value);

    void ISecretSink.Backspace() => BackspacePassword();

    void ISecretSink.Clear() => ClearPassword();

    Task ISecretSink.Paste() => PastePasswordAsync();

    /// <summary>
    /// Reads the clipboard into a buffer of the paste's own, as the master-password fields do (D-0495), and appends
    /// it only once the clipboard has answered and while the file still waits for its password and is not being unlocked.
    /// </summary>
    internal async Task PastePasswordAsync()
    {
        if (!TakesPassword || _paste is null)
        {
            return;
        }

        using var pasted = new SecretBuffer();
        var outcome = await _paste(pasted).ConfigureAwait(true);

        if (!TakesPassword)
        {
            return;
        }

        if (outcome == PasteOutcome.Pasted)
        {
            _password.Append(pasted.Value);
            OnPropertyChanged(nameof(PasswordLength));
            Message = string.Empty;
        }
        else
        {
            Message = SecretField.NoteFor(outcome);
        }
    }

    /// <summary>Unlocks the file off the UI thread and lays out its rows.</summary>
    /// <remarks>Internal so a test can await what <see cref="UnlockCommand"/> starts.</remarks>
    internal async Task UnlockAsync()
    {
        if (Disposed || _probe is not { } probe || Source is not null)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;
        var keyfile = _keyfilePath;
        var slot = HardwareKeySlot;
        using var key = slot is { } chosen && _session.HardwareKeys is { } keys ? new HardwareKey(keys, chosen) : null;

        if (key is not null)
        {
            key.WaitingForTouch += OnWaitingForTouch;
            Volatile.Write(ref _asking, key);
        }

        try
        {
            // The key answers the file's one challenge here; a source is never saved, so nothing asks it again.
            var opened = await Task.Run(() => KdbxImport.Open(probe.Path, _password.Value, keyfile, key)).ConfigureAwait(true);

            if (Disposed || !_session.IsUnlocked)
            {
                opened.Dispose();
                return;
            }

            Source = opened;
            Lay(null);
        }
        catch (InvalidMasterPasswordException)
        {
            Message = WrongFactors(keyfile is not null, slot is not null);
        }
        catch (HardwareKeyException ex)
        {
            Message = ExplainHardwareKey(ex, slot);
        }
        catch (VaultException ex)
        {
            Message = DisplayTextSanitizer.Sanitize(ex.Message).Text;
        }
        finally
        {
            if (key is not null)
            {
                Interlocked.CompareExchange(ref _asking, null, key);
                key.WaitingForTouch -= OnWaitingForTouch;
            }

            ClearPassword();
            Busy = false;
        }
    }

    /// <summary>The refusal of a password, worded for the factors that went with it, since which one was wrong is not knowable (D-0284).</summary>
    private string WrongFactors(bool withKeyfile, bool withHardwareKey) => (withKeyfile, withHardwareKey) switch
    {
        (false, false) => $"That password does not open {FileName}.",
        (true, false) => $"That password and key file do not open {FileName}.",
        (false, true) => $"That password and YubiKey do not open {FileName}. If it uses the other slot, switch to it.",
        (true, true) => $"That password, key file and YubiKey do not open {FileName}. If it uses the other slot, switch to it.",
    };

    /// <summary>Why the YubiKey gave no answer, naming this file where the unlock screen names its vault.</summary>
    private string ExplainHardwareKey(HardwareKeyException failure, int? slot) => failure.Failure switch
    {
        HardwareKeyFailure.NotFound => "No YubiKey found. Plug it in and unlock again.",
        HardwareKeyFailure.MoreThanOne => $"More than one YubiKey is plugged in. Leave only the one {FileName} uses.",
        HardwareKeyFailure.SlotNotConfigured =>
            $"That YubiKey's slot {slot} isn't set up for challenge-response. If {FileName} uses the other slot, switch to it.",
        HardwareKeyFailure.TimedOut => "The YubiKey wasn't touched in time. Unlock again and touch it when it flashes.",
        HardwareKeyFailure.Cancelled => "Stopped waiting for the YubiKey. Nothing was opened.",
        _ => DisplayTextSanitizer.Sanitize(failure.Message).Text,
    };

    private void OnWaitingForTouch(object? sender, bool waiting)
    {
        if (_post is { } post)
        {
            post(() => IsWaitingForTouch = waiting && !Disposed);
        }
        else
        {
            IsWaitingForTouch = waiting && !Disposed;
        }
    }

    /// <summary>Closes the file and forgets its password and rows.</summary>
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        _session.Locked -= OnLocked;
        Volatile.Read(ref _asking)?.CancelWait();
        IsWaitingForTouch = false;
        Source?.Dispose();
        Source = null;
        _password.Dispose();
        Rows.Clear();
        OnPropertyChanged(nameof(ConfirmText));
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ChooseKeyfileAsync()
    {
        if (_pickKeyfile is null || await _pickKeyfile().ConfigureAwait(true) is not { } picked || Disposed)
        {
            return;
        }

        var full = System.IO.Path.GetFullPath(picked);
        var inspection = VaultKeyfile.Inspect(full);

        if (!inspection.Accepted)
        {
            Message = UnlockViewModel.ExplainKeyfile(inspection.Outcome);
            return;
        }

        Message = string.Empty;
        KeyfilePath = full;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (!CanConfirm || _probe is not { } probe)
        {
            return;
        }

        if (KeepEditingInPlace)
        {
            var keyfile = _keyfilePath;
            var slot = HardwareKeySlot;
            Dispose();
            _openInPlace(probe.Path, keyfile, slot);
            return;
        }

        if (Source is not { } source)
        {
            return;
        }

        var plan = Plan();

        // The commit saves for itself and takes its copies back when the save fails, so the open vault
        // never holds an import its file does not, and Confirm can be pressed again (D-0567).
        var write = _session.WriteWhole((vault, save) => source.Commit(vault, plan, save));

        switch (write.Outcome)
        {
            case WriteOutcome.Locked:
                return;
            case WriteOutcome.Saved:
                _announce($"Imported {Entries(write.Value!.Entries)} from {FileName}");
                Dispose();
                return;
            case WriteOutcome.ChangedOnDisk:
                Message = $"{write.Problem("import again")} Nothing was imported.";
                return;
            default:
                var reason = DisplayTextSanitizer.Sanitize(write.Reason!).Text;
                Check();
                Message = reason.StartsWith("Nothing was imported", StringComparison.Ordinal) ? reason : $"{reason} Nothing was imported. Correct that, then choose the file again.";
                return;
        }
    }

    /// <summary>Lays every row out as the default plan puts it, under <paramref name="into"/> or the file's name.</summary>
    private void Lay(string? into)
    {
        if (Source is null || _session.Unlocked is not { } vault)
        {
            return;
        }

        var plan = Source.DefaultPlan(vault, into);
        _into = plan.Into;
        OnPropertyChanged(nameof(Into));

        Rows.Clear();
        foreach (var row in plan.Rows)
        {
            Rows.Add(new ImportRowViewModel(row, Check));
        }

        Check();
    }

    private ImportPlan Plan() => new([.. Rows.Select(row => row.Row)], _into);

    /// <summary>Checks the rows as they stand and says what blocks.</summary>
    private void Check()
    {
        if (Source is not null && _session.Unlocked is { } vault)
        {
            var problems = Source.Check(vault, Plan());

            foreach (var row in Rows)
            {
                var own = problems.Where(problem => problem.Index == row.Row.Index).ToList();
                var blocks = own.Any(problem => problem.Blocks);
                var text = string.Join("; ", own.Select(problem => DisplayTextSanitizer.Sanitize(problem.Message).Text));
                row.Show(blocks ? $"{text}. Type another destination or untick {row.SourceGroup}." : text, blocks);
            }

            _blocked = problems.Any(problem => problem.Blocks);
            Message = string.Join(" ", problems.Where(problem => problem.Index < 0)
                .Select(problem => DisplayTextSanitizer.Sanitize(problem.Message).Text));
        }

        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The probe's refusal about this file, without the directory it names.</summary>
    private static string WhyUnreadable(string path, string error)
    {
        var quoted = $"'{System.IO.Path.GetFullPath(path)}' ";
        var why = error.StartsWith(quoted, StringComparison.Ordinal) ? "This file " + error[quoted.Length..] : error;
        return DisplayTextSanitizer.Sanitize(why).Text;
    }

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        if (_post is { } post)
        {
            post(Dispose);
        }
        else
        {
            Dispose();
        }
    }

    private static string Show(string? name) => name is null ? string.Empty : EntryNameSanitizer.SanitizePath(name).Text;

    private static string Entries(int count) =>
        count == 1 ? "1 item" : string.Create(CultureInfo.InvariantCulture, $"{count} items");
}

/// <summary>One group of the file being imported: where it lands, whether it is copied, and what is wrong.</summary>
internal sealed partial class ImportRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private ImportRow _row;

    internal ImportRowViewModel(ImportRow row, Action changed)
    {
        _row = row;
        _changed = changed;
    }

    /// <summary>The row as the plan will copy it.</summary>
    internal ImportRow Row => _row;

    /// <summary>The group in the file, or <c>(top level)</c> for the entries in its root.</summary>
    internal string SourceGroup => _row.IsRootEntries ? "(top level)" : EntryNameSanitizer.SanitizePath(_row.SourceGroup).Text;

    internal int Count => _row.EntryCount;

    internal string CountText => Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>The group path it lands at in the open vault.</summary>
    internal string Destination
    {
        get => _row.Destination;
        set => Change(_row with { Destination = value ?? string.Empty });
    }

    internal bool Include
    {
        get => _row.Include;
        set => Change(_row with { Include = value });
    }

    /// <summary>Why this row cannot be copied as it stands, or a note about it; empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    internal partial string Problem { get; private set; } = string.Empty;

    /// <summary>Whether <see cref="Problem"/> stops the import.</summary>
    [ObservableProperty]
    internal partial bool Blocks { get; private set; }

    internal bool HasProblem => Problem.Length > 0;

    internal void Show(string problem, bool blocks)
    {
        Problem = problem;
        Blocks = blocks;
    }

    private void Change(ImportRow row)
    {
        if (SetProperty(ref _row, row, nameof(Row)))
        {
            OnPropertyChanged(nameof(Destination));
            OnPropertyChanged(nameof(Include));
            _changed();
        }
    }
}
