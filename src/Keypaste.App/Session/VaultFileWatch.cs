using Keypaste.Core;

namespace Keypaste.App.Session;

/// <summary>
/// Watches one open vault's file for another program's save, such as a sync tool's, and merges it with the open copy
/// within five seconds, whichever vault the window shows.
/// </summary>
/// <remarks>
/// It watches the vault's folder, not the file alone, because another program often saves by renaming a new file over it,
/// and compares the file a moment after the last change it sees; a folder that cannot be watched leaves the five-second
/// poll. A save keypaste refused is merged by the session itself, and a merge it refuses is left to the person's Reload.
/// </remarks>
internal sealed class VaultFileWatch : IDisposable
{
    internal static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(250);

    private readonly AppVaultSession _session;
    private readonly Lock _gate = new();
    private readonly ITimer _poll;
    private readonly ITimer _settleTimer;
    private FileSystemWatcher? _folder;
    private Task _merging = Task.CompletedTask;
    private bool _isMerging;
    private bool _disposed;

    /// <param name="session">The vault's session, whose clock the poll and the settling run on.</param>
    internal VaultFileWatch(AppVaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _poll = session.Clock.CreateTimer(_ => Check(), null, Poll, Poll);
        _settleTimer = session.Clock.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _session.Opened += OnOpened;
        _session.Locked += OnLocked;

        if (_session.IsUnlocked)
        {
            WatchFolder();

            // A save made before the watch began is merged now rather than at the first poll.
            Check();
        }
    }

    /// <summary>The vault's session.</summary>
    internal AppVaultSession Session => _session;

    /// <summary>Raised on whichever thread noticed it, when the file may hold another save or a merge started or finished.</summary>
    internal event EventHandler? Changed;

    /// <summary>Whether a merge of another program's save runs now.</summary>
    internal bool IsMerging
    {
        get
        {
            lock (_gate)
            {
                return _isMerging;
            }
        }
    }

    /// <summary>The merge the poll or the folder watch started, or a completed task when none runs.</summary>
    internal Task Merging
    {
        get
        {
            lock (_gate)
            {
                return _merging;
            }
        }
    }

    /// <summary>Compares the file with the open copy now, merging another program's save where the session merges.</summary>
    internal void Check()
    {
        if (_session.Unlocked is not { } vault)
        {
            return;
        }

        VaultSaveState state;

        try
        {
            state = vault.SaveState();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (state.Status == VaultSaveStatus.ChangedOnDisk && File.Exists(vault.Path) && _session.MergeRefusal() is null && _session.Merges)
        {
            StartMerge();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void StartMerge()
    {
        lock (_gate)
        {
            if (_isMerging || _disposed)
            {
                return;
            }

            _isMerging = true;
            _merging = AppVaultSession.OnItsOwnThread(() =>
            {
                try
                {
                    _session.Merge();
                }
                finally
                {
                    lock (_gate)
                    {
                        _isMerging = false;
                    }

                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpened(object? sender, EventArgs e) => WatchFolder();

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        lock (_gate)
        {
            _folder?.Dispose();
            _folder = null;
        }
    }

    private void WatchFolder()
    {
        if (_session.VaultPath is not { } path || Path.GetDirectoryName(Path.GetFullPath(path)) is not { Length: > 0 } folder)
        {
            return;
        }

        FileSystemWatcher? watch = null;

        try
        {
            watch = new FileSystemWatcher(folder, Path.GetFileName(path));
            watch.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
            watch.Changed += OnFileChanged;
            watch.Created += OnFileChanged;
            watch.Deleted += OnFileChanged;
            watch.Renamed += OnFileChanged;
            watch.Error += OnWatchError;
            watch.EnableRaisingEvents = true;

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _folder?.Dispose();
                (_folder, watch) = (watch, null);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The poll still compares the file every five seconds.
        }
        finally
        {
            watch?.Dispose();
        }
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e) => Settle();

    // A watch that lost track compares rather than waiting for the poll.
    private void OnWatchError(object? sender, ErrorEventArgs e) => Settle();

    /// <summary>Compares the file a moment after the last change, so a save still being written is compared once it has finished.</summary>
    private void Settle()
    {
        try
        {
            _settleTimer.Change(_settle, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposed while the folder watch was still reporting, on a thread of its own.
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
            _folder?.Dispose();
            _folder = null;
        }

        _session.Opened -= OnOpened;
        _session.Locked -= OnLocked;
        _poll.Dispose();
        _settleTimer.Dispose();
    }
}
