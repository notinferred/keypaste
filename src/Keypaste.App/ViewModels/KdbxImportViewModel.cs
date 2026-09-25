using System.Collections.ObjectModel;
using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
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
/// Nothing here names a value: rows carry group paths, titles' counts and problems, and the password
/// typed for the file stays in a buffer that is cleared once it has been tried.
/// </para>
/// </remarks>
internal sealed class KdbxImportViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly KdbxProbe? _probe;
    private readonly Action<string, string?> _openInPlace;
    private readonly Action<string> _announce;
    private readonly SecretBuffer _password = new();

    private ImportSource? _source;
    private string? _keyfilePath;
    private string _into = string.Empty;
    private string _message = string.Empty;
    private bool _keepInPlace;
    private bool _busy;
    private bool _blocked;
    private bool _disposed;

    /// <summary>Reads the file's header and waits for its password.</summary>
    /// <param name="session">The session whose vault an import copies into.</param>
    /// <param name="path">The file to import.</param>
    /// <param name="openInPlace">
    /// Opens a file as the vault, as the unlock screen's open-another-vault flow does: the session
    /// locks the vault it has and asks for this one's password.
    /// </param>
    /// <param name="announce">Says what an import did, once it is saved.</param>
    internal KdbxImportViewModel(
        AppVaultSession session,
        string path,
        Action<string, string?> openInPlace,
        Action<string> announce)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(openInPlace);
        ArgumentNullException.ThrowIfNull(announce);

        _session = session;
        _openInPlace = openInPlace;
        _announce = announce;

        if (KdbxImport.TryProbe(path, out var probe, out var error))
        {
            _probe = probe;
            _keyfilePath = probe.SiblingKeyfile;
        }
        else
        {
            _message = DisplayTextSanitizer.Sanitize(error).Text;
        }

        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => !_busy && _probe is not null && _source is null);
        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Dispose);

        _session.Locked += OnLocked;
    }

    internal string FileName => Show(_probe?.FileName);

    internal string Version => _probe?.Version ?? string.Empty;

    internal string Kdf => _probe?.Kdf ?? string.Empty;

    internal string Cipher => _probe?.Cipher ?? string.Empty;

    /// <summary>What unlocked the file, as the card words it: <c>key file + password</c>.</summary>
    internal string KeyFactors => _source is null
        ? string.Empty
        : string.Join(" + ", _source.KeyFactors.Reverse());

    /// <summary>How many entries a whole import copies.</summary>
    internal int EntryCount => _source?.EntryCount ?? 0;

    internal bool IsDecrypted => _source is not null;

    /// <summary>The file card's one line.</summary>
    internal string CardText => _source is null
        ? $"{FileName} · {Version} · {Kdf} · Locked"
        : $"{FileName} · {Version} · {Kdf} · {Entries(EntryCount)} · {KeyFactors} · Decrypted";

    /// <summary>How many characters of the file's password have been typed, which is all a screen may know.</summary>
    internal int PasswordLength => _disposed ? 0 : _password.Length;

    /// <summary>The file's keyfile, prefilled from one found beside it.</summary>
    internal string? KeyfilePath
    {
        get => _keyfilePath;
        set => Set(ref _keyfilePath, string.IsNullOrEmpty(value) ? null : value);
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
            if (Set(ref _into, value ?? string.Empty) && _source is not null)
            {
                Lay(_into);
            }
        }
    }

    /// <summary>Whether confirming opens the file as the vault instead of copying it in.</summary>
    internal bool KeepEditingInPlace
    {
        get => _keepInPlace;
        set
        {
            if (Set(ref _keepInPlace, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>What is wrong with the file or the import as a whole, or empty.</summary>
    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                UnlockCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary><c>Import 142 entries</c>, or <c>Open acme.kdbx</c> when keeping the file in place.</summary>
    internal string ConfirmText => _keepInPlace
        ? $"Open {FileName}"
        : $"Import {Entries(Rows.Where(row => row.Include).Sum(row => row.Count))}";

    /// <summary>False while any included row is blocked, or before there is anything to confirm.</summary>
    internal bool CanConfirm => !_disposed && _probe is not null
        && (_keepInPlace || (_source is not null && _session.IsUnlocked && !_blocked && Rows.Any(row => row.Include)));

    internal RelayCommand ConfirmCommand { get; }

    internal RelayCommand CancelCommand { get; }

    internal void TypePassword(char c)
    {
        if (!_disposed)
        {
            _password.Append(c);
            Raise(nameof(PasswordLength));
        }
    }

    internal void BackspacePassword()
    {
        if (!_disposed)
        {
            _password.Backspace();
            Raise(nameof(PasswordLength));
        }
    }

    internal void ClearPassword()
    {
        if (!_disposed)
        {
            _password.Clear();
            Raise(nameof(PasswordLength));
        }
    }

    /// <summary>Unlocks the file off the UI thread and lays out its rows.</summary>
    /// <remarks>Internal so a test can await what <see cref="UnlockCommand"/> starts.</remarks>
    internal async Task UnlockAsync()
    {
        if (_disposed || _probe is not { } probe || _source is not null)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;
        var keyfile = _keyfilePath;

        try
        {
            var opened = await Task.Run(() => KdbxImport.Open(probe.Path, _password.Value, keyfile)).ConfigureAwait(true);

            if (_disposed || !_session.IsUnlocked)
            {
                opened.Dispose();
                return;
            }

            _source = opened;
            Lay(null);
        }
        catch (InvalidMasterPasswordException)
        {
            Message = keyfile is null
                ? $"That password does not open {FileName}."
                : $"That password and key file do not open {FileName}.";
        }
        catch (VaultException ex)
        {
            Message = DisplayTextSanitizer.Sanitize(ex.Message).Text;
        }
        finally
        {
            ClearPassword();
            Busy = false;
        }
    }

    /// <summary>Closes the file and forgets its password and rows.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Locked -= OnLocked;
        _source?.Dispose();
        _source = null;
        _password.Dispose();
        Rows.Clear();
        Refresh();
    }

    private void Confirm()
    {
        if (!CanConfirm || _probe is not { } probe)
        {
            return;
        }

        if (_keepInPlace)
        {
            var keyfile = _keyfilePath;
            Dispose();
            _openInPlace(probe.Path, keyfile);
            return;
        }

        if (_source is not { } source || _session.Unlocked is not { } vault)
        {
            return;
        }

        var plan = Plan();

        try
        {
            var result = source.ApplyTo(vault, plan);
            vault.Save();
            _announce($"Imported {Entries(result.Entries)} into {Show(plan.Into)}");
        }
        catch (VaultChangedOnDiskException)
        {
            Message = "Something else changed this vault since you opened it. Lock and unlock to see it, then import again.";
            return;
        }
        catch (VaultException ex)
        {
            Message = DisplayTextSanitizer.Sanitize(ex.Message).Text;
            Check();
            return;
        }

        Dispose();
    }

    /// <summary>Lays every row out as the default plan puts it, under <paramref name="into"/> or the file's name.</summary>
    private void Lay(string? into)
    {
        if (_source is null || _session.Unlocked is not { } vault)
        {
            return;
        }

        var plan = _source.DefaultPlan(vault, into);
        _into = plan.Into;
        Raise(nameof(Into));

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
        if (_source is not null && _session.Unlocked is { } vault)
        {
            var problems = _source.Check(vault, Plan());

            foreach (var row in Rows)
            {
                var own = problems.Where(problem => problem.Index == row.Row.Index).ToList();
                row.Show(
                    string.Join("; ", own.Select(problem => DisplayTextSanitizer.Sanitize(problem.Message).Text)),
                    own.Any(problem => problem.Blocks));
            }

            _blocked = problems.Any(problem => problem.Blocks);
            Message = string.Join(" ", problems.Where(problem => problem.Index < 0)
                .Select(problem => DisplayTextSanitizer.Sanitize(problem.Message).Text));
        }

        Refresh();
    }

    private void Refresh()
    {
        Raise(nameof(IsDecrypted));
        Raise(nameof(EntryCount));
        Raise(nameof(KeyFactors));
        Raise(nameof(CardText));
        Raise(nameof(ConfirmText));
        Raise(nameof(CanConfirm));
        UnlockCommand.RaiseCanExecuteChanged();
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private void OnLocked(object? sender, VaultLockReason reason) => Dispose();

    private static string Show(string? name) => name is null ? string.Empty : EntryNameSanitizer.SanitizePath(name).Text;

    private static string Entries(int count) =>
        count == 1 ? "1 entry" : string.Create(CultureInfo.InvariantCulture, $"{count} entries");
}

/// <summary>One group of the file being imported: where it lands, whether it is copied, and what is wrong.</summary>
internal sealed class ImportRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private ImportRow _row;
    private string _problem = string.Empty;
    private bool _blocks;

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
    internal string Problem => _problem;

    /// <summary>Whether <see cref="Problem"/> stops the import.</summary>
    internal bool Blocks => _blocks;

    internal void Show(string problem, bool blocks)
    {
        Set(ref _problem, problem, nameof(Problem));
        Set(ref _blocks, blocks, nameof(Blocks));
    }

    private void Change(ImportRow row)
    {
        if (row == _row)
        {
            return;
        }

        _row = row;
        Raise(nameof(Destination));
        Raise(nameof(Include));
        _changed();
    }
}
