using System.Globalization;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.CrashReports;

/// <summary>
/// Crash reports waiting to be sent, one file per distinct report in a folder of keypaste's home (D-0559).
/// </summary>
/// <remarks>
/// It takes only a <see cref="CrashReport"/>, which only <see cref="CrashScrubber"/> makes, and keeps a file only while it
/// reads back as exactly what the scrubber writes; any other file named like a report is deleted. Identical reports merge
/// into one with a count. At most <see cref="MaximumReports"/> files and <see cref="MaximumBytes"/> are kept, the oldest
/// dropped first, and a report from more than <see cref="MaximumDays"/> days ago is dropped. Files are owner-only outside
/// Windows. Nothing here throws for want of a disk: a report that cannot be kept is lost.
/// <para>
/// The folder also holds the run marker, which names only the version and when the run started: a start that finds it
/// knows the run before ended with nothing able to report it, and makes it an <c>ended-unexpectedly</c> report (D-0595).
/// </para>
/// </remarks>
public sealed class CrashOutbox
{
    /// <summary>The most reports kept.</summary>
    public const int MaximumReports = 20;

    /// <summary>The most bytes kept, across every report.</summary>
    public const int MaximumBytes = 256 * 1024;

    /// <summary>How many days before today a report may have happened and still be kept.</summary>
    public const int MaximumDays = 30;

    /// <summary>The run marker's name in the folder.</summary>
    public const string RunMarkerName = "running";

    private const int _markerBytes = 128;
    private const string _extension = ".json";
    private const UnixFileMode _ownerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;

    /// <summary>Keeps reports in one folder.</summary>
    /// <param name="folder">The folder, created owner-only with the first report: <c>crash-reports</c> in keypaste's home.</param>
    /// <param name="clock">The clock a report's age and its place in the outbox are read on.</param>
    public CrashOutbox(string folder, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        Folder = Path.GetFullPath(folder);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The folder the reports are in.</summary>
    public string Folder { get; }

    /// <summary>Keeps a report, merged into an identical one already waiting.</summary>
    /// <returns>Whether it was written.</returns>
    public bool Add(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            try
            {
                CreateFolder();

                var path = PathOf(report.Identity);
                var kept = Read(path);
                var written = kept is null ? report : CrashScrubber.Merge(kept, report);

                AtomicFile.Write(path, written.Json.Span);
                File.SetLastWriteTimeUtc(path, _clock.GetUtcNow().UtcDateTime);
                Tidy(report.Identity);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>The reports waiting, oldest first, after dropping what the limits and the age rule no longer allow.</summary>
    public IReadOnlyList<CrashReport> Waiting()
    {
        lock (_gate)
        {
            try
            {
                return Tidy(keep: null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    /// <summary>Deletes a report, unless an identical failure has been merged into it since it was read.</summary>
    /// <returns>Whether it is no longer waiting as it was.</returns>
    public bool Remove(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            try
            {
                var path = PathOf(report.Identity);

                if (Read(path) is { } kept && kept.Json.Span.SequenceEqual(report.Json.Span))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Deletes every waiting report and the folder.</summary>
    /// <returns>Whether nothing is left.</returns>
    public bool Clear()
    {
        lock (_gate)
        {
            try
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Marks this run as started, so a later start can tell it ended with nothing able to report it.</summary>
    /// <returns>Whether the marker was written.</returns>
    public bool MarkRunning()
    {
        lock (_gate)
        {
            try
            {
                CreateFolder();
                AtomicFile.Write(MarkerPath, Encoding.UTF8.GetBytes(CoreInfo.Version + " " + _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Removes the run marker, as a run that ends cleanly or has written its crash's report does.</summary>
    /// <returns>Whether no marker is left.</returns>
    public bool ClearRunning()
    {
        lock (_gate)
        {
            try
            {
                File.Delete(MarkerPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return !File.Exists(MarkerPath);
            }
        }
    }

    /// <summary>Takes the marker an earlier run left, which means that run ended with nothing able to report it.</summary>
    /// <returns>
    /// An <c>ended-unexpectedly</c> report dated when that run started, or null when no run left one, when it was another
    /// version's, whose report would name the wrong one, or when it cannot be read.
    /// </returns>
    public CrashReport? TakeEndedRun()
    {
        lock (_gate)
        {
            try
            {
                var marker = new FileInfo(MarkerPath);

                if (!marker.Exists)
                {
                    return null;
                }

                var text = marker.Length <= _markerBytes ? File.ReadAllText(MarkerPath, Encoding.UTF8) : string.Empty;
                File.Delete(MarkerPath);

                return text.Split(' ') is [var version, var started]
                    && version == CoreInfo.Version
                    && DateTimeOffset.TryParseExact(started, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                        ? CrashScrubber.EndedUnexpectedly(new CrashContext(at, Uptime: null, Screen: null))
                        : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private string MarkerPath => Path.Combine(Folder, RunMarkerName);

    private List<CrashReport> Tidy(string? keep)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var oldest = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(-MaximumDays);
        List<(CrashReport Report, string Path, DateTime Written)> waiting = [];

        foreach (var path in Directory.GetFiles(Folder, "*" + _extension))
        {
            if (Read(path) is { } report && Path.GetFileNameWithoutExtension(path) == report.Identity && report.OccurredOn >= oldest)
            {
                waiting.Add((report, path, File.GetLastWriteTimeUtc(path)));
            }
            else
            {
                File.Delete(path);
            }
        }

        waiting.Sort((a, b) => a.Written != b.Written ? a.Written.CompareTo(b.Written) : string.CompareOrdinal(a.Path, b.Path));

        var bytes = waiting.Sum(each => each.Report.Json.Length);

        foreach (var each in waiting.Where(entry => entry.Report.Identity != keep).ToList())
        {
            if (waiting.Count <= MaximumReports && bytes <= MaximumBytes)
            {
                break;
            }

            File.Delete(each.Path);
            bytes -= each.Report.Json.Length;
            waiting.Remove(each);
        }

        return [.. waiting.Select(each => each.Report)];
    }

    private static CrashReport? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);

            if (!file.Exists || file.Length > CrashScrubber.MaximumBytes)
            {
                return null;
            }

            return CrashScrubber.TryRead(File.ReadAllBytes(path), out var report) ? report : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private string PathOf(string identity) => Path.Combine(Folder, identity + _extension);

    private void CreateFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Folder);
        }
        else
        {
            Directory.CreateDirectory(Folder, _ownerOnlyFolder);
        }
    }
}
