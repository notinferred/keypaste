using Keypaste.App.ViewModels;
using Keypaste.Core.Clipboard;

namespace Keypaste.App.Clipboard;

/// <summary>
/// Puts a secret on the clipboard, counts down in front of the user, and takes it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>It names no Avalonia type, and must not.</b> The clipboard arrives as
/// <see cref="IAppClipboard"/>, the tick arrives on a <see cref="TimeProvider"/> timer, and the hop
/// to the UI thread arrives as an injected <c>Action&lt;Action&gt;</c> — the same shape
/// <see cref="ShellViewModel"/> takes <c>applyTheme</c> in, and for the same reason. The whole
/// countdown is then assertable with a fake clock and no display, which is where the security
/// assertions live (docs/PRODUCT.md law 4.5).
/// </para>
/// <para>
/// <b>The clear rule is not written here.</b>
/// <see cref="ClipboardClear.Should"/> is, and the CLI's blocking strategy calls the same function,
/// so the two front ends cannot come to different conclusions about whether the clipboard still
/// holds what keypaste put there.
/// </para>
/// <para>
/// <b>This object holds a hash and never a value.</b> The equality guard needs to know whether the
/// clipboard changed, which a SHA-256 answers. KeePassXC keeps the copied secret in a plain string
/// for the whole timeout window purely to power the same guard (O-0008); there is no reason to.
/// </para>
/// <para>
/// <b>What it can promise, and what it cannot.</b> It clears at the deadline, on a lock, and on an
/// orderly quit, in each case only if the clipboard still holds the secret. It promises <i>nothing</i>
/// against <c>kill -9</c>, End Task, an OOM kill, a power cut or a logout — nothing running is left
/// to do the clearing. THREATS.md T-19 and docs/desktop.md say that rather than implying otherwise.
/// </para>
/// </remarks>
internal sealed class ClipboardCountdown : ObservableObject, IDisposable
{
    private readonly IAppClipboard _clipboard;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private readonly TimeSpan _window;

    private byte[]? _expected;
    private ITimer? _timer;
    private DateTimeOffset _startedWall;
    private long _startedStamp;
    private string _label = string.Empty;
    private string? _failure;
    private bool _disposed;

    internal ClipboardCountdown(
        IAppClipboard clipboard,
        TimeProvider clock,
        Action<Action>? post = null,
        TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(clock);

        _clipboard = clipboard;
        _clock = clock;
        _post = post ?? (run => run());
        _window = window ?? ClipboardClear.DefaultWindow;

        ClearNowCommand = new RelayCommand(() => _ = ClearNowAsync(), () => IsCounting);
    }

    /// <summary>Whether a countdown is running.</summary>
    internal bool IsCounting => _timer is not null;

    /// <summary>Whether the toast should be on screen.</summary>
    internal bool IsVisible => IsCounting || _failure is not null;

    /// <summary>What was copied — a field name, never a value.</summary>
    internal string Label => _label;

    /// <summary>Whole seconds before the clipboard is cleared.</summary>
    internal int SecondsLeft => Math.Max(0, (int)Math.Ceiling(Remaining.TotalSeconds));

    /// <summary>How much of the window is left, for the draining bar.</summary>
    internal double Fraction => _window > TimeSpan.Zero
        ? Math.Clamp(Remaining / _window, 0d, 1d)
        : 0d;

    /// <summary>
    /// How long is left, asked of the clock rather than counted down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A tick that does not arrive must not extend the window.</b> Subtracting a second per tick
    /// makes the deadline a function of how many callbacks ran, so a busy UI thread, a garbage
    /// collection or a suspended machine each buy the secret more time on the clipboard. Deriving
    /// it from the clock means a single late tick still finds the deadline passed and clears.
    /// </para>
    /// <para>
    /// <b>Both clocks, taking whichever elapsed more</b> — the rule
    /// <see cref="Session.AppVaultSession"/> already uses, and for the same reason inverted: the
    /// monotonic clock does not advance across suspend on every platform, and the wall clock can be
    /// moved backwards by a correction. Whichever says more time has passed is the one that clears
    /// sooner, and clearing sooner is the safe direction for a secret.
    /// </para>
    /// </remarks>
    private TimeSpan Remaining
    {
        get
        {
            if (_timer is null)
            {
                return TimeSpan.Zero;
            }

            var wall = _clock.GetUtcNow() - _startedWall;
            var monotonic = _clock.GetElapsedTime(_startedStamp);

            return _window - (wall > monotonic ? wall : monotonic);
        }
    }

    /// <summary>The honest sentence when the copy did not happen, or null.</summary>
    internal string? Failure => _failure;

    /// <summary>Clears the clipboard now.</summary>
    internal RelayCommand ClearNowCommand { get; }

    /// <summary>Copies a secret and starts the countdown.</summary>
    /// <param name="secret">The value. Never stored on this object.</param>
    /// <param name="label">What it was — "Password", "STRIPE_KEY". Never a value.</param>
    /// <remarks>
    /// A second copy supersedes the first: the running countdown is stopped without clearing,
    /// because what is on the clipboard now is the new secret and the new countdown owns it.
    /// </remarks>
    internal async Task CopyAsync(string secret, string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();
        _failure = null;

        if (!await _clipboard.TrySetSecretAsync(secret).ConfigureAwait(true))
        {
            Fail("Could not reach the clipboard. Hold to reveal the value and copy it yourself.");
            return;
        }

        // The baseline is read straight after the copy, so whatever the platform's read-back does
        // to the bytes it does identically at both ends of the wait.
        _expected = await _clipboard.TryReadHashAsync().ConfigureAwait(true);
        _label = label;
        _startedWall = _clock.GetUtcNow();
        _startedStamp = _clock.GetTimestamp();

        _timer = _clock.CreateTimer(
            _ => _post(Tick),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

        RaiseEverything();
    }

    /// <summary>Copies something that is not a secret, with no countdown and no clear.</summary>
    /// <param name="text">The text.</param>
    /// <param name="label">What it was.</param>
    /// <remarks>
    /// The <c>keypaste run &lt;project&gt; --</c> helper. Taking a command line back twenty seconds
    /// after somebody asked for it would be a small hostility, and there is nothing to protect.
    /// </remarks>
    internal async Task CopyPlainAsync(string text, string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();
        _failure = null;
        _label = label;

        if (!await _clipboard.TrySetPlainAsync(text).ConfigureAwait(true))
        {
            Fail("Could not reach the clipboard.");
            return;
        }

        RaiseEverything();
    }

    /// <summary>
    /// Clears the clipboard if it still holds what keypaste put there, and stops the countdown.
    /// </summary>
    /// <remarks>Idempotent, and a no-op when nothing is counting.</remarks>
    internal async Task ClearNowAsync()
    {
        if (_expected is not { } expected)
        {
            Stop();
            RaiseEverything();
            return;
        }

        var current = await _clipboard.TryReadHashAsync().ConfigureAwait(true);

        if (ClipboardClear.Should(current is not null, current ?? [], expected))
        {
            await _clipboard.TryClearAsync().ConfigureAwait(true);
        }

        Stop();
        RaiseEverything();
    }

    /// <summary>
    /// Stops counting and clears, because the vault has locked or the app is going away.
    /// </summary>
    /// <remarks>
    /// <b>Locking clears the clipboard, and that is a decision rather than a side effect.</b>
    /// <see cref="ShellViewModel"/>'s rule is that nothing derived from an open vault survives a
    /// lock, and a secret sitting on the clipboard is derived from an open vault. It is also the one
    /// place the app can promise more than the CLI, which has no lock to hang it on. The clear is
    /// still conditional, so a clipboard the user has since changed is left alone.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Fire and forget: Dispose cannot await, and a clipboard implementation that completes
        // synchronously — which the fake in the tests does — runs this to completion inline. The
        // orderly-quit path calls ClearNowAsync directly and awaits it before the process exits.
        _ = ClearNowAsync();
    }

    private void Tick()
    {
        if (_timer is null)
        {
            return;
        }

        if (Remaining <= TimeSpan.Zero)
        {
            _ = ClearNowAsync();
            return;
        }

        Raise(nameof(SecondsLeft));
        Raise(nameof(Fraction));
    }

    private void Fail(string message)
    {
        _failure = message;
        _expected = null;
        RaiseEverything();
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _expected = null;
    }

    private void RaiseEverything()
    {
        Raise(nameof(IsCounting));
        Raise(nameof(IsVisible));
        Raise(nameof(Label));
        Raise(nameof(SecondsLeft));
        Raise(nameof(Fraction));
        Raise(nameof(Failure));
        ClearNowCommand.RaiseCanExecuteChanged();
    }
}
