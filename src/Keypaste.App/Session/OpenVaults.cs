using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Keypaste.Core.Ownership;
using Keypaste.Core.Settings;

namespace Keypaste.App.Session;

/// <summary>One vault open in the app: its session and the authority serving it to agents.</summary>
/// <param name="Session">The vault's session, with its own claim, lifetime, idle timer and quick unlock copies.</param>
/// <param name="Authority">The authority serving it on its own pipe, which owns the session.</param>
/// <param name="Watch">What merges another program's save of its file while it is unlocked.</param>
internal sealed record OpenVault(AppVaultSession Session, AppAuthority Authority, VaultFileWatch Watch);

/// <summary>
/// The vaults open in the app at once, each with a session and an authority of its own, and what they
/// share: one prompt queue, so the person meets one prompt at a time whichever vault asks (THREATS.md T-11), each
/// other's checkpoint keys for the one audit log their owners write to, and the lock.
/// </summary>
/// <remarks>
/// <para>
/// <b>They lock together.</b> A lock the person or the app makes, the idle lock, Ctrl/Cmd+L, a minimize, closing into the
/// menu bar or tray and quitting, locks every vault; a lock only one vault's state calls for, <c>keypaste lock</c> naming
/// it, an access change or a reload refused, locks that vault alone, and so does <see cref="LockAlone"/>. Each person's
/// action in the window touches every vault, so no vault idles out under a person using another.
/// </para>
/// <para>
/// The first vault is the one in view, which the unlock screen opens and the window is built on while it is unlocked; it stays for the app's life, unlocked and locked again. The vaults the local account holds are listed as locked places after an unlock, each known by its file until it opens.
/// It names no Avalonia type, as <see cref="AppVaultSession"/> does not, so every assertion here runs without a display.
/// </para>
/// </remarks>
internal sealed class OpenVaults : IIdleWatched, IDisposable
{
    private readonly Func<AppVaultSession> _newSession;
    private readonly Func<OpenVaults, AppVaultSession, AppAuthority> _serve;
    private readonly DesktopPreferences? _preferences;
    private readonly Lock _gate = new();
    private readonly List<OpenVault> _vaults = [];
    private readonly Dictionary<AppVaultSession, string> _paths = [];
    private AppVaultSession? _alone;
    private bool _inViewClosed;
    private bool _disposed;

    /// <param name="inView">The locked session of the vault the window shows, which this now owns.</param>
    /// <param name="newSession">Composes a locked session for each vault after it, as launch composed the first.</param>
    /// <param name="serve">Composes the authority serving a session, sharing <see cref="Turns"/> and <see cref="KeysBesides"/>.</param>
    /// <param name="preferences">The preferences whose idle timeout every vault follows as it changes, or null.</param>
    internal OpenVaults(
        AppVaultSession inView,
        Func<AppVaultSession> newSession,
        Func<OpenVaults, AppVaultSession, AppAuthority> serve,
        DesktopPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(inView);
        ArgumentNullException.ThrowIfNull(newSession);
        ArgumentNullException.ThrowIfNull(serve);

        _newSession = newSession;
        _serve = serve;
        _preferences = preferences;
        Pairings = new OwnerPairings(BrowserPairings.In(inView.Home), PairingVaults, LockedNames);
        InView = Place(inView);

        if (_preferences is not null)
        {
            _preferences.Changed += OnPreferencesChanged;
        }
    }

    /// <summary>The one prompt queue every open vault's gate takes its turn from.</summary>
    internal PromptTurns Turns { get; } = new();

    /// <summary>This computer's browser pairings, one per browser for every vault unlocked here, which vouch for them together.</summary>
    internal OwnerPairings Pairings { get; }

    /// <summary>The vault the unlock screen opens, and the window is built on while it is unlocked.</summary>
    internal OpenVault InView { get; }

    /// <summary>Every vault open in the app, the one in view first, unlocked or not.</summary>
    internal IReadOnlyList<OpenVault> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _vaults];
            }
        }
    }

    internal IReadOnlyList<OpenVault> Unlocked => [.. All.Where(vault => vault.Session.IsUnlocked)];

    /// <summary>The vaults the window lists: every one open, but the vault in view while it stays closed.</summary>
    internal IReadOnlyList<OpenVault> Listed
    {
        get
        {
            lock (_gate)
            {
                return [.. _vaults.Where(vault => !(_inViewClosed && ReferenceEquals(vault, InView) && !vault.Session.IsUnlocked))];
            }
        }
    }

    /// <summary>Raised on the locking thread when any open vault locks, after the vaults it locks with have locked.</summary>
    internal event EventHandler<VaultLockReason>? Locked;

    /// <summary>Raised on the opening thread when any open vault opens, after a locked place that named its file has given way.</summary>
    internal event EventHandler? Opened;

    /// <summary>Raised on the thread that changed them when a place is added, learns its file or is closed.</summary>
    internal event EventHandler? PlacesChanged;

    /// <summary>The file a vault holds, or held when it last unlocked or was opened by path, or null for a place that never held one.</summary>
    internal string? PathOf(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.VaultPath is { } path)
        {
            return path;
        }

        lock (_gate)
        {
            return _paths.GetValueOrDefault(session);
        }
    }

    /// <summary>The open vault holding <paramref name="path"/>, unlocked or not, or null.</summary>
    internal OpenVault? Holding(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return Listed.FirstOrDefault(vault => PathOf(vault.Session) is { } held && ChosenVault.Same(held, path));
    }

    /// <summary>The open vault whose file is named <paramref name="name"/> without its extension, as a page's list names a locked one, or null.</summary>
    internal OpenVault? Named(string name) =>
        Listed.FirstOrDefault(vault => PathOf(vault.Session) is { } path && string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.Ordinal));

    /// <summary>Whether a lock for <paramref name="reason"/> locks every vault, as a person's lock and the app's do, rather than one vault's state.</summary>
    internal static bool LocksTogether(VaultLockReason reason) =>
        reason is VaultLockReason.Idle or VaultLockReason.Manual or VaultLockReason.Minimized or VaultLockReason.Closed or VaultLockReason.Shutdown;

    /// <summary>Opens a place for another vault: a locked session, with its own claim and authority on its own pipe, unlocked as the first is.</summary>
    /// <returns>The vault's place.</returns>
    /// <exception cref="ObjectDisposedException">The app has quit.</exception>
    internal OpenVault Add()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        var vault = Place(_newSession());
        PlacesChanged?.Invoke(this, EventArgs.Empty);
        return vault;
    }

    /// <summary>Opens a locked place for the vault at <paramref name="path"/>, or returns the place already holding it.</summary>
    /// <param name="path">The vault's file, which the place names until it unlocks.</param>
    /// <returns>The vault's place.</returns>
    /// <exception cref="ObjectDisposedException">The app has quit.</exception>
    internal OpenVault Add(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var full = Path.GetFullPath(path);

        if (Holding(full) is { } held)
        {
            return held;
        }

        var vault = Add();

        lock (_gate)
        {
            _paths[vault.Session] = full;
        }

        PlacesChanged?.Invoke(this, EventArgs.Empty);
        return vault;
    }

    /// <summary>
    /// Opens a locked place for each vault the local account holds that no place holds yet, as the window lists them after an
    /// unlock; a vault whose file is not there stays in the account, unlisted until it is.
    /// </summary>
    internal void HoldAccount()
    {
        foreach (var path in LocalAccount.Vaults(InView.Session.Home))
        {
            if (Holding(path) is null && File.Exists(path))
            {
                Add(path);
            }
        }
    }

    /// <summary>Closes a vault and takes it out of the local account; its file stays where it is.</summary>
    /// <param name="vault">An open vault.</param>
    /// <returns>Whether the account no longer holds it.</returns>
    internal bool Remove(OpenVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        var path = PathOf(vault.Session);
        Close(vault);
        return path is null || LocalAccount.Release(vault.Session.Home, path);
    }

    private OpenVault Place(AppVaultSession session)
    {
        AppAuthority authority;

        try
        {
            authority = _serve(this, session);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        // The open vault owns its watch, which Drop disposes.
#pragma warning disable CA2000
        var vault = new OpenVault(session, authority, new VaultFileWatch(session));
#pragma warning restore CA2000
        session.Locked += OnLocked;
        session.Opened += OnOpened;

        lock (_gate)
        {
            _vaults.Add(vault);
        }

        return vault;
    }

    /// <summary>Locks one vault alone and drops its place; the vault in view keeps its place, locked and unlisted until it opens again.</summary>
    /// <param name="vault">A vault <see cref="Add()"/> opened.</param>
    internal void Close(OpenVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        LockAlone(vault);

        if (ReferenceEquals(vault, InView))
        {
            lock (_gate)
            {
                _inViewClosed = true;
            }

            PlacesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        lock (_gate)
        {
            if (!_vaults.Remove(vault))
            {
                return;
            }

            _paths.Remove(vault.Session);
        }

        Drop(vault);
        PlacesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Locks every vault for <paramref name="reason"/>.</summary>
    internal void LockAll(VaultLockReason reason)
    {
        foreach (var vault in All)
        {
            vault.Session.Lock(reason);
        }
    }

    /// <summary>Locks every vault as <see cref="LockAll"/> does, on a thread of its own, for a lock asked on the UI thread.</summary>
    /// <returns>Completes once every vault's endpoint has stopped.</returns>
    internal Task LockAllAsync(VaultLockReason reason) => AppVaultSession.OnItsOwnThread(() => LockAll(reason));

    /// <summary>Locks one vault and leaves every other as it is.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="reason">Why; the person's own lock unless said otherwise.</param>
    internal void LockAlone(OpenVault vault, VaultLockReason reason = VaultLockReason.Manual)
    {
        ArgumentNullException.ThrowIfNull(vault);

        Volatile.Write(ref _alone, vault.Session);

        try
        {
            vault.Session.Lock(reason);
        }
        finally
        {
            Volatile.Write(ref _alone, null);
        }
    }

    /// <summary>Locks one vault alone as <see cref="LockAlone"/> does, on a thread of its own.</summary>
    internal Task LockAloneAsync(OpenVault vault, VaultLockReason reason = VaultLockReason.Manual) =>
        AppVaultSession.OnItsOwnThread(() => LockAlone(vault, reason));

    /// <summary>A person did something in the window, which keeps every vault unlocked.</summary>
    public void Touch()
    {
        foreach (var vault in All)
        {
            vault.Session.Touch();
        }
    }

    /// <summary>Re-checks every vault's idle deadline, as a window coming forward does.</summary>
    public void Reevaluate()
    {
        foreach (var vault in All)
        {
            vault.Session.Reevaluate();
        }
    }

    /// <summary>Brings the prompt waiting for the person forward, whichever vault asked; called on the UI thread.</summary>
    /// <returns>Whether a prompt was waiting on screen.</returns>
    internal bool ShowPrompt() => All.Any(vault => vault.Authority.ShowPrompt());

    /// <summary>Whether a prompt waits for the person's answer, whichever vault asked; called on the UI thread.</summary>
    internal bool IsAsking => All.Any(vault => vault.Authority.IsAsking);

    /// <summary>The name a prompt for <paramref name="session"/>'s vault shows beside its item, or null when the session has no vault.</summary>
    internal static string? NameShown(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.VaultPath is { } path ? Path.GetFileNameWithoutExtension(path) : null;
    }

    /// <summary>The checkpoint keys of the unlocked vaults other than <paramref name="session"/>'s, read now, each a copy the caller zeroes.</summary>
    internal IReadOnlyList<AuditCheckpointKey> KeysBesides(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        List<AuditCheckpointKey> keys = [];

        foreach (var other in All.Where(vault => !ReferenceEquals(vault.Session, session)))
        {
            try
            {
                if (other.Session.Identity is { } identity && AuditCheckpointKey.Read(other.Session.Unlocked, identity) is { } key)
                {
                    keys.Add(key);
                }
            }
            catch (ObjectDisposedException)
            {
                // That vault locked as it was read.
            }
        }

        return keys;
    }

    /// <summary>Each unlocked vault, with a save through its own session, as a pairing write seals under them.</summary>
    private List<PairingVault> PairingVaults()
    {
        List<PairingVault> vaults = [];

        foreach (var open in All)
        {
            var session = open.Session;

            if (session.Lifetime is { } lifetime && session.UnlockedFor(lifetime) is { } vault)
            {
                vaults.Add(new PairingVault(vault, edit => SessionHost.Saved(session.WriteFor(lifetime, edit, changed => changed))));
            }
        }

        return vaults;
    }

    /// <summary>The names of the vaults open here and locked, as a page's list names them.</summary>
    private List<string> LockedNames() =>
        [.. Listed.Where(vault => !vault.Session.IsUnlocked).Select(vault => PathOf(vault.Session)).OfType<string>().Select(Path.GetFileNameWithoutExtension).OfType<string>()];

    /// <summary>Quits as <see cref="Dispose"/> does, on a thread of its own, so a window awaiting it keeps painting while the endpoints stop.</summary>
    internal Task QuitAsync() => AppVaultSession.OnItsOwnThread(Dispose);

    /// <summary>Quits: every vault locks before its endpoint stops.</summary>
    public void Dispose()
    {
        List<OpenVault> vaults;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            vaults = [.. _vaults];
            _vaults.Clear();
        }

        if (_preferences is not null)
        {
            _preferences.Changed -= OnPreferencesChanged;
        }

        foreach (var vault in vaults)
        {
            Drop(vault);
        }
    }

    private void Drop(OpenVault vault)
    {
        vault.Session.Locked -= OnLocked;
        vault.Session.Opened -= OnOpened;
        vault.Watch.Dispose();
        vault.Authority.Dispose();
    }

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        if (LocksTogether(reason) && !ReferenceEquals(sender, Volatile.Read(ref _alone)))
        {
            foreach (var other in All.Where(vault => !ReferenceEquals(vault.Session, sender)))
            {
                other.Session.Lock(reason);
            }
        }

        Locked?.Invoke(sender, reason);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not AppVaultSession { VaultPath: { } path } session)
        {
            return;
        }

        List<OpenVault> stale;

        lock (_gate)
        {
            _paths[session] = path;

            if (ReferenceEquals(session, InView.Session))
            {
                _inViewClosed = false;
            }

            // A locked place that named this file gives way to the session that opened it.
            stale = [.. _vaults.Where(vault => !ReferenceEquals(vault.Session, session) && !vault.Session.IsUnlocked
                && _paths.TryGetValue(vault.Session, out var held) && ChosenVault.Same(held, path))];
        }

        foreach (var vault in stale)
        {
            Close(vault);
        }

        LocalAccount.Hold(session.Home, path);
        Opened?.Invoke(sender, e);
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        var timeout = AppVaultSession.Clamp(_preferences!.IdleTimeout);

        foreach (var vault in All.Where(vault => vault.Session.IdleTimeout != timeout))
        {
            vault.Session.IdleTimeout = timeout;
        }
    }
}
