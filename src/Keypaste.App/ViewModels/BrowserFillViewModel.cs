using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core.Ownership;

namespace Keypaste.App.ViewModels;

/// <summary>A browser's pairing as Settings lists it.</summary>
/// <param name="Name">The pairing's name, as the extension lists it.</param>
/// <param name="Identity">The build that paired.</param>
/// <param name="Used">When it was paired, and when it was last proven here.</param>
internal sealed record PairingItem(string Name, string Identity, string Used);

/// <summary>
/// Settings › Browser fill: Pair a browser, which arms pairing for two minutes and one pairing, and the pairings the vault
/// holds, each with its last use and Remove (D-0634).
/// </summary>
/// <remarks>
/// Only a press here arms pairing; nothing a browser or a pipe sends does. While armed, the card counts down each second and
/// reads the pairings again once the arming is spent or runs out, so a pairing just made appears.
/// </remarks>
internal sealed class BrowserFillViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly AppAuthority _authority;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private ITimer? _timer;
    private IReadOnlyList<PairingItem> _rows = [];
    private DateTimeOffset? _armedUntil;
    private string _message = string.Empty;

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
        StopCommand = new RelayCommand(Stop, () => IsArmed);
        RemoveCommand = new RelayCommand<PairingItem>(Remove, row => row is not null);

        DependsOn(nameof(IsArmed), nameof(ArmedUntil));
        DependsOn(nameof(ArmedText), nameof(ArmedUntil));
        DependsOn(nameof(HasRows), nameof(Rows));
        DependsOn(nameof(HasNoRows), nameof(Rows));
        DependsOn(nameof(HasMessage), nameof(Message));
        DependsOn(PairCommand, nameof(ArmedUntil));
        DependsOn(StopCommand, nameof(ArmedUntil));

        ArmedUntil = _authority.PairingArmedUntil;
        Rows = Read();

        if (IsArmed)
        {
            StartCounting();
        }
    }

    /// <summary>Arms pairing for two minutes and one pairing.</summary>
    internal RelayCommand PairCommand { get; }

    /// <summary>Ends the arming before its two minutes are up.</summary>
    internal RelayCommand StopCommand { get; }

    /// <summary>Removes one pairing, so the browser holding it is refused from its next check.</summary>
    internal RelayCommand<PairingItem> RemoveCommand { get; }

    /// <summary>The pairings the vault holds, by name.</summary>
    internal IReadOnlyList<PairingItem> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    internal bool HasRows => _rows.Count > 0;

    internal bool HasNoRows => _rows.Count == 0;

    /// <summary>Whether a browser may ask to pair now.</summary>
    internal bool IsArmed => _armedUntil is not null;

    /// <summary>What the card says while armed, with the time left.</summary>
    internal string ArmedText
    {
        get
        {
            if (_armedUntil is not { } until)
            {
                return string.Empty;
            }

            var left = until - _clock.GetUtcNow();
            var seconds = left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Pairing is on for {seconds / 60}:{seconds % 60:00}. In KeePassXC-Browser, press Connect, then answer keypaste's window.");
        }
    }

    /// <summary>What the last act here did, or empty.</summary>
    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool HasMessage => _message.Length > 0;

    private DateTimeOffset? ArmedUntil
    {
        get => _armedUntil;
        set => Set(ref _armedUntil, value);
    }

    public void Dispose() => StopCounting();

    private void Pair()
    {
        if (!_authority.ArmPairing())
        {
            Message = "keypaste is not answering browsers for this vault now, so pairing could not be turned on.";
            return;
        }

        Message = string.Empty;
        ArmedUntil = _authority.PairingArmedUntil;
        StartCounting();
    }

    private void Stop()
    {
        _authority.DisarmPairing();
        Tick();
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
            : $"The pairing {row.Name} was not removed: {removed.Reason}";
        Rows = Read();
    }

    private void Tick()
    {
        var before = Rows.Count;
        ArmedUntil = _authority.PairingArmedUntil;
        Raise(nameof(ArmedText));

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
        var paired = row.Pairing.Created.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var used = row.LastUsed is { } at
            ? "last used " + at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "not used since keypaste started";

        return $"Paired {paired} · {used}";
    }
}
