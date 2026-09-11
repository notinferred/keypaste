namespace Keypaste.Core;

/// <summary>
/// A temporary directory private to this process, which every save writes its transacted temporary
/// file into.
/// </summary>
/// <remarks>
/// <para>
/// Windows build 10.0.26100 refuses to create a file whose 8.3 alias another transaction has
/// reserved, and every KeePass-family program names its transacted temporary
/// <c>KeePass_TxF_*.tmp</c> — so all of them want <c>KEEPAS~1.TMP</c> in one <c>%TEMP%</c>. D-0122
/// measured 1387 of 1600 concurrent saves refused there, and none at all once each saver had a
/// directory of its own.
/// </para>
/// <para>
/// <b>Redirected once and never swapped back and forth.</b> <c>TMP</c> and <c>TEMP</c> are
/// process-wide, which is measured rather than assumed. A value swapped around each save would be
/// read by any other thread that wanted a temporary file at that moment, and that thread's file
/// would then sit in a directory this one is about to delete. The .NET runtime and Avalonia both
/// allocate temporary files and neither is auditable from this repository, so the window is removed
/// rather than reasoned about.
/// </para>
/// </remarks>
public static class ProcessTemporaryDirectory
{
    private const string _prefix = "keypaste-tmp-";
    private const string _ownerFile = "owner.lock";

    /// <summary>A directory with no owner file this recent is still being set up, not abandoned.</summary>
    private static readonly TimeSpan _youngEnoughToStillBeStartingUp = TimeSpan.FromSeconds(30);

    private static readonly Lock _gate = new();
    private static volatile string? _directory;
    private static volatile Dictionary<string, string?>? _originals;
    private static FileStream? _owner;

    /// <summary>
    /// The <c>TMP</c> and <c>TEMP</c> this process started with, and a child must be given back.
    /// Empty until a save has redirected them.
    /// </summary>
    /// <remarks>
    /// A child of <c>keypaste run</c> is the user's own program. It must not inherit the directory
    /// keypaste redirected itself into, which keypaste deletes when it exits. A null value means the
    /// variable was unset and must be removed from the child rather than set to anything.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> OriginalTemporaryVariables =>
        _originals ?? new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Points this process's temporary path at a directory nothing else writes into.</summary>
    internal static void EnsureRedirected()
    {
        if (_directory is not null)
        {
            return;
        }

        lock (_gate)
        {
            if (_directory is not null)
            {
                return;
            }

            var parent = Path.GetTempPath();
            SweepAbandoned(parent);

            var directory = Directory.CreateDirectory(
                Path.Combine(parent, _prefix + Guid.NewGuid().ToString("N"))).FullName;

            // Held open for the life of the process, and deleted the moment the handle closes -
            // including a kill, where no exit path runs. Another keypaste starting up asks for this
            // file and learns from the answer alone whether anyone still owns the directory.
            _owner = new FileStream(
                Path.Combine(directory, _ownerFile),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            _originals = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TMP"] = Environment.GetEnvironmentVariable("TMP"),
                ["TEMP"] = Environment.GetEnvironmentVariable("TEMP"),
            };

            Environment.SetEnvironmentVariable("TMP", directory);
            Environment.SetEnvironmentVariable("TEMP", directory);

            _directory = directory;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();
        }
    }

    /// <summary>Deletes directories whose owning process is gone.</summary>
    /// <remarks>
    /// A crash leaves the directory behind, because the exit handler never runs. It does not leave
    /// the owner file behind: that handle closes however the process died. So a directory without
    /// one has no owner — unless it was created moments ago and has not opened it yet, which is the
    /// one case the age check is here to spare.
    /// </remarks>
    private static void SweepAbandoned(string parent)
    {
        string[] candidates;

        try
        {
            candidates = Directory.GetDirectories(parent, _prefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(candidate, _ownerFile)))
                {
                    continue;
                }

                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(candidate) < _youngEnoughToStillBeStartingUp)
                {
                    continue;
                }

                Directory.Delete(candidate, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Someone else's, and in use. Leaving it is always the safe answer.
            }
        }
    }

    private static void Release()
    {
        var directory = _directory;

        if (directory is null)
        {
            return;
        }

        try
        {
            _owner?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next keypaste to start sweeps it.
        }
    }
}
