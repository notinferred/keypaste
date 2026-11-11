using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Globalization;
using Keypaste.App.Resources;
using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>Which records the Activity table holds.</summary>
internal enum LogFilter
{
    All,

    /// <summary>What agents and tokens asked for, and what came of it, approvals included.</summary>
    Agents,

    /// <summary>What the person did themselves: shares, revoked links, bundles.</summary>
    You,

    /// <summary>Every refusal, whoever or whatever refused.</summary>
    Denied,
}

/// <summary>A filter, and the words the footer states it in, so a filtered table never reads as the whole log.</summary>
internal sealed record LogFilterOption(LogFilter Filter, string Words);

/// <summary>
/// The Activity screen: the audit log as a table, newest first, with the chain's verdict on request.
/// </summary>
/// <remarks>
/// <para>
/// <b>It needs no unlocked vault.</b> The audit log is machine state, plaintext by design, and <c>keypaste log</c>
/// reads it without a master password.
/// </para>
/// <para>
/// <b>The records are the core's.</b> <see cref="AuditHistory"/> reads them through <see cref="AuditReader"/> and
/// checks the chain with <see cref="AuditChainVerifier"/>, as <c>keypaste log</c> does. <see cref="AuditText"/> words
/// the file for a terminal; this screen states the same counts, marks and verdict in a window's layout, and never shows
/// a filtered table without saying how many records it hid.
/// </para>
/// <para>
/// <b>The chain is checked on every load.</b> A row the chain cannot vouch for is marked, or a record somebody
/// inserted would read exactly like one keypaste wrote. The verdict's paragraphs wait to be asked for.
/// </para>
/// </remarks>
internal sealed partial class LogViewModel : ObservableObject, IDisposable
{
    /// <summary>What an empty machine is told.</summary>
    internal const string NothingYet = "Nothing has asked keypaste for a credential on this machine yet.";

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly ClipboardCountdown? _clipboard;

    private AuditHistory _history = new(AuditReadKind.Missing, [], [], string.Empty);
    private IReadOnlyList<LogRow> _all = [];
    private List<LogRow> _rows = [];
    private const string _editedSince = "This log has been edited since keypaste wrote it.";

    private ChainVerdict? _verdict;
    private bool _unverifiedShown;
    private bool _unreadShown;
    private string _summary = string.Empty;
    private string _message = string.Empty;
    private bool _verdictShown;
    private LogFilterOption _filter;

    /// <param name="home">keypaste's home, whose <c>audit.jsonl</c> is read.</param>
    /// <param name="clock">The clock times are shown against.</param>
    /// <param name="clipboard">Where the chain's hash is copied.</param>
    /// <param name="openAgents">Goes to Agents, the next step from an empty log.</param>
    /// <param name="table">How the table is sorted and sized, or null for its own way.</param>
    internal LogViewModel(string? home, TimeProvider? clock = null, ClipboardCountdown? clipboard = null, Action? openAgents = null, TableState? table = null)
    {
        OpenAgentsCommand = new RelayCommand(() => openAgents?.Invoke(), () => openAgents is not null);
        Table = table ?? TableStates.NewLog();
        Table.OrderChanged += Apply;
        _path = KeypasteHome.AuditPath(home);
        _clock = clock ?? TimeProvider.System;
        _clipboard = clipboard;
        _filter = Filters[0];

        VerifyCommand = new RelayCommand(ToggleVerdict, () => _verdict is not null);
        CopyHashCommand = new RelayCommand(() => _ = CopyHashAsync(), () => _clipboard is not null && _verdict is { HasHash: true });

        Refresh();
    }

    /// <summary>How the table is sorted and sized. Sorting reads the rows already drawn: the chain, its verdict and the file are untouched.</summary>
    internal TableState Table { get; }

    /// <summary>Whether Verify chain is drawn: a table was read.</summary>
    internal bool OffersVerify => HasTable;

    /// <summary>Where a person is sent to learn why a row or the file is not vouched for.</summary>
    private static string ChainSays(string what) => $"Verify chain says {what}.";

    /// <summary>The legend for a row the hash chain does not vouch for.</summary>
    public static string UnverifiedLegend => $"The hash chain does not vouch for rows marked like this. {ChainSays("why")}";

    public static string Subtitle => "Every agent request, token run and share. Kept on this machine, each line tied to the one before it, so an edit shows.";

    internal IReadOnlyList<LogFilterOption> Filters { get; } =
    [
        new(LogFilter.All, string.Empty),
        new(LogFilter.Agents, "agent and token requests only"),
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

    /// <summary>The line opened to its detail, or null.</summary>
    [ObservableProperty]
    internal partial LogRow? Chosen { get; set; }

    /// <summary>The records the filter keeps, newest first until another order is chosen.</summary>
    internal IReadOnlyList<LogRow> Rows => _rows;

    internal bool HasRows => _rows.Count > 0;

    /// <summary>Whether the table is drawn at all: the file was read and checked.</summary>
    internal bool HasTable => _history.Kind is AuditReadKind.Intact or AuditReadKind.Broken;

    internal bool FilterEmpty => HasTable && _rows.Count == 0;

    /// <summary>Whether a filter hides every line, so widening it would show some.</summary>
    internal bool OffersShowAll => FilterEmpty && _filter.Filter != LogFilter.All;

    [RelayCommand]
    private void ShowWholeLog() => ShowAll = true;

    /// <summary>How many records the table shows of how many the file holds, and under which filter.</summary>
    internal string Summary => _summary;

    /// <summary>The log file, in full.</summary>
    internal string LogPath => _path;

    /// <summary>The log file with the home folder written as <c>~</c>, for the footer.</summary>
    internal string ShortPath => AuditHistory.ShortPath(_path);

    /// <summary>Whether a row shown is one the hash chain does not vouch for, which the legend then explains.</summary>
    internal bool HasUnverifiedNote => _unverifiedShown;

    /// <summary>Whether a row shown was served under a reason nobody read (THREATS.md T-12).</summary>
    internal bool HasUnreadNote => _unreadShown;

    /// <summary>How many lines of the file are not records, said when there are any.</summary>
    internal string UnreadableNote => _history.Unreadable switch
    {
        0 => string.Empty,
        1 => $"1 line of this log could not be read as a record. {ChainSays("why")}",
        var n => string.Create(CultureInfo.InvariantCulture, $"{n} lines of this log could not be read as records. {ChainSays("why")}"),
    };

    internal bool HasUnreadableNote => HasTable && _history.Unreadable > 0;

    internal bool HasNotes => HasUnverifiedNote || HasUnreadNote || HasUnreadableNote;

    /// <summary>What the hash chain says about the whole file, or null when the file was not checked.</summary>
    internal ChainVerdict? Verdict => _verdict;

    /// <summary>Whether the verdict has been asked for.</summary>
    internal bool VerdictShown => _verdictShown;

    internal string VerifyLabel => _verdictShown ? "Hide verdict" : "Verify chain";

    /// <summary>One calm sentence about the file as a whole, or nothing: no log yet, unreadable, unchecked, or broken.</summary>
    internal string Message => _message;

    /// <summary>Whether there is a message to draw; the open verdict of a broken log says it itself.</summary>
    internal bool HasMessage => _message.Length > 0 && !(_verdictShown && IsBroken);

    /// <summary>Whether no log has been written yet, which the screen draws as its empty state rather than a notice.</summary>
    internal bool IsEmpty => _history.Kind == AuditReadKind.Missing;

    internal RelayCommand OpenAgentsCommand { get; }

    internal bool OffersAgents => IsEmpty && OpenAgentsCommand.CanExecute(null);

    /// <summary>Whether the message is drawn as a notice above the table.</summary>
    internal bool HasNotice => HasMessage && !IsEmpty;

    /// <summary>Whether the message is the chain being broken, which the screen marks as a warning.</summary>
    internal bool IsBroken => _history.Kind == AuditReadKind.Broken;

    internal RelayCommand VerifyCommand { get; }

    /// <summary>Copies the latest hash, which is not a secret, so it can be kept somewhere else.</summary>
    internal RelayCommand CopyHashCommand { get; }

    /// <summary>Re-reads the log from disk and re-checks it; the verdict folds away, because it described the last read.</summary>
    [RelayCommand]
    internal void Refresh()
    {
        _history = AuditHistory.Read(_path);
        _verdict = _history.Report is { } report ? ChainVerdict.From(report, _clock.LocalTimeZone) : null;
        _verdictShown = false;
        _message = _history.Kind switch
        {
            AuditReadKind.Missing => NothingYet,
            AuditReadKind.Unreadable => $"That log couldn't be read: {_history.Error.TrimEnd('.')}. Check the file, then open this screen again.",
            AuditReadKind.Unchecked => "That log couldn't be checked, so nothing from it is shown here. Check the file, then open this screen again.",
            AuditReadKind.Broken => $"{_editedSince} {ChainSays("where")}",
            _ => string.Empty,
        };

        _all = [.. _history.Entries.Reverse().Select(entry => LogRow.From(entry, !_history.Unverified.Contains(entry.Line), _clock))];

        OnPropertyChanged(nameof(HasTable));
        OnPropertyChanged(nameof(Verdict));
        OnPropertyChanged(nameof(VerdictShown));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(OffersAgents));
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(UnreadableNote));
        OnPropertyChanged(nameof(OffersVerify));
        OnPropertyChanged(nameof(FilterEmpty));
        OnPropertyChanged(nameof(OffersShowAll));
        OnPropertyChanged(nameof(HasUnreadableNote));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(VerifyLabel));
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
            LogFilter.You => row => row.ByYou,
            LogFilter.Denied => row => row.Denied,
            _ => _ => true,
        };

        _rows = Divide([.. TableSorting.Sorted(_all.Where(keep), Table.Order, SortValueOf, TieBetween)]);
        _unverifiedShown = _rows.Any(row => row.Unverified);
        _unreadShown = _rows.Any(row => row.ReasonUnread);
        _summary = HasTable ? Tally(_rows.Count, _history.Total, _filter.Words) : string.Empty;

        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasUnverifiedNote));
        OnPropertyChanged(nameof(HasUnreadNote));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(FilterEmpty));
        OnPropertyChanged(nameof(OffersShowAll));
        OnPropertyChanged(nameof(HasNotes));
    }

    private static SortValue SortValueOf(LogRow row, string column) => column switch
    {
        TableStates.Actor => new(row.Actor),
        TableStates.Action => new(row.Action),
        TableStates.Secrets => new(LogRow.UnlessNone(row.Secrets)),
        TableStates.Where => new(LogRow.UnlessNone(row.Where)),
        TableStates.Result => new(row.Result),
        _ => SortValue.At(row.Source.At?.UtcTicks),
    };

    /// <summary>Rows that tie stay newest first, by where the file has them, so a sorted table's days keep together; oldest first reverses them too.</summary>
    private int TieBetween(LogRow left, LogRow right) => Table.Order is { Column: TableStates.Time, Descending: false }
        ? left.Source.Line.CompareTo(right.Source.Line)
        : right.Source.Line.CompareTo(left.Source.Line);

    public void Dispose() => Table.OrderChanged -= Apply;

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

    private void ToggleVerdict()
    {
        _verdictShown = !_verdictShown;
        OnPropertyChanged(nameof(VerdictShown));
        OnPropertyChanged(nameof(VerifyLabel));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasNotice));
    }
}
