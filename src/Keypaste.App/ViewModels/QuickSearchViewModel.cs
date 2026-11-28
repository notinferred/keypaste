using System.ComponentModel;
using Keypaste.App.Clipboard;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>One match in quick search: the item's title, username and vault, and never a value.</summary>
/// <param name="Row">The row Items' list would show for it.</param>
/// <param name="Vault">The title of the vault that holds it.</param>
internal sealed record QuickSearchRow(EntryRow Row, string Vault)
{
    /// <summary>The title, with what tells it from another item of the same title.</summary>
    internal string Title => Row.DisplayName;

    internal string Username => Row.DisplayUsername;

    internal bool HasUsername => Username.Length > 0;

    /// <summary>What a screen reader says of the row, which reads a list item by its content's text: what it draws, which holds no secret.</summary>
    public override string ToString() => string.Join(", ", new[] { Title, Username, Vault }.Where(part => part.Length > 0));
}

/// <summary>
/// Quick search: a search over every unlocked vault from any app, whose chosen item's password, username or one-time code is
/// copied by a key, after which the window closes and the app in use has the keyboard back.
/// </summary>
/// <remarks>
/// <b>Nothing here searches or copies.</b> The matches are Items' own (<see cref="EntriesViewModel"/>, which asks
/// <see cref="Vault.Search"/>), and each copy is the chosen item's own command on its pane (<see cref="EntryDetailViewModel"/>),
/// through the shell's <see cref="ClipboardCountdown"/>, so the rules and the clearing are the main window's.
/// </remarks>
internal sealed partial class QuickSearchViewModel : ObservableObject, IDisposable
{
    private readonly EntriesViewModel _entries;
    private readonly ClipboardCountdown _clipboard;
    private readonly Func<AppVaultSession, string> _vaultTitle;
    private readonly Action _done;
    private readonly Action<EntryName, AppVaultSession> _open;

    /// <param name="entries">Items' list over every unlocked vault, which this owns from here.</param>
    /// <param name="clipboard">The shell's clipboard, which the list's panes copy through.</param>
    /// <param name="vaultTitle">A vault's title, as its row names it.</param>
    /// <param name="done">Closes quick search and gives the keyboard back to the app in use.</param>
    /// <param name="open">Closes quick search and opens an item in the keypaste window.</param>
    internal QuickSearchViewModel(
        EntriesViewModel entries,
        ClipboardCountdown clipboard,
        Func<AppVaultSession, string> vaultTitle,
        Action done,
        Action<EntryName, AppVaultSession> open)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(vaultTitle);
        ArgumentNullException.ThrowIfNull(done);
        ArgumentNullException.ThrowIfNull(open);

        _entries = entries;
        _clipboard = clipboard;
        _vaultTitle = vaultTitle;
        _done = done;
        _open = open;
        _entries.PropertyChanged += OnEntriesChanged;

        CopyPasswordCommand = new AsyncRelayCommand(CopyPasswordAsync, () => Selected is not null);
        OpenInWindowCommand = new RelayCommand(OpenInWindow, () => Selected is not null);
    }

    /// <summary>Copy password, as Return does.</summary>
    internal AsyncRelayCommand CopyPasswordCommand { get; }

    /// <summary>Open in keypaste, as Ctrl/Cmd+Return does.</summary>
    internal RelayCommand OpenInWindowCommand { get; }

    /// <summary>Open in keypaste's tooltip, with its chord in this platform's spelling.</summary>
    public static string OpenTip => $"{Strings.OpenInKeypaste}, {(OperatingSystem.IsMacOS() ? "⌘↩" : "Ctrl+Enter")}";

    /// <summary>Copy password's tooltip: its key, and that the copy clears itself.</summary>
    public static string CopyTip => $"{Strings.CopyPassword}, {(OperatingSystem.IsMacOS() ? "↩" : "Enter")}; the clipboard clears itself";

    /// <summary>The other keys, which have no button, in this platform's spelling.</summary>
    public static string OtherKeys => $"{ShortcutText.ShiftChord("C")} username · {ShortcutText.Chord("T")} one-time code · Esc closes";

    /// <summary>What the person typed; each change chooses its first match, as Spotlight's top hit.</summary>
    internal string Search
    {
        get => _entries.Search;
        set
        {
            if (string.Equals(value, _entries.Search, StringComparison.Ordinal))
            {
                return;
            }

            _entries.Search = value;
            Message = null;

            if (Asks && _entries.Rows is [var first, ..])
            {
                _entries.Selected = first;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsHint));
            OnPropertyChanged(nameof(ShowsNoMatch));
            OnPropertyChanged(nameof(NoMatch));
        }
    }

    /// <summary>The matches, none until something is typed.</summary>
    internal IReadOnlyList<QuickSearchRow> Rows { get; private set; } = [];

    internal bool HasRows => Rows.Count > 0;

    /// <summary>The match the keys act on.</summary>
    internal QuickSearchRow? Selected
    {
        get => _entries.Selected is { } chosen ? Rows.FirstOrDefault(row => row.Row == chosen) : null;
        set
        {
            // The list writes null as its rows change, which is no choice.
            if (value is not null)
            {
                _entries.Selected = value.Row;
            }
        }
    }

    /// <summary>Why the last key copied nothing, kept on screen until the next search or copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string? Message { get; private set; }

    internal bool HasMessage => Message is not null;

    /// <summary>Whether nothing is typed yet, so the window says what it searches.</summary>
    internal bool ShowsHint => !Asks;

    internal bool ShowsNoMatch => Asks && Rows.Count == 0;

    /// <summary>What an empty result says, as Items' list says it.</summary>
    internal string NoMatch => _entries.ListEmptyNote;

    private bool Asks => _entries.Search.Trim().Length > 0;

    /// <summary>↓ or ↑: the next or previous match.</summary>
    internal void MoveChoice(int by) => _entries.MoveChoice(by);

    /// <summary>Return: copies the chosen item's password, cleared after the countdown, then hands the keyboard back.</summary>
    internal Task CopyPasswordAsync() => CopyAsync(detail => detail.CopyPasswordCommand, "This item has no password to copy.");

    /// <summary>Shift+Ctrl/Cmd+C: copies the chosen item's username, as the main window does, then hands the keyboard back.</summary>
    internal Task CopyUsernameAsync() => CopyAsync(detail => detail.CopyUsernameCommand, "This item has no username to copy.");

    /// <summary>Ctrl/Cmd+T: copies the chosen item's one-time code, cleared after the countdown, then hands the keyboard back.</summary>
    internal Task CopyOneTimeCodeAsync() => CopyAsync(detail => detail.Totp?.CopyCommand, "This item has no one-time code.");

    /// <summary>Ctrl/Cmd+Return, or Open in keypaste: the chosen item in the keypaste window.</summary>
    internal void OpenInWindow()
    {
        if (Asks && _entries.Selected is { Vault: { } vault } chosen)
        {
            _open(chosen.Name, vault);
        }
    }

    /// <summary>Escape: closes and hands the keyboard back, copying nothing.</summary>
    internal void Close() => _done();

    private async Task CopyAsync(Func<EntryDetailViewModel, IAsyncRelayCommand?> command, string missing)
    {
        if (!Asks || _entries.Rows.Count == 0)
        {
            return;
        }

        _entries.ChooseFirstMatch();
        Message = null;

        if (_entries.Detail is not { } detail)
        {
            Message = _entries.Error;
            return;
        }

        if (command(detail) is not { } copy || !copy.CanExecute(null))
        {
            Message = missing;
            return;
        }

        var landed = _clipboard.Landings;
        await copy.ExecuteAsync(null).ConfigureAwait(true);

        if (_clipboard.Landings > landed)
        {
            _done();
            return;
        }

        Message = _clipboard.Failure ?? _entries.Error ?? "That couldn't be copied. Try again.";
    }

    private void OnEntriesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntriesViewModel.Rows))
        {
            Rows = Asks ? [.. _entries.Rows.Select(row => new QuickSearchRow(row, row.Vault is { } vault ? _vaultTitle(vault) : string.Empty))] : [];
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(Selected));
            CopyPasswordCommand.NotifyCanExecuteChanged();
            OpenInWindowCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ShowsNoMatch));
            OnPropertyChanged(nameof(NoMatch));
        }
        else if (e.PropertyName == nameof(EntriesViewModel.Selected))
        {
            OnPropertyChanged(nameof(Selected));
            CopyPasswordCommand.NotifyCanExecuteChanged();
            OpenInWindowCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(EntriesViewModel.Error) && _entries.Error is { } error)
        {
            Message = error;
        }
    }

    public void Dispose()
    {
        _entries.PropertyChanged -= OnEntriesChanged;
        _entries.Dispose();
    }
}
