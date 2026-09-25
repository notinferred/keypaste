using System.Globalization;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One backup, as the restore panel lists it.</summary>
/// <remarks>The time comes from the backup's name, which is the only one a copy keeps (V.4a).</remarks>
internal sealed class BackupRow(VaultBackup backup, bool isDamaged)
{
    internal VaultBackup Backup { get; } = backup;

    internal string Taken { get; } =
        backup.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>Whether the file has no KDBX header, which needs no password to find out.</summary>
    internal bool IsDamaged { get; } = isDamaged;
}

/// <summary>
/// Restoring a whole-vault backup from the locked screen: which copy, the password it was made
/// under, what it holds, and a confirmation before the vault is replaced.
/// </summary>
/// <remarks>
/// <para>
/// Every byte moves inside <see cref="VaultBackups"/>. This chooses, asks and reports.
/// </para>
/// <para>
/// <b>This object owns the backup's password</b>, for <see cref="UnlockViewModel"/>'s reason, and
/// holds it from the check until the restore so the restored vault opens without asking twice. That
/// is a correct master password one click from an unlocked vault, so it does not wait indefinitely:
/// cancelling, choosing another copy, any outcome, a minimize and a spell of idleness each zero it.
/// </para>
/// <para>
/// What the check shows is counts and the backup's own time. No entry name and no value reaches this
/// screen, which is still the locked one.
/// </para>
/// </remarks>
internal sealed class RestoreBackupViewModel : ObservableObject, IDisposable
{
    internal const string PasswordNote =
        "A backup opens with the master password it was made under, which may be an earlier one. " +
        "Nothing recovers a password nobody remembers.";

    internal const string OtherPrograms =
        "Close anything else that has this vault open first, KeePassXC and a running keypaste agent " +
        "included: they keep their own copy and will not notice the change.";

    private const string _refused = "That password doesn't open this backup, or the backup is damaged. Try another copy.";

    private const string _refusedWithKeyfile =
        "That password and keyfile don't open this backup, or the backup is damaged. Try another copy.";

    private const string _expired = "That was left waiting a while, so the password was cleared. Nothing was changed.";

    private readonly string _vaultPath;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan> _idleTimeout;
    private readonly Action<Action> _post;
    private readonly Func<VaultRestoreReport, SecretBuffer, string?, Task> _restored;
    private readonly Func<Task<string?>> _pickKeyfile;

    private SecretBuffer _password = new();
    private VaultBackupSummary? _validated;
    private ITimer? _expiry;
    private BackupRow? _selected;
    private string? _keyfilePath;
    private string _message = string.Empty;
    private bool _busy;
    private bool _disposed;

    internal RestoreBackupViewModel(
        string vaultPath,
        TimeProvider clock,
        Func<TimeSpan> idleTimeout,
        Action<Action> post,
        Func<VaultRestoreReport, SecretBuffer, string?, Task> restored,
        string? keyfilePath,
        Func<Task<string?>> pickKeyfile)
    {
        _vaultPath = vaultPath;
        _clock = clock;
        _idleTimeout = idleTimeout;
        _post = post;
        _restored = restored;
        _keyfilePath = keyfilePath;
        _pickKeyfile = pickKeyfile;

        CheckCommand = new AsyncRelayCommand(CheckAsync, () => CanCheck);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => IsConfirming && !_busy);
        CancelCommand = new RelayCommand(() => Forget(string.Empty), () => IsConfirming && !_busy);
        ChooseKeyfileCommand = new AsyncRelayCommand(ChooseKeyfileAsync, () => IsChoosing && !_busy);
        ClearKeyfileCommand = new RelayCommand(() => SetKeyfile(null), () => IsChoosing && !_busy && _keyfilePath is not null);

        Rows = [.. VaultBackups.List(vaultPath).Select(backup => new BackupRow(backup, !KdbxHeader.IsVaultFile(backup.Path)))];
        _selected = Rows.FirstOrDefault(row => !row.IsDamaged);
    }

    internal IReadOnlyList<BackupRow> Rows { get; private set; }

    internal AsyncRelayCommand CheckCommand { get; }

    internal AsyncRelayCommand ConfirmCommand { get; }

    /// <summary>Leaves the confirmation without restoring, and forgets the password.</summary>
    internal RelayCommand CancelCommand { get; }

    /// <summary>Asks for the keyfile the backup was made under.</summary>
    internal AsyncRelayCommand ChooseKeyfileCommand { get; }

    /// <summary>Checks the backup with no keyfile.</summary>
    internal RelayCommand ClearKeyfileCommand { get; }

    /// <summary>
    /// The keyfile the backup was made under, starting as the one the unlock screen had chosen. A copy
    /// keeps the factors the vault had when it was taken, so after an access change it is the old one.
    /// </summary>
    internal string? KeyfilePath => _keyfilePath;

    internal string KeyfileName => _keyfilePath is null ? string.Empty : Path.GetFileName(_keyfilePath);

    internal bool HasKeyfile => _keyfilePath is not null;

    internal BackupRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                // Moving the selection abandons a check made of a different copy.
                Forget(value is { IsDamaged: true }
                    ? "That copy is not a KeePass file any more, so it can't be restored. Try another."
                    : string.Empty);
            }
        }
    }

    internal int MaskedLength => _password.Length;

    internal bool IsChoosing => _validated is null;

    internal bool IsConfirming => _validated is not null;

    internal bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                RaiseCommands();
            }
        }
    }

    internal string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
                Raise(nameof(IsError));
                Raise(nameof(HasNote));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>Whether <see cref="Message"/> is a refusal, drawn in red under the password; only the idle note is not.</summary>
    internal bool IsError => HasMessage && !string.Equals(_message, _expired, StringComparison.Ordinal);

    internal bool HasNote => HasMessage && !IsError;

    internal string Taken => _validated is { } summary
        ? $"Backup taken {summary.Backup.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}."
        : string.Empty;

    internal string Holds => _validated is { } summary
        ? $"It holds {Count(summary.Entries, "entry", "entries")} in {Count(summary.Groups, "group", "groups")}, " +
          $"{Count(summary.EnvProjects, "env project", "env projects")} among them."
        : string.Empty;

    internal string Replaces => _validated switch
    {
        null => string.Empty,
        { Replaces: null } => "There is no file at the vault's path now, so nothing is replaced.",
        { Replaces: { } facts } =>
            $"It replaces {Path.GetFileName(facts.Path)}, {Size(facts.Length)}, last written " +
            $"{facts.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}" +
            (KdbxHeader.IsVaultFile(facts.Path) ? "." : ", which is not readable as a vault.") +
            " That file is kept as a backup first.",
    };

#pragma warning disable CA1822
    internal string Note => PasswordNote;

    internal string Warning => OtherPrograms;
#pragma warning restore CA1822

    private bool CanCheck =>
        !_busy && IsChoosing && _selected is { IsDamaged: false } && (_password.Length > 0 || _keyfilePath is not null);

    internal void Type(char c) => Edit(() => _password.Append(c));

    internal void Backspace() => Edit(_password.Backspace);

    internal void ClearPassword() => Edit(_password.Clear);

    /// <summary>Drops a pending restore without a word, as a minimize does.</summary>
    internal void CancelPending()
    {
        if (!_disposed && !_busy)
        {
            Forget(string.Empty);
        }
    }

    /// <summary>Opens the selected backup and, if it opens, shows what restoring it would do.</summary>
    internal async Task CheckAsync()
    {
        if (_disposed || !CanCheck || _selected is not { } row)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;

        try
        {
            // Argon2, as unlocking is, so off the UI thread for the same reason.
            var keyfile = _keyfilePath;
            _validated = await Task.Run(() => VaultBackups.Inspect(_vaultPath, row.Backup, _password.Value, keyfile))
                .ConfigureAwait(true);

            Arm();
            RaiseState();
        }
        catch (InvalidMasterPasswordException)
        {
            Forget(_keyfilePath is null ? _refused : _refusedWithKeyfile);
        }
        catch (VaultException e)
        {
            Forget(e.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Replaces the vault with the checked backup, then hands over the password that opens it.</summary>
    internal async Task ConfirmAsync()
    {
        if (_disposed || _busy || _validated is not { } validated)
        {
            return;
        }

        Busy = true;
        _expiry?.Dispose();
        _expiry = null;

        try
        {
            var report = await Task.Run(() => VaultBackups.Restore(validated)).ConfigureAwait(true);
            await _restored(report, _password, _keyfilePath).ConfigureAwait(true);
            Forget(string.Empty);
        }
        catch (VaultChangedOnDiskException)
        {
            Forget("The vault file changed while the backup was being restored, so it was left alone. Nothing was replaced.");
        }
        catch (VaultException e)
        {
            Forget($"{e.Message} Nothing was replaced.");
        }
        finally
        {
            Busy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Forget(string.Empty);
        _disposed = true;
        _password.Dispose();
        _selected = null;
        Rows = [];
        Raise(nameof(Rows));
        Raise(nameof(Selected));
    }

    private async Task ChooseKeyfileAsync()
    {
        if (_disposed || await _pickKeyfile().ConfigureAwait(true) is not { } picked)
        {
            return;
        }

        var full = Path.GetFullPath(picked);
        var inspection = VaultKeyfile.Inspect(full);

        if (!inspection.Accepted)
        {
            Message = UnlockViewModel.ExplainKeyfile(inspection.Outcome);
            return;
        }

        SetKeyfile(full);
    }

    private void SetKeyfile(string? keyfile)
    {
        if (_disposed || _busy || IsConfirming)
        {
            return;
        }

        _keyfilePath = keyfile;
        Message = string.Empty;
        Raise(nameof(KeyfilePath));
        Raise(nameof(KeyfileName));
        Raise(nameof(HasKeyfile));
        RaiseCommands();
    }

    private void Edit(Action edit)
    {
        if (_disposed || _busy || IsConfirming)
        {
            return;
        }

        edit();
        Arm();
        Raise(nameof(MaskedLength));
        CheckCommand.RaiseCanExecuteChanged();

        // A damaged copy keeps its explanation: the button stays off, and it should still say why.
        if (HasMessage && _selected is not { IsDamaged: true })
        {
            Message = string.Empty;
        }
    }

    /// <summary>Zeroes the password and drops the check, on every way out of a pending restore.</summary>
    private void Forget(string message)
    {
        if (_disposed)
        {
            return;
        }

        _expiry?.Dispose();
        _expiry = null;
        _validated = null;
        _password.Dispose();
        _password = new SecretBuffer();
        Message = message;
        RaiseState();
    }

    /// <summary>Starts the idle deadline again, measured on the clock the session locks by.</summary>
    private void Arm()
    {
        _expiry?.Dispose();
        _expiry = _clock.CreateTimer(_ => _post(Expire), null, _idleTimeout(), Timeout.InfiniteTimeSpan);
    }

    private void Expire()
    {
        if (!_disposed && !_busy && (_validated is not null || _password.Length > 0))
        {
            Forget(_expired);
        }
    }

    private void RaiseState()
    {
        Raise(nameof(MaskedLength));
        Raise(nameof(IsChoosing));
        Raise(nameof(IsConfirming));
        Raise(nameof(Taken));
        Raise(nameof(Holds));
        Raise(nameof(Replaces));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        CheckCommand.RaiseCanExecuteChanged();
        ChooseKeyfileCommand.RaiseCanExecuteChanged();
        ClearKeyfileCommand.RaiseCanExecuteChanged();
        ConfirmCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.CurrentCulture, $"{count} {(count == 1 ? one : many)}");

    private static string Size(long bytes) => bytes < 1024
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes} bytes")
        : string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB");
}
