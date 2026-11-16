using System.Globalization;
using Keypaste.App.Globalization;
using Keypaste.App.Session;
using Keypaste.Core.Ownership;

namespace Keypaste.App.ViewModels;

/// <summary>A browser's pairing as Settings lists it.</summary>
/// <param name="name">The pairing's name, as the extension lists it.</param>
/// <param name="identity">The build that paired.</param>
/// <param name="used">When it was paired, and when it was last proven here.</param>
internal sealed partial class PairingItem(string name, string identity, string used) : ObservableObject
{
    internal string Name { get; } = name;

    internal string Identity { get; } = identity;

    internal string Used { get; } = used;

    [ObservableProperty]
    internal partial bool IsConfirmingRemove { get; set; }
}

/// <summary>An "Always fill" allowance as Settings lists it: the item, the page and the browser, removed in one press since a fill asked again undoes it.</summary>
/// <param name="allowance">The allowance.</param>
/// <param name="item">Its item's title.</param>
internal sealed class AllowanceItem(Keypaste.Core.Agents.BrowserAllowance allowance, string item)
{
    internal Keypaste.Core.Agents.BrowserAllowance Allowance { get; } = allowance;

    internal string Item { get; } = item;

    internal string Where { get; } = $"on {allowance.Origin} in {allowance.Pairing}";
}

/// <summary>
/// Settings › Browser fill: Pair a browser, which arms pairing for two minutes and one pairing, and the pairings the vault
/// holds, each with its last use and Remove (D-0634).
/// </summary>
/// <remarks>
/// Only a press here arms pairing; nothing a browser or a pipe sends does. While armed, the card counts down each second and
/// reads the pairings again once the arming is spent or runs out, so a pairing just made appears.
/// </remarks>
internal sealed partial class BrowserFillViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly AppAuthority _authority;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private ITimer? _timer;

    /// <param name="authority">The app's authority, which pairs browsers for each unlock.</param>
    /// <param name="clock">What the countdown runs on.</param>
    /// <param name="post">Runs an action on the UI thread, or null to run it where the tick lands.</param>
    internal BrowserFillViewModel(AppAuthority authority, TimeProvider? clock = null, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(authority);

        _authority = authority;
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (run => run());

        PairCommand = new RelayCommand(Pair, () => !IsArmed);
        AskRemoveCommand = new RelayCommand<PairingItem>(row => SetConfirming(row, true), row => row is not null);
        CancelRemoveCommand = new RelayCommand<PairingItem>(row => SetConfirming(row, false), row => row is not null);
        RemoveCommand = new RelayCommand<PairingItem>(Remove, row => row is not null);
        UnallowCommand = new RelayCommand<AllowanceItem>(Unallow, row => row is not null);

        ArmedUntil = _authority.PairingArmedUntil;
        Rows = Read();
        Allowances = ReadAllowances();

        if (IsArmed)
        {
            StartCounting();
        }
    }

    /// <summary>Arms pairing for two minutes and one pairing.</summary>
    internal RelayCommand PairCommand { get; }

    /// <summary>Asks in the row before removing its pairing.</summary>
    internal RelayCommand<PairingItem> AskRemoveCommand { get; }

    internal RelayCommand<PairingItem> CancelRemoveCommand { get; }

    /// <summary>Removes one pairing, having been confirmed, so the browser holding it is refused from its next check.</summary>
    internal RelayCommand<PairingItem> RemoveCommand { get; }

    /// <summary>Stops always filling one item on one page, so the next fill there is asked again.</summary>
    internal RelayCommand<AllowanceItem> UnallowCommand { get; }

    /// <summary>The items filled without asking, oldest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAllowances), nameof(HasNoAllowances))]
    internal partial IReadOnlyList<AllowanceItem> Allowances { get; private set; } = [];

    internal bool HasAllowances => Allowances.Count > 0;

    internal bool HasNoAllowances => Allowances.Count == 0;

    /// <summary>The pairings the vault holds, by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows), nameof(HasNoRows))]
    internal partial IReadOnlyList<PairingItem> Rows { get; private set; } = [];

    internal bool HasRows => Rows.Count > 0;

    internal bool HasNoRows => Rows.Count == 0;

    /// <summary>Whether a browser may ask to pair now.</summary>
    internal bool IsArmed => ArmedUntil is not null;

    /// <summary>What the card says while armed, with the time left.</summary>
    internal string ArmedText
    {
        get
        {
            if (ArmedUntil is not { } until)
            {
                return string.Empty;
            }

            var left = until - _clock.GetUtcNow();
            var seconds = left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Pairing is on for {seconds / 60}:{seconds % 60:00}. In keypaste's extension, press Connect, then answer keypaste's window.");
        }
    }

    /// <summary>What the last act here did, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArmed), nameof(ArmedText))]
    [NotifyCanExecuteChangedFor(nameof(PairCommand), nameof(StopCommand))]
    private partial DateTimeOffset? ArmedUntil { get; set; }

    public void Dispose() => StopCounting();

    private void Pair()
    {
        if (!_authority.ArmPairing())
        {
            Message = "keypaste is not answering browsers for this vault now, so pairing could not be turned on. Lock and unlock the vault, then try again.";
            return;
        }

        Message = string.Empty;
        ArmedUntil = _authority.PairingArmedUntil;
        StartCounting();
    }

    /// <summary>Ends the arming before its two minutes are up.</summary>
    [RelayCommand(CanExecute = nameof(IsArmed))]
    private void Stop()
    {
        _authority.DisarmPairing();
        Tick();
    }

    private static void SetConfirming(PairingItem? row, bool confirming)
    {
        if (row is not null)
        {
            row.IsConfirmingRemove = confirming;
        }
    }

    private void Remove(PairingItem? row)
    {
        if (row is null)
        {
            return;
        }

        var removed = _authority.RemovePairing(row.Name);
        Message = removed.Removed
            ? $"Removed the pairing {row.Name}. That browser has to pair again before it can ask for anything."
            : $"The pairing {row.Name} was not removed: {removed.Reason?.TrimEnd('.')}. Try again.";
        Rows = Read();
        Allowances = ReadAllowances();
    }

    private void Unallow(AllowanceItem? row)
    {
        if (row is null)
        {
            return;
        }

        Message = _authority.Unallow(row.Allowance, out var problem)
            ? $"keypaste will ask before filling {row.Item} {row.Where} again."
            : $"keypaste still fills {row.Item} {row.Where} without asking: {problem.TrimEnd('.')}. Try again.";
        Allowances = ReadAllowances();
    }

    private IReadOnlyList<AllowanceItem> ReadAllowances() =>
        [.. _authority.Allowances().Select(row => new AllowanceItem(row.Allowance, row.Item))];

    private void Tick()
    {
        var before = Rows.Count;
        ArmedUntil = _authority.PairingArmedUntil;
        OnPropertyChanged(nameof(ArmedText));

        if (IsArmed)
        {
            return;
        }

        StopCounting();
        Rows = Read();

        if (Rows.Count > before)
        {
            Message = "Paired. The browser lists this vault's pairing under its name.";
        }
    }

    private void StartCounting() => _timer ??= _clock.CreateTimer(_ => _post(Tick), null, _tick, _tick);

    private void StopCounting()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private IReadOnlyList<PairingItem> Read() =>
        [.. _authority.Pairings().Select(row => new PairingItem(row.Pairing.Id, row.Pairing.Identity.Described, Used(row)))];

    private static string Used(PairingRow row)
    {
        var paired = PersonTime.Date(row.Pairing.Created.ToLocalTime());
        var used = row.LastUsed is { } at
            ? "last used " + PersonTime.Moment(at.ToLocalTime())
            : "not used since keypaste started";

        return $"Paired {paired} · {used}";
    }
}
