namespace Keypaste.Core.Ownership;

/// <summary>
/// Watches one open vault's file for another program's save, such as a sync tool's, and merges it with the open copy
/// within five seconds, for either approver.
/// </summary>
/// <remarks>
/// It watches the vault's folder, not the file alone, because another program often saves by renaming a new file over it,
/// and compares the file a moment after the last change it sees; a folder that cannot be watched leaves the five-second
/// poll. A save the approver refused is merged by the approver itself, and a merge it refuses waits for the file to change.
/// </remarks>
public sealed class VaultFileWatch : IDisposable
{
    /// <summary>How often the file is compared with the open copy, whatever the folder watch reports.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(250);

    private readonly IWatchedVault _watched;
    private readonly Lock _gate = new();
    private readonly ITimer _poll;
    private readonly ITimer _settleTimer;
    private FileSystemWatcher? _folder;
    private Task _merging = Task.CompletedTask;
    private bool _isMerging;
    private bool _disposed;

    /// <param name="watched">The approver's open vault, whose clock the poll and the settling run on.</param>
    public VaultFileWatch(IWatchedVault watched)
    {
        ArgumentNullException.ThrowIfNull(watched);

        _watched = watched;
        _poll = watched.Clock.CreateTimer(_ => Check(), null, Poll, Poll);
        _settleTimer = watched.Clock.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watched.Opened += OnOpened;
        _watched.Locked += OnLocked;

        if (_watched.Open is not null)
        {
            WatchFolder();

            // A save made before the watch began is merged now rather than at the first poll.
            Check();
        }
    }

    /// <summary>The vault watched.</summary>
    public IWatchedVault Watched => _watched;

    /// <summary>Raised on whichever thread noticed it, when the file may hold another save or a merge started or finished.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether a merge of another program's save runs now.</summary>
    public bool IsMerging
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
    public Task Merging
    {
        get
        {
            lock (_gate)
            {
                return _merging;
            }
        }
    }

    /// <summary>Compares the file with the open copy now, merging another program's save where the approver merges.</summary>
    public void Check()
    {
        if (_watched.Open is not { } vault)
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

        if (state.Status == VaultSaveStatus.ChangedOnDisk && File.Exists(vault.Path) && _watched.MergesAnotherSave)
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
            _merging = OnItsOwnThread(() =>
            {
                try
                {
                    _watched.MergeAnotherSave();
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

    /// <summary>Runs <paramref name="work"/> on a thread of its own, which starts at once however busy the thread pool is.</summary>
    /// <remarks>Not the pool's: a merge withdraws what agents wait for, which the pool delivers, so it must not first wait for the pool to start it.</remarks>
    private static Task OnItsOwnThread(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            work();
            done.SetResult();
        })
        { IsBackground = true };

        thread.Start();
        return done.Task;
    }

    private void OnOpened(object? sender, EventArgs e) => WatchFolder();

    private void OnLocked(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _folder?.Dispose();
            _folder = null;
        }
    }

    private void WatchFolder()
    {
        if (_watched.Open?.Path is not { } path || Path.GetDirectoryName(Path.GetFullPath(path)) is not { Length: > 0 } folder)
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

    /// <inheritdoc/>
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

        _watched.Opened -= OnOpened;
        _watched.Locked -= OnLocked;
        _poll.Dispose();
        _settleTimer.Dispose();
    }
}
