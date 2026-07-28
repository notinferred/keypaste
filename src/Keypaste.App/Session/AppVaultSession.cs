using Keypaste.Core;

namespace Keypaste.App.Session;

/// <summary>
/// Holds the unlocked vault for as long as somebody is using the app, and drops it when they stop.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what Stage 4.1 is for.</b> <c>keypaste agent</c> says in its own remarks that "there
/// is no idle auto-lock in this version — closing the terminal is the lock", and that Stage 4.1
/// owns idle locking. This is that.
/// </para>
/// <para>
/// <b><see cref="Unlocked"/> is shaped for Stage 4.3 and not for this one.</b> It returns
/// <see langword="null"/> when locked because that is precisely what
/// <see cref="Core.Approval.VaultCredentialSource"/> and
/// <see cref="Core.Approval.IEntryNameLister"/> already take — a <c>Func&lt;Vault?&gt;</c> whose
/// null means locked. When the app becomes an approver it is
/// <c>new VaultCredentialSource(() =&gt; session.Unlocked)</c> and nothing here changes. Building
/// to that signature now costs nothing and is the difference between a seam and a rewrite.
/// </para>
/// <para>
/// <b>It names no Avalonia type, and must not.</b> Everything below is testable with a
/// <see cref="TimeProvider"/> and no application, no window and no display — which is where the
/// security assertions live (CORE.md law 4.5). A dispatcher timer would have been fewer lines and
/// would have made the whole idle policy untestable.
/// </para>
/// </remarks>
internal sealed class AppVaultSession : IDisposable
{
    /// <summary>The shipped default, and what any unreadable setting falls back to.</summary>
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>The shortest idle timeout the settings screen will accept.</summary>
    internal static readonly TimeSpan MinimumIdleTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest idle timeout the settings screen will accept.
    /// </summary>
    /// <remarks>
    /// There is deliberately no "never". It would be the one setting that quietly turns off the
    /// feature this stage exists to ship, and a vault left open all night is the threat idle
    /// locking answers. Eight hours is long enough to cover a working day without being an
    /// off switch wearing a number.
    /// </remarks>
    internal static readonly TimeSpan MaximumIdleTimeout = TimeSpan.FromHours(8);

    /// <summary>How long before locking the user is warned.</summary>
    internal static readonly TimeSpan WarningWindow = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;

    private Vault? _vault;
    private ITimer? _timer;
    private DateTimeOffset _activityWall;
    private long _activityStamp;
    private TimeSpan _idleTimeout;
    private bool _warned;
    private bool _disposed;

    internal AppVaultSession(TimeProvider clock, TimeSpan? idleTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _idleTimeout = Clamp(idleTimeout ?? DefaultIdleTimeout);
    }

    /// <summary>Raised after the vault has been disposed, never before.</summary>
    internal event EventHandler<VaultLockReason>? Locked;

    /// <summary>Raised once per idle period, <see cref="WarningWindow"/> before locking.</summary>
    internal event EventHandler<TimeSpan>? LockingSoon;

    /// <summary>The open vault, or <see langword="null"/> when locked.</summary>
    internal Vault? Unlocked
    {
        get
        {
            lock (_gate)
            {
                return _vault;
            }
        }
    }

    /// <summary>The file the open vault came from, or <see langword="null"/> when locked.</summary>
    internal string? VaultPath
    {
        get
        {
            lock (_gate)
            {
                return _vault?.Path;
            }
        }
    }

    /// <summary>Whether a vault is open.</summary>
    internal bool IsUnlocked => Unlocked is not null;

    /// <summary>How long the app may sit untouched before it locks.</summary>
    /// <remarks>Setting it re-arms immediately, so a change in Settings takes effect at once.</remarks>
    internal TimeSpan IdleTimeout
    {
        get
        {
            lock (_gate)
            {
                return _idleTimeout;
            }
        }

        set
        {
            lock (_gate)
            {
                _idleTimeout = Clamp(value);
                _warned = false;
                Rearm();
            }
        }
    }

    /// <summary>Clamps a timeout into the range the app will honour.</summary>
    /// <remarks>
    /// Out of range clamps rather than throwing, and an unreadable settings file is handled by the
    /// caller passing nothing at all. Both roads lead to a number that locks; neither leads to a
    /// vault that stays open because a file was malformed (CORE.md law 3.7).
    /// </remarks>
    internal static TimeSpan Clamp(TimeSpan value) =>
        value < MinimumIdleTimeout ? MinimumIdleTimeout
        : value > MaximumIdleTimeout ? MaximumIdleTimeout
        : value;

    /// <summary>Opens a vault.</summary>
    /// <param name="path">The <c>.kdbx</c> file.</param>
    /// <param name="master">The master password. The caller owns the buffer behind it.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// <para>
    /// <b>A span, exactly as <see cref="Vault.Open"/> takes.</b> The buffer stays the caller's, in
    /// a <c>using</c>, which zeroes it on every path out — including the wrong-password path, which
    /// is the one that happens most and the one people forget. Taking ownership of a
    /// <see cref="SecretBuffer"/> here instead would have been defensible, but it makes CA2000
    /// unprovable at every call site, and <c>.editorconfig</c> makes CA2000 an error precisely so
    /// that disposal is visible rather than promised in a comment.
    /// </para>
    /// <para>
    /// This blocks for as long as Argon2 takes, which is a good fraction of a second by design. The
    /// caller runs it off the UI thread; keeping it synchronous here is what lets the whole idle
    /// and unlock policy be tested without an async harness.
    /// </para>
    /// </remarks>
    internal UnlockOutcome TryUnlock(string path, ReadOnlySpan<char> master)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return UnlockOutcome.NotFound;
        }

        // Answered before the password is used, so a file that was never a vault is reported as
        // that rather than as a wrong password. The header is twelve unencrypted bytes.
        try
        {
            _ = KdbxHeader.Read(path);
        }
        catch (VaultException)
        {
            return UnlockOutcome.NotAKdbx;
        }

        return Open(path, master);
    }

    private UnlockOutcome Open(string path, ReadOnlySpan<char> master)
    {
        Vault opened;

        try
        {
            // The vault's ownership transfers to this session, and every route out of it — Lock,
            // Dispose, and a second TryUnlock replacing it — disposes it. CA2000 cannot see a
            // lifetime that leaves the method. Same shape and same reason as GrantCache.Store.
#pragma warning disable CA2000
            opened = Vault.Open(path, master);
#pragma warning restore CA2000
        }
        catch (InvalidMasterPasswordException)
        {
            return UnlockOutcome.WrongPassword;
        }
        catch (VaultException)
        {
            return UnlockOutcome.Failed;
        }

        VaultLockReason? replaced = null;

        lock (_gate)
        {
            if (_disposed)
            {
                opened.Dispose();
                return UnlockOutcome.Failed;
            }

            if (_vault is not null)
            {
                _vault.Dispose();
                replaced = VaultLockReason.Replaced;
            }

            _vault = opened;
            _warned = false;
            MarkActivity();
            Rearm();
        }

        if (replaced is { } reason)
        {
            Locked?.Invoke(this, reason);
        }

        return UnlockOutcome.Opened;
    }

    /// <summary>Records that a person did something.</summary>
    /// <remarks>
    /// Two field writes and nothing else. It is called from a tunnelling input handler on the
    /// window, so it happens on every keystroke and every click, and anything more expensive here
    /// would be paid for thousands of times an hour. The timer is coarse precisely so that this
    /// can be cheap: it re-arms when it fires, not when activity happens.
    /// </remarks>
    internal void Touch()
    {
        lock (_gate)
        {
            if (_vault is null)
            {
                return;
            }

            _warned = false;
            MarkActivity();
        }
    }

    /// <summary>
    /// Re-checks the deadline now, instead of waiting for the timer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A timer alone cannot survive suspend, and this is the hole it leaves.</b> Timers are
    /// scheduled against the monotonic clock, and on a machine whose monotonic clock stops while it
    /// sleeps, a laptop shut for eight hours wakes with the timer still waiting for the four
    /// minutes it had left. Consulting the wall clock inside the tick does not help, because the
    /// tick is exactly what did not happen. Something outside has to ask.
    /// </para>
    /// <para>
    /// The window calls this when it is activated and when the session resumes, which is the first
    /// moment a person could see anything anyway — so an unattended sleeping machine wakes locked
    /// rather than locking a few seconds after somebody is already looking at it.
    /// </para>
    /// </remarks>
    internal void Reevaluate() => Tick();

    /// <summary>Closes the vault. Doing it twice is not an error.</summary>
    internal void Lock(VaultLockReason reason)
    {
        bool locked;

        lock (_gate)
        {
            locked = _vault is not null;

            _timer?.Dispose();
            _timer = null;
            _vault?.Dispose();
            _vault = null;
            _warned = false;
        }

        if (locked)
        {
            Locked?.Invoke(this, reason);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Lock(VaultLockReason.Shutdown);
    }

    private void MarkActivity()
    {
        _activityWall = _clock.GetUtcNow();
        _activityStamp = _clock.GetTimestamp();
    }

    /// <summary>
    /// How long the app has been idle, according to whichever clock says longer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both clocks, and the longer answer wins.</b> Monotonic time does not advance across
    /// suspend on every platform, so a laptop that slept for eight hours could wake with a
    /// monotonic elapsed of seconds — and an unattended sleeping laptop is exactly the threat this
    /// feature exists for. Wall-clock time covers that, but it can be moved backwards by an NTP
    /// correction or by hand, which would push the deadline away. Taking the larger of the two
    /// closes both holes and costs four lines.
    /// </para>
    /// </remarks>
    private TimeSpan Idle()
    {
        var wall = _clock.GetUtcNow() - _activityWall;
        var monotonic = _clock.GetElapsedTime(_activityStamp);

        var longer = wall > monotonic ? wall : monotonic;
        return longer < TimeSpan.Zero ? TimeSpan.Zero : longer;
    }

    /// <summary>Arms one shot at the next moment worth waking for. Call under the lock.</summary>
    private void Rearm()
    {
        _timer?.Dispose();
        _timer = null;

        if (_vault is null)
        {
            return;
        }

        var idle = Idle();
        var untilLock = _idleTimeout - idle;
        var untilWarning = _idleTimeout - WarningWindow - idle;

        var next = !_warned && untilWarning > TimeSpan.Zero ? untilWarning : untilLock;

        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }

        _timer = _clock.CreateTimer(_ => Tick(), null, next, Timeout.InfiniteTimeSpan);
    }

    private void Tick()
    {
        var lockNow = false;
        TimeSpan? warn = null;

        lock (_gate)
        {
            if (_vault is null)
            {
                return;
            }

            var idle = Idle();

            if (idle >= _idleTimeout)
            {
                lockNow = true;
            }
            else
            {
                if (!_warned && idle >= _idleTimeout - WarningWindow)
                {
                    _warned = true;
                    warn = _idleTimeout - idle;
                }

                Rearm();
            }
        }

        if (lockNow)
        {
            Lock(VaultLockReason.Idle);
            return;
        }

        if (warn is { } remaining)
        {
            LockingSoon?.Invoke(this, remaining);
        }
    }
}
