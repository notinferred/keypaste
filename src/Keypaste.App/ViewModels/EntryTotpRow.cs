using Keypaste.Core.Totp;

namespace Keypaste.App.ViewModels;

/// <summary>
/// An item's TOTP in the entry pane: a mask as wide as its code, the seconds until the next code, and a way
/// to see or copy the current one (D-0481).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neither the code nor the seed is a property of this object.</b> <see cref="Reveal"/> and Copy compute
/// the code from the open vault at the moment of the press, as <see cref="EntryFieldRow"/> reads a value,
/// and the spec holding the seed is zeroed before either returns. The countdown is worked out from the
/// period alone, so a tick reads nothing from the vault.
/// </para>
/// <para>
/// The code is masked until held, as the password is: while it is current it is a credential, and a
/// screen share or a recording would carry it.
/// </para>
/// </remarks>
internal sealed class EntryTotpRow : ObservableObject, IRevealSource, IDisposable
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(1);

    private readonly EntryDetailViewModel _owner;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _period;
    private readonly ITimer _timer;
    private int _secondsLeft;

    internal EntryTotpRow(EntryDetailViewModel owner, TimeProvider clock, Action<Action> post, int digits, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(post);

        _owner = owner;
        _clock = clock;
        _period = period;
        MaskedLength = digits;
        CopyCommand = new AsyncRelayCommand(CopyAsync);

        DependsOn(nameof(Countdown), nameof(SecondsLeft));

        Tick();
        _timer = clock.CreateTimer(_ => post(Tick), null, _tick, _tick);
    }

    /// <inheritdoc/>
    public int MaskedLength { get; }

    /// <summary>Whole seconds until the next code, read from the clock rather than counted down.</summary>
    internal int SecondsLeft
    {
        get => _secondsLeft;
        private set => Set(ref _secondsLeft, value);
    }

    /// <summary>The countdown as the pane draws it.</summary>
    internal string Countdown => $"{SecondsLeft}s";

    /// <summary>Copies the current code, with the clipboard's clearing countdown.</summary>
    internal AsyncRelayCommand CopyCommand { get; }

    /// <inheritdoc/>
    public string? Reveal() => _owner.ReadTotpCode();

    /// <inheritdoc/>
    /// <remarks>The code was only ever the control's; there is nothing here to give back.</remarks>
    public void Conceal()
    {
    }

    /// <summary>Stops the countdown.</summary>
    public void Dispose() => _timer.Dispose();

    private void Tick() =>
        SecondsLeft = (int)Math.Ceiling(TotpSpec.Remaining(_period, _clock.GetUtcNow()).TotalSeconds);

    private async Task CopyAsync()
    {
        if (_owner.ReadTotpCode() is { } code)
        {
            await _owner.Clipboard.CopyAsync(code, "TOTP code").ConfigureAwait(true);
        }
    }
}
