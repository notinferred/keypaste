using System.Globalization;
using System.Security.Cryptography;
using Keypaste.App.Clipboard;
using Keypaste.App.Globalization;
using Keypaste.App.Resources;
using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>Which records the Activity table holds.</summary>
internal enum LogFilter
{
    All,

    /// <summary>What agents and tokens asked for, and what came of it, approvals included; never the person's browser.</summary>
    Agents,

    /// <summary>What the person's paired browser asked for through keypaste's extension: pairings, fills and lookups.</summary>
    Browser,

    /// <summary>What the person did themselves: in their browser, shares, revoked links, bundles.</summary>
    You,

    /// <summary>Every refusal, whoever or whatever refused.</summary>
    Denied,
}

/// <summary>A filter, and the words the footer states it in, so a filtered table never reads as the whole log.</summary>
internal sealed record LogFilterOption(LogFilter Filter, string Words);

/// <summary>
/// The Activity screen: the audit log as a table, newest first in day sections, with the chain's verdict on request.
/// </summary>
/// <remarks>
/// <para>
/// <b>It needs no unlocked vault.</b> The audit log is machine state, plaintext by design, and <c>keypaste log</c>
/// reads it without a master password. The unlocked vault's checkpoint key, when it is given one, also checks the
/// checkpoints the vault's owner signed, as <c>keypaste log verify --checkpoints</c> does.
/// </para>
/// <para>
/// <b>The records are the core's.</b> <see cref="AuditHistory"/> reads them through <see cref="AuditReader"/> and
/// checks the chain with <see cref="AuditChainVerifier"/>, as <c>keypaste log</c> does. <see cref="AuditText"/> words
/// the file for a terminal; this screen states the same counts, marks and verdict in a window's layout, and never shows
/// a filtered table without saying how many records it hid.
/// </para>
/// <para>
/// <b>The table has no sort.</b> It runs newest first by each record's full time, a line later in the file first where
/// two times tie, so its day sections hold; the filters narrow it.
/// </para>
/// <para>
/// <b>The browser's lookups wait to be asked for.</b> A page's list of its logins by name releases no password and comes on
/// every page with a login form, so it would bury what agents asked for. Show browser lookups brings them back, the status
/// counts them while they are hidden, and a lookup the chain cannot vouch for is never hidden.
/// </para>
/// <para>
/// <b>The chain is checked on every load.</b> A row the chain cannot vouch for is marked, or a record somebody
/// inserted would read exactly like one keypaste wrote. The verdict waits to be asked for, and its limits behind Learn more.
/// </para>
/// </remarks>
internal sealed partial class LogViewModel : ObservableObject
{
    /// <summary>What an empty machine is told.</summary>
    internal static string NothingYet => Strings.NothingHasAskedKeypasteForAValueYet;

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly ClipboardCountdown? _clipboard;
    private readonly Func<AuditCheckpointKey?>? _checkpointKey;

    private AuditHistory _history = new(AuditReadKind.Missing, string.Empty);
    private IReadOnlyList<LogRow> _all = [];
    private List<LogRow> _rows = [];

    private ChainVerdict? _verdict;
    private string _status = string.Empty;
    private string _message = string.Empty;
    private bool _verdictShown;
    private bool _limitsShown;
    private LogFilterOption _filter;
    private bool _showLookups;
    private bool _hidesLookups;
    private bool _offersLookups;

    /// <param name="home">keypaste's home, whose <c>audit.jsonl</c> is read.</param>
    /// <param name="clock">The clock times are shown against.</param>
    /// <param name="clipboard">Where the chain's hash is copied.</param>
    /// <param name="connect">Opens Agents' Connect, the next step from an empty log.</param>
    /// <param name="table">How the table is sized, or null for its own widths.</param>
    /// <param name="checkpointKey">Reads the unlocked vault's checkpoint key for each check, or null when there is none.</param>
    internal LogViewModel(string? home, TimeProvider? clock = null, ClipboardCountdown? clipboard = null, Action? connect = null, TableState? table = null, Func<AuditCheckpointKey?>? checkpointKey = null)
    {
        ConnectCommand = new RelayCommand(() => connect?.Invoke(), () => connect is not null);
        Table = table ?? TableStates.NewLog();
        _path = KeypasteHome.AuditPath(home);
        _clock = clock ?? TimeProvider.System;
        _clipboard = clipboard;
        _checkpointKey = checkpointKey;
        _filter = Filters[0];

        VerifyCommand = new RelayCommand(() => ShowVerdict(true), () => _verdict is not null);
        HideVerdictCommand = new RelayCommand(() => ShowVerdict(false));
        CopyHashCommand = new RelayCommand(() => _ = CopyHashAsync(), () => _clipboard is not null && _verdict is { HasHash: true });

        Refresh();
    }

    /// <summary>How the table's columns are sized; the screen never sorts it.</summary>
    internal TableState Table { get; }

    public static string Subtitle => "Every agent request, token run and share. Kept on this machine, each line tied to the one before it, so an edit shows.";

    internal IReadOnlyList<LogFilterOption> Filters { get; } =
    [
        new(LogFilter.All, string.Empty),
        new(LogFilter.Agents, "agent and token requests only"),
        new(LogFilter.Browser, "your browser only"),
        new(LogFilter.You, "your own actions only"),
        new(LogFilter.Denied, "refused calls only"),
    ];

    internal LogFilterOption Filter
    {
        get => _filter;
        set
        {
            if (value is not null && SetProperty(ref _filter, value))
            {
                OnPropertyChanged(nameof(ShowAll));
                OnPropertyChanged(nameof(ShowYou));
                OnPropertyChanged(nameof(ShowAgents));
                OnPropertyChanged(nameof(ShowBrowser));
                OnPropertyChanged(nameof(ShowDenied));
                Apply();
            }
        }
    }

    /// <summary>The segments' checked states; checking one chooses it, and the unchecking that follows is ignored.</summary>
    internal bool ShowAll
    {
        get => _filter.Filter == LogFilter.All;
        set => Choose(value, LogFilter.All);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowAgents
    {
        get => _filter.Filter == LogFilter.Agents;
        set => Choose(value, LogFilter.Agents);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowBrowser
    {
        get => _filter.Filter == LogFilter.Browser;
        set => Choose(value, LogFilter.Browser);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowYou
    {
        get => _filter.Filter == LogFilter.You;
        set => Choose(value, LogFilter.You);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowDenied
    {
        get => _filter.Filter == LogFilter.Denied;
        set => Choose(value, LogFilter.Denied);
    }

    /// <summary>Whether the browser's lookups are listed; off each time the screen opens.</summary>
    internal bool ShowLookups
    {
        get => _showLookups;
        set
        {
            if (SetProperty(ref _showLookups, value))
            {
                Apply();
            }
        }
    }

    /// <summary>Whether the filter keeps a browser lookup that may be hidden, so Show browser lookups would change the table.</summary>
    internal bool OffersLookups => _offersLookups;

    /// <summary>The line opened to its detail, or null.</summary>
    [ObservableProperty]
    internal partial LogRow? Chosen { get; set; }

    /// <summary>The records the filter keeps, newest first.</summary>
    internal IReadOnlyList<LogRow> Rows => _rows;

    internal bool HasRows => _rows.Count > 0;

    /// <summary>Whether the table is drawn at all: the file was read and checked.</summary>
    internal bool HasTable => _history.Kind is AuditReadKind.Intact or AuditReadKind.Broken;

    internal bool FilterEmpty => HasTable && _rows.Count == 0;

    /// <summary>Whether a filter, or hiding the browser's lookups, hides every line, so widening it would show some.</summary>
    internal bool OffersShowAll => FilterEmpty && (_filter.Filter != LogFilter.All || _hidesLookups);

    [RelayCommand]
    private void ShowWholeLog()
    {
        ShowAll = true;
        ShowLookups = true;
    }

    /// <summary>Whether the log holds and how many records the table shows of how many the file holds, under which filter; empty with no table.</summary>
    internal string Status => _status;

    /// <summary>Whether the status says the log was edited.</summary>
    internal bool StatusIsEdited => IsBroken;

    /// <summary>The log file, in full: the status line's tooltip.</summary>
    internal string LogPath => _path;

    /// <summary>How many lines of the file are not records, said when there are any.</summary>
    internal string UnreadableNote => _history.Unreadable switch
    {
        0 => string.Empty,
        1 => Strings.OneLineOfThisLogCouldNotBeRead,
        var n => string.Create(CultureInfo.InvariantCulture, $"{n} lines of this log could not be read as records. Check log integrity says why."),
    };

    internal bool HasUnreadableNote => HasTable && _history.Unreadable > 0;

    /// <summary>What the hash chain says about the whole file, or null when the file was not checked.</summary>
    internal ChainVerdict? Verdict => _verdict;

    /// <summary>Whether the verdict has been asked for.</summary>
    internal bool VerdictShown => _verdictShown;

    /// <summary>Whether Learn more has opened the verdict's breaks and limits.</summary>
    internal bool LimitsShown => _limitsShown;

    [RelayCommand]
    private void LearnMore()
    {
        _limitsShown = !_limitsShown;
        OnPropertyChanged(nameof(LimitsShown));
    }

    /// <summary>One calm sentence about the file as a whole, or nothing: no log yet, unreadable, unchecked, or broken.</summary>
    internal string Message => _message;

    internal bool HasMessage => _message.Length > 0;

    /// <summary>Whether no log has been written yet, which the screen draws as its empty state rather than a notice.</summary>
    internal bool IsEmpty => _history.Kind == AuditReadKind.Missing;

    /// <summary>Connect an AI tool…, from an empty log.</summary>
    internal RelayCommand ConnectCommand { get; }

    internal bool OffersConnect => IsEmpty && ConnectCommand.CanExecute(null);

    /// <summary>Whether the message is drawn as a notice above the table.</summary>
    internal bool HasNotice => HasMessage && !IsEmpty;

    /// <summary>Whether the message is the chain being broken, which the screen marks as a warning.</summary>
    internal bool IsBroken => _history.Kind == AuditReadKind.Broken;

    /// <summary>Check log integrity: shows the chain's verdict.</summary>
    internal RelayCommand VerifyCommand { get; }

    internal RelayCommand HideVerdictCommand { get; }

    /// <summary>Copies the latest hash, which is not a secret, so it can be kept somewhere else.</summary>
    internal RelayCommand CopyHashCommand { get; }

    /// <summary>Re-reads the log from disk and re-checks it; the verdict folds away, because it described the last read.</summary>
    [RelayCommand]
    internal void Refresh()
    {
        var key = _checkpointKey?.Invoke();

        try
        {
            _history = AuditHistory.Read(_path, key);
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key.Key);
            }
        }

        _verdict = _history.Report is { } report ? ChainVerdict.From(report, _clock.LocalTimeZone) : null;
        _verdictShown = false;
        _limitsShown = false;
        _message = _history.Kind switch
        {
            AuditReadKind.Missing => NothingYet,
            AuditReadKind.Unreadable => $"That log couldn't be read: {_history.Error.TrimEnd('.')}. Check the file, then open this screen again.",
            AuditReadKind.Unchecked => "That log couldn't be checked, so nothing from it is shown here. Check the file, then open this screen again.",
            _ => string.Empty,
        };

        _all = NewestFirst([.. _history.Entries.Select(entry => LogRow.From(entry, !_history.Unverified.Contains(entry.Line), _clock, _history.Edited.Contains(entry.Line)))]);

        OnPropertyChanged(nameof(HasTable));
        OnPropertyChanged(nameof(Verdict));
        OnPropertyChanged(nameof(VerdictShown));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(LimitsShown));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(OffersConnect));
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(StatusIsEdited));
        OnPropertyChanged(nameof(UnreadableNote));
        OnPropertyChanged(nameof(FilterEmpty));
        OnPropertyChanged(nameof(OffersShowAll));
        OnPropertyChanged(nameof(HasUnreadableNote));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasNotice));
        VerifyCommand.NotifyCanExecuteChanged();
        CopyHashCommand.NotifyCanExecuteChanged();

        Apply();
    }

    /// <summary><c>8 records</c>, or <c>2 of 8 records · refused calls only</c> when a filter hides some.</summary>
    internal static string Tally(int shown, int total, string words)
    {
        var noun = total == 1 ? "record" : "records";

        return words.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{total} {noun}")
            : string.Create(CultureInfo.InvariantCulture, $"{shown} of {total} {noun} · {words}");
    }

    /// <summary>Dated rows newest first, a later line first on a tie, then the undated ones, a later line first.</summary>
    /// <remarks>A log read in file order is already oldest first unless a clock went back, so it is reversed rather than sorted.</remarks>
    private static List<LogRow> NewestFirst(List<LogRow> rows)
    {
        var dated = rows.Where(row => row.Source.At is not null).ToList();
        var undated = rows.Where(row => row.Source.At is null).ToList();

        if (!OldestFirst(dated) || !OldestFirst(undated))
        {
            return
            [
                .. rows
                    .OrderBy(row => row.Source.At is null)
                    .ThenByDescending(row => row.Source.At?.UtcTicks)
                    .ThenByDescending(row => row.Source.Line),
            ];
        }

        dated.Reverse();
        undated.Reverse();
        dated.AddRange(undated);

        return dated;
    }

    /// <summary>Whether each row is strictly after the one before it by time, then line, since reversing a tie would swap what the sort keeps.</summary>
    private static bool OldestFirst(List<LogRow> rows)
    {
        for (var i = 1; i < rows.Count; i++)
        {
            var before = (rows[i - 1].Source.At?.UtcTicks ?? 0, rows[i - 1].Source.Line);
            var after = (rows[i].Source.At?.UtcTicks ?? 0, rows[i].Source.Line);

            if (before.CompareTo(after) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string DayWords(DateTime? date, DateTime today) => date switch
    {
        null => Strings.LogDayUndated,
        { } day when day == today => Strings.LogDayToday,
        { } day when day == today.AddDays(-1) => Strings.LogDayYesterday,
        { } day => PersonTime.Day(day, today),
    };

    private void Apply()
    {
        Func<LogRow, bool> keep = _filter.Filter switch
        {
            LogFilter.Agents => row => !row.ByYou,
            LogFilter.Browser => row => row.ByBrowser,
            LogFilter.You => row => row.ByYou,
            LogFilter.Denied => row => row.Denied,
            _ => _ => true,
        };

        // A lookup the chain cannot vouch for is never hidden, or a line written into the log could pass as one unseen.
        static bool Hideable(LogRow row) => row.IsLookup && row.Verified;

        var kept = _all.Where(keep).ToList();
        _offersLookups = kept.Any(Hideable);
        _rows = Divide(_showLookups ? kept : [.. kept.Where(row => !Hideable(row))]);
        _hidesLookups = _rows.Count < kept.Count;
        var words = string.Join(" · ", new[] { _filter.Words, _hidesLookups ? "browser lookups hidden" : string.Empty }.Where(part => part.Length > 0));
        var tally = Tally(_rows.Count, _history.Total, words);
        _status = _history.Kind switch
        {
            AuditReadKind.Intact => $"Log intact · {tally}",
            AuditReadKind.Broken when _verdict?.EditedAt is { } at => $"Log edited at {at} · {tally}",
            AuditReadKind.Broken => $"Log edited · {tally}",
            _ => string.Empty,
        };

        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(OffersLookups));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(FilterEmpty));
        OnPropertyChanged(nameof(OffersShowAll));
    }

    /// <summary>Marks the first row of each day with its divider; a table of today alone needs none.</summary>
    private List<LogRow> Divide(List<LogRow> rows)
    {
        var today = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).Date;
        var days = rows.Select(row => row.Date).Distinct().Count();

        for (var i = 0; i < rows.Count; i++)
        {
            var date = rows[i].Date;
            var starts = i == 0 ? days > 1 || date != today : date != rows[i - 1].Date;

            if (starts)
            {
                rows[i] = rows[i] with { Day = DayWords(date, today) };
            }
        }

        return rows;
    }

    private async Task CopyHashAsync()
    {
        if (_clipboard is not null && _verdict is { HasHash: true } verdict)
        {
            await _clipboard.CopyPlainAsync(verdict.Hash, "Latest hash").ConfigureAwait(true);
        }
    }

    private void Choose(bool chosen, LogFilter filter)
    {
        if (chosen)
        {
            Filter = Filters.Single(option => option.Filter == filter);
        }
    }

    private void ShowVerdict(bool shown)
    {
        _verdictShown = shown;
        _limitsShown = false;
        OnPropertyChanged(nameof(VerdictShown));
        OnPropertyChanged(nameof(LimitsShown));
    }
}
