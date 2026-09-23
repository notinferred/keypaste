using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The selected entry's KeePass history: what it held before, and the way back to one of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own object rather than more properties on <see cref="EntryDetailViewModel"/>.</b> A
/// revision is addressed by an index that is only good for one reading, and every hold, copy and
/// restore re-checks it; the pane addresses one current entry and has none of that to do.
/// </para>
/// <para>
/// <b>History is read when somebody asks for it, not when they select an entry.</b>
/// <see cref="Vault.ReadHistory"/> answers with every revision's fields, so each reading
/// materialises every superseded password as a string the runtime will not let anyone wipe
/// (THREATS.md T-18). Core's API has no narrower shape, so the cost is paid on the toggle and on
/// each hold rather than on every click in the list.
/// </para>
/// <para>
/// <b>An index is only good for the reading that produced it (DECISIONS.md D-0229).</b> Every use
/// re-reads and checks the reading still agrees about that position — the same count, time, title
/// and length — and refuses rather than acting on a number that now names a different revision.
/// </para>
/// </remarks>
internal sealed class EntryHistoryViewModel : ObservableObject, IDisposable
{
    private const string _stale =
        "This entry changed since its history was read. The list has been refreshed — choose again.";

    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
    private readonly EntryDetailViewModel _owner;
    private readonly Action<EntryName> _restored;

    private IReadOnlyList<EntryRevisionRow> _rows = [];
    private EntryRevisionRow? _selected;
    private EntryRevisionRow? _revealed;
    private bool _isOpen;
    private bool _read;
    private int _countAtRead;

    internal EntryHistoryViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        EntryDetailViewModel owner,
        Action<EntryName> restored)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(restored);

        _session = session;
        _clipboard = clipboard;
        _owner = owner;
        _restored = restored;

        ToggleCommand = new RelayCommand(Toggle);
        RestoreCommand = new RelayCommand(Restore, () => _selected is not null);
    }

    /// <summary>Whether the history section is showing.</summary>
    internal bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (Set(ref _isOpen, value))
            {
                Raise(nameof(ToggleLabel));
                Raise(nameof(ShowsEmptyNote));
            }
        }
    }

    /// <summary>What the toggle says.</summary>
    internal string ToggleLabel => IsOpen ? "Hide history" : "Show history";

    /// <summary>The revisions, newest first, in the order the vault answered.</summary>
    internal IReadOnlyList<EntryRevisionRow> Rows
    {
        get => _rows;
        private set
        {
            if (Set(ref _rows, value))
            {
                Raise(nameof(ShowsEmptyNote));
            }
        }
    }

    /// <summary>The revision being compared with the current values, or null.</summary>
    internal EntryRevisionRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
            {
                return;
            }

            Raise(nameof(HasSelection));
            RestoreCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether a revision is being compared.</summary>
    internal bool HasSelection => _selected is not null;

    /// <summary>
    /// Whether to say the entry has never been changed.
    /// </summary>
    /// <remarks>
    /// Only after a reading that succeeded. <see cref="Vault.ReadHistory"/> tells an entry with no
    /// history apart from an entry that is not there, and a refused reading must not read as
    /// "nothing here".
    /// </remarks>
    internal bool ShowsEmptyNote => IsOpen && _read && Rows.Count == 0;

    /// <summary>What that says.</summary>
    internal string EmptyNote =>
        $"No earlier values yet. When you change {_owner.DisplayTitle}, the value it replaces is kept here.";

    /// <summary>Which revision is revealed, named by its time. Never by its value.</summary>
    internal string RevealedWhen => _revealed?.When ?? string.Empty;

    /// <summary>Opens and closes the section.</summary>
    internal RelayCommand ToggleCommand { get; }

    /// <summary>Makes the selected revision the entry's current values.</summary>
    internal RelayCommand RestoreCommand { get; }

    /// <summary>Reads again if the section is open, and reads nothing if it is not.</summary>
    /// <remarks>
    /// What an edit elsewhere on the pane calls. Reading while the section is closed would put
    /// every superseded password of the entry in memory for a list nobody is looking at.
    /// </remarks>
    internal void Refresh()
    {
        if (IsOpen)
        {
            Load();
        }
    }

    /// <summary>Reads the entry's history again.</summary>
    internal void Load()
    {
        if (_session.Unlocked is not { } vault)
        {
            Forget();
            return;
        }

        IReadOnlyList<EntryRevision>? revisions;
        try
        {
            revisions = vault.ReadHistory(_owner.Name);
        }
        catch (VaultException e)
        {
            Forget();
            _owner.Report(e.Message);
            return;
        }

        if (revisions is null)
        {
            var gone = Gone();
            Forget();
            _owner.Report(gone);
            return;
        }

        _revealed = null;
        _countAtRead = revisions.Count;
        _read = true;
        Selected = null;
        Rows = [.. revisions.Select(revision => new EntryRevisionRow(this, revision))];
        Raise(nameof(RevealedWhen));
    }

    /// <summary>Hands a row its password, and takes it away from whichever row had it.</summary>
    internal string? Reveal(EntryRevisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!ReferenceEquals(_revealed, row))
        {
            _revealed = row;
            Raise(nameof(RevealedWhen));
        }

        return Read(row);
    }

    /// <summary>Notes that a row's hold ended.</summary>
    internal void Conceal(EntryRevisionRow row)
    {
        if (ReferenceEquals(_revealed, row))
        {
            _revealed = null;
            Raise(nameof(RevealedWhen));
        }
    }

    /// <summary>
    /// Copies a revision's password with the countdown every other secret copy uses (D-0300).
    /// </summary>
    /// <remarks>
    /// Re-read and re-checked like a hold, so a list read before an edit elsewhere cannot put a
    /// different revision's password on the clipboard.
    /// </remarks>
    internal async Task Copy(EntryRevisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_session.Unlocked is null)
        {
            _owner.Report("That password could not be read. The vault may have locked.");
            return;
        }

        if (Read(row) is not { Length: > 0 } password)
        {
            _owner.Report(_stale);
            Load();
            return;
        }

        await _clipboard.CopyAsync(password, $"Password from {row.When}").ConfigureAwait(true);
    }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        IsOpen = false;
        Forget();
    }

    private void Toggle()
    {
        if (IsOpen)
        {
            IsOpen = false;
            Forget();
            return;
        }

        IsOpen = true;
        Load();
    }

    private void Restore()
    {
        if (_selected is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            _owner.Report("The vault is locked.");
            return;
        }

        // The revision's own title: restoring one from before a rename renames the entry back.
        var restored = new EntryName(_owner.GroupPath, row.Title);

        try
        {
            if (vault.ReadHistory(_owner.Name) is not { } fresh)
            {
                var gone = Gone();
                Forget();
                _owner.Report(gone);
                return;
            }

            if (!Agrees(fresh, row))
            {
                _owner.Report(_stale);
                Load();
                return;
            }

            if (!vault.RestoreRevision(_owner.Name, row.Index))
            {
                var gone = Gone();
                Forget();
                _owner.Report(gone);
                return;
            }

            vault.Save();
        }
        catch (ArgumentOutOfRangeException)
        {
            _owner.Report(_stale);
            Load();
            return;
        }
        catch (VaultChangedOnDiskException)
        {
            _owner.Report("Something else changed this vault since you opened it. Lock and unlock to see it, then restore this again.");
            return;
        }
        catch (VaultException e)
        {
            _owner.Report(e.Message);
            return;
        }

        _revealed = null;
        Selected = null;
        Raise(nameof(RevealedWhen));
        _owner.Report(null);

        // Last, because the screen answers a restored title by rebuilding this pane, which disposes
        // the object this call is running on.
        _restored(restored);
    }

    /// <summary>Reads one revision's password out of the open vault, for a hold.</summary>
    private string? Read(EntryRevisionRow row)
    {
        if (_session.Unlocked is not { } vault)
        {
            return null;
        }

        IReadOnlyList<EntryRevision>? fresh;
        try
        {
            fresh = vault.ReadHistory(_owner.Name);
        }
        catch (VaultException)
        {
            return null;
        }

        return fresh is not null && Agrees(fresh, row) ? fresh[row.Index].Fields.Password : null;
    }

    /// <summary>Whether a fresh reading still holds this row's revision at this row's index.</summary>
    private bool Agrees(IReadOnlyList<EntryRevision> fresh, EntryRevisionRow row) =>
        fresh.Count == _countAtRead
        && row.Index >= 0
        && row.Index < fresh.Count
        && fresh[row.Index].ModifiedUtc == row.ModifiedUtc
        && string.Equals(fresh[row.Index].Fields.Title, row.Title, StringComparison.Ordinal)
        && fresh[row.Index].Fields.Password.Length == row.MaskedLength;

    private string Gone() => $"'{_owner.DisplayPath}' is no longer in this vault.";

    private void Forget()
    {
        _revealed = null;
        _read = false;
        _countAtRead = 0;
        Selected = null;
        Rows = [];
        Raise(nameof(RevealedWhen));
    }
}
