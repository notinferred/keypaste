using System.Diagnostics.CodeAnalysis;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ownership;

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
/// <b>It holds the vault's claim while unlocked</b>, taken before the password is spent, so a vault
/// another keypaste process holds is refused by name and never opened, and it mints a fresh
/// <see cref="SessionId"/> for every unlock (D-0309).
/// </para>
/// <para>
/// <b>Every lock ends the unlock's <see cref="SessionLifetime"/> first</b>, before the vault is
/// disposed, whatever the reason: manual, idle, minimize, an access change, a replaced vault or
/// shutdown. That one transition is what withdraws requests an agent has waiting and zeroes the
/// grants they were given (D-0313).
/// </para>
/// <para>
/// <b>It names no Avalonia type, and must not.</b> Everything below is testable with a
/// <see cref="TimeProvider"/> and no application, no window and no display — which is where the
/// security assertions live (docs/PRODUCT.md law 4.5). A dispatcher timer would have been fewer lines and
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
    private readonly string _home;

    private Vault? _vault;
    private VaultClaim? _claim;
    private SessionLifetime? _lifetime;
    private ITimer? _timer;
    private DateTimeOffset _activityWall;
    private long _activityStamp;
    private TimeSpan _idleTimeout;
    private bool _warned;
    private bool _disposed;

    /// <param name="clock">The clock idleness is measured on.</param>
    /// <param name="idleTimeout">How long the app may sit untouched, or null for the default.</param>
    /// <param name="home">keypaste's home, where the vault's claim is kept; null resolves it as the app does.</param>
    internal AppVaultSession(TimeProvider clock, TimeSpan? idleTimeout = null, string? home = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _idleTimeout = Clamp(idleTimeout ?? DefaultIdleTimeout);
        _home = home ?? KeypasteHome.Resolve(Environment.GetEnvironmentVariable(KeypasteHome.EnvironmentVariable));
    }

    /// <summary>Raised after the vault has been disposed, never before.</summary>
    internal event EventHandler<VaultLockReason>? Locked;

    /// <summary>Raised after a vault has been opened or created, with its session in place.</summary>
    internal event EventHandler? Opened;

    /// <summary>
    /// Raised when the open vault changes, naming what the change touched, before the change is saved.
    /// </summary>
    /// <remarks>
    /// Forwarded from whichever vault is open, so what agents were given from an entry is withdrawn
    /// when it changes, and everything when the vault's access does (D-0318).
    /// </remarks>
    internal event EventHandler<VaultEdit>? Edited;

    /// <summary>keypaste's home, where the vault's claim is kept.</summary>
    internal string Home => _home;

    /// <summary>The open vault's identity, or <see langword="null"/> when locked.</summary>
    internal VaultIdentity? Identity
    {
        get
        {
            lock (_gate)
            {
                return _claim?.Vault;
            }
        }
    }

    /// <summary>This unlock's session identifier, or <see langword="null"/> when locked.</summary>
    internal string? SessionId
    {
        get
        {
            lock (_gate)
            {
                return _lifetime?.Id;
            }
        }
    }

    /// <summary>
    /// This unlock's lifetime as agents may be answered under it, or <see langword="null"/> when
    /// locked or past its idle deadline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reading this is not activity.</b> Agents are answered through it, and PRODUCT §2 says their
    /// traffic cannot extend the idle deadline, so it never marks activity.
    /// </para>
    /// <para>
    /// <b>A deadline that has passed is honoured here, not only by the timer.</b> A machine that
    /// slept past it wakes with the timer still pending, and an agent's request can arrive before
    /// anybody activates the window. The request is refused and the lock is taken on the thread pool,
    /// never on the caller's thread, which may be the listener the lock stops.
    /// </para>
    /// </remarks>
    internal SessionLifetime? Lifetime
    {
        get
        {
            lock (_gate)
            {
                if (_vault is null)
                {
                    return null;
                }

                if (Idle() < _idleTimeout)
                {
                    return _lifetime;
                }
            }

            ThreadPool.QueueUserWorkItem(_ => Reevaluate());
            return null;
        }
    }

    /// <summary>The open vault, while <paramref name="lifetime"/> is still the current one.</summary>
    /// <param name="lifetime">The lifetime a request belongs to.</param>
    /// <returns>The vault, or <see langword="null"/> once that lifetime has ended.</returns>
    /// <remarks>So a request begun under one unlock can never read the vault a later unlock opened.</remarks>
    internal Vault? UnlockedFor(SessionLifetime lifetime)
    {
        lock (_gate)
        {
            return ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null;
        }
    }

    /// <summary>Why the last unlock or create was refused as <see cref="UnlockOutcome.HeldElsewhere"/>.</summary>
    internal string? HeldElsewhere { get; private set; }

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

    /// <summary>The clock idleness is measured on, for anything else that must expire by it.</summary>
    internal TimeProvider Clock => _clock;

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
    /// vault that stays open because a file was malformed (docs/PRODUCT.md law 3.7).
    /// </remarks>
    internal static TimeSpan Clamp(TimeSpan value) =>
        value < MinimumIdleTimeout ? MinimumIdleTimeout
        : value > MaximumIdleTimeout ? MaximumIdleTimeout
        : value;

    /// <summary>Opens a vault.</summary>
    /// <param name="path">The <c>.kdbx</c> file.</param>
    /// <param name="master">
    /// The master password. The caller owns the buffer behind it. Empty is a vault a keyfile alone
    /// opens when <paramref name="keyfilePath"/> is given, and a wrong password when it is not.
    /// </param>
    /// <param name="keyfilePath">The keyfile the vault needs as well, or <see langword="null"/>.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// <para>
    /// <b>A span, exactly as <see cref="Vault.Open(string, ReadOnlySpan{char})"/> takes.</b> The buffer stays the caller's, in
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
    internal UnlockOutcome TryUnlock(string path, ReadOnlySpan<char> master, string? keyfilePath = null)
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

        // A keyfile that is not there is said before the password is spent on it (D-0284).
        if (keyfilePath is not null && !VaultKeyfile.Inspect(keyfilePath).Accepted)
        {
            return UnlockOutcome.KeyfileUnusable;
        }

        if (!TryClaim(path, out var claim))
        {
            return UnlockOutcome.HeldElsewhere;
        }

        return Open(path, master, keyfilePath, claim);
    }

    private UnlockOutcome Open(string path, ReadOnlySpan<char> master, string? keyfilePath, VaultClaim claim)
    {
        Vault opened;

        try
        {
            // The vault's ownership transfers to this session, and every route out of it — Lock,
            // Dispose, and a second TryUnlock replacing it — disposes it. CA2000 cannot see a
            // lifetime that leaves the method. Same shape and same reason as GrantCache.Store.
#pragma warning disable CA2000
            opened = Vault.Open(path, master, keyfilePath);
#pragma warning restore CA2000
        }
        catch (InvalidMasterPasswordException)
        {
            Release(claim);
            return UnlockOutcome.WrongPassword;
        }
        catch (UnreadableKeyfileException)
        {
            Release(claim);
            return UnlockOutcome.KeyfileUnusable;
        }
        catch (VaultException)
        {
            Release(claim);
            return UnlockOutcome.Failed;
        }

        return Adopt(opened, claim);
    }

    /// <summary>Takes the claim on a vault, or reuses the one this session already holds on it.</summary>
    private bool TryClaim(string path, [NotNullWhen(true)] out VaultClaim? claim)
    {
        lock (_gate)
        {
            if (_claim is { } held && held.Vault.Names(path))
            {
                claim = held;
                HeldElsewhere = null;
                return true;
            }
        }

        // Ownership passes to Adopt, or back through Release on every refusal after this.
#pragma warning disable CA2000
        var taken = VaultClaim.TryAcquire(_home, path, OwnerKind.DesktopApp, out claim, out var refusal);
#pragma warning restore CA2000
        HeldElsewhere = refusal;
        return taken;
    }

    /// <summary>Gives back a claim an unlock took and did not use.</summary>
    private void Release(VaultClaim claim)
    {
        lock (_gate)
        {
            if (ReferenceEquals(claim, _claim))
            {
                return;
            }
        }

        claim.Dispose();
    }

    /// <summary>
    /// Takes ownership of an open vault and starts its idle countdown.
    /// </summary>
    /// <remarks>
    /// Shared by unlocking and creating so there is one place a vault becomes <c>_vault</c>, and one
    /// place that disposes whatever it replaced. Two of these would be two chances to leak a vault
    /// that is still holding a master key.
    /// </remarks>
    private UnlockOutcome Adopt(Vault opened, VaultClaim claim)
    {
        VaultLockReason? replaced = null;
        SessionLifetime? ended = null;
        Vault? previous = null;

        lock (_gate)
        {
            if (_disposed)
            {
                opened.Dispose();

                if (!ReferenceEquals(claim, _claim))
                {
                    claim.Dispose();
                }

                return UnlockOutcome.Failed;
            }

            if (_vault is not null)
            {
                ended = _lifetime;
                previous = _vault;
                replaced = VaultLockReason.Replaced;
            }

            if (!ReferenceEquals(claim, _claim))
            {
                _claim?.Dispose();
            }

            if (previous is not null)
            {
                previous.Edited -= OnVaultEdited;
            }

            opened.Edited += OnVaultEdited;
            _vault = opened;
            _claim = claim;
            _lifetime = new SessionLifetime();
            _warned = false;
            MarkActivity();
            Rearm();
        }

        Retire(ended, previous);

        if (replaced is { } reason)
        {
            Locked?.Invoke(this, reason);
        }

        Opened?.Invoke(this, EventArgs.Empty);

        return UnlockOutcome.Opened;
    }

    /// <summary>
    /// Creates a new vault and, if it was made, opens this session on it.
    /// </summary>
    /// <param name="path">Where the vault goes.</param>
    /// <param name="password">The new master password. The caller owns the buffer behind it.</param>
    /// <param name="confirmation">The same password, typed again.</param>
    /// <param name="keyfilePath">An existing keyfile the vault will need as well, or <see langword="null"/>.</param>
    /// <returns>What <see cref="VaultCreation"/> decided.</returns>
    /// <remarks>
    /// <para>
    /// The rules are not repeated here. Every refusal — an occupied path, an empty password, a
    /// confirmation that does not match — is <see cref="VaultCreation"/>'s answer, which is the same
    /// answer <c>keypaste init</c> gets (docs/PRODUCT.md law 4.2).
    /// </para>
    /// <para>
    /// <b>The created vault is adopted rather than reopened.</b> <see cref="Vault.Save"/> stamps the
    /// file it just wrote, so the vault handed back already detects an outside change and the
    /// refuse-a-stale-save protection is live from the first moment. Reopening would derive Argon2 a
    /// second time to arrive at the same state.
    /// </para>
    /// </remarks>
    internal VaultCreationOutcome TryCreate(
        string path,
        ReadOnlySpan<char> password,
        ReadOnlySpan<char> confirmation,
        string? keyfilePath = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!TryClaim(path, out var claim))
        {
            return VaultCreationOutcome.Failed;
        }

        // Same shape and same reason as Open: the vault's ownership transfers to this session, and
        // every route out of it — Lock, Dispose, and a later unlock replacing it — disposes it.
        // Adopt disposes it itself if this session is already gone.
#pragma warning disable CA2000
        var outcome = VaultCreation.TryCreate(path, password, confirmation, keyfilePath, out var created, out _);
#pragma warning restore CA2000

        if (outcome != VaultCreationOutcome.Created || created is null)
        {
            Release(claim);
            return outcome;
        }

        return Adopt(created, claim) == UnlockOutcome.Opened
            ? VaultCreationOutcome.Created
            : VaultCreationOutcome.Failed;
    }

    /// <summary>
    /// Changes what unlocks the open vault, and carries on with it open under the new factors.
    /// </summary>
    /// <param name="current">The current master password, checked against the file on disk first.</param>
    /// <param name="change">What to change.</param>
    /// <param name="newPassword">The new master password, read only when the change sets one.</param>
    /// <param name="confirmation">The same password, typed again.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="VaultException">
    /// The vault changed on disk, or the backup, the check of the new bytes or the write failed. The
    /// vault is as it was, unless it is a <see cref="VaultAccessUnconfirmedException"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The current password is asked for although the vault is open</b>, so a person who walks up
    /// to an unlocked app cannot re-key the vault out from under its owner. It is checked by opening
    /// the file from disk with it and the session's keyfile, before anything is written.
    /// </para>
    /// <para>
    /// A vault whose access changed refuses every later write until it is reopened (D-0293), so the
    /// session opens it again under the new factors and swaps it in without reporting a lock: the
    /// person stays where they were, as in KeePassXC. If that reopen fails the session locks instead,
    /// with <see cref="VaultLockReason.AccessChanged"/>.
    /// </para>
    /// </remarks>
    internal AccessChangeResult ChangeAccess(
        ReadOnlySpan<char> current,
        VaultAccessChange change,
        ReadOnlySpan<char> newPassword,
        ReadOnlySpan<char> confirmation)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (Unlocked is not { } vault)
        {
            return new AccessChangeResult(AccessChangeOutcome.Locked);
        }

        var keyfile = vault.KeyfilePath;

        try
        {
            using var check = Vault.Open(vault.Path, current, keyfile);
        }
        catch (InvalidMasterPasswordException)
        {
            return new AccessChangeResult(AccessChangeOutcome.WrongCurrentSecret);
        }

        var result = vault.ChangeAccess(change, newPassword, confirmation);

        if (result.Outcome != VaultAccessOutcome.Changed)
        {
            return new AccessChangeResult(AccessChangeOutcome.Refused, result);
        }

        var keyfileAfter = change.Keyfile switch
        {
            AccessKeyfileChange.Attach => Path.GetFullPath(change.KeyfilePath!),
            AccessKeyfileChange.Remove => null,
            _ => keyfile,
        };

        Vault reopened;
        try
        {
            // Owned by the session once swapped in; Swap disposes it when it is not.
#pragma warning disable CA2000
            reopened = Vault.Open(vault.Path, change.SetPassword ? newPassword : current, keyfileAfter);
#pragma warning restore CA2000
        }
        catch (VaultException)
        {
            Lock(VaultLockReason.AccessChanged);
            return new AccessChangeResult(AccessChangeOutcome.ChangedAndLocked, result);
        }

        return Swap(vault, reopened)
            ? new AccessChangeResult(AccessChangeOutcome.Changed, result)
            : new AccessChangeResult(AccessChangeOutcome.ChangedAndLocked, result);
    }

    /// <summary>Puts <paramref name="reopened"/> in the place of <paramref name="expected"/>, keeping the idle countdown.</summary>
    /// <returns>False, having disposed <paramref name="reopened"/>, when the session locked or moved on meanwhile.</returns>
    private bool Swap(Vault expected, Vault reopened)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_vault, expected))
            {
                reopened.Dispose();
                return false;
            }

            _vault.Edited -= OnVaultEdited;
            _vault.Dispose();
            reopened.Edited += OnVaultEdited;
            _vault = reopened;
            return true;
        }
    }

    private void OnVaultEdited(object? sender, VaultEdit edit) => Edited?.Invoke(this, edit);

    /// <summary>Records that a person did something.</summary>
    /// <remarks>
    /// Two clock reads and two field writes. It is called from a tunnelling input handler on the
    /// window, so it happens on every keystroke and every click, and anything more expensive here
    /// would be paid for thousands of times an hour. The timer is coarse precisely so that this
    /// can be cheap: it re-arms when it fires, not when activity happens.
    /// <para>
    /// <b>A touch past the deadline locks.</b> Input can reach the window before its activation
    /// re-check on a machine waking past the timeout; reviving the session then would undo exactly
    /// the lock <see cref="Reevaluate"/> exists to deliver (F.13).
    /// </para>
    /// </remarks>
    internal void Touch()
    {
        lock (_gate)
        {
            if (_vault is null)
            {
                return;
            }

            if (Idle() < _idleTimeout)
            {
                _warned = false;
                MarkActivity();
                return;
            }
        }

        Lock(VaultLockReason.Idle);
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
        SessionLifetime? ended;
        Vault? vault;

        lock (_gate)
        {
            locked = _vault is not null;
            ended = _lifetime;
            _lifetime = null;
            vault = _vault;
            _vault = null;

            if (vault is not null)
            {
                vault.Edited -= OnVaultEdited;
            }

            _timer?.Dispose();
            _timer = null;
            _claim?.Dispose();
            _claim = null;
            _warned = false;
        }

        Retire(ended, vault);

        if (locked)
        {
            Locked?.Invoke(this, reason);
        }
    }

    /// <summary>Ends a lifetime, then disposes the vault it was answered from.</summary>
    /// <remarks>
    /// <para>
    /// In that order, so nothing waiting on the lifetime can be released from the vault as it goes.
    /// </para>
    /// <para>
    /// Outside <c>_gate</c>, because ending a lifetime runs the cancellations of everything waiting
    /// on it, and those must not run while this session's lock is held. The lifetime and vault are
    /// already detached, so nothing reaches them through the session meanwhile.
    /// </para>
    /// </remarks>
    private static void Retire(SessionLifetime? lifetime, Vault? vault)
    {
        lifetime?.Dispose();
        vault?.Dispose();
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
