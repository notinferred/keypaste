using Avalonia.Collections;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Trash screen: what deleting put in the vault's recycle bin, the way back out, and the
/// second deliberate act that erases one or all of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Simpler than the history pane, because an identity does not go stale.</b>
/// <see cref="EntryHistoryViewModel"/> re-reads and checks a revision still sits at the index its
/// row was built from; a <see cref="RecycledEntryId"/> names one entry for as
/// long as that entry is in the bin, so a row whose entry has gone is answered by
/// <see cref="RestoreOutcome.NothingMatched"/> rather than by acting on the wrong one.
/// </para>
/// <para>
/// <b>Nothing here survives a lock</b>, by the shell's rule rather than by one of its own:
/// <see cref="ShellViewModel"/> disposes its content on every navigation and on every lock, and
/// <see cref="Dispose"/> empties the list, the selection, the confirmations, the offer and the outcome line.
/// The properties keep answering afterwards, because a getter that throws after a lock cannot be
/// told apart from a screen that held nothing, which is what <c>SecretHygieneTests</c> refuses.
/// </para>
/// </remarks>
internal sealed partial class TrashViewModel : ObservableObject, IDisposable
{
    internal const string RecycledNote =
        "Deleted items wait here until you delete them for good, and KeePassXC shows them as its Recycle Bin.";

    internal const string NoBinNote =
        "This vault's Trash is switched off in KeePassXC, so deleting removes an item and "
        + "its history for good. Nothing can arrive here until that setting is turned back on.";

    private readonly AppVaultSession _session;

    private TrashRow? _selected;
    private bool _sorting;

    /// <summary>The rows selected in the list, which Restore takes together when there are several.</summary>
    internal AvaloniaList<object> SelectedRows { get; } = [];

    internal TrashViewModel(AppVaultSession session, TableState? table = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Table = table ?? TableStates.NewTrash();
        Table.OrderChanged += OnOrderChanged;

        RestoreCommand = new RelayCommand(Restore, () => _selected is not null && !IsConfirming);
        SelectedRows.CollectionChanged += (_, _) =>
        {
            PurgeCommand?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasSeveralSelected));
            OnPropertyChanged(nameof(DetailTitle));
        };
        PurgeCommand = new RelayCommand(
            () => IsConfirmingPurge = true,
            () => SelectedRows.Count <= 1 && _selected is not null && !IsConfirming);
        ConfirmPurgeCommand = new RelayCommand(Purge, () => IsConfirmingPurge);
        CancelPurgeCommand = new RelayCommand(() => IsConfirmingPurge = false, () => IsConfirmingPurge);
        EmptyCommand = new RelayCommand(() => IsConfirmingEmpty = true, () => Rows.Count > 0 && !IsConfirming);
        ConfirmEmptyCommand = new RelayCommand(Empty, () => IsConfirmingEmpty);
        CancelEmptyCommand = new RelayCommand(() => IsConfirmingEmpty = false, () => IsConfirmingEmpty);

        Load();
    }

    /// <summary>How the list is sorted and sized.</summary>
    internal TableState Table { get; }

    /// <summary>What is in the bin, most recently deleted first until another order is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(EmptyPrompt), nameof(OffersChoice))]
    [NotifyCanExecuteChangedFor(nameof(EmptyCommand))]
    internal partial IReadOnlyList<TrashRow> Rows { get; private set; } = [];

    /// <summary>The row a restore or a permanent deletion would act on, or null.</summary>
    internal TrashRow? Selected
    {
        get => _selected;
        set
        {
            if (_sorting)
            {
                return;
            }

            // A confirmation is armed for one row; moving the selection while it shows would leave its button pointing at another entry.
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(OffersChoice));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(PurgePrompt));
                RestoreCommand.NotifyCanExecuteChanged();
                PurgeCommand.NotifyCanExecuteChanged();
                IsConfirmingPurge = false;
            }
        }
    }

    /// <summary>Whether there is nothing to recover.</summary>
    internal bool IsEmpty => Rows.Count == 0;

    /// <summary>Whether the bin holds entries but none is picked, so Restore and Delete wait on one.</summary>
    internal bool HasSelection => _selected is not null;

    /// <summary>Whether the detail invites a choice: something is in the bin and nothing is picked.</summary>
    internal bool OffersChoice => !IsEmpty && !HasSelection;

#pragma warning disable CA1822
    internal string ListTitle => Strings.Trash;
#pragma warning restore CA1822

    /// <summary>Whether more than one row is picked, which Restore takes together and Delete for good does not.</summary>
    internal bool HasSeveralSelected => SelectedRows.Count > 1;

    /// <summary>What the detail is headed with: the picked row's name, or how many are picked.</summary>
    internal string DetailTitle => SelectedRows.Count > 1 ? Invariant($"{SelectedRows.Count} items") : _selected?.DisplayTitle ?? string.Empty;

    /// <summary>The list's sorts: each of its columns, and which way the one in force runs.</summary>
    internal IReadOnlyList<SortChoice> SortChoices =>
    [
        .. Table.Columns.Select(column => new SortChoice(
            column.Key,
            column.Name,
            Table.HeadName(column.Key),
            Table.Order is { AsRowsArrive: false } order && order.Column == column.Key ? (order.Descending ? "arrow-down" : "arrow-up") : null)),
    ];

    /// <summary>A sort's choice from the list's header: its column, and again to reverse.</summary>
    [RelayCommand]
    private void SortBy(string column) => Table.Press(column);

    /// <summary>The way back from the detail that stands in a narrow list's place.</summary>
    [RelayCommand]
    private void CloseDetail()
    {
        SelectedRows.Clear();
        Selected = null;
    }

    /// <summary>Whether this vault recycles a deleted entry at all.</summary>
    internal bool RecyclesDeletedEntries => _session.Unlocked?.RecyclesDeletedEntries == true;

    /// <summary>What the screen says about this vault's bin, above the list.</summary>
    internal string Note => RecyclesDeletedEntries ? RecycledNote : NoBinNote;

    /// <summary>What the screen says when the bin holds nothing.</summary>
    internal string EmptyNote => RecyclesDeletedEntries
        ? "Nothing has been deleted from this vault, or what was has already been erased."
        : "Nothing can be recovered from this vault.";

    /// <summary>Whether the permanent deletion is waiting to be confirmed.</summary>
    /// <remarks>
    /// The second of the two deliberate acts, and with <see cref="IsConfirmingEmpty"/> where
    /// <c>KpDanger</c> appears on this screen: a restore is ordinary, and erasing a value is not.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand), nameof(PurgeCommand), nameof(ConfirmPurgeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPurgeCommand), nameof(EmptyCommand))]
    internal partial bool IsConfirmingPurge { get; private set; }

    /// <summary>What that confirmation asks.</summary>
    internal string PurgePrompt => _selected is { } row
        ? $"Delete {row.DisplayTitle} for good? This one cannot be undone."
        : string.Empty;

    /// <summary>Whether emptying the whole bin is waiting to be confirmed.</summary>
    /// <remarks>The same second act as <see cref="IsConfirmingPurge"/>, for every row at once.</remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand), nameof(PurgeCommand), nameof(EmptyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmEmptyCommand), nameof(CancelEmptyCommand))]
    internal partial bool IsConfirmingEmpty { get; private set; }

    /// <summary>What that confirmation asks, naming how many items go.</summary>
    internal string EmptyPrompt => Rows.Count switch
    {
        0 => string.Empty,
        1 => "Delete the one item in the Trash and its history for good? This cannot be undone.",
        var count => Invariant($"Delete all {count} items in the Trash and their history for good? This cannot be undone."),
    };

    /// <summary>What the last action did, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    internal partial string? Notice { get; private set; }

    internal bool HasNotice => Notice is not null;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    internal partial string? Error { get; private set; }

    internal bool HasError => Error is not null;

    /// <summary>Puts the selected entry back where it was deleted from.</summary>
    internal RelayCommand RestoreCommand { get; }

    /// <summary>Puts every selected entry back in one save; one that cannot go back stays in the bin and is named.</summary>
    private void RestoreSeveral(IReadOnlyList<TrashRow> rows)
    {
        var write = _session.Write(
            vault => rows.Where(row => vault.RestoreRecycled(row.Id) is not (RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot)).ToList(),
            left => left.Count < rows.Count);

        if (write.Problem("restore these again") is { } problem)
        {
            Report(null, problem);
            return;
        }

        var left = write.Value ?? [];
        var back = rows.Count - left.Count;

        Report(
            back == 0 ? null : back == 1 ? "1 item is back." : Invariant($"{back} items are back."),
            left.Count == 0 ? null : $"Not restored: {string.Join(", ", left.Select(row => row.DisplayTitle))}. Restore each one alone to see why.");
        SelectedRows.Clear();
        Load();
    }

    /// <summary>Asks for confirmation. It deletes nothing.</summary>
    internal RelayCommand PurgeCommand { get; }

    /// <summary>Erases the selected entry, having been confirmed.</summary>
    internal RelayCommand ConfirmPurgeCommand { get; }

    internal RelayCommand CancelPurgeCommand { get; }

    /// <summary>Asks for confirmation to empty the whole bin. It deletes nothing.</summary>
    internal RelayCommand EmptyCommand { get; }

    /// <summary>Erases everything in the bin, having been confirmed.</summary>
    internal RelayCommand ConfirmEmptyCommand { get; }

    internal RelayCommand CancelEmptyCommand { get; }

    /// <summary>Whether either permanent deletion is waiting to be confirmed, which holds every other action.</summary>
    private bool IsConfirming => IsConfirmingPurge || IsConfirmingEmpty;

    /// <summary>Reads the bin again, keeping the selection if its entry is still in there.</summary>
    internal void Load()
    {
        if (_session.Unlocked is not { } vault)
        {
            Rows = [];
            Selected = null;
            return;
        }

        var wanted = _selected?.Id;

        Rows = [.. Arranged(vault.ReadRecycled().Select(recycled => new TrashRow(recycled)))];

        Selected = wanted is { } id ? Rows.FirstOrDefault(row => row.Id == id) : null;

        OnPropertyChanged(nameof(RecyclesDeletedEntries));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(EmptyNote));
    }

    private IEnumerable<TrashRow> Arranged(IEnumerable<TrashRow> rows) => TableSorting.Sorted(
        rows,
        Table.Order,
        static (row, column) => column switch
        {
            TableStates.Title => new SortValue(row.DisplayTitle),
            TableStates.DeletedFrom => new SortValue(row.Where),
            _ => SortValue.At(row.DeletedUtc.Ticks),
        },
        static (left, right) =>
        {
            var compared = right.DeletedUtc.CompareTo(left.DeletedUtc);
            return compared != 0 ? compared : string.CompareOrdinal(left.Title, right.Title);
        });

    /// <summary>A sort moves rows and nothing else: the list lets go of its selection as its rows change, and takes it back, so a confirmation armed for a row stays armed for it.</summary>
    private void OnOrderChanged()
    {
        var chosen = SelectedRows.ToList();

        _sorting = true;
        Rows = [.. Arranged(Rows)];
        SelectedRows.Clear();
        SelectedRows.AddRange(chosen);
        _sorting = false;

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(SortChoices));
    }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        Table.OrderChanged -= OnOrderChanged;
        Rows = [];
        Selected = null;
        IsConfirmingPurge = false;
        IsConfirmingEmpty = false;
        Notice = null;
        Error = null;
    }

    private void Restore()
    {
        if (SelectedRows.Count > 1)
        {
            RestoreSeveral([.. SelectedRows.OfType<TrashRow>()]);
            return;
        }

        if (_selected is not { } row)
        {
            return;
        }

        var write = _session.Write(
            vault => vault.RestoreRecycled(row.Id),
            outcome => outcome is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot);

        if (write.Problem("restore this again") is { } problem)
        {
            Report(null, problem);
            return;
        }

        switch (write.Value)
        {
            case RestoreOutcome.Restored:
                Report($"{row.DisplayTitle} is back in {row.Where}.", null);
                break;

            case RestoreOutcome.RestoredToRoot:
                Report($"{row.DisplayTitle} is back at the top level, because the group it came from is gone.", null);
                break;

            case RestoreOutcome.DestinationOccupied:
                Report(null, $"Something else is called {row.DisplayTitle} in {row.Where} now, so this cannot go back under its own name. Rename or delete that one first.");
                break;

            default:
                Report(null, $"{row.DisplayTitle} is not in the Trash any more. Check the list.");
                break;
        }

        Load();
    }

    private void Purge()
    {
        if (_selected is not { } row)
        {
            return;
        }

        var write = _session.Write(vault => vault.PurgeRecycled(row.Id), removed => removed);

        if (write.Problem("delete this for good again") is { } problem)
        {
            Report(null, problem);
            return;
        }

        if (write.Value)
        {
            Report($"{row.DisplayTitle} and its history are gone.", null);
        }
        else
        {
            Report(null, $"{row.DisplayTitle} is not in the Trash any more. Check the list.");
        }

        IsConfirmingPurge = false;
        Load();
    }

    /// <summary>Erases everything in the bin, having been confirmed with how many items go.</summary>
    /// <remarks>
    /// Only the items the confirmation counted: one a terminal's <c>keypaste rm</c> recycled through this app since the
    /// list was read was never agreed to, so the bin is emptied only while it holds exactly the rows shown.
    /// </remarks>
    private void Empty()
    {
        var shown = Rows.Select(row => row.Id).ToHashSet();
        var write = _session.Write(
            vault => shown.SetEquals(vault.ReadRecycled().Select(recycled => recycled.Id)) ? vault.EmptyRecycleBin() : (int?)null,
            removed => removed > 0);

        if (write.Problem("empty the Trash again") is { } problem)
        {
            Report(null, problem);
            return;
        }

        switch (write.Value)
        {
            case null:
                Report(null, "The Trash changed after you asked, so nothing was deleted. Check what is in it and empty it again.");
                break;

            case 1:
                Report("The Trash is empty. The item in it and its history are gone.", null);
                break;

            case var removed:
                Report(Invariant($"The Trash is empty. {removed} items and their history are gone."), null);
                break;
        }

        IsConfirmingEmpty = false;
        Load();
    }

    private void Report(string? notice, string? error)
    {
        Notice = notice;
        Error = error;
    }
}
