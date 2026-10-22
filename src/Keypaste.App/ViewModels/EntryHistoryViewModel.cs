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
        "This item changed since its history was read. The list has been refreshed — choose again.";

    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;
    private readonly EntryDetailViewModel _owner;
    private readonly Action<EntryName> _restored;

    private IReadOnlyList<EntryRevisionRow> _rows = [];
    private EntryRevisionRow? _selected;
    private EntryRevisionRow? _revealed;
    private RestoreQuestion? _asking;
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
        RestoreCommand = new RelayCommand(() => Restore(_selected, confirmed: null), () => _selected is not null);
        ConfirmRestoreCommand = new RelayCommand(ConfirmRestore, () => Asking is not null);
        CancelRestoreCommand = new RelayCommand(() => Asking = null, () => Asking is not null);

        DependsOn(nameof(ToggleLabel), nameof(IsOpen));
        DependsOn(nameof(ShowsEmptyNote), nameof(IsOpen), nameof(Rows));
        DependsOn(nameof(HasSelection), nameof(Selected));
        DependsOn(nameof(RevealedWhen), nameof(Revealed));
        DependsOn(nameof(IsConfirmingRestore), nameof(Asking));
        DependsOn(nameof(RestoreLines), nameof(Asking));
        DependsOn(RestoreCommand, nameof(Selected));
        DependsOn(ConfirmRestoreCommand, nameof(Asking));
        DependsOn(CancelRestoreCommand, nameof(Asking));
    }

    /// <summary>Whether the history section is showing.</summary>
    internal bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    /// <summary>What the toggle says.</summary>
    internal string ToggleLabel => IsOpen ? "Hide history" : "Show history";

    /// <summary>The revisions, newest first, in the order the vault answered.</summary>
    internal IReadOnlyList<EntryRevisionRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>The revision being compared with the current values, or null.</summary>
    internal EntryRevisionRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Asking = null;
            }
        }
    }

    /// <summary>Whether a revision is being compared.</summary>
    internal bool HasSelection => _selected is not null;

    /// <summary>
    /// Whether a restore waits for the person to confirm what it does to the projects the item is in, because the
    /// revision's project tags differ from the item's and a restore puts them back (D-0510).
    /// </summary>
    internal bool IsConfirmingRestore => _asking is not null;

    /// <summary>What the waiting restore does to the item's projects, in the sentences a tag change shows (D-0415); names only, never a value.</summary>
    internal IReadOnlyList<string> RestoreLines => _asking?.Lines ?? [];

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
    internal string RevealedWhen => Revealed?.When ?? string.Empty;

    private EntryRevisionRow? Revealed
    {
        get => _revealed;
        set => Set(ref _revealed, value);
    }

    private RestoreQuestion? Asking
    {
        get => _asking;
        set => Set(ref _asking, value);
    }

    /// <summary>Opens and closes the section.</summary>
    internal RelayCommand ToggleCommand { get; }

    /// <summary>Makes the selected revision the entry's current values, first asking when that changes the item's projects.</summary>
    internal RelayCommand RestoreCommand { get; }

    /// <summary>Restores the revision the waiting question is about.</summary>
    internal RelayCommand ConfirmRestoreCommand { get; }

    /// <summary>Drops the waiting question, restoring nothing.</summary>
    internal RelayCommand CancelRestoreCommand { get; }

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

        Revealed = null;
        _countAtRead = revisions.Count;
        _read = true;
        Selected = null;
        Rows = [.. revisions.Select(revision => new EntryRevisionRow(this, revision))];
    }

    /// <summary>Hands a row its password, and takes it away from whichever row had it.</summary>
    internal string? Reveal(EntryRevisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Revealed = row;
        return Read(row);
    }

    /// <summary>Notes that a row's hold ended.</summary>
    internal void Conceal(EntryRevisionRow row)
    {
        if (ReferenceEquals(Revealed, row))
        {
            Revealed = null;
        }
    }

    /// <summary>
    /// Copies a revision's password with the countdown every other secret copy uses (D-0300).
    /// </summary>
    /// <remarks>
    /// Re-read and re-checked like a hold, so a list read before an edit elsewhere cannot put a
    /// different revision's password on the clipboard. Its placeholders fill in from that revision's
    /// own fields, not the item's current ones (D-0465), and only while the revision at its position
    /// still holds the password just read, checked under the lock the fields are read under, so an
    /// edit saved between the two reads copies nothing.
    /// </remarks>
    internal async Task Copy(EntryRevisionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_session.Unlocked is not { } vault)
        {
            _owner.Report("That password could not be read. The vault may have locked.");
            return;
        }

        if (Read(row) is not { Length: > 0 } stored)
        {
            _owner.Report(_stale);
            Load();
            return;
        }

        PlaceholderFill? fill;
        try
        {
            fill = vault.FillPlaceholders(_owner.Name, row.Index, stored);
        }
        catch (VaultException e)
        {
            _owner.Report(e.Message);
            return;
        }

        if (fill is null)
        {
            _owner.Report(_stale);
            Load();
            return;
        }

        if (_owner.Leaving(fill, $"The password from {row.When}", "copied") is { } password)
        {
            await _clipboard.CopyAsync(password, $"Password from {row.When}").ConfigureAwait(true);
        }
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

    private void ConfirmRestore()
    {
        if (Asking is not { } asked)
        {
            return;
        }

        Asking = null;
        Restore(asked.Row, asked);
    }

    /// <summary>Restores a revision, or holds the question a change to the item's projects needs first.</summary>
    /// <param name="row">The revision.</param>
    /// <param name="confirmed">The question the person confirmed for it, or null when none was asked yet.</param>
    private void Restore(EntryRevisionRow? row, RestoreQuestion? confirmed)
    {
        if (row is null)
        {
            return;
        }

        // The revision's own title: restoring one from before a rename renames the entry back.
        var restored = new EntryName(_owner.GroupPath, row.Title);
        var write = _session.Write(vault => TryRestore(vault, row, confirmed), attempt => attempt.Outcome == RevisionRestore.Restored);

        if (write.Problem("restore this again") is { } problem)
        {
            _owner.Report(problem);
            return;
        }

        if (write.Value.Outcome == RevisionRestore.Asks)
        {
            _owner.Report(null);
            Asking = write.Value.Question;
            return;
        }

        if (write.Value.Outcome == RevisionRestore.Stale)
        {
            _owner.Report(_stale);
            Load();
            return;
        }

        if (write.Value.Outcome == RevisionRestore.Gone)
        {
            var gone = Gone();
            Forget();
            _owner.Report(gone);
            return;
        }

        Revealed = null;
        Selected = null;
        _owner.Report(null);

        // Last, because the screen answers a restored title by rebuilding this pane, which disposes
        // the object this call is running on.
        _restored(restored);
    }

    /// <summary>
    /// Restores the revision unless its project tags differ from the item's, which a restore puts back, and the person
    /// has not confirmed what that does with the item carrying the tags it carries now (D-0415, D-0510).
    /// </summary>
    private RestoreAttempt TryRestore(Vault vault, EntryRevisionRow row, RestoreQuestion? confirmed)
    {
        try
        {
            if (vault.ReadHistory(_owner.Name) is not { } fresh || vault.Tags(_owner.Name) is not { } held)
            {
                return new RestoreAttempt(RevisionRestore.Gone);
            }

            if (!Agrees(fresh, row))
            {
                return new RestoreAttempt(RevisionRestore.Stale);
            }

            if (confirmed is null || !confirmed.Held.SequenceEqual(held, StringComparer.Ordinal))
            {
                var changes = _session.PreviewRestore(vault, _owner.Name, fresh[row.Index]);

                if (changes.Count > 0)
                {
                    return new RestoreAttempt(RevisionRestore.Asks, new RestoreQuestion(row, held, [.. changes.SelectMany(change => change.Describe())]));
                }
            }

            return new RestoreAttempt(vault.RestoreRevision(_owner.Name, row.Index) ? RevisionRestore.Restored : RevisionRestore.Gone);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new RestoreAttempt(RevisionRestore.Stale);
        }
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
        Revealed = null;
        _read = false;
        _countAtRead = 0;
        Selected = null;
        Rows = [];
    }

    private enum RevisionRestore
    {
        Gone,
        Stale,
        Asks,
        Restored,
    }

    /// <summary>What one try at a restore came to, and the question it holds when it asks first.</summary>
    private readonly record struct RestoreAttempt(RevisionRestore Outcome, RestoreQuestion? Question = null);

    /// <summary>A restore waiting for the person, asked while the item carried <paramref name="Held"/>.</summary>
    /// <param name="Row">The revision to restore.</param>
    /// <param name="Held">The item's own tags when it was asked, so a confirmation answers only that change.</param>
    /// <param name="Lines">What the restore does to the item's projects.</param>
    private sealed record RestoreQuestion(EntryRevisionRow Row, IReadOnlyList<string> Held, IReadOnlyList<string> Lines);
}
