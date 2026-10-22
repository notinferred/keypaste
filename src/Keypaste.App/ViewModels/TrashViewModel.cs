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
/// row was built from (DECISIONS.md D-0229); a <see cref="RecycledEntryId"/> names one entry for as
/// long as that entry is in the bin (D-0250), so a row whose entry has gone is answered by
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
internal sealed class TrashViewModel : ObservableObject, IDisposable
{
    internal const string RecycledNote =
        "Deleting an item in keypaste moves it here, to the vault's own recycle bin. KeePassXC "
        + "opens the same file and shows the same bin.";

    internal const string NoBinNote =
        "This vault's recycle bin is switched off in KeePassXC, so deleting removes an item and "
        + "its history for good. Nothing can arrive here until that setting is turned back on.";

    /// <summary>What recycling anything costs the file's other readers (DECISIONS.md D-0247).</summary>
    internal const string ReaderFloorNote =
        "A vault that has recycled anything is saved in the KeePass database format 4.1, which KeePassXC 2.7 and "
        + "KeePass 2.48 and later read. Older readers do not.";

    private readonly AppVaultSession _session;

    private IReadOnlyList<TrashRow> _rows = [];
    private TrashRow? _selected;
    private bool _isConfirmingPurge;
    private bool _isConfirmingEmpty;
    private string? _restoreAs;
    private string? _notice;
    private string? _error;

    internal TrashViewModel(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        RestoreCommand = new RelayCommand(Restore, () => _selected is not null && !IsConfirming);
        RestoreAsCommand = new RelayCommand(RestoreAs, () => _selected is not null && _restoreAs is not null && !IsConfirming);
        PurgeCommand = new RelayCommand(
            () => IsConfirmingPurge = true,
            () => _selected is not null && !IsConfirming);
        ConfirmPurgeCommand = new RelayCommand(Purge, () => IsConfirmingPurge);
        CancelPurgeCommand = new RelayCommand(() => IsConfirmingPurge = false, () => IsConfirmingPurge);
        EmptyCommand = new RelayCommand(() => IsConfirmingEmpty = true, () => _rows.Count > 0 && !IsConfirming);
        ConfirmEmptyCommand = new RelayCommand(Empty, () => IsConfirmingEmpty);
        CancelEmptyCommand = new RelayCommand(() => IsConfirmingEmpty = false, () => IsConfirmingEmpty);

        DependsOn(nameof(IsEmpty), nameof(Rows));
        DependsOn(nameof(AwaitsSelection), nameof(Rows), nameof(Selected));
        DependsOn(nameof(PurgePrompt), nameof(Selected));
        DependsOn(nameof(EmptyPrompt), nameof(Rows));
        DependsOn(nameof(OffersRestoreAs), nameof(RestoreAsTitle));
        DependsOn(nameof(RestoreAsLabel), nameof(RestoreAsTitle));
        DependsOn(nameof(Note), nameof(RecyclesDeletedEntries));
        DependsOn(nameof(EmptyNote), nameof(RecyclesDeletedEntries));
        DependsOn(nameof(HasNotice), nameof(Notice));
        DependsOn(nameof(HasError), nameof(Error));
        DependsOn(RestoreCommand, nameof(Selected), nameof(IsConfirmingPurge), nameof(IsConfirmingEmpty));
        DependsOn(RestoreAsCommand, nameof(Selected), nameof(RestoreAsTitle), nameof(IsConfirmingPurge), nameof(IsConfirmingEmpty));
        DependsOn(PurgeCommand, nameof(Selected), nameof(IsConfirmingPurge), nameof(IsConfirmingEmpty));
        DependsOn(ConfirmPurgeCommand, nameof(IsConfirmingPurge));
        DependsOn(CancelPurgeCommand, nameof(IsConfirmingPurge));
        DependsOn(EmptyCommand, nameof(Rows), nameof(IsConfirmingPurge), nameof(IsConfirmingEmpty));
        DependsOn(ConfirmEmptyCommand, nameof(IsConfirmingEmpty));
        DependsOn(CancelEmptyCommand, nameof(IsConfirmingEmpty));

        Load();
    }

    /// <summary>What is in the bin, most recently deleted first.</summary>
    internal IReadOnlyList<TrashRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>The row a restore or a permanent deletion would act on, or null.</summary>
    internal TrashRow? Selected
    {
        get => _selected;
        set
        {
            // A confirmation is armed for one row, and so is an offer of another title. Moving the
            // selection while either is showing would leave its button pointing at another entry.
            if (Set(ref _selected, value))
            {
                IsConfirmingPurge = false;

                // The refusal beside an offer names its button, so it goes with it.
                if (RestoreAsTitle is not null)
                {
                    RestoreAsTitle = null;
                    Error = null;
                }
            }
        }
    }

    /// <summary>Whether there is nothing to recover.</summary>
    internal bool IsEmpty => _rows.Count == 0;

    /// <summary>Whether the bin holds entries but none is picked, so Restore and Delete wait on one.</summary>
    internal bool AwaitsSelection => _rows.Count > 0 && _selected is null;

    /// <summary>Whether this vault recycles a deleted entry at all.</summary>
    internal bool RecyclesDeletedEntries => _session.Unlocked?.RecyclesDeletedEntries == true;

    /// <summary>What the screen says about this vault's bin, above the list.</summary>
    internal string Note => RecyclesDeletedEntries ? RecycledNote : NoBinNote;

    /// <summary>What the screen says when the bin holds nothing.</summary>
    internal string EmptyNote => RecyclesDeletedEntries
        ? "Nothing has been deleted from this vault, or what was has already been erased."
        : "Nothing can be recovered from this vault.";

    /// <summary>The reader floor recycling costs the file.</summary>
    /// <remarks>
    /// An instance property over the constant, because a binding needs one — the same reason
    /// <see cref="ShellViewModel.Destinations_"/> gives.
    /// </remarks>
#pragma warning disable CA1822
    internal string ReaderFloor => ReaderFloorNote;
#pragma warning restore CA1822

    /// <summary>Whether the permanent deletion is waiting to be confirmed.</summary>
    /// <remarks>
    /// The second of the two deliberate acts, and with <see cref="IsConfirmingEmpty"/> where
    /// <c>KpDanger</c> appears on this screen: a restore is ordinary, and erasing a value is not.
    /// </remarks>
    internal bool IsConfirmingPurge
    {
        get => _isConfirmingPurge;
        private set => Set(ref _isConfirmingPurge, value);
    }

    /// <summary>What that confirmation asks.</summary>
    internal string PurgePrompt => _selected is { } row
        ? $"Delete {row.DisplayTitle} for good? This one cannot be undone."
        : string.Empty;

    /// <summary>Whether emptying the whole bin is waiting to be confirmed.</summary>
    /// <remarks>The same second act as <see cref="IsConfirmingPurge"/>, for every row at once.</remarks>
    internal bool IsConfirmingEmpty
    {
        get => _isConfirmingEmpty;
        private set => Set(ref _isConfirmingEmpty, value);
    }

    /// <summary>What that confirmation asks, naming how many items go.</summary>
    internal string EmptyPrompt => _rows.Count switch
    {
        0 => string.Empty,
        1 => "Delete the one item in the trash and its history for good? This cannot be undone.",
        var count => $"Delete all {count} items in the trash and their history for good? This cannot be undone.",
    };

    /// <summary>The title a restore refused for a taken name can put the selected entry back under, or null.</summary>
    internal string? RestoreAsTitle
    {
        get => _restoreAs;
        private set => Set(ref _restoreAs, value);
    }

    internal bool OffersRestoreAs => _restoreAs is not null;

    /// <summary>The offer's button: <c>Restore as production (restored)</c>.</summary>
    internal string RestoreAsLabel => _restoreAs is { } title ? $"Restore as {title}" : string.Empty;

    /// <summary>What the last action did, or null.</summary>
    internal string? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    internal bool HasNotice => _notice is not null;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    internal string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    internal bool HasError => _error is not null;

    /// <summary>Puts the selected entry back where it was deleted from.</summary>
    internal RelayCommand RestoreCommand { get; }

    /// <summary>Puts the selected entry back under the first free <c>(restored)</c> title, its own being taken.</summary>
    internal RelayCommand RestoreAsCommand { get; }

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
    private bool IsConfirming => _isConfirmingPurge || _isConfirmingEmpty;

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

        Rows =
        [
            .. vault.ReadRecycled()
                .OrderByDescending(recycled => recycled.DeletedUtc)
                .ThenBy(recycled => recycled.Title, StringComparer.Ordinal)
                .Select(recycled => new TrashRow(recycled))
        ];

        Selected = wanted is { } id ? Rows.FirstOrDefault(row => row.Id == id) : null;

        Raise(nameof(RecyclesDeletedEntries));
    }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        Rows = [];
        Selected = null;
        IsConfirmingPurge = false;
        IsConfirmingEmpty = false;
        RestoreAsTitle = null;
        Notice = null;
        Error = null;
    }

    private void Restore()
    {
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

        string? offer = null;

        switch (write.Value)
        {
            case RestoreOutcome.Restored:
                Report($"{row.DisplayTitle} is back in {row.Where}.", null);
                break;

            case RestoreOutcome.RestoredToRoot:
                Report($"{row.DisplayTitle} is back at the root, because the group it came from is gone.", null);
                break;

            case RestoreOutcome.DestinationOccupied:
                // Core refused the restore whole (D-0249); another title is offered when the entry can
                // take one, and taken only on a second press (D-0512).
                offer = _session.Unlocked?.RenamedRestoreName(row.Id) is { } free
                    ? EntryNameSanitizer.Sanitize(free.Title).Text
                    : null;
                Report(
                    null,
                    offer is null
                        ? $"Something else is called {row.DisplayTitle} in {row.Where} now, so this cannot go back under its own name. Rename or delete that one first."
                        : $"Something else is called {row.DisplayTitle} in {row.Where} now, so this cannot go back under its own name. Restore it as {offer}, or rename or delete that one first.");
                break;

            default:
                Report(null, $"{row.DisplayTitle} is not in the trash any more.");
                break;
        }

        Load();

        // Load's fresh rows reset the selection, which drops any offer, so it is made once they are in place.
        if (offer is not null && _selected?.Id == row.Id)
        {
            RestoreAsTitle = offer;
        }
    }

    /// <summary>Puts the selected entry back under the first free <c>(restored)</c> title, its own being taken.</summary>
    private void RestoreAs()
    {
        if (_selected is not { } row || _restoreAs is null)
        {
            return;
        }

        var write = _session.Write(
            vault => (Outcome: vault.RestoreRecycledRenamed(row.Id, out var name), Name: name),
            result => result.Outcome is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot);

        if (write.Problem("restore this again") is { } problem)
        {
            Report(null, problem);
            return;
        }

        var title = write.Value.Name is { } restored ? EntryNameSanitizer.Sanitize(restored.Title).Text : string.Empty;

        switch (write.Value.Outcome)
        {
            case RestoreOutcome.Restored:
                Report($"{row.DisplayTitle} is back in {row.Where} as {title}.", null);
                break;

            case RestoreOutcome.RestoredToRoot:
                Report($"{row.DisplayTitle} is back at the root as {title}, because the group it came from is gone.", null);
                break;

            case RestoreOutcome.DestinationOccupied:
                Report(null, $"Something else is called {row.DisplayTitle} in {row.Where} now, and this cannot take another name. Rename or delete that one first.");
                break;

            default:
                Report(null, $"{row.DisplayTitle} is not in the trash any more.");
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
            Report(null, $"{row.DisplayTitle} is not in the trash any more.");
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
        var shown = _rows.Select(row => row.Id).ToHashSet();
        var write = _session.Write(
            vault => shown.SetEquals(vault.ReadRecycled().Select(recycled => recycled.Id)) ? vault.EmptyRecycleBin() : (int?)null,
            removed => removed > 0);

        if (write.Problem("empty the trash again") is { } problem)
        {
            Report(null, problem);
            return;
        }

        switch (write.Value)
        {
            case null:
                Report(null, "The trash changed after you asked, so nothing was deleted. Check what is in it and empty it again.");
                break;

            case 1:
                Report("The trash is empty. The item in it and its history are gone.", null);
                break;

            case var removed:
                Report($"The trash is empty. {removed} items and their history are gone.", null);
                break;
        }

        IsConfirmingEmpty = false;
        Load();
    }

    private void Report(string? notice, string? error)
    {
        Notice = notice;
        Error = error;
        RestoreAsTitle = null;
    }
}
