using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Trash screen: what deleting put in the vault's recycle bin, the way back out, and the
/// second deliberate act that erases one.
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
/// <see cref="Dispose"/> empties the list, the selection, the confirmation and the outcome line.
/// The properties keep answering afterwards, because a getter that throws after a lock cannot be
/// told apart from a screen that held nothing, which is what <c>SecretHygieneTests</c> refuses.
/// </para>
/// </remarks>
internal sealed class TrashViewModel : ObservableObject, IDisposable
{
    internal const string RecycledNote =
        "Deleting an entry in keypaste moves it here, to the vault's own recycle bin. KeePassXC "
        + "opens the same file and shows the same bin.";

    internal const string NoBinNote =
        "This vault's recycle bin is switched off in KeePassXC, so deleting removes an entry and "
        + "its history for good. Nothing can arrive here until that setting is turned back on.";

    /// <summary>What recycling anything costs the file's other readers (DECISIONS.md D-0247).</summary>
    internal const string ReaderFloorNote =
        "A vault that has recycled anything is written as KDBX 4.1, which KeePassXC 2.7 and "
        + "KeePass 2.48 and later read. Older readers do not.";

    private readonly AppVaultSession _session;

    private IReadOnlyList<TrashRow> _rows = [];
    private TrashRow? _selected;
    private bool _isConfirmingPurge;
    private string? _notice;
    private string? _error;

    internal TrashViewModel(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        RestoreCommand = new RelayCommand(Restore, () => _selected is not null && !IsConfirmingPurge);
        PurgeCommand = new RelayCommand(
            () => IsConfirmingPurge = true,
            () => _selected is not null && !IsConfirmingPurge);
        ConfirmPurgeCommand = new RelayCommand(Purge, () => IsConfirmingPurge);
        CancelPurgeCommand = new RelayCommand(() => IsConfirmingPurge = false, () => IsConfirmingPurge);

        Load();
    }

    /// <summary>What is in the bin, most recently deleted first.</summary>
    internal IReadOnlyList<TrashRow> Rows
    {
        get => _rows;
        private set
        {
            if (Set(ref _rows, value))
            {
                Raise(nameof(IsEmpty));
            }
        }
    }

    /// <summary>The row a restore or a permanent deletion would act on, or null.</summary>
    internal TrashRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
            {
                return;
            }

            // A confirmation is armed for one row. Moving the selection while it is showing would
            // leave the button pointing at an entry nobody agreed to erase.
            IsConfirmingPurge = false;
            Raise(nameof(PurgePrompt));
            RestoreCommand.RaiseCanExecuteChanged();
            PurgeCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether there is nothing to recover.</summary>
    internal bool IsEmpty => _rows.Count == 0;

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
    /// The second of the two deliberate acts, and the one place <c>KpDanger</c> appears on this
    /// screen: a restore is ordinary, and erasing a value is not.
    /// </remarks>
    internal bool IsConfirmingPurge
    {
        get => _isConfirmingPurge;
        private set
        {
            if (Set(ref _isConfirmingPurge, value))
            {
                RestoreCommand.RaiseCanExecuteChanged();
                PurgeCommand.RaiseCanExecuteChanged();
                ConfirmPurgeCommand.RaiseCanExecuteChanged();
                CancelPurgeCommand.RaiseCanExecuteChanged();
                Raise(nameof(PurgePrompt));
            }
        }
    }

    /// <summary>What that confirmation asks.</summary>
    internal string PurgePrompt => _selected is { } row
        ? $"Delete {row.DisplayTitle} for good? This one cannot be undone."
        : string.Empty;

    /// <summary>What the last action did, or null.</summary>
    internal string? Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
            {
                Raise(nameof(HasNotice));
            }
        }
    }

    internal bool HasNotice => _notice is not null;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    internal string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    internal bool HasError => _error is not null;

    /// <summary>Puts the selected entry back where it was deleted from.</summary>
    internal RelayCommand RestoreCommand { get; }

    /// <summary>Asks for confirmation. It deletes nothing.</summary>
    internal RelayCommand PurgeCommand { get; }

    /// <summary>Erases the selected entry, having been confirmed.</summary>
    internal RelayCommand ConfirmPurgeCommand { get; }

    internal RelayCommand CancelPurgeCommand { get; }

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
        Raise(nameof(Note));
        Raise(nameof(EmptyNote));
    }

    /// <summary>Nothing read out of the vault outlives this.</summary>
    public void Dispose()
    {
        Rows = [];
        Selected = null;
        IsConfirmingPurge = false;
        Notice = null;
        Error = null;
    }

    private void Restore()
    {
        if (_selected is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            Report(null, "The vault is locked.");
            return;
        }

        RestoreOutcome outcome;

        try
        {
            outcome = vault.RestoreRecycled(row.Id);

            if (outcome is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot)
            {
                vault.Save();
            }
        }
        catch (VaultChangedOnDiskException)
        {
            Report(null, "Something else changed this vault since you opened it. Lock and unlock to see it, then restore this again.");
            return;
        }
        catch (VaultException e)
        {
            Report(null, e.Message);
            return;
        }

        switch (outcome)
        {
            case RestoreOutcome.Restored:
                Report($"{row.DisplayTitle} is back in {row.Where}.", null);
                break;

            case RestoreOutcome.RestoredToRoot:
                Report($"{row.DisplayTitle} is back at the root, because the group it came from is gone.", null);
                break;

            case RestoreOutcome.DestinationOccupied:
                // Core refused the restore whole rather than inventing a destination for it
                // (D-0249), and the app cannot rename anything until V.5b, so this names the
                // route that is open today.
                Report(null, $"Something else is called {row.DisplayTitle} in {row.Where} now, so this cannot go back yet. Rename or delete that one first, in KeePassXC until the app can rename.");
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

        if (_session.Unlocked is not { } vault)
        {
            Report(null, "The vault is locked.");
            return;
        }

        bool removed;

        try
        {
            removed = vault.PurgeRecycled(row.Id);

            if (removed)
            {
                vault.Save();
            }
        }
        catch (VaultChangedOnDiskException)
        {
            Report(null, "Something else changed this vault since you opened it. Lock and unlock to see it, then delete this for good again.");
            return;
        }
        catch (VaultException e)
        {
            Report(null, e.Message);
            return;
        }

        if (removed)
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

    private void Report(string? notice, string? error)
    {
        Notice = notice;
        Error = error;
    }
}
